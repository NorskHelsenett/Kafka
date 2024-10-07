using System.Runtime.CompilerServices;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry.Serdes;
using KafkaBlobChunking;

namespace BigPayloads;

public class ChunkedStreamConsumer: BackgroundService
{
    private readonly ILogger<ChunkedStreamConsumer> _logger;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly OutputStateService _outputStateService;
    private readonly string _topicChunks;
    private readonly string _topicMetadata;
    private readonly IConsumer<string, BlobChunk?> _chunkConsumer;

    public ChunkedStreamConsumer(ILogger<ChunkedStreamConsumer> logger, IHostApplicationLifetime hostApplicationLifetime, OutputStateService outputStateService)
    {
        _logger = logger;
        _hostApplicationLifetime = hostApplicationLifetime;
        _outputStateService = outputStateService;

        var topicNameChunksTopic = Environment.GetEnvironmentVariable(BIG_PAYLOADS_CHUNKS_TOPIC);
        if(string.IsNullOrWhiteSpace(topicNameChunksTopic))
        {
            _logger.LogError($"Cannot consume if topic is not specified. Environment variable {nameof(BIG_PAYLOADS_CHUNKS_TOPIC)} was not set/is empty.");
            throw new InvalidOperationException($"Environment variable {nameof(BIG_PAYLOADS_CHUNKS_TOPIC)} has to have value.");
        }
        _topicChunks = topicNameChunksTopic;

        var topicNameMetadataTopic = Environment.GetEnvironmentVariable(BIG_PAYLOADS_METADATA_TOPIC);
        if(string.IsNullOrWhiteSpace(topicNameMetadataTopic))
        {
            _logger.LogError($"Cannot consume if topic is not specified. Environment variable {nameof(BIG_PAYLOADS_METADATA_TOPIC)} was not set/is empty.");
            throw new InvalidOperationException($"Environment variable {nameof(BIG_PAYLOADS_METADATA_TOPIC)} has to have value.");
        }
        _topicMetadata = topicNameMetadataTopic;

        _logger.LogDebug($"{nameof(ChunkedStreamConsumer)} initialized");

        _chunkConsumer = GetChunkConsumer();
        _chunkConsumer.Subscribe(_topicChunks);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Kafka consumer service is doing pre startup blocking work.");
        await DoWork(stoppingToken);
        _hostApplicationLifetime.StopApplication();
    }

    private async Task DoWork(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(1));
        _logger.LogDebug("Setting up Kafka blob metadata consumer.");
        var metadataConsumer = GetChunkMetadataConsumer();
        _logger.LogDebug("Storing high watermarks at startup time");
        await SaveStartupTimeLastTopicPartitionOffsets(metadataConsumer);
        _logger.LogDebug($"Subscribing to topic {_topicMetadata}");
        metadataConsumer.Subscribe(_topicMetadata);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = metadataConsumer.Consume(stoppingToken);

