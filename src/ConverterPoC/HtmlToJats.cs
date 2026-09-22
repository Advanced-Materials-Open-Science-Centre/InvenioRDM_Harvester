using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ConverterPoC;

// Converts InvenioRDM rich-text descriptions (sanitized HTML) into JATS abstract paragraphs.
// Block-level tags become separate <jats:p>, basic formatting maps to JATS inline elements,
// any other tag is dropped while its text is kept.
public static class HtmlToJats
{
    private static readonly HashSet<string> BlockTags =
    [
        "p", "div", "br", "hr", "li", "ul", "ol", "blockquote", "pre", "table", "tr",
        "h1", "h2", "h3", "h4", "h5", "h6"
    ];

    private static readonly Dictionary<string, string> InlineTags = new()
    {
        ["strong"] = "bold",
        ["b"] = "bold",
        ["em"] = "italic",
        ["i"] = "italic",
        ["u"] = "underline",
        ["sup"] = "sup",
        ["sub"] = "sub",
    };

    private static readonly Regex Comment = new("<!--.*?-->", RegexOptions.Singleline);
    private static readonly Regex Tag = new(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)\b[^>]*>");
    private static readonly Regex Whitespace = new(@"\s+");

    public static List<XElement> ToParagraphs(string? html, XNamespace jats)
    {
        var paragraphs = new List<XElement>();

        if (string.IsNullOrWhiteSpace(html))
            return paragraphs;

        html = Comment.Replace(html, "");

        // Formatting currently open in the HTML, and its elements materialized in the current
        // paragraph (created lazily so a paragraph break inside <strong> reopens it in the next <p>)
        var formatting = new List<(string Tag, string Jats)>();
        var chain = new List<XElement>();
        var paragraph = new XElement(jats + "p");

        void Flush()
        {
            TrimParagraph(paragraph);

            if (paragraph.Value.Length > 0)
                paragraphs.Add(paragraph);

            paragraph = new XElement(jats + "p");
            chain.Clear();
        }

        void AddText(string raw)
        {
            // Soft hyphens (&shy;) are invisible in HTML but would end up as-is in the metadata
            var text = Whitespace.Replace(WebUtility.HtmlDecode(raw).Replace("­", ""), " ");

            if (EndsWithSpaceOrEmpty(paragraph))
                text = text.TrimStart();

            if (text.Length == 0)
                return;

            while (chain.Count < formatting.Count)
            {
                var element = new XElement(jats + formatting[chain.Count].Jats);
                (chain.Count == 0 ? paragraph : chain[^1]).Add(element);
                chain.Add(element);
            }

            (chain.Count == 0 ? paragraph : chain[^1]).Add(text);
        }

        var position = 0;

        foreach (Match match in Tag.Matches(html))
        {
            AddText(html[position..match.Index]);
            position = match.Index + match.Length;

            var isClosing = match.Groups[1].Value == "/";
            var tag = match.Groups[2].Value.ToLowerInvariant();

            if (BlockTags.Contains(tag))
            {
                Flush();
            }
            else if (InlineTags.TryGetValue(tag, out var jatsName))
            {
                if (!isClosing)
                {
                    formatting.Add((tag, jatsName));
                }
                else
                {
                    var index = formatting.FindLastIndex(f => f.Tag == tag);

                    if (index >= 0)
                    {
                        formatting.RemoveRange(index, formatting.Count - index);

                        if (chain.Count > index)
                            chain.RemoveRange(index, chain.Count - index);
                    }
                }
            }
        }

        AddText(html[position..]);
        Flush();

        return paragraphs;
    }

    private static bool EndsWithSpaceOrEmpty(XElement paragraph)
    {
        var last = paragraph.DescendantNodes().OfType<XText>().LastOrDefault();
        return last == null || last.Value.EndsWith(' ');
    }

    // Trims trailing whitespace, removing formatting elements it leaves empty (e.g. "text<strong> </strong>")
    private static void TrimParagraph(XElement paragraph)
    {
        while (paragraph.DescendantNodes().OfType<XText>().LastOrDefault() is { } last)
        {
            last.Value = last.Value.TrimEnd();

            if (last.Value.Length > 0)
                break;

            var parent = last.Parent;
            last.Remove();

            while (parent != null && parent != paragraph && !parent.Nodes().Any())
            {
                var grandParent = parent.Parent;
                parent.Remove();
                parent = grandParent;
            }
        }
    }
}
