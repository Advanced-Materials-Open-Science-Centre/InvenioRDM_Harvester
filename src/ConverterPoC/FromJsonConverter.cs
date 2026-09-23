using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ConverterPoC;

// Converts an InvenioRDM record (the /api/records/{id} JSON) into a Crossref 5.3.1 deposit.
// Helpers return null for optional elements that don't apply; XElement ignores null content.
public static class FromJsonConverter
{
    private static readonly XNamespace Crossref = "http://www.crossref.org/schema/5.3.1";
    private static readonly XNamespace Jats = "http://www.ncbi.nlm.nih.gov/JATS1";
    private static readonly XNamespace AccessIndicators = "http://www.crossref.org/AccessIndicators.xsd";
    private static readonly XNamespace Relations = "http://www.crossref.org/relations.xsd";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    // Throws when the record can't be converted, so the caller can report and skip it
    public static string Convert(
        Depositor depositor,
        string invenioRdmJson,
        string doi,
        string recordUrl)
    {
        using var record = JsonDocument.Parse(invenioRdmJson);

        var crossrefDoc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Crossref + "doi_batch",
                new XAttribute(XNamespace.Xmlns + "ai", AccessIndicators.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "jats", Jats.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "rel", Relations.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
                new XAttribute("version", "5.3.1"),
                new XAttribute(Xsi + "schemaLocation",
                    "http://www.crossref.org/schema/5.3.1 http://www.crossref.org/schemas/crossref5.3.1.xsd"),
                BuildHead(depositor),
                new XElement(Crossref + "body", BuildContent(record.RootElement, doi, recordUrl))
            )
        );

        var sb = new StringBuilder();

