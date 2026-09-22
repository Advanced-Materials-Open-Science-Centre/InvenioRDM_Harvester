using System.Text.Json;
using System.Xml.Linq;

namespace ConverterPoC.Tests;

public class ContributorsParserTests
{
    private static readonly XNamespace Crossref = TestRecords.Crossref;

    private const string Creators = """
        [
          { "person_or_org": { "type": "personal", "given_name": "Jane", "family_name": "Doe" } },
          { "person_or_org": { "type": "personal", "given_name": "John", "family_name": "Roe" }, "role": { "id": "datacollector" } }
        ]
        """;

    [Fact]
    public void Creators_AreAuthors_AndEditorContributorsAreIncluded()
    {
        var contributors = Parse(Creators, """
            [
              { "person_or_org": { "type": "personal", "given_name": "Eddie", "family_name": "Tor" }, "role": { "id": "editor" } },
              { "person_or_org": { "type": "personal", "given_name": "Dana", "family_name": "Manager" }, "role": { "id": "datamanager" } }
            ]
            """);

        Assert.Equal(
            [("Jane", "author", "first"), ("John", "author", "additional"), ("Eddie", "editor", "additional")],
            contributors.Elements(Crossref + "person_name").Select(p => (
                p.Element(Crossref + "given_name")!.Value,
                p.Attribute("contributor_role")!.Value,
                p.Attribute("sequence")!.Value)));
    }

    [Theory]
    [InlineData("""[{ "person_or_org": { "type": "personal", "family_name": "Manager" }, "role": { "id": "datamanager" } }]""")]
    [InlineData("""[{ "person_or_org": { "type": "personal", "family_name": "Contact" }, "role": { "id": "contactperson" } }]""")]
    [InlineData("""[{ "person_or_org": { "type": "personal", "family_name": "Norole" } }]""")]
    [InlineData("""[]""")]
    public void ContributorsWithoutCrossrefRole_AreSkipped(string contributorsJson)
    {
        var contributors = Parse(Creators, contributorsJson);

        Assert.Equal(["Doe", "Roe"], contributors.Descendants(Crossref + "surname").Select(s => s.Value));
        Assert.All(contributors.Elements(), e => Assert.Equal("author", e.Attribute("contributor_role")!.Value));
    }

    [Fact]
    public void OrganizationalCreator_IsOrganization()
    {
        var contributors = Parse("""[{ "person_or_org": { "type": "organizational", "name": "Test Organization" } }]""");

        var organization = Assert.Single(contributors.Elements(Crossref + "organization"));
        Assert.Equal("Test Organization", organization.Value);
        Assert.Equal("first", organization.Attribute("sequence")!.Value);
    }

    private static XElement Parse(string creators, string? contributors = null)
    {
        using var doc = JsonDocument.Parse(TestRecords.Json(creators: creators, contributors: contributors));

        return ContributorsParser.ConvertContributorsToXml(Crossref, doc.RootElement);
    }
}
