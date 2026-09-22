using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace ConverterPoC;

public static class FromJsonConverter
{
    // Throws when the record can't be converted, so the caller can report and skip it
    public static string Convert(
        CrossrefApiClient crossrefApiClient,
        Depositor depositor,
        string dataCiteJsonContents,
        string doi,
        string recordUrl)
    {
        using var dataCiteDoc = JsonDocument.Parse(dataCiteJsonContents);

        var crossrefDoc = ConvertDataCiteToCrossref(crossrefApiClient,
            depositor,
            dataCiteDoc.RootElement,
            doi,
            recordUrl
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

    private static XDocument ConvertDataCiteToCrossref(
        CrossrefApiClient crossrefApiClient,
        Depositor depositor,
        JsonElement dataCiteDoc,
        string doi,
        string recordUrl)
    {
        XNamespace ns = "http://www.crossref.org/schema/5.3.1";
        XNamespace ai = "http://www.crossref.org/AccessIndicators.xsd";
        XNamespace rel = "http://www.crossref.org/relations.xsd";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        XNamespace jats = "http://www.ncbi.nlm.nih.gov/JATS1";

        var crossrefDoc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "doi_batch",
                new XAttribute(XNamespace.Xmlns + "ai", ai.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "jats", jats),
                new XAttribute(XNamespace.Xmlns + "rel", rel.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "xsi", xsi.NamespaceName),
                new XAttribute("version", "5.3.1"),
                new XAttribute(xsi + "schemaLocation",
                    "http://www.crossref.org/schema/5.3.1 http://www.crossref.org/schemas/crossref5.3.1.xsd"
                ),
                BuildHead(ns, depositor),
                BuildBody(ns, dataCiteDoc, jats, crossrefApiClient, doi, recordUrl)
            )
        );

        return crossrefDoc;
    }


    private static XElement BuildBody(
        XNamespace ns, 
        JsonElement root, 
        XNamespace jats,
        CrossrefApiClient crossrefApiClient,
        string doi,
        string recordUrl
    )
    {
        // Vocabulary id (e.g. "publication-book"); the display title can be renamed or translated
        var type = root.GetProperty("metadata")
            .GetProperty("resource_type")
            .GetProperty("id")
            .GetString() ?? "";

        var docType = DocType(ns, root, jats, crossrefApiClient, type, doi, recordUrl);

        return new XElement(ns + "body",
            docType
        );
    }

    private static XElement DocType(XNamespace ns, JsonElement root, XNamespace jats, 
        CrossrefApiClient crossrefApiClient, 
        string type, 
        string? doi,
        string recordUrl)
    {
        if (type == "publication-book")
            return CreateBook(ns, root, jats, doi, recordUrl);

        if (type == "publication-article")
            return CreateJournalElement(ns, root, jats, doi, recordUrl);
        
        return CreatePresentation(ns, root, jats, crossrefApiClient, doi, recordUrl);
    }
    
    private static XElement CreateBook(
        XNamespace xmlns, 
        JsonElement root, 
        XNamespace jats, 
        string? doi,
        string recordUrl)
    {
        var bookElement = new XElement(xmlns + "book",
            new XAttribute("book_type", "monograph"));

        AddBookMetadata(xmlns, bookElement, root, jats, doi, recordUrl);
        
        return bookElement;
    }
    
    private static void AddBookMetadata(
        XNamespace xmlns, 
        XElement bookElement, 
        JsonElement root, 
        XNamespace jats,
        string? doi,
        string recordUrl)
    {
        var bookMetadata = new XElement(xmlns + "book_metadata",
            new XAttribute("language", "en")
        );

        if (root.TryGetProperty("metadata", out var metadata))
        {
            if (metadata.TryGetProperty("creators", out var creatorsElement) &&
                creatorsElement.ValueKind == JsonValueKind.Array)
            {
                var contributors = ContributorsParser.ConvertContributorsToXml(xmlns, root);

                if (contributors.HasElements)
                {
                    bookMetadata.Add(contributors);
                }
            }

            if (metadata.TryGetProperty("title", out var articleTitleElement))
            {
                var articleTitle = articleTitleElement.GetString();
                bookMetadata.Add(new XElement(xmlns + "titles",
                    new XElement(xmlns + "title", articleTitle)
                ));
            }

            AddAbstractFromDataCite(metadata, bookMetadata, jats);

            bookMetadata.Add(new XElement(xmlns + "publication_date",
                GetPublicationDate(metadata).ToCrossref(xmlns)));

            AddIsbn(xmlns, bookMetadata, metadata);

            AddPublisher(xmlns, bookMetadata, metadata);

            if (!string.IsNullOrEmpty(doi))
            {
                bookMetadata.Add(new XElement(xmlns + "doi_data",
                    new XElement(xmlns + "doi", doi),
                    new XElement(xmlns + "resource", recordUrl)
                ));
            }

            AddCitationList(xmlns, bookMetadata, metadata);
        }

        bookElement.Add(bookMetadata);
    }

