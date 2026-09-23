using System.Text.Json;
using System.Xml.Linq;

namespace ConverterPoC;

public class ContributorsParser
{
    // InvenioRDM contributor role id -> Crossref contributor_role. Crossref has no roles for the
    // data-related ones (datamanager, datacollector, contactperson, ...), so those are skipped.
    private static readonly Dictionary<string, string> CrossrefRoles = new()
    {
        ["editor"] = "editor",
    };

    public static XElement ConvertContributorsToXml(XNamespace nameSpace, JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("metadata", out var metadata))
            {
                throw new Exception("No metadata found in the InvenioRDM JSON");
            }

            // Creators are the authors of the work regardless of their InvenioRDM role
            var entries = new List<(JsonElement Contributor, string Role)>();

            if (metadata.TryGetProperty("creators", out var creators) &&
                creators.ValueKind == JsonValueKind.Array)
            {
                entries.AddRange(creators.EnumerateArray().Select(c => (c, "author")));
            }

            var skippedRoles = new List<string>();

            if (metadata.TryGetProperty("contributors", out var contributors) &&
                contributors.ValueKind == JsonValueKind.Array)
            {
                foreach (var contributor in contributors.EnumerateArray())
                {
                    var role = contributor.TryGetProperty("role", out var roleElement) &&
                               roleElement.TryGetProperty("id", out var roleId)
                        ? roleId.GetString() ?? ""
                        : "";

                    if (CrossrefRoles.TryGetValue(role, out var crossrefRole))
                        entries.Add((contributor, crossrefRole));
                    else
                        skippedRoles.Add(role == "" ? "(none)" : role);
                }
            }

            if (skippedRoles.Count > 0)
            {
                Console.WriteLine($"Skipped {skippedRoles.Count} contributor(s) with roles that have no Crossref equivalent: " +
                                  string.Join(", ", skippedRoles.Distinct()));
            }

            if (entries.Count == 0)
            {
                throw new Exception("No contributors/creators found in the InvenioRDM JSON");
            }

            var contributorsElement = new XElement(nameSpace + "contributors");

            var contributorCount = 0;

            foreach (var (contributor, contributorType) in entries)
            {
                contributorCount++;
                var sequence = contributorCount == 1 ? "first" : "additional";

                if (contributor.TryGetProperty("person_or_org", out var personOrOrg))
                {
                    if (personOrOrg.TryGetProperty("type", out var type) && 
                        type.GetString() == "personal")
                    {
                        var familyName = "";
                        var givenName = "";
                        
                        if (personOrOrg.TryGetProperty("family_name", out var familyNameElement))
                        {
                            familyName = familyNameElement.GetString() ?? "";
                        }
                        
                        if (personOrOrg.TryGetProperty("given_name", out var givenNameElement))
                        {
                            givenName = givenNameElement.GetString() ?? "";
                        }

                        var personElement = new XElement(nameSpace + "person_name",
                            new XAttribute("sequence", sequence),
                            new XAttribute("contributor_role", contributorType)
                        );
                        
                        if (!string.IsNullOrEmpty(givenName))
                        {
                            personElement.Add(new XElement(nameSpace + "given_name", givenName));
                        }
                        
                        if (!string.IsNullOrEmpty(familyName))
                        {
                            personElement.Add(new XElement(nameSpace + "surname", familyName));
                        }

                        if (contributor.TryGetProperty("affiliations", out var affiliations))
                        {
                            var institutions = new List<string>();
                            
                            
                            foreach (var affiliation in affiliations.EnumerateArray())
                            {
                                if (affiliation.TryGetProperty("name", out var affName))
                                {
                                    institutions.Add(affName.GetString() ?? "");
                                }
                            }

                            if (institutions.Any())
                            {
                                var insts = institutions.Select(ins => new XElement(nameSpace + "institution",
                                    new XElement(nameSpace + "institution_name", ins)));
                                
                                personElement.Add(new XElement(nameSpace + "affiliations", insts));
                            }
                        }
                        
                        if (personOrOrg.TryGetProperty("identifiers", out var identifiers))
                        {
                            foreach (var identifier in identifiers.EnumerateArray())
                            {
                                if (identifier.TryGetProperty("scheme", out var scheme) && 
                                    string.Equals(scheme.GetString(), "orcid", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (identifier.TryGetProperty("identifier", out var orcidValue))
                                    {
                                        var orcid = orcidValue.GetString();

                                        personElement.Add(new XElement(nameSpace + "ORCID", "https://orcid.org/" + orcid));
                                    }
                                }
                            }
                        }

                        contributorsElement.Add(personElement);
                    }
                    else if (type.GetString() == "organizational")
                    {
                        if (personOrOrg.TryGetProperty("name", out var nameElement))
                        {
                            var orgElement = new XElement(nameSpace + "organization", 
                                new XAttribute("sequence", sequence),
                                new XAttribute("contributor_role", contributorType),
                                nameElement.GetString()
                            );
                            
                            contributorsElement.Add(orgElement);
                        }
                    }
                }
            }
            
            return contributorsElement;
        }
        catch (Exception ex)
        {
            return new XElement("contributors", 
                new XComment($"Error converting contributors: {ex.Message}"));
        }
    }
}
