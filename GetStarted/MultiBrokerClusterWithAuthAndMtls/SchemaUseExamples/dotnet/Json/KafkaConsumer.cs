using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

public class KafkaConsumer : BackgroundService
{
    private readonly ILogger<KafkaConsumer> _logger;
    private readonly ConsumedState _consumedState;
    private readonly IConsumer<string, Person> _consumer;
    private readonly string _topic;

    public KafkaConsumer(ILogger<KafkaConsumer> logger, ConsumedState consumedState)
    {
        _logger = logger;
        _consumedState = consumedState;
        _consumer = GetConsumer();

        var topicName = Environment.GetEnvironmentVariable("PERSONS_JSON");
        if(string.IsNullOrEmpty(topicName))
        {
            topicName = "persons-json";
        }
        _topic = topicName;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{nameof(KafkaConsumer)} performing blocking work before startup.");
        await DoWork(stoppingToken);
    }

    private async Task DoWork(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), stoppingToken); // ToDo: Remove if we end up making this truly async
            _consumer.Subscribe(_topic);
            while (true)
            {
                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);
                    var person = consumeResult.Message.Value;
                    _consumedState.LastProcessedPerson = person;
                    _logger.LogInformation($"Processed person {System.Text.Json.JsonSerializer.Serialize(person)}");
                }
                catch (ConsumeException e)
                {
                    _logger.LogError(e, $"Consume error: {e.Error.Reason}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _consumer.Close();
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        // Perform gracefull shutdown, i.e. producer flush queued events to kafka, consumer close connection so that offsets are stored and groups left.
        _consumer.Close();
        await base.StopAsync(stoppingToken);
    }

    private IConsumer<string, Person> GetConsumer()
    {
        var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS");
        if(string.IsNullOrEmpty(bootstrapServers))
        {
            bootstrapServers = "localhost:9094,localhost:9095,localhost:9096";
        }
        var sslCaPemLocation = Environment.GetEnvironmentVariable("KAFKA_SSL_CA_PEM_LOCATION");
        if(string.IsNullOrEmpty(sslCaPemLocation))
        {
            sslCaPemLocation = "../../../ContainerData/GeneratedCerts/Kafka/Users/json-user/ca.crt";
        }
        var sslCertLocation = Environment.GetEnvironmentVariable("KAFKA_SSL_CERTIFICATE_LOCATION");
        if(string.IsNullOrEmpty(sslCertLocation))
        {
            sslCertLocation = "../../../ContainerData/GeneratedCerts/Kafka/Users/json-user/acl-principal.crt";
        }
        var sslKeyLocation = Environment.GetEnvironmentVariable("KAFKA_SSL_KEY_LOCATION");
        if(string.IsNullOrEmpty(sslKeyLocation))
        {
            sslKeyLocation = "../../../ContainerData/GeneratedCerts/Kafka/Users/json-user/acl-principal.key";
        }
        var sslKeyPasswordLocation = Environment.GetEnvironmentVariable("KAFKA_SSL_KEY_PASSWORD_LOCATION");
        if(string.IsNullOrEmpty(sslKeyPasswordLocation))
        {
            sslKeyPasswordLocation = "../../../ContainerData/GeneratedCerts/Kafka/Users/json-user/password.txt";
        }
        var consumerGroup = Environment.GetEnvironmentVariable("KAFKA_GROUP_ID");
        if(string.IsNullOrEmpty(consumerGroup))
        {
            consumerGroup = "persons-json-group";
        }

        // KAFKA_GROUP_ID
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = consumerGroup,
            AllowAutoCreateTopics = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaPem = File.ReadAllText(sslCaPemLocation),
            SslCertificatePem = File.ReadAllText(sslCertLocation),
            SslKeyPem = File.ReadAllText(sslKeyLocation),
            SslKeyPassword = File.ReadAllLines(sslKeyPasswordLocation).First(),
        };

        var schemaRegistryUrl = Environment.GetEnvironmentVariable("KAFKA_SCHEMA_REGISTRY_ADDRESS");
        if(string.IsNullOrEmpty(schemaRegistryUrl))
        {
            schemaRegistryUrl = "http://localhost:8083/apis/ccompat/v7";
        }
        var schemaRegistryConfig = new SchemaRegistryConfig
        {
            // Note: you can specify more than one schema registry url using the
            // schema.registry.url property for redundancy (comma separated list).
            // The property name is not plural to follow the convention set by
            // the Java implementation.
            Url = schemaRegistryUrl
        };

        var jsonSerializerConfig = new JsonSerializerConfig
        {
            // optional json serializer properties:
            BufferBytes = 100
        };

        var schemaRegistry = new CachedSchemaRegistryClient(schemaRegistryConfig);

        return new ConsumerBuilder<string, Person>(consumerConfig)
            .SetValueDeserializer(new JsonDeserializer<Person>().AsSyncOverAsync())
            .SetErrorHandler((_, e) => _logger.LogError($"Error: {e.Reason}"))
            // .SetPartitionsAssignedHandler((c, partitions) =>
            // {
            //     // Read entire topic form start at every startup
            //     var offsets = partitions.Select(tp => new TopicPartitionOffset(tp, Offset.Beginning));
            //     return offsets;
            // })
            .Build();
    }
}
