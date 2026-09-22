using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ConverterPoC.Tests;

// Hits dataset.cnu.edu.ua and Crossref's schema parser (https://www.crossref.org/02publishers/parser.html),
// which only validates the uploaded XML and never deposits it.
[Trait("Category", "Integration")]
public class CrossrefParserIntegrationTests
{
    private const string RepositoryUrl = "https://dataset.cnu.edu.ua/";
    private const string CrossrefParserUrl = "https://apps.crossref.org/XSDParse";
    private const string TestDoi = "10.15330/test.26.09.01";

    private static readonly XNamespace Crossref = "http://www.crossref.org/schema/5.3.1";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    [Theory]
    [InlineData("ej8xg-c9628")] // Book with an ISBN identifier
    [InlineData("kej9f-w3t43")] // Dataset
    [InlineData("qzt09-98r12")] // Dataset
    [InlineData("dzk9x-70170")] // Publication
    [InlineData("dfzqr-f9b81")] // Publication
    [InlineData("aevms-4jb16")] // Dataset
    [InlineData("n0w35-kyj13")] // Dataset
    [InlineData("aaz12-88b42")] // Dataset
    public async Task RealRecord_ConvertedXml_PassesCrossrefParser(string recordId)
    {
        var xml = await ConvertRealRecordAsync(recordId);

        var result = await ValidateWithCrossrefParserAsync($"{recordId}.xml", xml);

        Assert.True(result.IsValid, $"Crossref parser rejected {recordId}: {result}");
    }

    // Guards against the check above passing vacuously: the pre-fix output must be rejected
    [Fact]
    public async Task CrossrefParser_RejectsBookWithoutIsbnOrNoisbn()
    {
        var doc = XDocument.Parse(await ConvertRealRecordAsync("ej8xg-c9628"));
        doc.Descendants(Crossref + "isbn").Remove();

        var result = await ValidateWithCrossrefParserAsync("ej8xg-c9628-no-isbn.xml", doc.ToString());

        Assert.False(result.IsValid, $"Expected Crossref parser to reject XML without isbn/noisbn: {result}");
        Assert.Contains(result.Errors, e => e.Contains("noisbn"));
    }

    private static async Task<string> ConvertRealRecordAsync(string recordId)
    {
        var json = await Http.GetStringAsync($"{RepositoryUrl}api/records/{recordId}");

        var xml = FromJsonConverter.Convert(
            new CrossrefApiClient("", "", ""),
            json,
            TestDoi,
            $"{RepositoryUrl}records/{recordId}");

        Assert.NotNull(xml);

        return xml;
    }

    private static async Task<ParserResult> ValidateWithCrossrefParserAsync(string fileName, string xml)
    {
        using var content = new MultipartFormDataContent();

        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        content.Add(file, "mdFile", fileName);

        using var response = await Http.PostAsync(CrossrefParserUrl, content);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();

        // Results page: <p>✅ Parsing is complete, your file is valid ...</p>
        // or <p>❌ Parsing is complete, there were N errors:</p><ol><li>[Error]: ...</li></ol>
        // or <p>❌ Parsing failed, XML document is malformed. ...</p>
        var summary = Regex.Match(html, "<p>([✅❌].*?)</p>", RegexOptions.Singleline);
        Assert.True(summary.Success, $"Unexpected Crossref parser response:\n{html}");

        var errors = Regex.Matches(html, "<li>(.*?)</li>", RegexOptions.Singleline)
            .Select(m => ToText(m.Groups[1].Value))
            .ToList();

        var summaryText = ToText(summary.Groups[1].Value);

        return new ParserResult(summaryText.StartsWith('✅'), summaryText, errors);
    }

    private static string ToText(string html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();

    private record ParserResult(bool IsValid, string Summary, IReadOnlyList<string> Errors)
    {
        public override string ToString() =>
            string.Join(Environment.NewLine, [Summary, .. Errors]);
    }
}
