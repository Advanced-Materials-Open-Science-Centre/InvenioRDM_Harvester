using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace ConverterPoC;

// Validates deposit XML against the Crossref 5.3.1 schema bundled in Schemas/, so a bad
// deposit fails before upload instead of in Crossref's asynchronous submission log.
public static class CrossrefSchema
{
    private const string Namespace = "http://www.crossref.org/schema/5.3.1";

    // The embedded files mirror this location, so relative imports resolve the same way
    private static readonly Uri BaseUri = new("https://www.crossref.org/schemas/");

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
        var schemas = new XmlSchemaSet { XmlResolver = new EmbeddedSchemaResolver() };

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

    private sealed class EmbeddedSchemaResolver : XmlResolver
    {
        private static readonly Uri MathMlUri = new("http://www.w3.org/Math/XMLSchema/mathml3/");

        private static readonly Assembly Assembly = typeof(CrossrefSchema).Assembly;

        // RecursiveDir in the resource names uses the build machine's path separator
        private static readonly Dictionary<string, string> Resources = Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Schemas/"))
            .ToDictionary(name => name["Schemas/".Length..].Replace('\\', '/'));

        public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
        {
            var resolved = base.ResolveUri(baseUri, relativeUri);

            // crossref5.3.1.xsd imports MathML from w3.org and JATS from the bundled copy;
            // loading both would declare every MathML element twice
            return MathMlUri.IsBaseOf(resolved)
                ? new Uri(BaseUri, "standard-modules/mathml3/" + resolved.Segments[^1])
                : resolved;
        }

        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var path = BaseUri.IsBaseOf(absoluteUri) ? BaseUri.MakeRelativeUri(absoluteUri).OriginalString : null;

            if (path == null || !Resources.TryGetValue(path, out var resourceName))
                throw new XmlSchemaException($"Schema {absoluteUri} is not bundled in Schemas/");

            return Assembly.GetManifestResourceStream(resourceName)!;
        }
    }
}