    // Crossref book_metadata requires either up to 6 <isbn> elements or a single <noisbn>
    private static void AddIsbn(XNamespace xmlns, XElement bookMetadata, JsonElement metadata)
    {
        var isbns = new List<string>();

        if (metadata.TryGetProperty("identifiers", out var identifiers) &&
            identifiers.ValueKind == JsonValueKind.Array)
        {
            foreach (var identifier in identifiers.EnumerateArray())
            {
                if (identifier.TryGetProperty("scheme", out var scheme) &&
                    scheme.GetString() == "isbn" &&
                    identifier.TryGetProperty("identifier", out var value))
                {
                    var isbn = value.GetString()?.Trim();

                    if (IsCrossrefIsbn(isbn))
                        isbns.Add(isbn!);
                    else
                        Console.WriteLine($"Skipping ISBN not accepted by Crossref schema: {isbn}");
                }
            }
        }

        if (isbns.Count == 0)
        {
            bookMetadata.Add(new XElement(xmlns + "noisbn",
                new XAttribute("reason", "monograph")));
            return;
        }

        foreach (var isbn in isbns.Distinct().Take(6))
        {
            bookMetadata.Add(new XElement(xmlns + "isbn",
                new XAttribute("media_type", "print"), isbn));
        }
    }

    // Mirrors isbn_t from common5.3.1.xsd
    private static bool IsCrossrefIsbn(string? isbn) =>
        !string.IsNullOrEmpty(isbn) &&
        isbn.Length is >= 10 and <= 17 &&
        Regex.IsMatch(isbn, @"^(97[89]-)?[0-9][0-9 \-]+[0-9X]$");

    private static void AddPublisher(XNamespace xmlns, XElement bookMetadata, JsonElement metadata)
    {
        var publisher = metadata.GetProperty("publisher").GetString();
        if (!string.IsNullOrEmpty(publisher))
        {
            bookMetadata.Add(new XElement(xmlns + "publisher",
                new XElement(xmlns + "publisher_name", publisher)
            ));
        }
    }
    
    private static XElement CreatePresentation(XNamespace xmlns, JsonElement root, XNamespace jats,
        CrossrefApiClient crossrefApiClient, string? doi, string recordUrl)
    {
        var postedContent = new XElement(xmlns + "posted_content",
            new XAttribute("type", "report")
        );

        if (root.TryGetProperty("metadata", out var metadata))
        {
            if (doi != null)
            {
                var re = crossrefApiClient.DoiExistsAsync(doi).Result;

                if (re != null)
                {
                    Console.WriteLine($"Already published: Doi: {doi}");
                    Console.WriteLine($"Doi: {doi}");
                }
            }

            if (metadata.TryGetProperty("creators", out var creatorsElement) &&
                creatorsElement.ValueKind == JsonValueKind.Array)
            {
                var contributors = ContributorsParser.ConvertContributorsToXml(xmlns, root);

                if (contributors.HasElements)
                {
                    postedContent.Add(contributors);
                }
            }
            
            if (metadata.TryGetProperty("title", out var articleTitleElement))
            {
                var articleTitle = articleTitleElement.GetString();
                postedContent.Add(new XElement(xmlns + "titles",
                    new XElement(xmlns + "title", articleTitle)
                ));
            }
            
            postedContent.Add(new XElement(xmlns + "posted_date",
                GetPublicationDate(metadata).ToCrossref(xmlns)));

            AddAbstractFromDataCite(metadata, postedContent, jats);

            if (!string.IsNullOrEmpty(doi))
            {
                postedContent.Add(new XElement(xmlns + "doi_data",
                    new XElement(xmlns + "doi", doi),
                    new XElement(xmlns + "resource", recordUrl)
                ));
            }

            AddCitationList(xmlns, postedContent, metadata);
        }
        
        return postedContent;
    }

    private static XElement CreateJournalElement(
        XNamespace xmlns, 
        JsonElement root, 
        XNamespace jats, 
        string? doi, 
        string recordUrl)
    {
        var journal = new XElement(xmlns + "journal");
        var journalMetadata = new XElement(xmlns + "journal_metadata");
        var journalIssue = new XElement(xmlns + "journal_issue");
        var journalArticle = new XElement(xmlns + "journal_article");

        journalArticle.SetAttributeValue(XName.Get("publication_type"), "abstract_only");
        
        if (root.TryGetProperty("metadata", out var metadata))
        {
            if (metadata.TryGetProperty("title", out var articleTitleElement))
            {
                var articleTitle = articleTitleElement.GetString();
                journalArticle.Add(new XElement(xmlns + "titles",
                    new XElement(xmlns + "title", articleTitle)
                ));
                
                journalMetadata.Add(new XElement(xmlns + "full_title", articleTitle));
            }

            if (metadata.TryGetProperty("creators", out var creatorsElement) &&
                creatorsElement.ValueKind == JsonValueKind.Array)
            {
                var contributors = ContributorsParser.ConvertContributorsToXml(xmlns, root);

                if (contributors.HasElements)
                {
                    journalArticle.Add(contributors);
                }
            }

            AddAbstractFromDataCite(metadata, journalArticle, jats);

            var publicationDate = GetPublicationDate(metadata);

            journalArticle.Add(new XElement(xmlns + "publication_date",
                publicationDate.ToCrossref(xmlns)));

            if (!string.IsNullOrEmpty(doi))
            {
                journalArticle.Add(new XElement(xmlns + "doi_data",
                    new XElement(xmlns + "doi", doi),
                    new XElement(xmlns + "resource", recordUrl)
                ));
            }

            AddCitationList(xmlns, journalArticle, metadata);

            journalIssue.Add(new XElement(xmlns + "publication_date",
                new XElement(xmlns + "year", publicationDate.Year)
            ));
        }

        journal.Add(journalMetadata);
        journal.Add(journalIssue);
        journal.Add(journalArticle);

        return journal;
    }
    
