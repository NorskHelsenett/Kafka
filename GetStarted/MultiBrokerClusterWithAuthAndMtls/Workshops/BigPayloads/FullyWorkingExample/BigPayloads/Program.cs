global using static EnvVarNames;
using BigPayloads;

// Original POC

// var bigPayload = File.ReadAllText("./CatIpsum.txt");
// var bigPayloadBytes = System.Text.Encoding.UTF8.GetBytes(bigPayload);
// var bigPayloadChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.Create().ComputeHash(bigPayloadBytes));
// var bytesPerChunk = 128;
// var payloadChunks = bigPayloadBytes.Chunk(bytesPerChunk).ToArray();
// var numberOfChunks = payloadChunks.Length;
//
// var reconstructedPayloadBytes = new List<byte>();
// foreach (var chunk in payloadChunks)
// {
//     reconstructedPayloadBytes.AddRange(chunk);
// }
// var reconstructedBytes = reconstructedPayloadBytes.ToArray();
// var reconstructedChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.Create().ComputeHash(reconstructedBytes));
// var checksumsMatch = reconstructedChecksum == bigPayloadChecksum;
// var reconstructed = System.Text.Encoding.UTF8.GetString(reconstructedBytes);
// var reconstructedMatches = reconstructed == bigPayload;
//
// Console.WriteLine($"Reconstructed payload:\n\n{reconstructed}\nENDED\n");
//
// Console.WriteLine($"Checksums match: {checksumsMatch}");
// Console.WriteLine($"Payloads match: {reconstructedMatches}");
// Console.WriteLine($"Original as bytes length (bytes): {bigPayloadBytes.Length}");
// Console.WriteLine($"Chunk size (bytes): {bytesPerChunk}");
// Console.WriteLine($"Number of chunks: {numberOfChunks}");

// END Original POC
// BEGIN New kafkaesque existence

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
