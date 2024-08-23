public static class Mapping
{
    public static ApiParamPerson ToPersonJson(this Person personProtobuf)
    {
        throw new NotImplementedException();
    }

    public static Person ToPersonProtobuf(this ApiParamPerson personJson)
    {
        throw new NotImplementedException();
    }

    public static string SerializeExplicitlyForAot(this ApiParamPerson inputPerson)
    {
        return System.Text.Json.JsonSerializer.Serialize(inputPerson!, SourceGenerationContext.Default.ApiParamPerson);
    }

    public static byte[] GetUtf8Bytes(this string input)
    {
        return System.Text.Encoding.UTF8.GetBytes(input);
    }

    public static string GetUtf8String(this byte[] input)
    {
        return System.Text.Encoding.UTF8.GetString(input);
    }
}
