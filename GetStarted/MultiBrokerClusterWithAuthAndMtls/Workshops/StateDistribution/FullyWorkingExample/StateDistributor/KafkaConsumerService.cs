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
                var savedTpos = _outputStateService.GetLastConsumedTopicPartitionOffsets();
                // Check if we can start consuming where we left of, if persisted state
                if(savedTpos.Count > 0)
                {
                    // If partitions changed, all hope is lost, just start from the beginning on all of them
                    if(savedTpos.Count == partitions.Count)
                    {
                        if(partitions.All(p => savedTpos.Any(s => s.Partition.Value == p.Partition.Value)))
                        {
                            return savedTpos.Select(tpo => new TopicPartitionOffset(tpo.Topic, new Partition(tpo.Partition.Value), new Offset(tpo.Offset.Value)));
                        }
                        _logger.LogWarning($"There were saved processed topic partition offsets in storage, but the partition IDs didn't match the ones received from the consumer group. Something is disturbing.");
                    }
                    _logger.LogWarning($"There were saved processed topic partition offsets in storage, but number of saved partitions didn't match number of partitions received from consumer group. Something is fishy.");
                    _logger.LogInformation($"Topics received form consumer: {string.Join(";\n",partitions.Select(p => "Topic: " + p.Topic + ", Partition: " + p.Partition.Value))}");
                    _logger.LogInformation($"Topics form state storage: {string.Join(";\n",savedTpos.Select(p => "Topic: " + p.Topic + ", Partition: " + p.Partition.Value + ", offset: " + p.Offset.Value))}");
                }
                _logger.LogDebug($"Starting consuming all topics from beginning");
                // When starting up, always read the topic from the beginning.
                var offsets = partitions.Select(tp => new TopicPartitionOffset(tp, Offset.Beginning));
                return offsets;
            })
            // .SetValueDeserializer(new ProtobufDeserializer<Person>(schemaRegistryClient, protobufSerializerConfig).AsSyncOverAsync())
            .SetValueDeserializer(new ProtobufDeserializer<Person>().AsSyncOverAsync())
            .SetErrorHandler((_, e) => _logger.LogError($"Error: {e.Reason}"))
            .Build();
        return consumer;
    }

    private async Task SaveStartupTimeLastTopicPartitionOffsets(IConsumer<string, Person> consumer)
    {
        var partitions = await GetTopicPartitions(_topic);
        List<TopicPartitionOffset> highOffsetsAtStartupTime = [];
        List<TopicPartitionOffset> lowOffsetsAtStartupTime = [];
        foreach(var partition in partitions)
        {
            var currentOffsets = consumer.QueryWatermarkOffsets(partition, timeout: TimeSpan.FromSeconds(5));
            if(currentOffsets?.High.Value != null)
            {
                // Subtract 1, because received value is "the next that would be written"
                long offsetHigh = currentOffsets.High.Value == 0 ? 0 : currentOffsets.High.Value - 1;
                highOffsetsAtStartupTime.Add(new TopicPartitionOffset(_topic, partition.Partition.Value, offsetHigh));

                // Offset value defaults to 0 if none are written
                lowOffsetsAtStartupTime.Add(new TopicPartitionOffset(_topic, partition.Partition.Value, currentOffsets.Low.Value));
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

        await base.StopAsync(stoppingToken);
    }
}
