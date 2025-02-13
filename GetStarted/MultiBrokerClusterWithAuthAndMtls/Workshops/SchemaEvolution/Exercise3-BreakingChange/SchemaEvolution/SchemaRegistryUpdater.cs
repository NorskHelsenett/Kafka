using Confluent.SchemaRegistry;

namespace SchemaEvolution;

public static class SchemaRegistryUpdater
{
    public static async Task RegisterSchema(string schemaValue, string schemaSubject, SchemaType schemaType = SchemaType.Protobuf)
    {
        try
        {
            var schema = new Schema(schemaString: schemaValue, schemaType: schemaType);


            var schemaRegistryConfig = KafkaConfigBinder.GetSchemaRegistryConfig();
            CachedSchemaRegistryClient schemaRegistryClient = new CachedSchemaRegistryClient(schemaRegistryConfig);

            _ = await schemaRegistryClient.RegisterSchemaAsync(subject: schemaSubject, schema: schema, normalize: true);
            _ = await schemaRegistryClient.UpdateCompatibilityAsync(Compatibility.Backward, subject: schemaSubject);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Got exception when registering schema with subject {schemaSubject}");
            Console.WriteLine(e);
        }
    }
}
