This is a project to demo how to use kafka topics with an avro schema in dotnet (both consuming and producing).

Avro, like protobuf, is in dotnet based on compiling the the schema definition to C# files you can use together with the rest of your code.
In avro land, this is called using a specific schema.
To do this, first install the dotnet tool `Apache.Avro.Tools` by running this command `dotnet tool install --global Apache.Avro.Tools`.
Then you can create your `.avsc` schema (or download it from the schema registriy).
Once you've obtained your schema definition, you can compile it by running `avrogen -s ExampleSchemaDefinition.avsc . --namespace "avro.namespace:csharp.namespace"`.
This should produce a `ExampleSchemaDefinition.cs` you can reference from your code.

With avro, you can also compose messages on the fly, without pre-baking the schema.
The avronauts call this using a generic schema.
This works much the same as if you'd use dotnet's expando objects to create json payload.

Confluents examples:
- Avro Specific: https://github.com/confluentinc/confluent-kafka-dotnet/tree/master/examples/AvroSpecific
- Avro Generic: https://github.com/confluentinc/confluent-kafka-dotnet/tree/master/examples/AvroGeneric

# Follow along setup

```sh
dotnet new web --name "AvroExample" --output .
dotnet add package "Confluent.Kafka"
dotnet add package "Confluent.SchemaRegistry.Serdes.Avro"
# dotnet add package "Apache.Avro"
```

NB! The project name cannot be "Avro", because that causes all kinds of nasty collisions with the avro packages.

Basic kafka background job

```cs
public class KafkaProducer : BackgroundService
{
    private readonly ILogger<KafkaProducer> _logger;

    public KafkaProducer(ILogger<KafkaProducer> logger)
    {
        _logger = logger;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{nameof(KafkaProducer)} performing blocking work before startup.");
        await DoWork(stoppingToken);
    }

    private async Task DoWork(CancellationToken stoppingToken)
    {
        throw new NotImplementedException();
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        // Perform gracefull shutdown, i.e. producer flush queued events to kafka, consumer close connection so that offsets are stored and groups left.
        throw new NotImplementedException();
    }
}
```

```sh
avrogen -s Person.avsc . --namespace "no.nhn.examples.serialization.avro:GeneratedSchemaTypes" --skip-directories
```
