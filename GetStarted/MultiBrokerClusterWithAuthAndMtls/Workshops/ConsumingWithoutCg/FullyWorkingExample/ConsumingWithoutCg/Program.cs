global using static EnvVarNames;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<KafkaConsumerService>();
var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
