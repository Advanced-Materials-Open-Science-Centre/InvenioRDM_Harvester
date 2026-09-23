using System.Text.Json.Serialization;

namespace ConverterPoC;

// Response of https://api.crossref.org/works/{doi}, reduced to the fields this tool reads
public class CrossrefApiResponse
{
    [JsonPropertyName("message")]
    public CrossrefWork? Message { get; set; }
}

public class CrossrefWork
{
    [JsonPropertyName("DOI")]
    public string? Doi { get; set; }

    // Registered content type, e.g. "posted-content", "monograph", "journal-article"
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("resource")]
    public CrossrefResource? Resource { get; set; }
}

public class CrossrefResource
{
    [JsonPropertyName("primary")]
    public CrossrefResourceLink? Primary { get; set; }
}

public class CrossrefResourceLink
{
    // The URL the DOI resolves to
    [JsonPropertyName("URL")]
    public string? Url { get; set; }
}
