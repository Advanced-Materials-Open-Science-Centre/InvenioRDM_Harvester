using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
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
        var fileLink = GetFileLink(root);
        var journalIssnAndName = fileLink != null
            ? ExtractJournalIssnAndTitle(fileLink, invenioRdmClient, crossrefApiClient)
            : null;

        var type = root.GetProperty("metadata")
            .GetProperty("resource_type")
            .GetProperty("title")
            .GetProperty("en")
            .GetString() ?? "";
        
        if (journalIssnAndName == null)
        {
            journalIssnAndName = TryLookForDoi(root, crossrefApiClient, type);
        }

        var docType = DocType(ns, root, jats, crossrefApiClient, journalIssnAndName, type);

        return new XElement(ns + "body",
            docType
        );
    }

    private static XElement DocType(XNamespace ns, JsonElement root, XNamespace jats, CrossrefApiClient crossrefApiClient, (string, string)? journalIssnAndName, string type)
    {
        if (type == "Book")
            return CreateBook(ns, root, jats, journalIssnAndName);
        
        return journalIssnAndName != null
            ? type == "Book"
                ? CreateBook(ns, root, jats, journalIssnAndName.Value)
                : CreateJournalElement(ns, root, jats, journalIssnAndName.Value)
            : CreatePresentation(ns, root, jats, crossrefApiClient, type);
    }

    private static (string, string)? TryLookForDoi(
        JsonElement root, 
        CrossrefApiClient crossrefApiClient,
        string publicationType)
    {
        if (root.TryGetProperty("metadata", out _))
        {
            var pdfLink = GetFileLink(root);

            var doi = ExtractDoi(root, pdfLink, publicationType);

            if (doi != null)
            {
                var re = crossrefApiClient.DoiExistsAsync(doi).Result;

                if (re != null)
                {
                    Console.WriteLine($"Already published: Doi: {doi}");
                    Console.WriteLine($"Doi: {doi}");

                    if (re.Message.Issn.Any())
                    {
                        var issn = re.Message.Issn.First();
                        var journalName = crossrefApiClient.GetJournalTitleByISSN(issn).Result;

                        return (journalName, issn);
                    }
                }
            }
        }
        
        return null;
    }
    
    private static XElement CreateBook(XNamespace xmlns, JsonElement root, XNamespace jats, (string, string)? title)
    {
        var bookElement = new XElement(xmlns + "book",
            new XAttribute("book_type", "monograph"));

        AddBookMetadata(xmlns, bookElement, root, jats, title?.Item2);
        
        return bookElement;
    }
    
    private static void AddBookMetadata(XNamespace xmlns, XElement bookElement, JsonElement root, XNamespace jats,
        string? isbn)
    {
        var bookMetadata = new XElement(xmlns + "book_metadata",
            new XAttribute("language", "en")
        );

        if (root.TryGetProperty("metadata", out var metadata))
        {
            var pdfLink = GetFileLink(root);

            var doi = ExtractDoi(root, pdfLink, isbn);

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
            
            if (metadata.TryGetProperty("publication_date", out var pubDateElement))
            {
                var pubDate = pubDateElement.GetString();
                if (DateTime.TryParse(pubDate, out var parsedDate))
                {
                    bookMetadata.Add(new XElement(xmlns + "publication_date",
                        new XElement(xmlns + "month", parsedDate.Month.ToString("D2")),
                        new XElement(xmlns + "day", parsedDate.Day.ToString("D2")),
                        new XElement(xmlns + "year", parsedDate.Year)
                    ));
                }
            }

            AddIsbn(xmlns, bookMetadata, isbn);
            
            AddPublisher(xmlns, bookMetadata, metadata);

            if (!string.IsNullOrEmpty(doi))
            {
                bookMetadata.Add(new XElement(xmlns + "doi_data",
                    new XElement(xmlns + "doi", doi),
                    new XElement(xmlns + "resource", pdfLink)
                ));
            }

            JsonElement references;
            if (metadata.TryGetProperty("references", out references) && references.ValueKind == JsonValueKind.Array)
            {
                var citationList = new XElement(xmlns + "citation_list");
                bookMetadata.Add(citationList);

                foreach (var reference in references.EnumerateArray())
                {
                    var citation = ProcessCitation(xmlns, reference);
                    citationList.Add(citation);
                }
            }
        }

        bookElement.Add(bookMetadata);
    }

    private static void AddIsbn(XNamespace xmlns, XElement bookMetadata, string? isbn)
    {
        if (!string.IsNullOrEmpty(isbn))
        {
            var cleanIsbn = isbn.Replace("-", "").Replace(" ", "");

            if (cleanIsbn.Length == 13)
            {
                bookMetadata.Add(new XElement(xmlns + "isbn",
                    new XAttribute("media_type", "print"), cleanIsbn));
            }
        }
        else
        {
            bookMetadata.Add(new XElement(xmlns + "noisbn",
                new XAttribute("reason", "monograph"))); 
        }
    }

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
        CrossrefApiClient crossrefApiClient, string publicationType)
    {
        var postedContent = new XElement(xmlns + "posted_content",
            new XAttribute("type", "report")
        );

        if (root.TryGetProperty("metadata", out var metadata))
        {
            var pdfLink = GetFileLink(root);
           
            var doi = ExtractDoi(root, pdfLink, publicationType);

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
            var pdfLink = GetFileLink(root);
            
            journalMetadata.Add(new XElement(xmlns + "full_title", journalIssnAndName.Item1));
            journalMetadata.Add(new XElement(xmlns + "issn", journalIssnAndName.Item2));
            
            var doi = ExtractDoi(root, pdfLink, journalIssnAndName.Item2);

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

    private static (string, string)? ExtractJournalIssnAndTitle(string fileLink,
        InvenioRDMClient invenioRdmClient, CrossrefApiClient crossrefApiClient)
    {
        var file = invenioRdmClient.GetAsync(fileLink).Result;
        
        if (file == null)
            return null;

        var bytes = file;

        var issn = ExtractIssn(bytes);

        if (issn == null)
        {
            var isbn = ExtractISBNFromPdf(bytes);

            if (isbn != null)
            {
                var journalName3 = crossrefApiClient.SearchCrossrefByIsbnAsync(isbn).Result;
                
                if(journalName3 != null)
                    return (journalName3.Title.FirstOrDefault() ?? "", isbn);

                return (null, isbn);
            }
        }
        
        var journalName = crossrefApiClient.GetJournalTitleByISSN(issn).Result;

        return (journalName, issn);
    }

    private static string? ExtractIssn(byte[] pdfPath)
    {
        try
        {
            return ExtractISSNFromPdf(pdfPath);
        }
        catch (Exception e)
        {
            try
            {
                return ExtractISSNFromDocx(pdfPath);
            }
            catch (Exception e1)
            {
                try
                {
                    return ExtractISBNFromPdf(pdfPath);
                }
                catch (Exception e2)
                {
                    return null;
                }
            }
        }
    }

    private static string? ExtractISBNFromPdf(byte[] contents)
    {
        using var document = PdfDocument.Open(contents);
        foreach (var page in document.GetPages())
        {
            var text = page.Text;

            var match = Regex.Match(text, @"ISBN[\s:]*([0-9]{3}-[0-9]{3}-[0-9]{3}-[0-9]{3}-[0-9]{1})");
            if (match.Success)
            {
                string isbn = match.Value
                    .Replace("ISBN","")    
                    .Replace("-", "").Replace(" ", "");
                if (IsValidIsbn(isbn))
                    return match.Groups[1].Value.Replace("ISBN","")  ;
            }
        }

        return null;
    }
    
    static bool IsValidIsbn(string isbn)
    {
        if (isbn.Length == 10)
        {
            int sum = 0;
            for (int i = 0; i < 9; i++)
                sum += (10 - i) * (isbn[i] - '0');
            char check = isbn[9];
            sum += (check == 'X' || check == 'x') ? 10 : (check - '0');
            return sum % 11 == 0;
        }
        else if (isbn.Length == 13)
        {
            int sum = 0;
            for (int i = 0; i < 12; i++)
                sum += (isbn[i] - '0') * ((i % 2 == 0) ? 1 : 3);
            int checkDigit = (10 - (sum % 10)) % 10;
            return checkDigit == (isbn[12] - '0');
        }
        return false;
    }
    
    private static string? ExtractISSNFromDocx(byte[] pdfPath)
    {
        using var stream = new MemoryStream(pdfPath);
        var text = OpenXmlWordSearcher.ReadTextFromDocx(stream);

        
        var match = Regex.Match(text, @"ISSN[\s:]*([0-9]{4}-[0-9]{3}[\dXx])");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        
        return null;
    }

    private static string? ExtractISSNFromPdf(byte[] contents)
    {
        using var document = PdfDocument.Open(contents);
        foreach (var page in document.GetPages())
        {
            var text = page.Text;

            var match = Regex.Match(text, @"ISSN[\s:]*([0-9]{4}-[0-9]{3}[\dXx])");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static string? GetFileLink(JsonElement root)
    {
        if (root.TryGetProperty("files", out var files) &&
            files.TryGetProperty("entries", out var entries))
        {
            foreach (var p in entries.EnumerateObject())
            {
                var file = p.Value;

                if (file.TryGetProperty("mimetype", out var mimetype) &&
                    !string.IsNullOrEmpty(mimetype.GetString()))
                {
                    if (file.TryGetProperty("links", out var links) &&
                        links.TryGetProperty("content", out var fileLink))
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

    private static string? ExtractDoi(JsonElement root, string? pdfLink, string publicationType, string? isbn = null)
    {
        if (string.Equals(publicationType, "dataset", StringComparison.InvariantCultureIgnoreCase))
        {
            var now = DateTime.Now;
            var existingDoi = DatasetDoiNumberProvider.GetCurrentAndSaveNewDoiNumber(now);

            var generatedDoi = "10.15330/dataset." + now.ToString("yy.MM") + "." + existingDoi.ToString("00");
            Console.WriteLine("Generated doi: " + generatedDoi);
            return generatedDoi;
        }
        
        try
        {
            if (root.TryGetProperty("metadata", out var metadata) &&
                metadata.TryGetProperty("identifiers", out var identifiers))
            {
                foreach (var identifier in identifiers.EnumerateArray())
                {
                    if (identifier.TryGetProperty("scheme", out var scheme) &&
                        scheme.GetString() == "crossreffunderid" &&
                        identifier.TryGetProperty("identifier", out var doiElement1))
                    {
                        return doiElement1.GetString()?.Replace("DOI:", "");
                    }
                    
                    if (identifier.TryGetProperty("scheme", out var scheme2) &&
                        scheme2.GetString() == "doi" &&
                        identifier.TryGetProperty("identifier", out var doiElement2))
                    {
                        return doiElement2.GetString();
                    }
                }
            }

            string generatedDoi;

            if (isbn != null)
                generatedDoi = "10.15330/" + isbn.Replace("-", "");
            else
                generatedDoi = "10.15330/" + GenerateSuffixFromFileLink(pdfLink);
            Console.WriteLine("Generated doi: " + generatedDoi);
            return generatedDoi;
        }
        catch
        {
        }

        return null;
    }

    private static string GenerateSuffixFromFileLink(string? pdfLink)
    {
        if (pdfLink != null)
        {
            var decoded = WebUtility.UrlDecode(pdfLink);
            
            var fileNamePart = decoded.Split("/").FirstOrDefault(t => t.Contains(".pdf") || t.Contains(".docx")) ?? "";

            return string.Join("", fileNamePart
                    .Where(t => char.IsAsciiDigit(t) || t == '_' || t == '-')
                    .Select(t => char.IsAsciiDigit(t) ? t : '.'))
                .Trim('.');
        }

        return Guid.NewGuid().ToString("D").Replace("-", ".");
    }

    private static XElement BuildHead(XNamespace crossrefNs, JsonElement root)
    {
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
                new XElement(crossrefNs + "email_address", givenName + "." + surName + "@pnu.edu.ua")
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