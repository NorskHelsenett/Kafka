global using static EnvVarNames;
using BigPayloads;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSingleton<ChunkingProducer>();
builder.Services.AddScoped<ChunkConsumer>();

builder.Services.AddHostedService<ChunkedStreamConsumer>();
builder.Services.AddSingleton<OutputStateService>();

var app = builder.Build();

app.Logger.LogInformation("Registering schemas");
await KafkaSchemaRegistration.RegisterSchemasAsync();
app.Logger.LogInformation("Creating topics");
await KafkaTopicCreation.CreateTopicsAsync();
app.Logger.LogInformation("Waiting for a couple of seconds so that the Kafka cluster has time to sync topics and things");
await Task.Delay(TimeSpan.FromSeconds(5));

string GetBlobId(string nameOfOwner, string suppliedBlobName)
{
    // Don't rely on propagating externally supplied IDs (they could be user supplied :O)
    // Get 2 different checksums of name to reduce odds of ID collision to near enough zero.
    // Use checksum of users name to avoid having to deal with weird characters and stuff.
    var ownerNameChecksum = Convert.ToHexString(System.IO.Hashing.Crc32.Hash(System.Text.Encoding.UTF8.GetBytes(nameOfOwner))).ToLowerInvariant();
    var blobNameBytes = System.Text.Encoding.UTF8.GetBytes(suppliedBlobName);
    var suppliedBlobNameFirstChecksum = Convert.ToHexString(System.IO.Hashing.Crc32.Hash(blobNameBytes)).ToLowerInvariant();
    var suppliedBlobNameSecondChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(blobNameBytes)).ToLowerInvariant();
    return $"{ownerNameChecksum}.{suppliedBlobNameFirstChecksum}.{suppliedBlobNameSecondChecksum}";
}


app.MapGet("/ProduceExamplePayload", async (HttpRequest req, ChunkingProducer chunkingProducer) =>
{
    var correlationId = System.Guid.NewGuid().ToString("D");
    if(req.Headers.TryGetValue("X-Correlation-Id", out Microsoft.Extensions.Primitives.StringValues headerCorrelationId))
    {
        if(!string.IsNullOrWhiteSpace(headerCorrelationId.ToString()))
        {
            correlationId = headerCorrelationId.ToString();
        }
    }
    var suppliedBlobName = "ToDo: Example value for now";
    if(req.Headers.TryGetValue("X-Blob-Name", out Microsoft.Extensions.Primitives.StringValues headerSuppliedBlobName))
    {
        if(!string.IsNullOrWhiteSpace(headerSuppliedBlobName.ToString()))
        {
            suppliedBlobName = headerSuppliedBlobName.ToString();
        }
    }
    var cancellationToken = req.HttpContext.RequestAborted;

    var ownerId = "ToDo";
    var internalBlobId = GetBlobId(nameOfOwner: ownerId, suppliedBlobName: suppliedBlobName);

    app.Logger.LogInformation("Received request to produce example payload");
    var examplePayload = File.ReadAllText("./CatIpsum.txt");
    var examplePayloadBytes = System.Text.Encoding.UTF8.GetBytes(examplePayload);
    app.Logger.LogInformation("Sending example payload");

    await chunkingProducer.ProduceAsync(new MemoryStream(examplePayloadBytes), blobId: internalBlobId, ownerId: ownerId, callersBlobName: suppliedBlobName, correlationId: correlationId, cancellationToken);
    return Results.Ok($"Example payload produced!");
});


