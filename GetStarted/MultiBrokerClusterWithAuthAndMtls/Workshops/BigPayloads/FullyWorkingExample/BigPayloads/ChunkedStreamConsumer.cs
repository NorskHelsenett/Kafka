using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry.Serdes;
using KafkaBlobChunking;

namespace BigPayloads;

public class ChunkedStreamConsumer: BackgroundService
{
    private readonly ILogger<ChunkedStreamConsumer> _logger;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly string _topicChunks;
    private readonly string _topicMetadata;
    private readonly IConsumer<string, BlobChunk> _chunkConsumer;

    public ChunkedStreamConsumer(ILogger<ChunkedStreamConsumer> logger, IHostApplicationLifetime hostApplicationLifetime)
    {
        _logger = logger;
        _hostApplicationLifetime = hostApplicationLifetime;

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
                        var nextBlob = await GetBlobByMetadataAsync(nextBlobMetadata, stoppingToken);
                        var nextBlobPrintFriendly = System.Text.Encoding.UTF8.GetString(nextBlob);
                        _logger.LogInformation($"Blob {result.Message.Key}:\n=== BEGIN PAYLOAD ===\n{nextBlobPrintFriendly}\n=== END PAYLOAD ===");
                    }
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

    private async Task<byte[]> GetBlobByMetadataAsync(BlobChunksMetadata metadata, CancellationToken cancellationToken)
    {
        _logger.LogDebug($"Got request to retrieve chunks for payload {metadata.BlobId}");
        // For simplicity of example, don't handle weird extreme of chunks across multiple topics
        if (metadata.ChunksTopicPartitionOffsets.Any(tpo => tpo.Topic != _topicChunks))
        {
            _logger.LogError("No chunk topic partition offsets");
            throw new Exception($"Received metadata about chunks that are not on our configured topic for chunks (configured is \"{_topicChunks}\")");
        }
        _logger.LogDebug($"Setting up consumer to consume from earliest offsets specified in metadata");
        var chunkTopicPartitionOffsetsEarliest = metadata.ChunksTopicPartitionOffsets
            .GroupBy(tpo => (tpo.Topic, tpo.Partition))
            .ToDictionary(tpGroupedOffsets=> tpGroupedOffsets.Key.Partition, offsets => offsets.Min(o => o.Offset))
            .Select(partitionToOffsetMap => new TopicPartitionOffset(_topicChunks, new Partition(partitionToOffsetMap.Key), new Offset(partitionToOffsetMap.Value)))
            .ToArray();
        _logger.LogDebug($"Earliest offsets: {System.Text.Json.JsonSerializer.Serialize(chunkTopicPartitionOffsetsEarliest)}");

        _chunkConsumer.Commit(chunkTopicPartitionOffsetsEarliest);
        _chunkConsumer.Unassign();
        _chunkConsumer.Assign(chunkTopicPartitionOffsetsEarliest);

        var consumedChunks = new Dictionary<ulong, BlobChunk>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug($"Waiting for messages to consume on chunks topic");
                var result = _chunkConsumer.Consume(cancellationToken);

                if (result?.Message == null)
                {
                    _logger.LogDebug("We've reached the end of the chunks topic.");
                    await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
                }
                else
                {
                    _logger.LogDebug($"Consumed chunk {result.Message.Key} at topic {result.Topic} partition {result.Partition.Value} offset {result.Offset.Value}");
                    var nextChunk = result.Message.Value;
                    if (nextChunk != null)
                    {
                        if (metadata.ChunkTopicKeys.Contains(nextChunk.ChunkId))
                        {
                            // This check is for most scenarios uneccessary, you'd rather just complete downloading all of the chunks and verifying the final blob.
                            // But it is included for completeness, if you are not certain that you need it just remove it for the performance gain.
                            var chunkChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.Create().ComputeHash(nextChunk.ChunkPayload.ToByteArray()));
                            if (chunkChecksum != nextChunk.ChunkChecksum)
                            {
                                _logger.LogError($"Chunk checksum mismatch in chunk {nextChunk.ChunkId}. Expected embedded: {nextChunk.ChunkChecksum}, computed: {chunkChecksum}");
                                throw new Exception("Chunk checksum mismatch");
                            }
                            consumedChunks[nextChunk.ChunkNumber] = nextChunk;
                            if ((ulong) consumedChunks.Count == metadata.TotalNumberOfChunks)
                            {
                                _logger.LogDebug($"All chunks of blob {metadata.BlobId} consumed, stopping consuming and proceeding to reassemble");
                                break;
                            }
                        }
                        else
                        {
                            _logger.LogDebug($"Consumed chunk {nextChunk.ChunkId} is not referenced in the metadata with blob id {metadata.BlobId}.");
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Kafka chunk consumer received exception while consuming, exiting");
            // _hostApplicationLifetime.StopApplication();
            throw;
        }

        var reassembled = ReassembleBlobFromChunks(consumedChunks.Values);
        var reassembledChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.Create().ComputeHash(reassembled));
        if (reassembledChecksum != metadata.FinalChecksum)
        {
            _logger.LogError($"Checksum for blob {metadata.BlobId} does not match checksum of reassembled chunks. Expected: {metadata.FinalChecksum}, actual: {reassembledChecksum}");
            throw new Exception("Checksum mismatch for reassembled chunks");
        }
        if ((ulong) reassembled.Length != metadata.CompleteBlobTotalNumberOfBytes)
        {
            _logger.LogError($"Number of bytes retrieved for blob {metadata.BlobId} does not match size sender specified. Expected {metadata.CompleteBlobTotalNumberOfBytes}, received {reassembled.Length}");
            throw new Exception("Number of bytes retrieved does not match size sender specified");
        }

        return reassembled;
    }

