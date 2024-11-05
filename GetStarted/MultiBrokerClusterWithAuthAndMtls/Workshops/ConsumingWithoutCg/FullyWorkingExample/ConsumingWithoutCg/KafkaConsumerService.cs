using Confluent.Kafka;

public class KafkaConsumerService(ILogger<KafkaConsumerService> logger, IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation($"{nameof(KafkaConsumerService)} doing blocking work before start of async work");
        var adminClient = new AdminClientBuilder(KafkaConfigBinder.GetAdminClientConfig()).Build();
        var topicName = Environment.GetEnvironmentVariable(CONSUMING_WITHOUT_CG_TOPIC);
        var topicsMetadata = adminClient.GetMetadata(topicName,TimeSpan.FromSeconds(3));
        if (topicsMetadata.Topics.Count != 1)
        {
            logger.LogError($"Topic {topicName} specified in env var {CONSUMING_WITHOUT_CG_TOPIC} is not found. Shutting down");
            hostApplicationLifetime.StopApplication();
        }
        var topicMetadata = topicsMetadata.Topics.Single();

        await DoWork(stoppingToken, topicMetadata);
    }

    private async Task DoWork(CancellationToken stoppingToken, TopicMetadata topicMetadata)
    {
        await Task.Delay(0, stoppingToken); // We are async now!

        var consumer = GetConsumer();
        var topicPartitions = topicMetadata.Partitions.Select(p=>new TopicPartition(topicMetadata.Topic, p.PartitionId)).ToArray();
        var topicPartitionOffsets = topicPartitions.Select(tp => new TopicPartitionOffset(tp,Offset.Beginning)).ToArray();
        var runMode = Environment.GetEnvironmentVariable(CONSUMING_WITHOUT_CG_ASSIGN_OR_SUBSCRIBE);
        if(runMode == "assign")
        {
            consumer.Assign(topicPartitionOffsets);
            logger.LogInformation("We are Assigning topics and partitions!!");
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
        else if (runMode == "subscribe")
        {
            consumer.Subscribe(topicMetadata.Topic);
            logger.LogInformation("We are Subscribing!!");
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
        else
        {
            logger.LogError($"Chonsen run mode {runMode} specified in env var {CONSUMING_WITHOUT_CG_ASSIGN_OR_SUBSCRIBE} is not supported. Shutting down");
            consumer.Close();
            hostApplicationLifetime.StopApplication();
        }
        while(!stoppingToken.IsCancellationRequested)
        {
            var result = consumer.Consume(stoppingToken);
            if(result?.IsPartitionEOF == true)
            {
                foreach(var tp in consumer.Assignment)
                {
                    logger.LogInformation($"Reached partition end of file for partition {tp.Partition}");
                    logger.LogInformation($"Current offset {consumer.PositionTopicPartitionOffset(tp)}");
                    consumer.Seek(new TopicPartitionOffset(tp,Offset.Beginning));
                    logger.LogInformation($"After reset offset is {consumer.PositionTopicPartitionOffset(tp)}");
                }
            }
            else
            {
                logger.LogInformation($"Consumed topic {result.Topic} partition {result.Partition} offset {result.Offset} with value {result.Message?.Value}");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(750), stoppingToken);
        }
        consumer.Close();
        hostApplicationLifetime.StopApplication();
    }

    private IConsumer<string, string> GetConsumer()
    {
        var consumerConfig = KafkaConfigBinder.GetConsumerConfig();
        var consumer = new ConsumerBuilder<string, string>(consumerConfig)
            .SetPartitionsAssignedHandler((c, partitions) =>
            {
                logger.LogDebug($"Starting consuming all topics from beginning");
                // When starting up, always read the topic from the beginning.
                var offsets = partitions.Select(tp => new TopicPartitionOffset(tp, Offset.Beginning));
                return offsets;
            })
            // .SetValueDeserializer(new ProtobufDeserializer<Person>(schemaRegistryClient, protobufSerializerConfig).AsSyncOverAsync())
            .SetErrorHandler((_, e) => logger.LogError($"Error: {e.Reason}"))
            .Build();
        return consumer;
    }
}
