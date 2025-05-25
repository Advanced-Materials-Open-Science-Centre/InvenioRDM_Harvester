using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace ConverterPoC;

public class ContributorsParser
{
    public static XElement ConvertContributorsToXml(XNamespace nameSpace, JsonElement root)
    {
        try
        {
            JsonElement contributors = default;
            
            if (!root.TryGetProperty("metadata", out var metadata) || 
                (!metadata.TryGetProperty("creators", out var creators) &&
                !metadata.TryGetProperty("contributors", out contributors))
                )
            {
                throw new Exception("No contributors/creators found in the InvenioRDM JSON");
            }

            var contributorsElement = new XElement(nameSpace + "contributors");
            
            var contributorCount = 0;

            var contr = (contributors.ValueKind is JsonValueKind.Array)
                ? contributors.EnumerateArray()
                : new JsonElement.ArrayEnumerator();
            
            foreach (var contributor in creators.EnumerateArray().Concat(contr))
            {
                contributorCount++;
                var sequence = contributorCount == 1 ? "first" : "additional";

                var contributorType = "author";

                if (contributor.TryGetProperty("person_or_org", out var personOrOrg))
                {
                    if (personOrOrg.TryGetProperty("type", out var type) && 
                        type.GetString() == "personal")
                    {
                        var familyName = "";
                        var givenName = "";
                        
                        if (personOrOrg.TryGetProperty("family_name", out var familyNameElement))
                        {
                            familyName = familyNameElement.GetString();
                        }
                        
                        if (personOrOrg.TryGetProperty("given_name", out var givenNameElement))
                        {
                            givenName = givenNameElement.GetString();
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
                                    institutions.Add(affName.GetString());
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
                                    scheme.GetString().ToLower() == "orcid")
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
