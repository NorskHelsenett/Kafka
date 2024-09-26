using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Namespace.Waffle;

namespace SchemaEvolution;

public static class WaffleMaker
{
    public static async Task ProduceWaffles(string topicName, int numberOfWaffles = 5)
    {
        var random = new Random(Seed:0);
        var protobufSerializerConfig = new ProtobufSerializerConfig
        {
            AutoRegisterSchemas = false,
            NormalizeSchemas = true,
            UseLatestVersion = true
        };
        var schemaRegistry = new CachedSchemaRegistryClient(KafkaConfigBinder.GetSchemaRegistryConfig());
        var waffleProducer =  new ProducerBuilder<string, Waffle>(KafkaConfigBinder.GetProducerConfig())
            .SetValueSerializer(new ProtobufSerializer<Waffle>(schemaRegistry, protobufSerializerConfig))
            .Build();

        Console.WriteLine("Making V1 waffles");
        for (int i = 0; i < numberOfWaffles; i++)
        {
            var nextWaffle = new Waffle
            {
                Id = $"waffle id {i}",
                Kind = random.NextSingle() < 0.5 ? Kind.Normal : Kind.Belgian,
                Condiments = {  (Condiments) random.Next(0, 3), (Condiments) random.Next(0, 3) }
            };
            var kafkaMessage = new Message<string, Waffle>{ Key = $"keywaffle {i}", Value = nextWaffle };
            _ = await waffleProducer.ProduceAsync(topicName, kafkaMessage);
            Console.WriteLine($"Waffle made and shipped to topic {topicName}!");
        }
    }
}
