using System.Text.Json;

namespace ConverterPoC;

// Checks run before a deposit so a wrong DoiMappings entry can't give a work a second DOI or
// redirect a DOI that is already registered for something else.
public static class DoiGuard
{
    // DOIs the record lists for itself: InvenioRDM's DOI pid and alternate identifiers
    public static IReadOnlyList<string> GetRecordDois(JsonElement root)
    {
        var dois = new List<string>();

        if (root.TryGetProperty("pids", out var pids) &&
            pids.TryGetProperty("doi", out var pidDoi) &&
            pidDoi.TryGetProperty("identifier", out var pidIdentifier))
        {
            dois.Add(pidIdentifier.GetString() ?? "");
        }

        if (root.TryGetProperty("metadata", out var metadata) &&
            metadata.TryGetProperty("identifiers", out var identifiers) &&
            identifiers.ValueKind == JsonValueKind.Array)
        {
            dois.AddRange(identifiers.EnumerateArray()
                .Where(i => i.TryGetProperty("scheme", out var scheme) && scheme.GetString() == "doi" &&
                            i.TryGetProperty("identifier", out _))
                .Select(i => i.GetProperty("identifier").GetString() ?? ""));
        }

        return dois.Select(Normalize).Where(d => d.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Throws when the record already has another DOI under the same prefix (a duplicate or a
    // mapping for the wrong record). DOIs with other prefixes were registered elsewhere (e.g.
    // Zenodo) and may legitimately identify another copy, so they only produce warnings.
    public static IReadOnlyList<string> CheckRecordDois(IReadOnlyList<string> recordDois, string doi)
    {
        doi = Normalize(doi);

        if (recordDois.Contains(doi, StringComparer.OrdinalIgnoreCase))
            return [];

        var samePrefix = recordDois.FirstOrDefault(d => Prefix(d).Equals(Prefix(doi), StringComparison.OrdinalIgnoreCase));

        if (samePrefix != null)
            throw new InvalidOperationException(
                $"Record already has DOI {samePrefix}; depositing {doi} would give it a second DOI under the same prefix");

        return recordDois.Select(d => $"Record also has DOI {d} registered elsewhere; {doi} will be an additional DOI").ToList();
    }

    // Throws when the DOI is registered for a different URL than this record's page. Only the
    // record id is compared, so a redeposit can move a DOI to a new repository domain.
    public static void CheckRegistration(string doi, string? registeredUrl, string recordId)
    {
        if (registeredUrl == null)
            return;

        var path = Uri.TryCreate(registeredUrl, UriKind.Absolute, out var uri) ? uri.AbsolutePath.TrimEnd('/') : "";

        if (!path.EndsWith("/records/" + recordId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"DOI {Normalize(doi)} is already registered for {registeredUrl}, not for record {recordId}");
    }

    public static string Normalize(string doi)
    {
        doi = doi.Trim();

        foreach (var prefix in new[] { "https://doi.org/", "http://doi.org/", "https://dx.doi.org/", "http://dx.doi.org/", "doi:" })
        {
            if (doi.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return doi[prefix.Length..];
        }

        return doi;
    }

    private static string Prefix(string doi) => doi.Split('/')[0];
}
