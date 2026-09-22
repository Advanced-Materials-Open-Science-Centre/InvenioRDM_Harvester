using System.Xml.Linq;

namespace ConverterPoC.Tests;

public class FromJsonConverterBookTests
{
    private static readonly XNamespace Crossref = "http://www.crossref.org/schema/5.3.1";

    [Fact]
    public void Book_WithIsbnIdentifier_EmitsIsbnBetweenPublicationDateAndPublisher()
    {
        var bookMetadata = ConvertBook("""[{ "identifier": "978-966-668-664-3", "scheme": "isbn" }]""");

        // crossref5.3.1.xsd: publication_date, acceptance_date?, (isbn{1,6} | noisbn), publisher
        Assert.Equal(
            ["contributors", "titles", "abstract", "publication_date", "isbn", "publisher", "doi_data"],
            bookMetadata.Elements().Select(e => e.Name.LocalName));

        var isbn = bookMetadata.Element(Crossref + "isbn")!;
        Assert.Equal("978-966-668-664-3", isbn.Value);
        Assert.Equal("print", isbn.Attribute("media_type")?.Value);
        Assert.Null(bookMetadata.Element(Crossref + "noisbn"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""[]""")]
    [InlineData("""[{ "identifier": "10.1234/abc", "scheme": "doi" }]""")]
    [InlineData("""[{ "identifier": "not-an-isbn", "scheme": "isbn" }]""")]
    public void Book_WithoutValidIsbn_EmitsNoisbnBetweenPublicationDateAndPublisher(string? identifiersJson)
    {
        var bookMetadata = ConvertBook(identifiersJson);

        Assert.Equal(
            ["contributors", "titles", "abstract", "publication_date", "noisbn", "publisher", "doi_data"],
            bookMetadata.Elements().Select(e => e.Name.LocalName));

        Assert.Equal("monograph", bookMetadata.Element(Crossref + "noisbn")!.Attribute("reason")?.Value);
    }

    [Fact]
    public void Book_WithMoreThanSixIsbns_EmitsOnlyFirstSix()
    {
        var identifiers = Enumerable.Range(0, 8)
            .Select(i => $$"""{ "identifier": "978-966-668-66{{i}}-3", "scheme": "isbn" }""");

        var bookMetadata = ConvertBook($"[{string.Join(",", identifiers)}]");

        Assert.Equal(6, bookMetadata.Elements(Crossref + "isbn").Count());
    }

    private static XElement ConvertBook(string? identifiersJson)
    {
        var identifiers = identifiersJson is null ? "" : $""" "identifiers": {identifiersJson},""";

        var json = $$"""
            {
              "metadata": {
                "resource_type": { "id": "publication-book", "title": { "en": "Book" } },
                "creators": [
                  { "person_or_org": { "type": "personal", "given_name": "Jane", "family_name": "Doe" } }
                ],
                "title": "Test book",
                "publisher": "Test Publisher",
                "publication_date": "2026-09-22",
                {{identifiers}}
                "description": "Test abstract"
              }
            }
            """;

        var xml = FromJsonConverter.Convert(
            new CrossrefApiClient("", "", ""),
            json,
            "10.15330/test.26.09.01",
            "https://example.org/records/test");

        Assert.NotNull(xml);

        return XDocument.Parse(xml).Descendants(Crossref + "book_metadata").Single();
    }
}
