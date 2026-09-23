using System.Xml.Linq;

namespace ConverterPoC.Tests;

// License, funding, relations, extra titles and extra abstracts
public class CrossrefProgramsTests
{
    private static readonly XNamespace Crossref = TestRecords.Crossref;
    private static readonly XNamespace Jats = TestRecords.Jats;
    private static readonly XNamespace FundRef = CrossrefPrograms.FundRef;
    private static readonly XNamespace AccessIndicators = CrossrefPrograms.AccessIndicators;
    private static readonly XNamespace Relations = CrossrefPrograms.Relations;

    private const string Rights = """
        [
          { "id": "cc-by-4.0", "props": { "url": "https://creativecommons.org/licenses/by/4.0/", "scheme": "spdx" } },
          { "id": "custom", "link": "https://example.org/license" },
          { "id": "no-url" }
        ]
        """;

    private const string Funding = """
        [
          { "funder": { "id": "00k4n6c32", "name": "European Commission" }, "award": { "number": "101127143" } },
          { "funder": { "name": "Local Foundation" } },
          { "award": { "number": "no funder" } }
        ]
        """;

    private const string RelatedIdentifiers = """
        [
          { "identifier": "978-617-8470-47-0", "scheme": "isbn", "relation_type": { "id": "documents" } },
          { "identifier": "10.1234/abc", "scheme": "doi", "relation_type": { "id": "isnewversionof" } },
          { "identifier": "https://example.org/data", "scheme": "url", "relation_type": { "id": "iscitedby" } },
          { "identifier": "10.3390/gels11120947", "scheme": "doi", "relation_type": { "id": "hasmetadata" } }
        ]
        """;

    [Theory]
    [InlineData("publication-book")]
    [InlineData("publication-article")]
    [InlineData("dataset")]
    [InlineData("presentation")]
    public void Programs_ComeBeforeDoiData_InSchemaOrder(string resourceType)
    {
        var doc = TestRecords.Convert(Json(resourceType));

        var doiData = doc.Descendants(Crossref + "doi_data").Single();
        var programs = doiData.ElementsBeforeSelf().Where(e => e.Name.LocalName == "program").Select(e => e.Name.Namespace).ToList();

        Assert.Equal([FundRef, AccessIndicators, Relations], programs);
        Assert.Empty(CrossrefSchema.Validate(doc.ToString()));
    }

    [Fact]
    public void License_UsesRightsUrls_ForTheVersionOfRecord()
    {
        var licenses = TestRecords.Convert(Json()).Descendants(AccessIndicators + "license_ref").ToList();

        Assert.Equal(["https://creativecommons.org/licenses/by/4.0/", "https://example.org/license"], licenses.Select(l => l.Value));
        Assert.All(licenses, l => Assert.Equal("vor", l.Attribute("applies_to")!.Value));
    }

    [Fact]
    public void Funding_UsesRorIdOrFunderName_WithAwardNumber()
    {
        var groups = TestRecords.Convert(Json()).Descendants(FundRef + "assertion")
            .Where(a => a.Attribute("name")!.Value == "fundgroup")
            .Select(g => string.Join(" | ", g.Elements().Select(a => $"{a.Attribute("name")!.Value}={a.Value}")))
            .ToList();

        Assert.Equal(["ror=https://ror.org/00k4n6c32 | award_number=101127143", "funder_name=Local Foundation"], groups);
    }

    [Fact]
    public void RelatedIdentifiers_MapToCrossrefRelations()
    {
        var relations = TestRecords.Convert(Json()).Descendants(Relations + "related_item")
            .Select(i => i.Elements().Single())
            .Select(r => $"{r.Name.LocalName} {r.Attribute("relationship-type")!.Value} {r.Attribute("identifier-type")!.Value} {r.Value}")
            .ToList();

        Assert.Equal(
        [
            "inter_work_relation documents isbn 978-617-8470-47-0",
            "intra_work_relation isVersionOf doi 10.1234/abc",
            "inter_work_relation isReferencedBy uri https://example.org/data"
        ], relations);
    }

    [Fact]
    public void RecordWithoutRightsFundingOrRelations_HasNoPrograms()
    {
        var doc = TestRecords.Convert(TestRecords.Json());

        Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName == "program");
    }

    [Fact]
    public void Subtitle_IsAddedToTitles()
    {
        var titles = TestRecords.Convert(Json()).Descendants(Crossref + "titles").Single();

        Assert.Equal(["title", "subtitle"], titles.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("A subtitle", titles.Element(Crossref + "subtitle")!.Value);
    }

    // Only journal articles may carry several titles
    [Fact]
    public void AlternativeAndTranslatedTitles_AreExtraJournalArticleTitles()
    {
        var article = TestRecords.Convert(Json("publication-article")).Descendants(Crossref + "journal_article").Single();

        Assert.Equal(
            ["Test record", "An alternative title", "A translated title"],
            article.Elements(Crossref + "titles").Select(t => t.Element(Crossref + "title")!.Value));
    }

    [Fact]
    public void AdditionalAbstracts_AreAddedInTheirLanguage()
    {
        var abstracts = TestRecords.Convert(Json()).Descendants(Jats + "abstract").ToList();

        Assert.Equal(
            ["|Test abstract", "uk|Анотація"],
            abstracts.Select(a => $"{a.Attribute(XNamespace.Xml + "lang")?.Value}|{a.Value}"));
    }

    private static string Json(string resourceType = "publication-book")
    {
        var json = TestRecords.Json(resourceType: resourceType, publisher: "Test Journal");

        // Rights, funding, relations and additional titles/descriptions go into metadata
        return json.Replace("\"title\": \"Test record\"", $$"""
            "title": "Test record",
            "rights": {{Rights}},
            "funding": {{Funding}},
            "related_identifiers": {{RelatedIdentifiers}},
            "additional_titles": [
              { "title": "A subtitle", "type": { "id": "subtitle" } },
              { "title": "An alternative title", "type": { "id": "alternative-title" } },
              { "title": "A translated title", "type": { "id": "translated-title" }, "lang": { "id": "eng" } }
            ],
            "additional_descriptions": [
              { "description": "<p>Анотація</p>", "type": { "id": "abstract" }, "lang": { "id": "ukr" } },
              { "description": "Methods text", "type": { "id": "methods" }, "lang": { "id": "eng" } }
            ]
            """);
    }
}
