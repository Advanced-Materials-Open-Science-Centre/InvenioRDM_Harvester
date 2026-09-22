using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace ConverterPoC.Tests;

internal static class TestRecords
{
    public static readonly XNamespace Crossref = "http://www.crossref.org/schema/5.3.1";
    public static readonly XNamespace Jats = "http://www.ncbi.nlm.nih.gov/JATS1";

    public static readonly Depositor Depositor = new("Test Depositor", "depositor@example.org", "Test Registrant");

    public const string PersonCreator =
        """[{ "person_or_org": { "type": "personal", "given_name": "Jane", "family_name": "Doe" } }]""";

    // Minimal InvenioRDM record; creators, contributors, identifiers and references are raw JSON arrays
    public static string Json(
        string resourceType = "publication-book",
        string creators = PersonCreator,
        string? contributors = null,
        string? identifiers = null,
        string? references = null,
        string? description = "Test abstract",
        string? publicationDate = "2026-09-22")
    {
        var optional = new StringBuilder();

        if (contributors != null)
            optional.Append($""" "contributors": {contributors},""");

        if (identifiers != null)
            optional.Append($""" "identifiers": {identifiers},""");

        if (references != null)
            optional.Append($""" "references": {references},""");

        if (description != null)
            optional.Append($""" "description": {JsonSerializer.Serialize(description)},""");

        if (publicationDate != null)
            optional.Append($""" "publication_date": {JsonSerializer.Serialize(publicationDate)},""");

        return $$"""
            {
              "metadata": {
                "resource_type": { "id": "{{resourceType}}", "title": { "en": "Test type" } },
                "creators": {{creators}},
                {{optional}}
                "title": "Test record",
                "publisher": "Test Publisher"
              }
            }
            """;
    }

    public static XDocument Convert(string json)
    {
        var xml = FromJsonConverter.Convert(
            Depositor,
            json,
            "10.15330/test.26.09.01",
            "https://example.org/records/test");

        Assert.NotNull(xml);

        return XDocument.Parse(xml);
    }
}