                if (result?.Message == null)
                {
                    _logger.LogDebug("We've reached the end of the metadata topic.");
                    await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
                }
                else
                {
                    _logger.LogDebug($"New blob metadata event received");
                    var nextBlobMetadata = result.Message.Value;
                    if (nextBlobMetadata != null)
                    {
                        _outputStateService.Store(blobChunksMetadata: nextBlobMetadata, correlationId: nextBlobMetadata.FinalChecksum);

                        List<byte> reassembled = new();
                        await foreach(var b in GetBlobByMetadataAsync(nextBlobMetadata, stoppingToken)){
                            reassembled.Add(b);
                        }
                        var nextBlobPrintFriendly = System.Text.Encoding.UTF8.GetString(reassembled.ToArray());
                        _logger.LogInformation($"Blob {result.Message.Key}:\n=== BEGIN PAYLOAD ===\n{nextBlobPrintFriendly}\n=== END PAYLOAD ===");
                    }
                    else
                    {
                        _logger.LogInformation($"Received tombstone for blob with id \"{result.Message.Key}\"");
                        _outputStateService.Remove(result.Message.Key, "");
                    }
                    _outputStateService.UpdateLastConsumedTopicPartitionOffsets(result.TopicPartitionOffset);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Kafka payload metadata consumer received exception while consuming, exiting");
        }
        finally
        {
            _logger.LogDebug("MetadataConsumer shutting down and disconnecting from the cluster");
            metadataConsumer.Close();
        }
    }

    public async IAsyncEnumerable<byte> GetBlobByMetadataAsync(BlobChunksMetadata metadata, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogDebug($"Got request to retrieve chunks for payload {metadata.BlobId}");
        var streamChecksum = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        ulong totalNumberOfBytesConsumed = 0;

        // For simplicity of example, don't handle weird extreme of chunks across multiple topics
        if (metadata.ChunksTopicPartitionOffsetsEarliest.Any(tpo => tpo.Topic != _topicChunks))
        {
            _logger.LogError("No chunk topic partition offsets");
            throw new Exception($"Received metadata about chunks that are not on our configured topic for chunks (configured is \"{_topicChunks}\")");
        }
        _logger.LogDebug($"Setting up consumer to consume from earliest offsets specified in metadata");
        var chunkTopicPartitionOffsetsEarliest = metadata.ChunksTopicPartitionOffsetsEarliest
            .GroupBy(tpo => (tpo.Topic, tpo.Partition))
            .ToDictionary(tpGroupedOffsets=> tpGroupedOffsets.Key.Partition, offsets => offsets.Min(o => o.Offset))
            .Select(partitionToOffsetMap => new TopicPartitionOffset(_topicChunks, new Partition(partitionToOffsetMap.Key), new Offset(partitionToOffsetMap.Value)))
            .ToArray();
        _logger.LogDebug($"Earliest offsets: {System.Text.Json.JsonSerializer.Serialize(chunkTopicPartitionOffsetsEarliest)}");

        _chunkConsumer.Commit(chunkTopicPartitionOffsetsEarliest);
        _chunkConsumer.Unassign();
        _chunkConsumer.Assign(chunkTopicPartitionOffsetsEarliest);

        ulong nextExpectedChunkNumber = 1;
        while (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug($"Waiting for messages to consume on chunks topic");
            var result = _chunkConsumer.Consume(cancellationToken);

            if (result?.Message == null)
            {
                // This should not happen.
                _logger.LogDebug("We've reached the end of the chunks topic.");
                await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
            }
            else
            {
                _logger.LogDebug($"Consumed chunk {result.Message.Key} at topic {result.Topic} partition {result.Partition.Value} offset {result.Offset.Value}");
                var nextChunk = result.Message.Value;
                if (nextChunk != null)
                {
                    if (nextChunk.CompleteBlobId == metadata.BlobId)
                    {
                        if(nextExpectedChunkNumber != nextChunk.ChunkNumber)
                        {
                            if(nextChunk.ChunkNumber+1 == nextExpectedChunkNumber) // Because uint, do check with + instead of -
                            {
                                _logger.LogWarning($"When consuming chunks for blob with id \"{metadata.BlobId}\" received duplicate chunk, number {nextChunk.ChunkNumber} at topic {result.Topic} partition {result.Partition.Value} offset {result.Offset.Value}");
                            }
                            else
                            {
                                throw new Exception($"When consuming chunks for blob with id \"{metadata.BlobId}\" expected next chunk to be chunk number {nextExpectedChunkNumber} but instead received chunk number {nextChunk.ChunkNumber}");
                            }
                        }
                        else
                        {
                            nextExpectedChunkNumber++;
                            streamChecksum.AppendData(nextChunk.ChunkPayload.ToByteArray(), 0, (int) nextChunk.ChunkNumberOfByes);
                            foreach (var payloadByte in nextChunk.ChunkPayload)
                            {
                                yield return payloadByte;
                            }
                            totalNumberOfBytesConsumed += (ulong) nextChunk.ChunkPayload.Length;
                            if(nextExpectedChunkNumber > metadata.TotalNumberOfChunks)
                            {
                                _logger.LogDebug($"All chunks of blob {metadata.BlobId} consumed, stopping consuming and proceeding to check checksum");
                                break;
                            }
                        }
                    }
                    else
                    {
                        _logger.LogDebug($"Consumed chunk {nextChunk.ChunkId} is not referenced in the metadata with blob id {metadata.BlobId}.");
                    }
                }
            }
        }

        var finalChecksum = Convert.ToHexString(streamChecksum.GetHashAndReset());
        var checksumsMatch = finalChecksum == metadata.FinalChecksum;
        var numberOfBytesMatch = totalNumberOfBytesConsumed == metadata.CompleteBlobTotalNumberOfBytes;
        if(!checksumsMatch || !numberOfBytesMatch)
        {
            throw new Exception($"When consuming chunks for blob with id \"{metadata.BlobId}\" uncovered mismatch in consumed vs expected. Consumed checksum was \"{finalChecksum}\", expected \"{metadata.FinalChecksum}\". Consumed number of bytes was \"{totalNumberOfBytesConsumed}\", expected \"{metadata.CompleteBlobTotalNumberOfBytes}\"");
        }
    }

    private IConsumer<string, BlobChunksMetadata?> GetChunkMetadataConsumer()
    {
        return  new ConsumerBuilder<string, BlobChunksMetadata?>(KafkaConfigBinder.GetConsumerConfig())
            .SetPartitionsAssignedHandler((c, partitions) =>
            {
                // Always start at the beginning, only use cg for tracking liveliness and lag from the outside
                return partitions.Select(tp => new TopicPartitionOffset(tp, Offset.Beginning));
            })
            .SetValueDeserializer(new ProtobufDeserializer<BlobChunksMetadata?>().AsSyncOverAsync())
            .SetErrorHandler((_, e) => _logger.LogError($"Error: {e.Reason}"))
            .Build();
    }

    private IConsumer<string, BlobChunk?> GetChunkConsumer()
    {
        var kafkaConfig = KafkaConfigBinder.GetConsumerConfig();
        kafkaConfig.GroupId = $"{kafkaConfig.GroupId}-chunks";
        return  new ConsumerBuilder<string, BlobChunk?>(kafkaConfig)
            .SetValueDeserializer(new ProtobufDeserializer<BlobChunk?>().AsSyncOverAsync())
            .SetErrorHandler((_, e) => _logger.LogError($"Error: {e.Reason}"))
            .Build();
    }

    private async Task SaveStartupTimeLastTopicPartitionOffsets(IConsumer<string, BlobChunksMetadata?> consumer)
    {
        var partitions = await GetTopicPartitions(_topicMetadata);
        List<TopicPartitionOffset> highOffsetsAtStartupTime = [];
        List<TopicPartitionOffset> lowOffsetsAtStartupTime = [];
        foreach(var partition in partitions)
        {
            var currentOffsets = consumer.QueryWatermarkOffsets(partition, timeout: TimeSpan.FromSeconds(5));
            if(currentOffsets?.High.Value != null)
            {
                // Subtract 1, because received value is "the next that would be written"
                long offsetHigh = currentOffsets.High.Value == 0 ? 0 : currentOffsets.High.Value - 1;
                highOffsetsAtStartupTime.Add(new TopicPartitionOffset(_topicMetadata, partition.Partition.Value, offsetHigh));

                // Offset value defaults to 0 if none are written
                lowOffsetsAtStartupTime.Add(new TopicPartitionOffset(_topicMetadata, partition.Partition.Value, currentOffsets.Low.Value));
            }
        }
        if(!_outputStateService.SetStartupTimeHightestTopicPartitionOffsets(highOffsetsAtStartupTime))
        {
            _logger.LogError($"Failed to save what topic high watermark offsets are at startup time");
        }
        if(_outputStateService.GetLastConsumedTopicPartitionOffsets().Count == 0)
        {
            foreach(var partitionOffset in lowOffsetsAtStartupTime)
            {
                if(!_outputStateService.UpdateLastConsumedTopicPartitionOffsets(partitionOffset))
                {
                    _logger.LogError($"Failed to set up low watermark offset for partition {partitionOffset.Offset.Value} at startup time");
                }
            }
        }
    }

    private async Task<List<TopicPartition>> GetTopicPartitions(string topic)
    {
        var adminClientConfig = KafkaConfigBinder.GetAdminClientConfig();
        using var adminClient = new AdminClientBuilder(adminClientConfig).Build();
        try
        {
            var description = await adminClient.DescribeTopicsAsync(TopicCollection.OfTopicNames([topic]));
            List<TopicPartition> topicPartitions = description.TopicDescriptions
                .FirstOrDefault(tDescription => tDescription.Name == topic)
                ?.Partitions
                .Select(tpInfo => new TopicPartition(topic, tpInfo.Partition))
                .ToList() ?? [];
            return topicPartitions;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"An error occurred when retrieving list of partitions on topic");
        }
        return [];
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Kafka consumer received request for graceful shutdown.");
        _chunkConsumer.Close();

        await base.StopAsync(stoppingToken);
    }
}
