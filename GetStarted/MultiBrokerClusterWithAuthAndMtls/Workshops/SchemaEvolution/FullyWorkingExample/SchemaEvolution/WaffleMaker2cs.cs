using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Namespace.Waffle.V2;

namespace SchemaEvolution.Protos;

public static class WaffleMaker2
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
                Id = $"waffle v2 id {i}",
                Kind = random.NextSingle() < 0.5 ? Kind.Normal : Kind.Belgian,
                ServingTemperature = (ServingTemperature) random.Next(0, 3),
                Condiments = {  (Condiment) random.Next(0, 3), (Condiment) random.Next(0, 7) }
            };
            var kafkaMessage = new Message<string, Waffle>{ Key = $"keywaffle v2 {i}", Value = nextWaffle };
            _ = await waffleProducer.ProduceAsync(topicName, kafkaMessage);
            Console.WriteLine($"V2 waffle is now consumable at topic {topicName}!");
        }
    }
}
