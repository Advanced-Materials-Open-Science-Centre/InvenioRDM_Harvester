using System.Text.Json;
using System.Xml.Linq;

namespace ConverterPoC;

// Funding (fr:program), license (ai:program) and relations (rel:program) from an InvenioRDM
// record; every Crossref content type takes them in this order, right before doi_data.
public static class CrossrefPrograms
{
    public static readonly XNamespace FundRef = "http://www.crossref.org/fundref.xsd";
    public static readonly XNamespace AccessIndicators = "http://www.crossref.org/AccessIndicators.xsd";
    public static readonly XNamespace Relations = "http://www.crossref.org/relations.xsd";

    // Crossref relationship types by InvenioRDM (DataCite) relation id; the "intra" ones relate
    // expressions of the same work. DataCite relations without a Crossref equivalent (e.g.
    // hasmetadata) are skipped.
    private static readonly Dictionary<string, (string Type, bool Intra)> RelationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cites"] = ("references", false),
        ["iscitedby"] = ("isReferencedBy", false),
        ["references"] = ("references", false),
        ["isreferencedby"] = ("isReferencedBy", false),
        ["issupplementto"] = ("isSupplementTo", false),
        ["issupplementedby"] = ("isSupplementedBy", false),
        ["ispartof"] = ("isPartOf", false),
        ["haspart"] = ("hasPart", false),
        ["documents"] = ("documents", false),
        ["isdocumentedby"] = ("isDocumentedBy", false),
        ["compiles"] = ("compiles", false),
        ["iscompiledby"] = ("isCompiledBy", false),
        ["continues"] = ("continues", false),
        ["iscontinuedby"] = ("isContinuedBy", false),
        ["isderivedfrom"] = ("isDerivedFrom", false),
        ["issourceof"] = ("hasDerivation", false),
        ["requires"] = ("requires", false),
        ["isrequiredby"] = ("isRequiredBy", false),
        ["reviews"] = ("isReviewOf", false),
        ["isreviewedby"] = ("hasReview", false),
        ["isversionof"] = ("isVersionOf", true),
        ["hasversion"] = ("hasVersion", true),
        ["isnewversionof"] = ("isVersionOf", true),
        ["ispreviousversionof"] = ("hasVersion", true),
        ["isidenticalto"] = ("isIdenticalTo", true),
        ["isvariantformof"] = ("isVariantFormOf", true),
        ["isoriginalformof"] = ("isOriginalFormOf", true),
        ["istranslationof"] = ("isTranslationOf", true),
        ["hastranslation"] = ("hasTranslation", true),
    };

    // Crossref identifier-type by InvenioRDM identifier scheme
    private static readonly Dictionary<string, string> IdentifierTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["doi"] = "doi",
        ["isbn"] = "isbn",
        ["issn"] = "issn",
        ["url"] = "uri",
        ["handle"] = "handle",
        ["ark"] = "ark",
        ["arxiv"] = "arxiv",
        ["pmid"] = "pmid",
        ["pmcid"] = "pmcid",
        ["purl"] = "purl",
    };

    public static IEnumerable<XElement> For(JsonElement metadata) =>
        new[] { Funding(metadata), License(metadata), RelatedItems(metadata) }.OfType<XElement>();

    // One fundgroup per award; funders are identified by ROR id, falling back to their name
    public static XElement? Funding(JsonElement metadata)
    {
        var groups = Array(metadata, "funding")
            .Select(funding =>
            {
                var funder = Object(funding, "funder");
                var rorId = String(funder, "id");
                var name = String(funder, "name");

                XElement? funderAssertion =
                    !string.IsNullOrWhiteSpace(rorId) ? Assertion("ror", "https://ror.org/" + rorId.Trim()) :
                    !string.IsNullOrWhiteSpace(name) ? Assertion("funder_name", name.Trim()) :
                    null;

                if (funderAssertion == null)
                    return null;

                var award = String(Object(funding, "award"), "number");

                return Assertion("fundgroup", funderAssertion,
                    string.IsNullOrWhiteSpace(award) ? null : Assertion("award_number", award.Trim()));
            })
            .OfType<XElement>()
            .ToList();

        return groups.Count > 0 ? new XElement(FundRef + "program", new XAttribute("name", "fundref"), groups) : null;
    }

    // License URLs from the record's rights; they apply to the deposited version of record
    public static XElement? License(JsonElement metadata)
    {
        var licenses = Array(metadata, "rights")
            .Select(rights => String(Object(rights, "props"), "url") ?? String(rights, "link"))
            .Where(url => url != null && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                          (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .Distinct()
            .Select(url => new XElement(AccessIndicators + "license_ref", new XAttribute("applies_to", "vor"), url))
            .ToList();

        return licenses.Count > 0
            ? new XElement(AccessIndicators + "program", new XAttribute("name", "AccessIndicators"), licenses)
            : null;
    }

    public static XElement? RelatedItems(JsonElement metadata)
    {
        var items = Array(metadata, "related_identifiers")
            .Select(related =>
            {
                var identifier = String(related, "identifier")?.Trim();

                if (string.IsNullOrEmpty(identifier) ||
                    !RelationTypes.TryGetValue(String(Object(related, "relation_type"), "id") ?? "", out var relation))
                    return null;

                var identifierType = IdentifierTypes.GetValueOrDefault(String(related, "scheme") ?? "", "other");

                return new XElement(Relations + "related_item",
                    new XElement(Relations + (relation.Intra ? "intra_work_relation" : "inter_work_relation"),
                        new XAttribute("relationship-type", relation.Type),
                        new XAttribute("identifier-type", identifierType),
                        identifier));
            })
            .OfType<XElement>()
            .ToList();

        return items.Count > 0 ? new XElement(Relations + "program", new XAttribute("name", "relations"), items) : null;
    }

    private static XElement Assertion(string name, params object?[] content) =>
        new(FundRef + "assertion", new XAttribute("name", name), content);

    private static IEnumerable<JsonElement> Array(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static JsonElement Object(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static string? String(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
