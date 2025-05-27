using System.Text.Json.Serialization;

namespace ConverterPoC;

public class CrossrefPrefixResponse
{
    [JsonPropertyName("message")]
    public CrossrefPrefixMessage Message { get; set; }
}

public class CrossrefPrefixMessage
{
    [JsonPropertyName("prefix")]
    public string Prefix { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("members")]
    public List<CrossrefMember> Members { get; set; }
}

public class CrossrefMember
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("primary-name")]
    public string PrimaryName { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; }
}