// curl --request POST 'https://localhost:<port>/register' --header 'Content-Type: application/json' --data-raw '{ "Name":"Samson", "Age": 23, "Country":"Nigeria" }'
// curl --request POST "https://localhost:<port>/register" --header "Content-Type: application/json" --data-raw "{ \"Name\":\"Samson\", \"Age\": 23, \"Country\":\"Nigeria\" }"
app.MapPost("/register", async (HttpRequest req, Stream body, ChunkingProducer chunkingProducer) =>
{
    var correlationId = System.Guid.NewGuid().ToString("D");
    if(req.Headers.TryGetValue("X-Correlation-Id", out Microsoft.Extensions.Primitives.StringValues headerCorrelationId))
    {
        if(!string.IsNullOrWhiteSpace(headerCorrelationId.ToString()))
        {
            correlationId = headerCorrelationId.ToString();
        }
    }
    var suppliedBlobName = "";
    if(req.Headers.TryGetValue("X-Blob-Name", out Microsoft.Extensions.Primitives.StringValues headerSuppliedBlobName))
    {
        if(!string.IsNullOrWhiteSpace(headerSuppliedBlobName.ToString()))
        {
            suppliedBlobName = headerSuppliedBlobName.ToString();
        }
    }
    var cancellationToken = req.HttpContext.RequestAborted;

    var ownerId = "ToDo";
    var internalBlobId = GetBlobId(nameOfOwner: ownerId, suppliedBlobName: suppliedBlobName);

    app.Logger.LogInformation($"CorrelationId {correlationId} Received request from \"{ownerId}\" to store blob they named \"{suppliedBlobName}\" with internal blob ID \"{internalBlobId}\"");

    var produceSuccessful = await chunkingProducer.ProduceAsync(body, blobId: internalBlobId, ownerId: ownerId, callersBlobName: suppliedBlobName, correlationId: correlationId, cancellationToken);
    if (produceSuccessful)
    {
        return Results.Ok();
    }
    return Results.StatusCode(StatusCodes.Status500InternalServerError);
});

app.MapGet("/retrievestream", async (HttpContext context, ChunkConsumer consumer, OutputStateService stateService) =>
{
    var correlationId = System.Guid.NewGuid().ToString("D");
    if(context.Request.Headers.TryGetValue("X-Correlation-Id", out Microsoft.Extensions.Primitives.StringValues headerCorrelationId))
    {
        if(!string.IsNullOrWhiteSpace(headerCorrelationId.ToString()))
        {
            correlationId = headerCorrelationId.ToString();
        }
    }
    var suppliedBlobName = "";
    if(context.Request.Headers.TryGetValue("X-Blob-Name", out Microsoft.Extensions.Primitives.StringValues headerSuppliedBlobName))
    {
        if(!string.IsNullOrWhiteSpace(headerSuppliedBlobName.ToString()))
        {
            suppliedBlobName = headerSuppliedBlobName.ToString();
        }
    }
    var cancellationToken = context.Request.HttpContext.RequestAborted;

    var ownerId = "ToDo";
    var internalBlobId = GetBlobId(nameOfOwner: ownerId, suppliedBlobName: suppliedBlobName);

    app.Logger.LogInformation($"CorrelationId {correlationId} Received request from \"{ownerId}\" for blob they named \"{suppliedBlobName}\" with internal blob ID \"{internalBlobId}\"");

    context.Response.Headers.Append("X-Correlation-Id", correlationId);

    if(!stateService.TryRetrieve(internalBlobId, out var blobChunksMetadata) || blobChunksMetadata == null)
    {
        app.Logger.LogInformation($"CorrelationId {correlationId} Received request from \"{ownerId}\" for blob they named \"{suppliedBlobName}\" with internal blob ID \"{internalBlobId}\" resulted in not found");
        return Results.NotFound();
    }

    context.Response.Headers.Append("X-Blob-Correlation-Id", blobChunksMetadata.CorrelationId);
    context.Response.Headers.Append("X-Blob-User-Supplied-Name", blobChunksMetadata.BlobName);
    context.Response.Headers.Append("X-Blob-Owner-Id", blobChunksMetadata.BlobOwnerId);
    context.Response.Headers.Append("X-Blob-Checksum", blobChunksMetadata.FinalChecksum);
    context.Response.Headers.Append("X-Blob-Checksum-Algorithm", "sha-256");
    // var contentStream = new MemoryStream();
    // await foreach(var b in consumer.GetBlobByMetadataAsync(blobChunksMetadata, cancellationToken))
    // {
    //     contentStream.WriteByte(b);
    //     // context.Response.BodyWriter.Wr(b);
    // }
    return Results.Ok(consumer.GetBlobByMetadataAsync(blobChunksMetadata, correlationId, cancellationToken));
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