    private static void AddAbstractFromDataCite(JsonElement root, XElement parentElement, XNamespace jats)
    {
        try
        {
            if (root.TryGetProperty("description", out var description) &&
                description.ValueKind == JsonValueKind.String)
            {
                var paragraphs = HtmlToJats.ToParagraphs(description.GetString(), jats);

                if (paragraphs.Count > 0)
                {
                    parentElement.Add(new XElement(jats + "abstract", paragraphs));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting abstract from DataCite: {ex.Message}");
        }
    }

    // publication_date/posted_date are required by Crossref, so a record without a usable date fails
    private static EdtfDate GetPublicationDate(JsonElement metadata)
    {
        var value = metadata.TryGetProperty("publication_date", out var element) &&
                    element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

        return EdtfDate.Parse(value) ??
               throw new InvalidOperationException($"Record has no valid EDTF publication_date: '{value}'");
    }

    private static void AddCitationList(XNamespace xmlns, XElement parentElement, JsonElement metadata)
    {
        if (!metadata.TryGetProperty("references", out var references) ||
            references.ValueKind != JsonValueKind.Array)
            return;

        // Keys must be unique within the citation_list
        var citations = references.EnumerateArray()
            .Select((reference, index) => ProcessCitation(xmlns, reference, $"ref-{index + 1}"))
            .ToList();

        if (citations.Count > 0)
            parentElement.Add(new XElement(xmlns + "citation_list", citations));
    }

    private static XElement ProcessCitation(XNamespace xmlns, JsonElement reference, string key)
    {
        if (reference.TryGetProperty("reference", out var refValue))
        {
            var value = refValue.GetString();
            return new XElement(xmlns + "citation", new XAttribute("key", key),
                new XElement(xmlns + "unstructured_citation", value)
            );
        }

        if (reference.TryGetProperty("raw_reference", out var rawRef))
        {
            return new XElement("unstructured_citation", new XAttribute("key", key), rawRef.GetString());
        }

        var citation = new XElement("citation");

        var elements = new List<XElement>();

        if (reference.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array)
        {
            var authorsText = string.Join(", ", authors.EnumerateArray().Select(a => a.GetString()));
            if (!string.IsNullOrEmpty(authorsText))
            {
                elements.Add(new XElement("author", authorsText));
            }
        }

        if (reference.TryGetProperty("title", out var title))
        {
            elements.Add(new XElement("article_title", title.GetString()));
        }

        if (reference.TryGetProperty("journal", out var journal))
        {
            elements.Add(new XElement("journal_title", journal.GetString()));
        }

        if (reference.TryGetProperty("volume", out var volume))
        {
            elements.Add(new XElement("volume", volume.GetString()));
        }

        if (reference.TryGetProperty("issue", out var issue))
        {
            elements.Add(new XElement("issue", issue.GetString()));
        }

        if (reference.TryGetProperty("first_page", out var firstPage))
        {
            elements.Add(new XElement("first_page", firstPage.GetString()));
        }

        if (reference.TryGetProperty("year", out var year))
        {
            elements.Add(new XElement("cYear", year.GetString()));
        }

        if (reference.TryGetProperty("doi", out var doi))
        {
            elements.Add(new XElement("doi", doi.GetString()));
        }

        citation.Add(elements);

        return citation;
    }

    private static XElement BuildHead(XNamespace crossrefNs, Depositor depositor)
    {
        var batchId = GenerateBatchId();

        return new XElement(crossrefNs + "head",
            new XElement(crossrefNs + "doi_batch_id", batchId),
            new XElement(crossrefNs + "timestamp", DateTime.UtcNow.ToString("yyyyMMddHHmmss")), new XElement(
                crossrefNs + "depositor",
                new XElement(crossrefNs + "depositor_name", depositor.Name),
                new XElement(crossrefNs + "email_address", depositor.Email)
            ),
            new XElement(crossrefNs + "registrant", depositor.Registrant)
        );
    }

    private static string GenerateBatchId()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        var uniqueId = GenerateSha1Hash(Guid.NewGuid().ToString());

        return $"{timestamp}-{uniqueId}";
    }

    private static string GenerateSha1Hash(string input)
    {
        using var sha1 = SHA1.Create();
        var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder();
        foreach (var b in hashBytes)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }
}