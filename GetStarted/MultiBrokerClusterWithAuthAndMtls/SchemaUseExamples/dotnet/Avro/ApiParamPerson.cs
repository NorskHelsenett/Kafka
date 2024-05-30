public record ApiParamPerson
{
    public required string Id { get; init; }
    public required ApiParamPersonName Name { get; init; }
    public List<string>? Tags { get; set; }
}

public record ApiParamPersonName
{
    public required string Given { get; init; }
    public required string Family { get; init; }
}
