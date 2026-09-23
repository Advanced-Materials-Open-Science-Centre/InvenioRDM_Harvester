using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace ConverterPoC;

// Validates deposit XML against the Crossref 5.3.1 schema, so a bad deposit fails before upload
// instead of in Crossref's asynchronous submission log. The schema files are downloaded from
// crossref.org the first time they're needed and cached in CacheDirectory.
public static class CrossrefSchema
{
    private const string Namespace = "http://www.crossref.org/schema/5.3.1";

    // Cached files keep their path relative to this location, so relative imports resolve the same way
    private static readonly Uri BaseUri = new("https://www.crossref.org/schemas/");

    public static readonly string CacheDirectory = Path.Combine(AppContext.BaseDirectory, "Schemas");

    private static readonly Lazy<XmlSchemaSet> Schemas = new(Compile);

    // Returns the schema errors; empty when the document is valid
    public static IReadOnlyList<string> Validate(string xml)
    {
        var errors = new List<string>();

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Schemas.Value,
            XmlResolver = null
        };

        settings.ValidationEventHandler += (_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error)
                errors.Add($"line {e.Exception.LineNumber}: {e.Message}");
        };

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            while (reader.Read()) { }
        }
        catch (XmlException ex)
        {
            errors.Add($"Malformed XML: {ex.Message}");
        }

        return errors;
    }

    private static XmlSchemaSet Compile()
    {
        var schemas = new XmlSchemaSet { XmlResolver = new CachingSchemaResolver() };

        // The JATS modules report "Empty choice" warnings; only errors matter
        schemas.ValidationEventHandler += (_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error)
                throw e.Exception;
        };

        schemas.Add(Namespace, new Uri(BaseUri, "crossref5.3.1.xsd").AbsoluteUri);
        schemas.Compile();

        return schemas;
    }

    private sealed class CachingSchemaResolver : XmlResolver
    {
        private static readonly Uri MathMlUri = new("http://www.w3.org/Math/XMLSchema/mathml3/");

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(1) };

        public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
        {
            var resolved = base.ResolveUri(baseUri, relativeUri);

            // crossref5.3.1.xsd imports MathML from w3.org, JATS the copy on crossref.org; loading
            // both would declare every MathML element twice
            return MathMlUri.IsBaseOf(resolved)
                ? new Uri(BaseUri, "standard-modules/mathml3/" + resolved.Segments[^1])
                : resolved;
        }

        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var path = BaseUri.IsBaseOf(absoluteUri) ? BaseUri.MakeRelativeUri(absoluteUri).OriginalString : null;

            if (path == null || path.Contains(".."))
                throw new XmlSchemaException($"Schema {absoluteUri} is not under {BaseUri}");

            var file = Path.Combine(CacheDirectory, path.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(file))
                Download(absoluteUri, file);

            return File.OpenRead(file);
        }

        private static void Download(Uri uri, string file)
        {
            Console.WriteLine($"Downloading {uri}");

            byte[] content;

            try
            {
                content = Http.GetByteArrayAsync(uri).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new XmlSchemaException($"Could not download the Crossref schema {uri}: {ex.Message}", ex);
            }

            // Never cache an error page: it would break validation until deleted by hand
            if (!IsSchema(content))
                throw new XmlSchemaException($"{uri} is not an XML schema");

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            var temp = file + ".tmp";
            File.WriteAllBytes(temp, content);
            File.Move(temp, file, overwrite: true);
        }

        private static bool IsSchema(byte[] content)
        {
            try
            {
                return XDocument.Load(new MemoryStream(content)).Root?.Name.LocalName == "schema";
            }
            catch (XmlException)
            {
                return false;
            }
        }
    }
}
