using System.Xml.Linq;

namespace ConverterPoC.Tests;

public class FromJsonConverterTests
{
    private static readonly XNamespace Crossref = TestRecords.Crossref;
    private static readonly XNamespace Jats = TestRecords.Jats;

    [Fact]
    public void Head_UsesConfiguredDepositorAndRegistrant()
    {
        var head = TestRecords.Convert(TestRecords.Json()).Descendants(Crossref + "head").Single();

        Assert.Equal("Test Depositor", head.Element(Crossref + "depositor")!.Element(Crossref + "depositor_name")!.Value);
        Assert.Equal("depositor@example.org", head.Element(Crossref + "depositor")!.Element(Crossref + "email_address")!.Value);
        Assert.Equal("Test Registrant", head.Element(Crossref + "registrant")!.Value);
    }

    // The head used to be built from the first creator's name, which threw for these
    [Theory]
    [InlineData("""[{ "person_or_org": { "type": "organizational", "name": "Test Organization" } }]""")]
    [InlineData("""[{ "person_or_org": { "type": "personal", "family_name": "Doe" } }]""")]
    [InlineData("""[{ "person_or_org": { "type": "personal", "given_name": "Олена", "family_name": "Коваль" } }]""")]
    public void Head_DoesNotDependOnCreators(string creators)
    {
        var doc = TestRecords.Convert(TestRecords.Json(resourceType: "dataset", creators: creators));

        Assert.Equal("depositor@example.org", doc.Descendants(Crossref + "email_address").Single().Value);
    }

    [Theory]
    [InlineData("publication-book", "book_metadata")]
    [InlineData("publication-article", "journal_article")]
    [InlineData("presentation", "posted_content")]
    public void Abstract_HtmlDescription_IsConvertedToJatsParagraphs(string resourceType, string parent)
    {
        var json = TestRecords.Json(
            resourceType: resourceType,
            description: "<p>First&nbsp;paragraph with <strong>bold</strong> text.</p><p>Second<br>Third</p>");

        var doc = TestRecords.Convert(json);
        var abstractElement = doc.Descendants(Crossref + parent).Single().Element(Jats + "abstract")!;

        Assert.Equal(
            ["First paragraph with bold text.", "Second", "Third"],
            abstractElement.Elements(Jats + "p").Select(p => p.Value));
        Assert.Equal("bold", abstractElement.Descendants(Jats + "bold").Single().Value);
        Assert.DoesNotContain("<p>", abstractElement.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p>&nbsp;</p><br>")]
    public void Abstract_EmptyDescription_IsOmitted(string? description)
    {
        var doc = TestRecords.Convert(TestRecords.Json(description: description));

        Assert.Empty(doc.Descendants(Jats + "abstract"));
    }

    // DateTime.TryParse used to drop year-only dates (leaving out a required element) and invent days
    [Theory]
    [InlineData("publication-book", "2025", "2025")]
    [InlineData("publication-article", "2025", "2025")]
    [InlineData("dataset", "2025", "2025")]
    [InlineData("dataset", "2026-09", "09 2026")]
    [InlineData("dataset", "2026-09-22", "09 22 2026")]
    [InlineData("dataset", "2020-05-01/2021", "05 01 2020")]
    public void PublicationDate_KeepsEdtfPrecision(string resourceType, string publicationDate, string expected)
    {
        var doc = TestRecords.Convert(TestRecords.Json(resourceType: resourceType, publicationDate: publicationDate));

        var date = doc.Descendants()
            .Single(e => e.Name.LocalName is "publication_date" or "posted_date" &&
                         e.Parent!.Name.LocalName != "journal_issue");

        Assert.Equal(expected, string.Join(" ", date.Elements().Select(e => e.Value)));
    }

    [Fact]
    public void JournalIssueDate_IsYear()
    {
        var doc = TestRecords.Convert(TestRecords.Json(resourceType: "publication-article"));

        var issueDate = doc.Descendants(Crossref + "journal_issue").Single().Element(Crossref + "publication_date")!;

        Assert.Equal(["year"], issueDate.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("2026", issueDate.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("22.09.2026")]
    [InlineData("2026-02-30")]
    public void PublicationDate_MissingOrInvalid_Throws(string? publicationDate)
    {
        var json = TestRecords.Json(resourceType: "dataset", publicationDate: publicationDate);

        var exception = Assert.Throws<InvalidOperationException>(() => TestRecords.Convert(json));

        Assert.Contains("publication_date", exception.Message);
    }

    // Selected by resource_type.id; TestRecords gives every type the same display title
    [Theory]
    [InlineData("publication-book", "book")]
    [InlineData("publication-article", "journal")]
    [InlineData("dataset", "database")]
    [InlineData("presentation", "posted_content")]
    [InlineData("publication-conferencepaper", "posted_content")]
    public void ResourceTypeId_SelectsCrossrefContentType(string resourceType, string expected)
    {
        var body = TestRecords.Convert(TestRecords.Json(resourceType: resourceType)).Descendants(Crossref + "body").Single();

        Assert.Equal(expected, Assert.Single(body.Elements()).Name.LocalName);
    }

    // Keys used to be 3 random hex characters, which collide within larger reference lists
    [Fact]
    public void Citations_HaveUniqueSequentialKeys()
    {
        var references = "[" + string.Join(",", Enumerable.Range(1, 30).Select(i => $$"""{ "reference": "Reference {{i}}" }""")) + "]";

        var citations = TestRecords.Convert(TestRecords.Json(references: references))
            .Descendants(Crossref + "citation")
            .ToList();

        Assert.Equal(Enumerable.Range(1, 30).Select(i => $"ref-{i}"), citations.Select(c => c.Attribute("key")!.Value));
        Assert.Equal("Reference 30", citations[^1].Element(Crossref + "unstructured_citation")!.Value);
    }

    [Fact]
    public void Citations_EmptyReferences_OmitsCitationList()
    {
        var doc = TestRecords.Convert(TestRecords.Json(references: "[]"));

        Assert.Empty(doc.Descendants(Crossref + "citation_list"));
    }
}
