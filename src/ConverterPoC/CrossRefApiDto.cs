using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public class CrossrefApiResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("message-type")]
    public string MessageType { get; set; }

    [JsonPropertyName("message-version")]
    public string MessageVersion { get; set; }

    [JsonPropertyName("message")]
    public CrossrefWork Message { get; set; }
}

public class CrossrefWork
{
    [JsonPropertyName("title")]
    public List<string> Title { get; set; }

    [JsonPropertyName("DOI")]
    public string Doi { get; set; }

    [JsonPropertyName("author")]
    public List<CrossrefAuthor> Author { get; set; }

    [JsonPropertyName("issued")]
    public CrossrefIssued Issued { get; set; }

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("URL")]
    public string Url { get; set; }

    [JsonPropertyName("ISSN")]
    public List<string> Issn { get; set; }
    
    [JsonPropertyName("ISBN")]
    public List<string> Isbn { get; set; }
}

public class CrossrefAuthor
{
    [JsonPropertyName("given")]
    public string Given { get; set; }

    [JsonPropertyName("family")]
    public string Family { get; set; }

    [JsonPropertyName("sequence")]
    public string Sequence { get; set; }

    [JsonPropertyName("affiliation")]
    public List<CrossrefAffiliation> Affiliation { get; set; }
}

public class CrossrefAffiliation
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
}

public class CrossrefIssued
{
  
}