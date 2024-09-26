using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Namespace.Waffle.V1_1;

namespace SchemaEvolution.Protos;

public static class WaffleMaker1_1
{
    public static async Task ProduceWaffles(string topicName, int numberOfWaffles = 5)
    {
        var random = new Random(Seed:0);
        var protobufSerializerConfig = new ProtobufSerializerConfig
        {
            AutoRegisterSchemas = false,
            UseLatestVersion = true
        };
        var schemaRegistry = new CachedSchemaRegistryClient(KafkaConfigBinder.GetSchemaRegistryConfig());
        var waffleProducer =  new ProducerBuilder<string, Waffle>(KafkaConfigBinder.GetProducerConfig())
            .SetValueSerializer(new ProtobufSerializer<Waffle>(schemaRegistry, protobufSerializerConfig))
            .Build();

        Console.WriteLine("Producing waffles");
        for (int i = 0; i < numberOfWaffles; i++)
        {
            var nextWaffle = new Waffle
            {
                Id = $"waffle v1.1 id {i}",
                Kind = random.NextSingle() < 0.5 ? Kind.Normal : Kind.Belgian,
                Condiments = {  (Condiments) random.Next(0, 3), (Condiments) random.Next(0, 4) }
            };
            var kafkaMessage = new Message<string, Waffle>{ Key = $"keywaffle v1.1 {i}", Value = nextWaffle };
            _ = await waffleProducer.ProduceAsync(topicName, kafkaMessage);
            Console.WriteLine($"V1.1 waffle ready for consumption at topic {topicName}!");
        }
    }
}
