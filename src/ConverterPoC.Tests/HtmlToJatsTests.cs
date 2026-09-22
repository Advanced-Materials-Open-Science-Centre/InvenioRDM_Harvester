using System.Xml.Linq;

namespace ConverterPoC.Tests;

public class HtmlToJatsTests
{
    [Theory]
    [InlineData("<p>First</p><p>Second</p>", new[] { "First", "Second" })]
    [InlineData("Line one<br>Line two<br/>", new[] { "Line one", "Line two" })]
    [InlineData("<h2>Title</h2><pre>code</pre><blockquote>quote</blockquote>", new[] { "Title", "code", "quote" })]
    [InlineData("Plain text without tags", new[] { "Plain text without tags" })]
    [InlineData("<p>  a \n\t b  </p>\n<p> </p><p>&nbsp;</p>", new[] { "a b" })]
    [InlineData("<p>text<strong> </strong></p>", new[] { "text" })]
    [InlineData("<p>a<!-- comment -->b</p>", new[] { "ab" })]
    public void SplitsParagraphsAndNormalizesWhitespace(string html, string[] expected)
    {
        Assert.Equal(expected, Convert(html).Select(p => p.Value));
    }

    [Fact]
    public void DecodesEntities_AndDropsSoftHyphens()
    {
        var paragraph = Assert.Single(Convert("<p>5&nbsp;&deg;C &ndash; &laquo;test&raquo; hy&shy;phen&rsquo;s a &lt;b&gt; c</p>"));

        Assert.Equal("5 °C – «test» hyphen’s a <b> c", paragraph.Value);
        Assert.Empty(paragraph.Elements());
    }

    [Theory]
    [InlineData("<p>Plain <strong>bold</strong> and <em>italic</em> x<sup>2</sup> H<sub>2</sub>O <u>u</u></p>",
        "Plain <bold>bold</bold> and <italic>italic</italic> x<sup>2</sup> H<sub>2</sub>O <underline>u</underline>")]
    [InlineData("<p><b>outer <i>inner</i> tail</b> after</p>",
        "<bold>outer <italic>inner</italic> tail</bold> after")]
    [InlineData("<p><span style=\"color: red\">Visit <a href=\"https://example.org\">site</a></span></p>",
        "Visit site")]
    [InlineData("<p>unmatched</strong> close</p>",
        "unmatched close")]
    public void MapsFormattingToJats_AndDropsOtherTags(string html, string expected)
    {
        Assert.Equal(expected, Markup(Assert.Single(Convert(html))));
    }

    [Fact]
    public void FormattingSpanningParagraphBreak_IsReopened()
    {
        Assert.Equal(["<bold>one</bold>", "<bold>two</bold> three"], Convert("<strong>one<br>two</strong> three").Select(Markup));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p><br>")]
    public void EmptyInput_ProducesNoParagraphs(string? html)
    {
        Assert.Empty(Convert(html));
    }

    private static List<XElement> Convert(string? html)
    {
        var paragraphs = HtmlToJats.ToParagraphs(html, TestRecords.Jats);

        Assert.All(paragraphs, p => Assert.Equal(TestRecords.Jats + "p", p.Name));

        return paragraphs;
    }

    // Renders JATS content with local names only, e.g. "a <bold>b</bold>"
    private static string Markup(XElement element) =>
        string.Concat(element.Nodes().Select(n => n is XElement e
            ? $"<{e.Name.LocalName}>{Markup(e)}</{e.Name.LocalName}>"
            : ((XText)n).Value));
}
