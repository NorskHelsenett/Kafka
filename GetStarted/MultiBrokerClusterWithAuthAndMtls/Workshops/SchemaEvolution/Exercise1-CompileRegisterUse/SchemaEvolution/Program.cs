global using static EnvVarNames;
using SchemaEvolution;
using SchemaEvolution.Protos;

var topicName = "workshop-schema-evolution-topic-v1";

Console.WriteLine("Creating first topic");
await KafkaTopicCreation.TryCreateTopic(topicName);
Console.WriteLine($"Created v1 topic {topicName}");

Console.WriteLine("Registering first schema");
var firstSchemaAsString = File.ReadAllText("./Protos/Waffle.proto");
var firstTopicSchemaSubject = $"{topicName}-value";
await SchemaRegistryUpdater.RegisterSchema(firstSchemaAsString, firstTopicSchemaSubject);
Console.WriteLine("Registered schema V1 with the schema registry, you can check it out at http://localhost:8083");

await WaffleMaker.ProduceWaffles(topicName);
