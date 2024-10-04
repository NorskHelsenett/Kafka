global using static EnvVarNames;
using System.Threading.Channels;
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

app.MapGet("/ProduceExamplePayload", async (HttpRequest req, ChunkingProducer chunkingProducer) =>
{
    app.Logger.LogInformation("Received request to produce example payload");
    var examplePayload = File.ReadAllText("./CatIpsum.txt");
    var examplePayloadBytes = System.Text.Encoding.UTF8.GetBytes(examplePayload);
    app.Logger.LogInformation("Sending example payload");
    var cancellationToken = req.HttpContext.RequestAborted;
    await chunkingProducer.ProduceAsync(new MemoryStream(examplePayloadBytes), "example blob id", cancellationToken);
    return Results.Ok($"Example payload produced!");
});


// curl --request POST 'https://localhost:<port>/register' --header 'Content-Type: application/json' --data-raw '{ "Name":"Samson", "Age": 23, "Country":"Nigeria" }'
// curl --request POST "https://localhost:<port>/register" --header "Content-Type: application/json" --data-raw "{ \"Name\":\"Samson\", \"Age\": 23, \"Country\":\"Nigeria\" }"
app.MapPost("/register", async (HttpRequest req, Stream body,
    ChunkingProducer chunkingProducer) =>
{
    var cancellationToken = req.HttpContext.RequestAborted;
    var produceSuccessful = await chunkingProducer.ProduceAsync(body, "ID: ToDo", cancellationToken);
    if(produceSuccessful) return Results.Ok();
    return Results.StatusCode(StatusCodes.Status500InternalServerError);
});



/* ToDo:
 * - Add submit custom payload endpoint
 *   - Figure out if specific format demanded, or just b64. Maybe try out file upload?
 * - Add retrieve all stored objects overview endpoint
 *   - Just store overview in dict
 * - Add retrieve individual blobs endpoint
 *   - This is point blob retrieval is done
 */

app.Run();
