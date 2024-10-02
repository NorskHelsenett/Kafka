using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Google.Protobuf;
using KafkaBlobChunking;

namespace BigPayloads;

public class ChunkingProducer
{
    private readonly ILogger<ChunkingProducer> _logger;
    private readonly string _topicChunks;
    private readonly string _topicMetadata;

    public ChunkingProducer(ILogger<ChunkingProducer> logger)
    {
        _logger = logger;
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
    }

    public async Task<bool> ProduceChunkedAsync(byte[] payload)
    {
        // Notable assumptions: Payload fits in memory -> it's not continuous stream of unknown size
        _logger.LogDebug("Received request to produce chunked message.");
        var payloadSize = payload.Length;
        var payloadChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.Create().ComputeHash(payload));
        var bytesPerChunk = 128;
        var chunks = payload.Chunk(bytesPerChunk).ToArray();
        var numberOfChunks = chunks.Length;

        var producedChunkIds = new List<string>();
        var producedChunksOffsets = new List<KafkaTopicPartitionOffset>();

        var metadataProducer = GetChunkMetadataProducer(); // Init now, because proceeding without, should it fail, can be iffy.
        var chunkProducer = GetChunkProducer();
        for (int i = 0; i < numberOfChunks; i++)
        {
            var nextChunk = chunks[i];
            var nextChunkCheksum = Convert.ToHexString(System.Security.Cryptography.SHA256.Create().ComputeHash(nextChunk));

            var nextChunkKey = $"{payloadChecksum}_{i}";
            var nextChunkPayload = new BlobChunk
            {
                ChunkId = nextChunkKey,
                CompleteBlobId = payloadChecksum,
                CompleteBlobChecksum = payloadChecksum,
                ChunkNumber = (ulong) i,
                ChunkChecksum = nextChunkCheksum,
                ChunkNumberOfByes = (ulong) nextChunkKey.Length,
                ChunkPayload = ByteString.CopyFrom(nextChunk),
                TotalNumberOfChunks = (ulong) numberOfChunks,
                CompleteBlobTotalNumberOfBytes = (ulong) payloadSize
            };
            var nextChunkMessage = new Message<string, BlobChunk>
            {
                Key = nextChunkKey,
                Value = nextChunkPayload
            };
            var produceResult = await chunkProducer.ProduceAsync(_topicChunks, nextChunkMessage);
            if (produceResult.Status == PersistenceStatus.Persisted)
            {
                producedChunkIds.Add(nextChunkKey);
                producedChunksOffsets.Add(new KafkaTopicPartitionOffset() { Topic = _topicChunks, Partition = produceResult.Partition, Offset = produceResult.Offset });
            }
            else
            {
                Console.WriteLine($"Failed when producing chunk {i} of payload with checksum {payloadChecksum}");
                return false;
                // throw new Exception($"Failed when producing chunk {i} of payload with checksum {payloadChecksum}");
            }
        }

        _logger.LogDebug("Payload chunked and given to producer, flushing to guarantee progress");
        chunkProducer.Flush(); // Don't proceed unless everything's successfully shipped

        var metadataPayload = new BlobChunksMetadata
        {
            BlobId = payloadChecksum,
            ChunkTopicKeys = { producedChunkIds },
            ChunksTopicPartitionOffsets = { producedChunksOffsets },
            TotalNumberOfChunks = (ulong) numberOfChunks,
            CompleteBlobTotalNumberOfBytes = (ulong) payloadSize,
            FinalChecksum = payloadChecksum,
            BlobSchemaSubject = "",
            BlobSchemaVersion = "",
        };
        var metadataMessage = new Message<string, BlobChunksMetadata>
        {
            Key = payloadChecksum,
            Value = metadataPayload
        };
        var metadataProduceResult = await metadataProducer.ProduceAsync(_topicMetadata, metadataMessage);
        if (metadataProduceResult.Status != PersistenceStatus.Persisted)
        {
            Console.WriteLine($"Failed when producing metadata for shipped payload with checksum {payloadChecksum}");
            return false;
            // throw new Exception($"Failed when producing metadata for shipped payload with checksum {payloadChecksum}");
        }
        return true;
    }

    private IProducer<string, BlobChunk> GetChunkProducer()
    {
        var schemaRegistry = new CachedSchemaRegistryClient(KafkaConfigBinder.GetSchemaRegistryConfig());
        return  new ProducerBuilder<string, BlobChunk>(KafkaConfigBinder.GetProducerConfig())
            .SetValueSerializer(new ProtobufSerializer<BlobChunk>(schemaRegistry, GetProtobufSerializerConfig()))
            .Build();
    }

    private IProducer<string, BlobChunksMetadata> GetChunkMetadataProducer()
    {
        var schemaRegistry = new CachedSchemaRegistryClient(KafkaConfigBinder.GetSchemaRegistryConfig());
        return  new ProducerBuilder<string, BlobChunksMetadata>(KafkaConfigBinder.GetProducerConfig())
            .SetValueSerializer(new ProtobufSerializer<BlobChunksMetadata>(schemaRegistry, GetProtobufSerializerConfig()))
            .Build();
    }

    private ProtobufSerializerConfig GetProtobufSerializerConfig()
    {
        return new ProtobufSerializerConfig
        {
            AutoRegisterSchemas = false,
            NormalizeSchemas = true,
            UseLatestVersion = true
        };
    }
}
