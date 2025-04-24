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
    public static string? Convert(InvenioRDMClient invenioRdmClient, CrossrefApiClient crossrefApiClient, string dataCiteJsonContents)
    {
        try
        {
            using var dataCiteDoc = JsonDocument.Parse(dataCiteJsonContents);

            var crossrefDoc = ConvertDataCiteToCrossref(invenioRdmClient, crossrefApiClient, dataCiteDoc.RootElement);

            using var memoryStream = new MemoryStream();

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
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        return null;
    }

    static XDocument ConvertDataCiteToCrossref(InvenioRDMClient invenioRdmClient, CrossrefApiClient crossrefApiClient, JsonElement dataCiteDoc)
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
                BuildHead(ns, dataCiteDoc),
                BuildBody(ns, dataCiteDoc, jats, invenioRdmClient, crossrefApiClient)
            )
        );

        return crossrefDoc;
    }


    private static XElement BuildBody(
        XNamespace ns, 
        JsonElement root, 
        XNamespace jats,
        InvenioRDMClient invenioRdmClient,
        CrossrefApiClient crossrefApiClient
    )
    {
        var pdfLink = GetPdfFileLink(root);
        var journalIssnAndName = pdfLink != null
            ? ExtractJournalIssnAndTitle(pdfLink, invenioRdmClient, crossrefApiClient)
            : null;

        var docType = journalIssnAndName != null
            ? CreateJournalElement(ns, root, jats, journalIssnAndName.Value)
            : CreatePresentation(ns, root, jats);

        return new XElement(ns + "body",
            docType
        );
    }

    private static XElement CreatePresentation(XNamespace xmlns, JsonElement root, XNamespace jats)
    {
        var postedContent = new XElement(xmlns + "posted_content",
            new XAttribute("type", "report")
        );

        if (root.TryGetProperty("metadata", out var metadata))
        {
            var pdfLink = GetPdfFileLink(root);
            
            var doi = ExtractDoi(root);

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
            
            if (metadata.TryGetProperty("publication_date", out var pubDateElement))
            {
                var pubDate = pubDateElement.GetString();
                if (DateTime.TryParse(pubDate, out var parsedDate))
                {
                    postedContent.Add(new XElement(xmlns + "posted_date",
                        new XElement(xmlns + "month", parsedDate.Month.ToString("D2")),
                        new XElement(xmlns + "day", parsedDate.Day.ToString("D2")),
                        new XElement(xmlns + "year", parsedDate.Year)
                    ));
                }
            }

            AddAbstractFromDataCite(metadata, postedContent, jats);
            
            if (!string.IsNullOrEmpty(doi))
            {
                postedContent.Add(new XElement(xmlns + "doi_data",
                    new XElement(xmlns + "doi", doi),
                    new XElement(xmlns + "resource", pdfLink)
                ));
            }

            JsonElement references;
            if (metadata.TryGetProperty("references", out references) && references.ValueKind == JsonValueKind.Array)
            {
                var citationList = new XElement(xmlns + "citation_list");
                postedContent.Add(citationList);

                foreach (var reference in references.EnumerateArray())
                {
                    var citation = ProcessCitation(xmlns, reference);
                    citationList.Add(citation);
                }
            }
        }

        /*
        if (metadata.TryGetProperty("publication_date", out var pubDateE))
        {
            var pubDate = pubDateE.GetString();

            if (DateTime.TryParse(pubDate, out var parsedDate))
            {
                postedContent.Add(new XElement(xmlns + "publication_date",
                    new XElement(xmlns + "year", parsedDate.Year)
                ));
            }
        }*/

       // postedContent.Add(journalMetadata);
       // postedContent.Add(journalIssue);
       // postedContent.Add(journalArticle);

        return postedContent;
    }

    private static XElement CreateJournalElement(XNamespace xmlns, JsonElement root, XNamespace jats, 
        (string, string) journalIssnAndName)
    {
        var journal = new XElement(xmlns + "journal");
        var journalMetadata = new XElement(xmlns + "journal_metadata");
        var journalIssue = new XElement(xmlns + "journal_issue");
        var journalArticle = new XElement(xmlns + "journal_article");

        journalArticle.SetAttributeValue(XName.Get("publication_type"), "abstract_only");
        
        if (root.TryGetProperty("metadata", out var metadata))
        {
            var pdfLink = GetPdfFileLink(root);
            
            journalMetadata.Add(new XElement(xmlns + "full_title", journalIssnAndName.Item1));
            journalMetadata.Add(new XElement(xmlns + "issn", journalIssnAndName.Item2));
            
            var doi = ExtractDoi(root);

            if (metadata.TryGetProperty("title", out var articleTitleElement))
            {
                var articleTitle = articleTitleElement.GetString();
                journalArticle.Add(new XElement(xmlns + "titles",
                    new XElement(xmlns + "title", articleTitle)
                ));
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

            if (metadata.TryGetProperty("publication_date", out var pubDateElement))
            {
                var pubDate = pubDateElement.GetString();
                if (DateTime.TryParse(pubDate, out var parsedDate))
                {
                    journalArticle.Add(new XElement(xmlns + "publication_date",
                        new XElement(xmlns + "month", parsedDate.Month.ToString("D2")),
                        new XElement(xmlns + "day", parsedDate.Day.ToString("D2")),
                        new XElement(xmlns + "year", parsedDate.Year)
                    ));
                }
            }

            if (!string.IsNullOrEmpty(doi))
            {
                journalArticle.Add(new XElement(xmlns + "doi_data",
                    new XElement(xmlns + "doi", doi),
                    new XElement(xmlns + "resource", pdfLink)
                ));
            }

            JsonElement references;
            if (metadata.TryGetProperty("references", out references) && references.ValueKind == JsonValueKind.Array)
            {
                var citationList = new XElement(xmlns + "citation_list");
                journalArticle.Add(citationList);

                foreach (var reference in references.EnumerateArray())
                {
                    var citation = ProcessCitation(xmlns, reference);
                    citationList.Add(citation);
                }
            }
        }

        if (metadata.TryGetProperty("publication_date", out var pubDateE))
        {
            var pubDate = pubDateE.GetString();

            if (DateTime.TryParse(pubDate, out var parsedDate))
            {
                journalIssue.Add(new XElement(xmlns + "publication_date",
                    new XElement(xmlns + "year", parsedDate.Year)
                ));
            }
        }

        journal.Add(journalMetadata);
        journal.Add(journalIssue);
        journal.Add(journalArticle);

        return journal;
    }

    private static (string, string)? ExtractJournalIssnAndTitle(string pdfLink,
        InvenioRDMClient invenioRdmClient, CrossrefApiClient crossrefApiClient)
    {
        var pdf = invenioRdmClient.GetAsync(pdfLink).Result;

        if (pdf == null)
            return null;

        var bytes = pdf;

        var issn = ExtractIssn(bytes);

        if (issn == null)
            return null;

        var journalName = crossrefApiClient.GetJournalTitleByISSN(issn).Result;

        return (journalName, issn);
    }
    
    private static string? ExtractIssn(byte[] pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        foreach (var page in document.GetPages())
        {
            string text = page.Text;

            var match = Regex.Match(text, @"ISSN[\s:]*([0-9]{4}-[0-9]{3}[\dXx])");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static string? GetPdfFileLink(JsonElement root)
    {
        if (root.TryGetProperty("files", out JsonElement files) &&
            files.TryGetProperty("entries", out JsonElement entries))
        {
            foreach (JsonProperty p in entries.EnumerateObject())
            {
                var file = p.Value;

                if (file.TryGetProperty("mimetype", out JsonElement mimetype) &&
                    !string.IsNullOrEmpty(mimetype.GetString()))
                {
                    if (file.TryGetProperty("links", out JsonElement links) &&
                        links.TryGetProperty("content", out JsonElement fileLink))
                    {
                        return fileLink.GetString();
                    }
                }
            }
        }

        return null;
    }

    private static void AddAbstractFromDataCite(JsonElement root, XElement parentElement, XNamespace jats)
    {
        try
        {
            if (root.TryGetProperty("description", out var abstractText))
            {
                {
                    var abstractElement = new XElement(jats + "abstract",
                        new XElement(jats + "p", abstractText)
                    );

                    parentElement.Add(abstractElement);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting abstract from DataCite: {ex.Message}");
        }
    }

    private static XElement ProcessCitation(XNamespace xmlns, JsonElement reference)
    {
        var key = "ref-" + Guid.NewGuid().ToString("N").Substring(0, 3);

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

    private static string ExtractDoi(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("metadata", out var metadata) &&
                metadata.TryGetProperty("identifiers", out var identifiers))
            {
                foreach (var identifier in identifiers.EnumerateArray())
                {
                    if (identifier.TryGetProperty("scheme", out var scheme) &&
                        scheme.GetString() == "doi" &&
                        identifier.TryGetProperty("identifier", out var doiElement))
                    {
                        return doiElement.GetString();
                    }
                }
            }

            if (root.TryGetProperty("id", out var id))
            {
                return id.GetString();
            }
        }
        catch
        {
        }

        return null;
    }

    private static XElement BuildHead(XNamespace crossrefNs, JsonElement root)
    {
        XNamespace ns = "http://datacite.org/schema/kernel-4";

        var publisher = "Vasyl Stefanyk Precarpathian National University";

        var creators = ContributorsParser.ConvertContributorsToXml(crossrefNs, root);

        var c = creators.Descendants().First();

        var a = c.Nodes().ToList();

        var givenName = ((XElement)a[0]).Value;
        var surName = ((XElement)a[1]).Value;

        var creatorName = $"{surName}, {givenName}";
        var batchId = GenerateBatchId();

        return new XElement(crossrefNs + "head",
            new XElement(crossrefNs + "doi_batch_id", batchId),
            new XElement(crossrefNs + "timestamp", DateTime.UtcNow.ToString("yyyyMMddHHmmss")), new XElement(
                crossrefNs + "depositor",
                new XElement(crossrefNs + "depositor_name", creatorName),
                new XElement(crossrefNs + "email_address", "test@example.com")
            ),
            new XElement(crossrefNs + "registrant", publisher)
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