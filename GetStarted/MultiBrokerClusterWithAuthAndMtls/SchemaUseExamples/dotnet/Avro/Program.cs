using System.Text;
using GeneratedSchemaTypes;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<KafkaConsumer>();
builder.Services.AddSingleton<KafkaProducer>();
builder.Services.AddSingleton<ConsumedState>();
var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapPost("/storePerson", (ApiParamPerson inputPerson, KafkaProducer kafkaProducer) =>
{
    var correlationId = System.Guid.NewGuid().ToString("D");

    var avroPerson = new Person
    {
        Id = inputPerson.Id,
        Name = new PersonName {
            Given = inputPerson.Name.Given,
            Family = inputPerson.Name.Family
        }
    };
    if(inputPerson.Tags?.Count != 0)
    {
        avroPerson.Tags = inputPerson.Tags;
    }

    var produceSuccess = kafkaProducer.Produce(avroPerson);
    if(produceSuccess)
    {
        return Results.Ok($"{correlationId}");
    }
    return Results.Text(
        content: $"{correlationId}",
        contentType: "text/html",
        contentEncoding: Encoding.UTF8,
        statusCode: (int?) System.Net.HttpStatusCode.InternalServerError);
});

app.MapPost("/retrievePerson/latest", (ApiParamRetrievePerson inputRetrieveParam, ConsumedState consumedState) =>
{
    var correlationId = System.Guid.NewGuid().ToString("D");

    var latestTopicPerson = consumedState.LastProcessedPerson;
    if(latestTopicPerson != null)
    {
        var apiPerson = new ApiParamPerson
        {
            Id = latestTopicPerson.Id,
            Name = new ApiParamPersonName
            {
                Given = latestTopicPerson.Name.Given,
                Family = latestTopicPerson.Name.Family
            },
            Tags = latestTopicPerson.Tags.ToList()
        };
        return Results.Json(apiPerson);
    }
    return Results.Text(
        content: $"{correlationId}",
        contentType: "text/html",
        contentEncoding: Encoding.UTF8,
        statusCode: (int?) System.Net.HttpStatusCode.InternalServerError);
});

app.Run();