    private byte[] ReassembleBlobFromChunks(IEnumerable<BlobChunk> chunks)
    {
        var orderedChunks = chunks
            .OrderBy(chunk => chunk.ChunkNumber)
            .DistinctBy(chunk => chunk.ChunkNumber); // Not really necessary because of what happens before we reach this function, but nice safeguard should someone just copy it out
        var reconstructed = orderedChunks
            .Aggregate(new List<byte>(), (aggregated, nextChunkToProcess) =>
            {
                aggregated.AddRange(nextChunkToProcess.ChunkPayload.ToByteArray());
                return aggregated;
            })
            .ToArray();
        // Could verify checksum here, but for the sake of keeping this function simple/not making it do everything in the world, leave that to the caller.
        return reconstructed;
    }

    private IConsumer<string, BlobChunksMetadata> GetChunkMetadataConsumer()
    {
        return  new ConsumerBuilder<string, BlobChunksMetadata>(KafkaConfigBinder.GetConsumerConfig())
            .SetPartitionsAssignedHandler((c, partitions) =>
            {
                // Always start at the beginning, only use cg for tracking liveliness and lag from the outside
                return partitions.Select(tp => new TopicPartitionOffset(tp, Offset.Beginning));
            })
            .SetValueDeserializer(new ProtobufDeserializer<BlobChunksMetadata>().AsSyncOverAsync())
            .SetErrorHandler((_, e) => _logger.LogError($"Error: {e.Reason}"))
            .Build();
    }

    private IConsumer<string, BlobChunk> GetChunkConsumer()
    {
        var kafkaConfig = KafkaConfigBinder.GetConsumerConfig();
        kafkaConfig.GroupId = $"{kafkaConfig.GroupId}-chunks";
        // var chunkConsumer = new ConsumerBuilder<string, BlobChunk>(kafkaConfig)
        return  new ConsumerBuilder<string, BlobChunk>(kafkaConfig)
            .SetValueDeserializer(new ProtobufDeserializer<BlobChunk>().AsSyncOverAsync())
            .SetErrorHandler((_, e) => _logger.LogError($"Error: {e.Reason}"))
            .Build();
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Kafka consumer received request for graceful shutdown.");
        _chunkConsumer.Close();

        await base.StopAsync(stoppingToken);
    }
}
