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

        // await SaveStartupTimeLastTopicPartitionOffsets(consumer);

        consumer.Subscribe(_topic);
        try
        {
            throw new NotImplementedException("");
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Kafka consumer received exception while consuming, exiting");
        }
        finally
        {
            // Close consumer
            _logger.LogDebug("Disconnecting consumer from Kafka cluster, leaving consumer group and all that");
            throw new NotImplementedException();
        }
    }

    private IConsumer<string, Person> GetConsumer()
    {
        var consumerConfig = KafkaConfigBinder.GetConsumerConfig();
        throw new NotImplementedException();
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
