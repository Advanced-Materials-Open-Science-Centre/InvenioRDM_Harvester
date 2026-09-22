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
        var doc = TestRecords.Convert(TestRecords.Json(resourceType: "Dataset", creators: creators));

        Assert.Equal("depositor@example.org", doc.Descendants(Crossref + "email_address").Single().Value);
    }

    [Theory]
    [InlineData("Book", "book_metadata")]
    [InlineData("Journal article", "journal_article")]
    [InlineData("Dataset", "posted_content")]
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
}
