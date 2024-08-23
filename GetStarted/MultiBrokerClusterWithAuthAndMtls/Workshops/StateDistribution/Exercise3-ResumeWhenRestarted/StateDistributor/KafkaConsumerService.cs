using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry.Serdes;

public class KafkaConsumerService : BackgroundService
{
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly OutputStateService _outputStateService;
    private readonly string _topic;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;

    public KafkaConsumerService(ILogger<KafkaConsumerService> logger, OutputStateService outputStateService, IHostApplicationLifetime hostApplicationLifetime)
    {
        _logger = logger;
        _hostApplicationLifetime = hostApplicationLifetime;
        _outputStateService = outputStateService;

        var topicName = Environment.GetEnvironmentVariable(STATE_DISTRIBUTOR_KAFKA_STATE_TOPIC);
        if(string.IsNullOrWhiteSpace(topicName))
        {
            _logger.LogError($"Cannot consume if topic is not specified. Environment variable {nameof(STATE_DISTRIBUTOR_KAFKA_STATE_TOPIC)} was not set/is empty.");
            throw new InvalidOperationException($"Environment variable {nameof(STATE_DISTRIBUTOR_KAFKA_STATE_TOPIC)} has to have value.");
        }
        _topic = topicName;

        _logger.LogDebug($"{nameof(KafkaConsumerService)} initialized");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Kafka consumer service is doing pre startup blocking work.");
        await DoWork(stoppingToken);
        _hostApplicationLifetime.StopApplication();
    }

    private async Task DoWork(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Kafka consumer service background task started.");

        var consumer = GetConsumer();

        await SaveStartupTimeLastTopicPartitionOffsets(consumer);

        consumer.Subscribe(_topic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);

                if (result?.Message == null)
                {
                    _logger.LogDebug("We've reached the end of the topic.");
                    await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
                }
                else
                {
                    var correlationIdFromHeader = string.Empty;
                    if(result.Message.Headers.TryGetLastBytes("Correlation-Id", out byte[] correlationIdHeaderValue)) correlationIdFromHeader = System.Text.Encoding.UTF8.GetString(correlationIdHeaderValue);
                    _logger.LogTrace($"The next event on topic {result.TopicPartitionOffset.Topic} partition {result.TopicPartitionOffset.Partition.Value} offset {result.TopicPartitionOffset.Offset.Value} received to the topic at the time {result.Message.Timestamp.UtcDateTime:o} has correlation ID \"{correlationIdFromHeader}\"");
                    if(result.Message.Value == null)
                    {
                        var key = result.Message.Key;
                        _outputStateService.Remove(key, correlationIdFromHeader);
                    }
                    else
                    {
                        var key = result.Message.Key;
                        var value = result.Message.Value;
                        _outputStateService.Store(key, value, correlationIdFromHeader);
                    }
                    _outputStateService.UpdateLastConsumedTopicPartitionOffsets(new TopicPartitionOffset(result.Topic, result.Partition.Value, result.Offset.Value));
                }
            }
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Kafka consumer received exception while consuming, exiting");
        }
        finally
        {
            // Close consumer
            _logger.LogDebug("Disconnecting consumer from Kafka cluster, leaving consumer group and all that");
            consumer.Close();
        }
    }

    private IConsumer<string, Person> GetConsumer()
    {
        var consumerConfig = KafkaConfigBinder.GetConsumerConfig();
        var consumer = new ConsumerBuilder<string, Person>(consumerConfig)
            .SetPartitionsAssignedHandler((c, partitions) =>
            {
                throw new NotImplementedException();
            })
            // .SetValueDeserializer(new ProtobufDeserializer<Person>(schemaRegistryClient, protobufSerializerConfig).AsSyncOverAsync())
            .SetValueDeserializer(new ProtobufDeserializer<Person>().AsSyncOverAsync())
            .SetErrorHandler((_, e) => _logger.LogError($"Error: {e.Reason}"))
            .Build();
        return consumer;
    }

    private async Task SaveStartupTimeLastTopicPartitionOffsets(IConsumer<string, Person> consumer)
    {
        throw new NotImplementedException();
    }

    private async Task<List<TopicPartition>> GetTopicPartitions(string topic)
    {
        throw new NotImplementedException();
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Kafka consumer received request for graceful shutdown.");
        await base.StopAsync(stoppingToken);
    }
}
