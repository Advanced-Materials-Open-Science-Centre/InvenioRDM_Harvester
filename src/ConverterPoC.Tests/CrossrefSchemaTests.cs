using System.Xml.Linq;

namespace ConverterPoC.Tests;

public class CrossrefSchemaTests
{
    private static readonly XNamespace Crossref = TestRecords.Crossref;

    [Theory]
    [InlineData("publication-book")]
    [InlineData("publication-article")]
    [InlineData("dataset")]
    public void ConvertedRecord_IsValid(string resourceType)
    {
        var json = TestRecords.Json(
            resourceType: resourceType,
            identifiers: """[{ "identifier": "978-966-668-664-3", "scheme": "isbn" }]""",
            references: """[{ "reference": "Reference 1" }]""",
            description: "<p>Abstract with <strong>bold</strong> and x<sup>2</sup></p>");

        Assert.Empty(CrossrefSchema.Validate(TestRecords.Convert(json).ToString()));
    }

    // The error Crossref returned for the original book deposit
    [Fact]
    public void BookWithoutIsbnOrNoisbn_IsRejected()
    {
        var doc = TestRecords.Convert(TestRecords.Json());
        doc.Descendants(Crossref + "noisbn").Remove();

        var error = Assert.Single(CrossrefSchema.Validate(doc.ToString()));

        Assert.Contains("publisher", error);
        Assert.Contains("noisbn", error);
    }

    [Fact]
    public void ElementWithoutNamespace_IsRejected()
    {
        var doc = TestRecords.Convert(TestRecords.Json());
        doc.Descendants(Crossref + "publisher").Single().AddBeforeSelf(new XElement("noisbn", new XAttribute("reason", "monograph")));
        doc.Descendants(Crossref + "noisbn").Remove();

        Assert.NotEmpty(CrossrefSchema.Validate(doc.ToString()));
    }

    [Theory]
    [InlineData("not xml")]
    [InlineData("<doi_batch")]
    public void MalformedXml_IsReported(string xml)
    {
        Assert.Contains(CrossrefSchema.Validate(xml), e => e.StartsWith("Malformed XML"));
    }
}
