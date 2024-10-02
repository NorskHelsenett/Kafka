global using static EnvVarNames;
using BigPayloads;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSingleton<ChunkingProducer>();
builder.Services.AddHostedService<ChunkedStreamConsumer>();

Console.WriteLine("Registering schemas");
await KafkaSchemaRegistration.RegisterSchemasAsync();
Console.WriteLine("Creating topics");
await KafkaTopicCreation.CreateTopicsAsync();
Console.WriteLine("Waiting for a couple of seconds so that the Kafka cluster has time to sync topics and things");
await Task.Delay(TimeSpan.FromSeconds(5));

var app = builder.Build();

app.MapGet("/", () => Results.Ok("Hello World!"));

app.MapGet("/ProduceExamplePayload", async (ChunkingProducer chunkingProducer) =>
{
    Console.WriteLine("Received request to produce example payload");
    var examplePayload = File.ReadAllText("./CatIpsum.txt");
    var examplePayloadBytes = System.Text.Encoding.UTF8.GetBytes(examplePayload);
    Console.WriteLine("Sending example payload");
    await chunkingProducer.ProduceChunkedAsync(examplePayloadBytes);
    return Results.Ok($"Example payload produced!");
});
// app.Urls.Add("http://*:8080");
app.Run();
