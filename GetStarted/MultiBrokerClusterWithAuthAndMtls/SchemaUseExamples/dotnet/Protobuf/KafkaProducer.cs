using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

public class KafkaProducer
{
    private readonly ILogger<KafkaProducer> _logger;
    private readonly IProducer<string,Person> _producer;
    private readonly string _topic;

    public KafkaProducer(ILogger<KafkaProducer> logger)
    {
        _logger = logger;
        _producer = GetProducer();

        var topicName = Environment.GetEnvironmentVariable("PERSONS_PROTOBUF");
        if(string.IsNullOrEmpty(topicName))
        {
            topicName = "persons-protobuf";
        }
        _topic = topicName;
    }

    public bool Produce(Person person)
    {
        try
        {
            var message = new Message<string, Person>
            {
                Key = person.Id,
                Value = person
            };
            _producer.ProduceAsync(_topic, message);
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, $"Got exception when producing person with ID {person.Id} to topic {_topic}");
            return false;
        }
        return true;
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        // Because finalizers are not necessarily called on program exit in newer dotnet:
        // https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/finalizers
        // Could maybe be handled by making this a BackgroundService and using the provided shutdown handling there,
        // but then again this is not really for doing long running background work.
        _logger.LogInformation("Kafka producer process exit event triggered.");
        try
        {
            _producer.Flush();
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Kafka producer got exception while flushing during process termination");
        }
    }

    private IProducer<string, Person> GetProducer()
    {
        var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS");
        if(string.IsNullOrEmpty(bootstrapServers))
        {
            bootstrapServers = "localhost:9094,localhost:9095,localhost:9096";
        }
        var sslCaPemLocation = Environment.GetEnvironmentVariable("KAFKA_SSL_CA_PEM_LOCATION");
        if(string.IsNullOrEmpty(sslCaPemLocation))
        {
            sslCaPemLocation = "../../../ContainerData/GeneratedCerts/Kafka/Users/protobuf-user/ca.crt";
        }
        var sslCertLocation = Environment.GetEnvironmentVariable("KAFKA_SSL_CERTIFICATE_LOCATION");
        if(string.IsNullOrEmpty(sslCertLocation))
        {
            sslCertLocation = "../../../ContainerData/GeneratedCerts/Kafka/Users/protobuf-user/acl-principal.crt";
        }
        var sslKeyLocation = Environment.GetEnvironmentVariable("KAFKA_SSL_KEY_LOCATION");
        if(string.IsNullOrEmpty(sslKeyLocation))
        {
            sslKeyLocation = "../../../ContainerData/GeneratedCerts/Kafka/Users/protobuf-user/acl-principal.key";
        }
        var sslKeyPasswordtLocation = Environment.GetEnvironmentVariable("KAFKA_SSL_KEY_PASSWORD_LOCATION");
        if(string.IsNullOrEmpty(sslKeyPasswordtLocation))
        {
            sslKeyPasswordtLocation = "../../../ContainerData/GeneratedCerts/Kafka/Users/protobuf-user/password.txt";
        }
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            // MessageSendMaxRetries = 10,
            Acks = Acks.All,
            SecurityProtocol = SecurityProtocol.Ssl,
            SslCaPem = File.ReadAllText(sslCaPemLocation),
            SslCertificatePem = File.ReadAllText(sslCertLocation),
            SslKeyPem = File.ReadAllText(sslKeyLocation),
            SslKeyPassword = File.ReadAllText(sslKeyPasswordtLocation),
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

        var protobufSerializerConfig = new ProtobufSerializerConfig
        {
            // optional Protobuf serializer properties:
            BufferBytes = 100
        };

        var schemaRegistry = new CachedSchemaRegistryClient(schemaRegistryConfig);

        return new ProducerBuilder<string, Person>(producerConfig)
            .SetValueSerializer(new ProtobufSerializer<Person>(schemaRegistry, protobufSerializerConfig))
            .Build();
    }
}
