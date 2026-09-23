using System.Globalization;
using System.Text.Json;

namespace ConverterPoC;

public static class Languages
{
    // ISO 639-3 (InvenioRDM languages vocabulary) -> ISO 639-1 (Crossref language attribute)
    private static readonly Dictionary<string, string> Iso6391 = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
        .Where(c => c.Name != "" && c.TwoLetterISOLanguageName.Length == 2 && c.ThreeLetterISOLanguageName.Length == 3)
        .GroupBy(c => c.ThreeLetterISOLanguageName, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().TwoLetterISOLanguageName, StringComparer.OrdinalIgnoreCase);

    // Crossref code for the record's first language, or null when it has none or it can't be mapped
    public static string? ToCrossref(JsonElement metadata)
    {
        if (!metadata.TryGetProperty("languages", out var languages) ||
            languages.ValueKind != JsonValueKind.Array ||
            languages.GetArrayLength() == 0)
            return null;

        var first = languages[0];
        var id = first.ValueKind == JsonValueKind.Object && first.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;

        return ToIso6391(id);
    }

    public static string? ToIso6391(string? iso6393) =>
        iso6393 != null && Iso6391.TryGetValue(iso6393, out var code) ? code : null;
}
