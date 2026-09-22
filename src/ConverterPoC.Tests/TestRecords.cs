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

    // Minimal InvenioRDM record; creators, contributors and identifiers are raw JSON arrays
    public static string Json(
        string resourceType = "Book",
        string creators = PersonCreator,
        string? contributors = null,
        string? identifiers = null,
        string? description = "Test abstract")
    {
        var optional = new StringBuilder();

        if (contributors != null)
            optional.Append($""" "contributors": {contributors},""");

        if (identifiers != null)
            optional.Append($""" "identifiers": {identifiers},""");

        if (description != null)
            optional.Append($""" "description": {JsonSerializer.Serialize(description)},""");

        return $$"""
            {
              "metadata": {
                "resource_type": { "id": "test", "title": { "en": "{{resourceType}}" } },
                "creators": {{creators}},
                {{optional}}
                "title": "Test record",
                "publisher": "Test Publisher",
                "publication_date": "2026-09-22"
              }
            }
            """;
    }

    public static XDocument Convert(string json)
    {
        var xml = FromJsonConverter.Convert(
            new CrossrefApiClient("", "", ""),
            Depositor,
            json,
            "10.15330/test.26.09.01",
            "https://example.org/records/test");

        Assert.NotNull(xml);

        return XDocument.Parse(xml);
    }
}
