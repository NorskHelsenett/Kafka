public class Person
{
    public string? Id { get; init; }
    public PersonName? Name { get; init; }
    public List<string>? Tags { get; set; }
}

public class PersonName
{
    public string? Given { get; set; }
    public string? Family { get; set; }
}