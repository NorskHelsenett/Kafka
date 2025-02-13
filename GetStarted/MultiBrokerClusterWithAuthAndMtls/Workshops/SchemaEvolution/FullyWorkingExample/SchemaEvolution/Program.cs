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

Console.WriteLine("Registering second schema");
var secondSchemaAsString = File.ReadAllText("./Protos/WaffleV1.1.proto");
await SchemaRegistryUpdater.RegisterSchema(secondSchemaAsString, firstTopicSchemaSubject); // Note that we reuse subject, because it's tied to the topic
Console.WriteLine("Registered schema V1.1 with the schema registry, you can check it out at http://localhost:8083");

await WaffleMaker1_1.ProduceWaffles(topicName);

var topicV2Name = "workshop-schema-evolution-topic-v2";

Console.WriteLine("Creating second topic");
await KafkaTopicCreation.TryCreateTopic(topicV2Name);
Console.WriteLine($"Created v2 topic {topicV2Name}");

Console.WriteLine("Registering third schema");
var thirdSchemaAsString = File.ReadAllText("./Protos/WaffleV2.proto");
var secondTopicSchemaSubject = $"{topicV2Name}-value";
await SchemaRegistryUpdater.RegisterSchema(thirdSchemaAsString, secondTopicSchemaSubject);
Console.WriteLine("Registered schema V2 with the schema registry, you can check it out at http://localhost:8083");

await WaffleMaker2.ProduceWaffles(topicV2Name);
