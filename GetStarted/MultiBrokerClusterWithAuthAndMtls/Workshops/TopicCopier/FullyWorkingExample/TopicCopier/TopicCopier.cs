using Confluent.Kafka;
using System.Text;

public class TopicCopier : BackgroundService
{
    private readonly ILogger<TopicCopier> _logger;
    private readonly KafkaConfigSource _kafkaConfigSource;
    private readonly KafkaConfigDestination _kafkaConfigDestination;
    private readonly IConsumer<byte[], byte[]> _consumer;
    private readonly IProducer<byte[], byte[]> _producer;
    private readonly byte[] _magicBytesDestinationValue;

    public TopicCopier(ILogger<TopicCopier> logger, KafkaConfigSource kafkaConfigSource, KafkaConfigDestination kafkaConfigDestination)
    {
        _logger = logger;
        _kafkaConfigSource = kafkaConfigSource;
        _kafkaConfigDestination = kafkaConfigDestination;
        _consumer = GetConsumer(kafkaConfigSource);
        _producer = GetProducer(kafkaConfigDestination);
        var cancellationToken = new CancellationTokenSource().Token;
        var httpClient = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator });
        _magicBytesDestinationValue = GetMagicBytesForTopicValue(
            kafkaConfigDestination.DestinationTopic,
            kafkaConfigDestination.SchemaRegistryAddress,
            httpClient,
            cancellationToken).Result;
        _logger.LogInformation("magicBytes: "+ Convert.ToHexString(_magicBytesDestinationValue));

        foreach(byte b in _magicBytesDestinationValue){
            _logger.LogInformation(Convert.ToHexString([b]));
        }


    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{nameof(TopicCopier)} performing blocking work before startup.");
        await DoWork(stoppingToken);
    }

    private async Task DoWork(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{nameof(TopicCopier)} Starting main consume loop");
        try
        {
            while (true)
            {
                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);
                    if (consumeResult == null)
                    {
                        _logger.LogInformation($"{nameof(TopicCopier)} Reached end of topic/consume result was null, sleeping and trying again");
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        continue;
                    }
                    if (consumeResult.Offset.Value % 1024 == 0)
                    {
                        _logger.LogInformation(new StringBuilder()
                            .Append($"{nameof(TopicCopier)} processed 1024 offsets. Current status is:")
                            .Append("\n\t").Append($"Current offset: {consumeResult.Offset.Value}")
                            .Append("\n\t").Append($"Current event timestamp: {consumeResult.Message.Timestamp.UtcDateTime:u}")
                            .ToString());
                    }

                    // Always strip magic bytes from source, because there is no way to know if destination is different cluster with different schema registry.
                    var messageCleaned = GetMessageFromResult(consumeResult, stoppingToken);

                    SendMessageToDestinations(messageCleaned, stoppingToken);
                }
                catch (ConsumeException e)
                {
                    _logger.LogError(e, $"Consume error: {e.Error.Reason}");
                }
            }
        }
        catch (Exception) { }
        finally
        {
            _consumer.Close();
            _producer.Flush(stoppingToken);
        }
    }

    private void SendMessageToDestinations(Message<byte[], byte[]> consumeResult, CancellationToken stoppingToken)
    {
        var messagePayload = new List<byte>();
        messagePayload.AddRange(_magicBytesDestinationValue);
        messagePayload.AddRange(consumeResult.Value);
        var message = new Message<byte[], byte[]>
            {
                Key = consumeResult.Key,
                Value = messagePayload.ToArray(),
                Headers = consumeResult.Headers
            };
        // Perform the more laborious producer queue full check here, because raw byte could go so fast we get fastness issues
        var notSent = true;
        while (notSent && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                _producer.Produce(_kafkaConfigDestination.DestinationTopic,message);
                notSent = false;
            }
            catch (ProduceException<byte[], byte[]> ex)
            {
                if (!ex.Message.Contains("Queue full"))
                {
                    throw;
                }

                _logger.LogWarning("Producer Queue is full, sleeping and retrying");
                Thread.Sleep(TimeSpan.FromSeconds(3));
            }
        }
    }

    private Message<byte[], byte[]> GetMessageFromResult(ConsumeResult<byte[], byte[]> consumeResult, CancellationToken stoppingToken)
    {
        if (consumeResult.Message.Value == null)
        {
            return new Message<byte[], byte[]>
            {
                Headers = consumeResult.Message.Headers,
                Key = consumeResult.Message.Key,
#pragma warning disable CS8601 // Possible null reference assignment.
                Value = consumeResult.Message.Value
#pragma warning restore CS8601 // Possible null reference assignment.
            };
        }
        return new Message<byte[], byte[]>
        {
            Headers = consumeResult.Message.Headers,
            Key = consumeResult.Message.Key,
            Value = _kafkaConfigSource.OriginalTopicHasSchema ? consumeResult.Message.Value[5..] : consumeResult.Message.Value
        };
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        // Perform gracefull shutdown, i.e. producer flush queued events to kafka, consumer close connection so that offsets are stored and groups left.
        _consumer.Close();
        _producer.Flush(stoppingToken);
        await base.StopAsync(stoppingToken);
    }

    private IConsumer<byte[], byte[]> GetConsumer(KafkaConfigSource kafkaConfig)
    {
        var config = new ConsumerConfig()
        {
            BootstrapServers = kafkaConfig.BootstrapServers,
            GroupId = kafkaConfig.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
        };
        if (kafkaConfig.SecurityProtocol?.ToLowerInvariant() == "ssl")
        {
            _logger.LogInformation($"Setting up SSL as auth for kafka consumer");
            config.SecurityProtocol = SecurityProtocol.Ssl;

            if (string.IsNullOrEmpty(kafkaConfig.SslCaPemLocation) || string.IsNullOrEmpty(kafkaConfig.SslCertificatePemLocation) || string.IsNullOrEmpty(kafkaConfig.SslKeyPemLocation) || string.IsNullOrEmpty(kafkaConfig.SslKeyPasswordLocation))
            {
                throw new Exception("When running consumer in SSL mode, all of the certificate locations have to be supplied");
            }

            config.SslCaPem = File.ReadAllText(kafkaConfig.SslCaPemLocation);
            config.SslCertificatePem = File.ReadAllText(kafkaConfig.SslCertificatePemLocation);
            config.SslKeyPem = File.ReadAllText(kafkaConfig.SslKeyPemLocation);
            config.SslKeyPassword = File.ReadAllLines(kafkaConfig.SslKeyPasswordLocation).First();

            _logger.LogInformation($"Kafka consumer config for SSL Auth set up. Using the following values for the Broker CA certificate (public info) and client certificate(public info)\nBroker CA Cert:\n{File.ReadAllText(kafkaConfig.SslCaPemLocation)}\nUser Cert:\n{File.ReadAllText(kafkaConfig.SslCertificatePemLocation)}");
        }
        else if (kafkaConfig.SecurityProtocol?.ToLowerInvariant() == "plaintext")
        {
            _logger.LogInformation("Setting up consumer to use Plaintext connection (are you connecting to the old Azure cluster?)");
            config.SecurityProtocol = SecurityProtocol.Plaintext;
        }
        else
        {
            throw new NotImplementedException($"Unsupported kafka security protocol \"{kafkaConfig.SecurityProtocol}\" supplied");
        }
        var consumer = new ConsumerBuilder<byte[], byte[]>(config)
            // .SetPartitionsAssignedHandler((c, partitions) =>
            // {
            //     // Read entire topic form start at every startup unless something else is specified
            //     var offsets = partitions.Select(tp => new TopicPartitionOffset(tp, Offset.Beginning));
            //     return offsets;
            // })
            .Build();
        consumer.Subscribe(kafkaConfig.SourceTopic);
        return consumer;
    }

    private IProducer<byte[], byte[]> GetProducer(KafkaConfigDestination kafkaConfig)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = kafkaConfig.BootstrapServers,
            Acks = Acks.Leader,
            MessageMaxBytes = kafkaConfig.MaxMessageBytes,
        };
        if (kafkaConfig.SecurityProtocol?.ToLowerInvariant() == "ssl")
        {
            _logger.LogInformation($"Setting up SSL as auth for kafka producer");
            config.SecurityProtocol = SecurityProtocol.Ssl;

            if (string.IsNullOrEmpty(kafkaConfig.SslCaPemLocation) || string.IsNullOrEmpty(kafkaConfig.SslCertificatePemLocation) || string.IsNullOrEmpty(kafkaConfig.SslKeyPemLocation) || string.IsNullOrEmpty(kafkaConfig.SslKeyPasswordLocation))
            {
                throw new Exception("When running producer in SSL mode, all of the certificate locations have to be supplied");
            }

            config.SslCaPem = File.ReadAllText(kafkaConfig.SslCaPemLocation);
            config.SslCertificatePem = File.ReadAllText(kafkaConfig.SslCertificatePemLocation);
            config.SslKeyPem = File.ReadAllText(kafkaConfig.SslKeyPemLocation);
            config.SslKeyPassword = File.ReadAllLines(kafkaConfig.SslKeyPasswordLocation).First();

            _logger.LogInformation($"Kafka producer config for SSL Auth set up. Using the following values for the Broker CA certificate (public info) and client certificate(public info)\nBroker CA Cert:\n{File.ReadAllText(kafkaConfig.SslCaPemLocation)}\nUser Cert:\n{File.ReadAllText(kafkaConfig.SslCertificatePemLocation)}");
        }
        else if (kafkaConfig.SecurityProtocol?.ToLowerInvariant() == "plaintext")
        {
            _logger.LogInformation("Setting up producer to use Plaintext connection (are you connecting to the old Azure cluster?)");
            config.SecurityProtocol = SecurityProtocol.Plaintext;
        }
        else
        {
            throw new NotImplementedException($"Unsupported kafka security protocol \"{kafkaConfig.SecurityProtocol}\" supplied");
        }
        return new ProducerBuilder<byte[], byte[]>(config).Build();
    }

    private async Task<byte[]> GetMagicBytesForTopicValue(string topicName, string? schemaRegistryAddress, HttpClient httpClient, CancellationToken cancellationToken)
    {
        if (schemaRegistryAddress == null)
        {
            return [];
        }
        var latestVersionAddress = $"{schemaRegistryAddress}/subjects/{topicName}-value/versions/latest";
        var uri = new Uri(latestVersionAddress);
        var httpReq = new HttpRequestMessage(HttpMethod.Get, uri);

        try
        {
            var response = await httpClient.SendAsync(httpReq, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var stringContent = await response.Content.ReadAsStringAsync(cancellationToken);
                // ToDo: Deserialize as json and access field "id", convert to number, represent as 4 bytes big endian
                var schema = System.Text.Json.JsonSerializer.Deserialize<Schema>(stringContent);
                if(schema?.id is null ){
                    throw new NullReferenceException("response does not contain known values");
                }
                int idRaw = schema.id;
                byte[] idBytes = BitConverter.GetBytes(idRaw);
                _logger.LogInformation("before reverse: "+Convert.ToHexString(idBytes));

                if (BitConverter.IsLittleEndian)
                {
                    _logger.LogInformation("Doing reverse");
                    Array.Reverse(idBytes);

                }
                _logger.LogInformation("After reverse: "+Convert.ToHexString(idBytes));

                var magicBytes = new List<byte>();
                magicBytes.Add(0x00);
                magicBytes.AddRange(idBytes);
                magicBytes.AddRange([0x00, 0x00, 0x00, 0x00]); // In case idBytes is anything less than 4 long
                var result = magicBytes[..5];
                return result.ToArray();
            }

            _logger.LogWarning($"Response from Schema registry \"{latestVersionAddress}\" when retrieving data about schema for values for topic {topicName} was not successful, status code {response.StatusCode}, reason \"{response.ReasonPhrase}\"");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Got exception while retrieving data about schema for values for topic {topicName} from schema registry endpoint  \"{latestVersionAddress}\"");
        }

        return [];
    }
}

internal record Schema
{
    public int id { get; set; }
}
