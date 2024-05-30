This is a project to demo how to use kafka topics with an avro schema in dotnet (both consuming and producing).

Protobuf, like avro, is in dotnet based on compiling the the schema definition to C# files you can use together with the rest of your code.
To do this, first install the protobuf compiler, which can be obtained here
https://protobuf.dev/downloads/
(alternatively `brew install protobuf`).
Then you can create your `.proto` schema (or download it from the schema registriy).
The conventional place to put the protobuf schema definitions, in dotnet, is a separate directory named `Protos`.
Compile your protobuf schema
`protoc --proto_path=. --csharp_out=.. example.proto`

Confuelnts example:
https://github.com/confluentinc/confluent-kafka-dotnet/tree/master/examples/Protobuf

Dotnet docs on protobuf schema types to dotnet types matching (useful if designing your own schema?)
https://learn.microsoft.com/en-us/aspnet/core/grpc/protobuf?view=aspnetcore-8.0
