global using static EnvVarNames;
using BigPayloads;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSingleton<ChunkingProducer>();
builder.Services.AddHostedService<ChunkedStreamConsumer>();

var app = builder.Build();

app.Logger.LogInformation("Registering schemas");
await KafkaSchemaRegistration.RegisterSchemasAsync();
app.Logger.LogInformation("Creating topics");
await KafkaTopicCreation.CreateTopicsAsync();
app.Logger.LogInformation("Waiting for a couple of seconds so that the Kafka cluster has time to sync topics and things");
await Task.Delay(TimeSpan.FromSeconds(5));


app.MapGet("/ProduceExamplePayload", async (ChunkingProducer chunkingProducer) =>
{
    app.Logger.LogInformation("Received request to produce example payload");
    var examplePayload = File.ReadAllText("./CatIpsum.txt");
    var examplePayloadBytes = System.Text.Encoding.UTF8.GetBytes(examplePayload);
    app.Logger.LogInformation("Sending example payload");
    await chunkingProducer.ProduceChunkedAsync(examplePayloadBytes);
    return Results.Ok($"Example payload produced!");
});

app.Run();
