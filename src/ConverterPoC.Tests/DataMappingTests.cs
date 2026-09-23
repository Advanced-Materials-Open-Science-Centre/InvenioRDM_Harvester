using System.Xml.Linq;

namespace ConverterPoC.Tests;

public class DataMappingTests
{
    private static readonly XNamespace Crossref = TestRecords.Crossref;

    private const string JournalField = """
        { "journal:journal": { "title": "Physics and Chemistry of Solid State", "issn": "1729-4428", "volume": "26", "issue": "1", "pages": "132-139" } }
        """;

    // Datasets

    [Fact]
    public void Dataset_IsDatasetInRepositoryDatabase()
    {
        var json = TestRecords.Json(
            resourceType: "dataset",
            description: "<p>First paragraph.</p><p>Second <strong>paragraph</strong>.</p>",
            languages: """[{ "id": "ukr" }]""",
            references: """[{ "reference": "Reference 1" }]""");

        var database = TestRecords.Convert(json).Descendants(Crossref + "database").Single();

        Assert.Equal(TestRecords.RepositoryName, database.Element(Crossref + "database_metadata")!.Element(Crossref + "titles")!.Value);

        var dataset = database.Element(Crossref + "dataset")!;
        Assert.Equal("record", dataset.Attribute("dataset_type")!.Value);
        Assert.Equal(
            ["contributors", "titles", "database_date", "description", "doi_data", "citation_list"],
            dataset.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("09 22 2026", string.Join(" ", dataset.Element(Crossref + "database_date")!.Element(Crossref + "publication_date")!.Elements().Select(e => e.Value)));

        var description = dataset.Element(Crossref + "description")!;
        Assert.Equal("uk", description.Attribute("language")!.Value);
        Assert.Equal("First paragraph.\n\nSecond paragraph.", description.Value);
    }

    [Fact]
    public void Dataset_WithoutDescription_OmitsDescription()
    {
        var dataset = TestRecords.Convert(TestRecords.Json(resourceType: "dataset", description: null))
            .Descendants(Crossref + "dataset").Single();

        Assert.Null(dataset.Element(Crossref + "description"));
    }

    // DOIs already registered keep their content type, e.g. the datasets registered as posted content
    [Theory]
    [InlineData("dataset", CrossrefContentType.PostedContent, "posted_content")]
    [InlineData("dataset", CrossrefContentType.Dataset, "database")]
    [InlineData("presentation", CrossrefContentType.Book, "book")]
    [InlineData("publication-book", CrossrefContentType.JournalArticle, "journal")]
    public void RegisteredType_OverridesResourceType(string resourceType, CrossrefContentType registeredType, string expected)
    {
        var body = TestRecords.Convert(TestRecords.Json(resourceType: resourceType), registeredType)
            .Descendants(Crossref + "body").Single();

        Assert.Equal(expected, Assert.Single(body.Elements()).Name.LocalName);
    }

    // Journal articles

    [Fact]
    public void Journal_FromJournalField()
    {
        var doc = TestRecords.Convert(TestRecords.Json(resourceType: "publication-article", customFields: JournalField));

        var journalMetadata = doc.Descendants(Crossref + "journal_metadata").Single();
        Assert.Equal("Physics and Chemistry of Solid State", journalMetadata.Element(Crossref + "full_title")!.Value);
        Assert.Equal("1729-4428", journalMetadata.Element(Crossref + "issn")!.Value);

        var issue = doc.Descendants(Crossref + "journal_issue").Single();
        Assert.Equal(["publication_date", "journal_volume", "issue"], issue.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("26", issue.Element(Crossref + "journal_volume")!.Element(Crossref + "volume")!.Value);
        Assert.Equal("1", issue.Element(Crossref + "issue")!.Value);

        var pages = doc.Descendants(Crossref + "pages").Single();
        Assert.Equal("132", pages.Element(Crossref + "first_page")!.Value);
        Assert.Equal("139", pages.Element(Crossref + "last_page")!.Value);
    }

    // This repository leaves the Journal field empty; depositors put the journal name in publisher
    [Fact]
    public void Journal_TitleFromPublisher()
    {
        var doc = TestRecords.Convert(TestRecords.Json(resourceType: "publication-article", publisher: "Radiotekhnika"));

        var journalMetadata = doc.Descendants(Crossref + "journal_metadata").Single();
        Assert.Equal(["full_title"], journalMetadata.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("Radiotekhnika", journalMetadata.Value);
        Assert.Empty(doc.Descendants(Crossref + "pages"));
    }

    // The journal title used to be the article's own title
    [Theory]
    [InlineData(TestRecords.RepositoryName)]
    [InlineData(" test repository ")]
    [InlineData("")]
    [InlineData(null)]
    public void Journal_WithoutJournalTitle_IsPostedContent(string? publisher)
    {
        var body = TestRecords.Convert(TestRecords.Json(resourceType: "publication-article", publisher: publisher))
            .Descendants(Crossref + "body").Single();

        Assert.Equal("posted_content", Assert.Single(body.Elements()).Name.LocalName);
    }

    // Crossref doesn't allow changing the type of a registered DOI, so this can't fall back
    [Fact]
    public void Journal_RegisteredWithoutJournalTitle_Throws()
    {
        var json = TestRecords.Json(resourceType: "publication-article", publisher: TestRecords.RepositoryName);

        var exception = Assert.Throws<InvalidOperationException>(() => TestRecords.Convert(json, CrossrefContentType.JournalArticle));

        Assert.Contains("journal title", exception.Message);
    }

    [Fact]
    public void Journal_InvalidIssnAndSinglePage()
    {
        var customFields = """{ "journal:journal": { "title": "Test Journal", "issn": "not an issn", "pages": "12" } }""";

        var doc = TestRecords.Convert(TestRecords.Json(resourceType: "publication-article", customFields: customFields));

        Assert.Empty(doc.Descendants(Crossref + "issn"));
        Assert.Equal(["first_page"], doc.Descendants(Crossref + "pages").Single().Elements().Select(e => e.Name.LocalName));
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "bibliographic_record")]
    public void Journal_PublicationTypeFollowsFiles(bool filesEnabled, string? expected)
    {
        var article = TestRecords.Convert(TestRecords.Json(resourceType: "publication-article", customFields: JournalField, filesEnabled: filesEnabled))
            .Descendants(Crossref + "journal_article").Single();

        Assert.Equal(expected, article.Attribute("publication_type")?.Value);
    }

    // Language

    [Theory]
    [InlineData("publication-book", "book_metadata")]
    [InlineData("publication-article", "journal_article")]
    [InlineData("presentation", "posted_content")]
    [InlineData("dataset", "description")]
    public void Language_IsFirstRecordLanguage(string resourceType, string element)
    {
        var json = TestRecords.Json(resourceType: resourceType, customFields: JournalField, languages: """[{ "id": "ukr" }, { "id": "eng" }]""");

        Assert.Equal("uk", TestRecords.Convert(json).Descendants(Crossref + element).Single().Attribute("language")?.Value);
    }

    // The book language used to be hardcoded to "en"
    [Theory]
    [InlineData(null)]
    [InlineData("""[]""")]
    [InlineData("""[{ "id": "xyz" }]""")]
    public void Language_UnknownOrMissing_IsOmitted(string? languages)
    {
        var doc = TestRecords.Convert(TestRecords.Json(languages: languages));

        Assert.DoesNotContain(doc.Descendants(), e => e.Attribute("language") != null);
    }

    [Theory]
    [InlineData("ukr", "uk")]
    [InlineData("eng", "en")]
    [InlineData("deu", "de")]
    [InlineData("pol", "pl")]
    [InlineData("ENG", "en")]
    [InlineData("xyz", null)]
    [InlineData(null, null)]
    public void Languages_MapIso6393ToIso6391(string? iso6393, string? expected)
    {
        Assert.Equal(expected, Languages.ToIso6391(iso6393));
    }

    [Theory]
    [InlineData("posted-content", CrossrefContentType.PostedContent)]
    [InlineData("monograph", CrossrefContentType.Book)]
    [InlineData("journal-article", CrossrefContentType.JournalArticle)]
    [InlineData("dataset", CrossrefContentType.Dataset)]
    [InlineData("proceedings-article", null)]
    [InlineData(null, null)]
    public void RegisteredTypes_MapToContentTypes(string? crossrefType, CrossrefContentType? expected)
    {
        Assert.Equal(expected, CrossrefContentTypes.FromRegisteredType(crossrefType));
    }
}
