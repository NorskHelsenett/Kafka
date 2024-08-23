global using static EnvVarNames;
using System.Net;
using System.Text;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddSingleton<OutputStateService>();
// builder.Services.AddHostedService<KafkaConsumerService>();
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

    throw new NotImplementedException("Produce a message for the received data");

    var produceSuccess = false;
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
    throw new NotImplementedException("This is left as an exercise for later");
});
app.MapPost("/remove", async (HttpContext http, ApiParamRetrievePerson postContent, KafkaProducerService kafkaProducerService) =>
{
    throw new NotImplementedException("This is left as an exercise for later");
});

app.MapGet("/healthz", () => Results.Ok("Started successfully"));
app.MapGet("/healthz/live", () => Results.Ok("Alive and well"));
app.MapGet("/healthz/ready", () => Results.Ok("Ready as can be!"));

app.Run();
