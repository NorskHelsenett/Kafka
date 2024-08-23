global using static EnvVarNames;
using System.Net;
using System.Text;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSingleton<OutputStateService>();
builder.Services.AddHostedService<KafkaConsumerService>();
builder.Services.AddSingleton<KafkaProducerService>();

var app = builder.Build();

app.MapPost("/store", async (HttpContext http, ApiParamPerson inputPerson, KafkaProducerService kafkaProducerService) =>
{
    var correlationId = System.Guid.NewGuid().ToString("D");
    if(http.Request.Headers.TryGetValue("X-Correlation-Id", out Microsoft.Extensions.Primitives.StringValues value))
    {
        if(!string.IsNullOrWhiteSpace(value.ToString()))
        {
            correlationId = value.ToString();
        }
    }

    http.Response.Headers.Append("X-Correlation-Id", correlationId);

    var protobufPerson = new Person
    {
        Id = inputPerson.Id,
        Name = new PersonName {
            Given = inputPerson.Name.Given,
            Family = inputPerson.Name.Family
        }
    };
    if(inputPerson.Tags?.Count != 0)
    {
        protobufPerson.Tags.Add(inputPerson.Tags);
    }

    Dictionary<string, byte[]> kafkaEventHeaders = [];

    var produceSuccess = await kafkaProducerService.Produce(inputPerson.Id.GetUtf8Bytes(), protobufPerson, kafkaEventHeaders, correlationId);
    if(produceSuccess)
    {
        return Results.Ok($"Stored");
    }
    return Results.Text(
        content: $"Storage failed",
        contentType: "text/html",
        contentEncoding: Encoding.UTF8,
        statusCode: (int?) HttpStatusCode.InternalServerError);
});
app.MapPost("/retrieve", (HttpContext http, ApiParamRetrievePerson postContent, OutputStateService outputStateService) =>
{
    var correlationId = System.Guid.NewGuid().ToString("D");
    if(http.Request.Headers.TryGetValue("X-Correlation-Id", out Microsoft.Extensions.Primitives.StringValues value))
    {
        if(!string.IsNullOrWhiteSpace(value.ToString()))
        {
            correlationId = value.ToString();
        }
    }
    var returnValue = string.Empty;

    var key = postContent.Id;
    if(outputStateService.TryRetrieve(key, out var retrieveResult))
    {
        returnValue = retrieveResult.Value;
        correlationId = retrieveResult.CorrelationId;
    }

    http.Response.Headers.Append("X-Correlation-Id", correlationId);
    return Results.Text(returnValue);
});
app.MapPost("/remove", async (HttpContext http, ApiParamRetrievePerson postContent, KafkaProducerService kafkaProducerService) =>
{
    var correlationId = System.Guid.NewGuid().ToString("D");
    if(http.Request.Headers.TryGetValue("X-Correlation-Id", out Microsoft.Extensions.Primitives.StringValues value))
    {
        if(!string.IsNullOrWhiteSpace(value.ToString()))
        {
            correlationId = value.ToString();
        }
    }
    http.Response.Headers.Append("X-Correlation-Id", correlationId);

    var eventKeyBytes = postContent.Id.GetUtf8Bytes();

    Dictionary<string, byte[]> headers = [];
    headers["Correlation-Id"] = System.Text.Encoding.UTF8.GetBytes(correlationId);

    var produceSuccess = await kafkaProducerService.Produce(eventKeyBytes, null, headers, correlationId);
    if(produceSuccess)
    {
        return Results.Ok($"Removed");
    }
    return Results.Text(
        content: $"Removal failed",
        contentType: "text/html",
        contentEncoding: Encoding.UTF8,
        statusCode: (int?) HttpStatusCode.InternalServerError);
});

app.MapGet("/healthz", () => Results.Ok("Started successfully"));
app.MapGet("/healthz/live", () => Results.Ok("Alive and well"));
app.MapGet("/healthz/ready", () => Results.Ok("Ready as can be!"));

app.Run();
