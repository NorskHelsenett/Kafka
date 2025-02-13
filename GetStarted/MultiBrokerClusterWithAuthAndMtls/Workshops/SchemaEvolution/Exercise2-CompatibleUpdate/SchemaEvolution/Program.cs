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
throw new NotImplementedException("Create a new backwards compatible schema and put it's path here");
var secondSchemaAsString = File.ReadAllText("./Protos/your-new-version-here.proto");

await SchemaRegistryUpdater.RegisterSchema(secondSchemaAsString, firstTopicSchemaSubject); // Note that we reuse subject, because it's tied to the topic
Console.WriteLine("Registered schema V1.1 with the schema registry, you can check it out at http://localhost:8083");

throw new NotImplementedException("Use your newly created backwards compatible schema make and send a payload");
