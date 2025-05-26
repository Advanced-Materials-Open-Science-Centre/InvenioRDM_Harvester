using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public class CrossrefSearchResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("message-type")]
    public string MessageType { get; set; }

    [JsonPropertyName("message-version")]
    public string MessageVersion { get; set; }

    [JsonPropertyName("message")]
    public CrossrefMessage Message { get; set; }
}

public class CrossrefMessage
{
    [JsonPropertyName("items")]
    public List<CrossrefWork> Items { get; set; }

    [JsonPropertyName("total-results")]
    public int TotalResults { get; set; }

    [JsonPropertyName("query")]
    public CrossrefQuery Query { get; set; }
}

public class CrossrefQuery
{
    [JsonPropertyName("search-terms")]
    public string SearchTerms { get; set; }

    [JsonPropertyName("start-index")]
    public int StartIndex { get; set; }
}


public class CrossrefDate
{
    [JsonPropertyName("date-parts")]
    public List<List<int>> DateParts { get; set; }
}