        using var sw = new Utf8StringWriter(sb);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8,
            NewLineOnAttributes = true,
            OmitXmlDeclaration = false
        };

        using var writer = XmlWriter.Create(sw, settings);

        crossrefDoc.WriteTo(writer);

        writer.Flush();
        return sb.ToString();
    }

    private static XElement BuildHead(Depositor depositor) =>
        new(Crossref + "head",
            new XElement(Crossref + "doi_batch_id", GenerateBatchId()),
            new XElement(Crossref + "timestamp", DateTime.UtcNow.ToString("yyyyMMddHHmmss")),
            new XElement(Crossref + "depositor",
                new XElement(Crossref + "depositor_name", depositor.Name),
                new XElement(Crossref + "email_address", depositor.Email)
            ),
            new XElement(Crossref + "registrant", depositor.Registrant)
        );

    private static XElement BuildContent(JsonElement root, string doi, string recordUrl)
    {
        var metadata = root.GetProperty("metadata");

        // Vocabulary id (e.g. "publication-book"); the display title can be renamed or translated
        var type = metadata.GetProperty("resource_type").GetProperty("id").GetString();

        return type switch
        {
            "publication-book" => CreateBook(root, metadata, doi, recordUrl),
            "publication-article" => CreateJournal(root, metadata, doi, recordUrl),
            _ => CreatePostedContent(root, metadata, doi, recordUrl)
        };
    }

    // Element order in each builder follows crossref5.3.1.xsd

    private static XElement CreateBook(JsonElement root, JsonElement metadata, string doi, string recordUrl) =>
        new(Crossref + "book",
            new XAttribute("book_type", "monograph"),
            new XElement(Crossref + "book_metadata",
                new XAttribute("language", "en"),
                Contributors(root),
                Titles(metadata),
                Abstract(metadata),
                new XElement(Crossref + "publication_date", GetPublicationDate(metadata).ToCrossref(Crossref)),
                Isbns(metadata),
                Publisher(metadata),
                DoiData(doi, recordUrl),
                CitationList(metadata)
            )
        );

    private static XElement CreatePostedContent(JsonElement root, JsonElement metadata, string doi, string recordUrl) =>
        new(Crossref + "posted_content",
            new XAttribute("type", "report"),
            Contributors(root),
            Titles(metadata),
            new XElement(Crossref + "posted_date", GetPublicationDate(metadata).ToCrossref(Crossref)),
            Abstract(metadata),
            DoiData(doi, recordUrl),
            CitationList(metadata)
        );

    private static XElement CreateJournal(JsonElement root, JsonElement metadata, string doi, string recordUrl)
    {
        var publicationDate = GetPublicationDate(metadata);

        return new XElement(Crossref + "journal",
            new XElement(Crossref + "journal_metadata",
                new XElement(Crossref + "full_title", GetString(metadata, "title"))
            ),
            new XElement(Crossref + "journal_issue",
                new XElement(Crossref + "publication_date", new XElement(Crossref + "year", publicationDate.Year))
            ),
            new XElement(Crossref + "journal_article",
                new XAttribute("publication_type", "abstract_only"),
                Titles(metadata),
                Contributors(root),
                Abstract(metadata),
                new XElement(Crossref + "publication_date", publicationDate.ToCrossref(Crossref)),
                DoiData(doi, recordUrl),
                CitationList(metadata)
            )
        );
    }

    private static XElement? Contributors(JsonElement root)
    {
        var contributors = ContributorsParser.ConvertContributorsToXml(Crossref, root);
        return contributors.HasElements ? contributors : null;
    }

    private static XElement? Titles(JsonElement metadata) =>
        metadata.TryGetProperty("title", out var title)
            ? new XElement(Crossref + "titles", new XElement(Crossref + "title", title.GetString()))
            : null;

    private static XElement? Abstract(JsonElement metadata)
    {
        var paragraphs = HtmlToJats.ToParagraphs(GetString(metadata, "description"), Jats);
        return paragraphs.Count > 0 ? new XElement(Jats + "abstract", paragraphs) : null;
    }

    // publication_date/posted_date are required by Crossref, so a record without a usable date fails
    private static EdtfDate GetPublicationDate(JsonElement metadata)
    {
        var value = GetString(metadata, "publication_date");

        return EdtfDate.Parse(value) ??
               throw new InvalidOperationException($"Record has no valid EDTF publication_date: '{value}'");
    }

    // Crossref book_metadata requires either up to 6 <isbn> elements or a single <noisbn>
    private static IEnumerable<XElement> Isbns(JsonElement metadata)
    {
        var isbns = new List<string>();

        if (metadata.TryGetProperty("identifiers", out var identifiers) &&
            identifiers.ValueKind == JsonValueKind.Array)
        {
            foreach (var identifier in identifiers.EnumerateArray())
            {
                if (GetString(identifier, "scheme") != "isbn")
                    continue;

                var isbn = GetString(identifier, "identifier")?.Trim();

                if (IsCrossrefIsbn(isbn))
                    isbns.Add(isbn!);
                else
                    Console.WriteLine($"Skipping ISBN not accepted by Crossref schema: {isbn}");
            }
        }

        if (isbns.Count == 0)
            return [new XElement(Crossref + "noisbn", new XAttribute("reason", "monograph"))];

        return isbns.Distinct().Take(6)
            .Select(isbn => new XElement(Crossref + "isbn", new XAttribute("media_type", "print"), isbn));
    }

    // Mirrors isbn_t from common5.3.1.xsd
    private static bool IsCrossrefIsbn(string? isbn) =>
        !string.IsNullOrEmpty(isbn) &&
        isbn.Length is >= 10 and <= 17 &&
        Regex.IsMatch(isbn, @"^(97[89]-)?[0-9][0-9 \-]+[0-9X]$");

    private static XElement? Publisher(JsonElement metadata)
    {
        var publisher = GetString(metadata, "publisher");

        return string.IsNullOrEmpty(publisher)
            ? null
            : new XElement(Crossref + "publisher", new XElement(Crossref + "publisher_name", publisher));
    }

    private static XElement? DoiData(string doi, string recordUrl) =>
        string.IsNullOrEmpty(doi)
            ? null
            : new XElement(Crossref + "doi_data",
                new XElement(Crossref + "doi", doi),
                new XElement(Crossref + "resource", recordUrl));

    // InvenioRDM references are free text ({"reference": "..."}), deposited as unstructured citations
    private static XElement? CitationList(JsonElement metadata)
    {
        if (!metadata.TryGetProperty("references", out var references) ||
            references.ValueKind != JsonValueKind.Array)
            return null;

        // Keys must be unique within the citation_list
        var citations = references.EnumerateArray()
            .Select(reference => GetString(reference, "reference"))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select((text, index) => new XElement(Crossref + "citation",
                new XAttribute("key", $"ref-{index + 1}"),
                new XElement(Crossref + "unstructured_citation", text)))
            .ToList();

        return citations.Count > 0 ? new XElement(Crossref + "citation_list", citations) : null;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string GenerateBatchId()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var uniqueId = System.Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())));

        return $"{timestamp}-{uniqueId}";
    }
}
