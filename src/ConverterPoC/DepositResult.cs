using System.Xml;
using System.Xml.Linq;

namespace ConverterPoC;

public record RecordDiagnostic(string Status, string Doi, string Message);

// Crossref's deposit log (doi_batch_diagnostic), as returned by submissionDownload?type=result
public record DepositResult(string Status, IReadOnlyList<RecordDiagnostic> Records, int FailureCount)
{
    // Other statuses (e.g. queued, in_process) mean Crossref hasn't processed the file yet
    public bool IsCompleted => Status == "completed";

    public bool Succeeded => IsCompleted && FailureCount == 0 && Records.Count > 0;

    // Throws FormatException for anything that isn't a doi_batch_diagnostic document
    public static DepositResult Parse(string response)
    {
        XDocument doc;

        try
        {
            doc = XDocument.Parse(response);
        }
        catch (XmlException ex)
        {
            throw new FormatException($"Not a Crossref deposit log: {Snippet(response)}", ex);
        }

        var root = doc.Root!;

        if (root.Name.LocalName != "doi_batch_diagnostic")
            throw new FormatException($"Not a Crossref deposit log: {Snippet(response)}");

        var records = root.Elements("record_diagnostic")
            .Select(r => new RecordDiagnostic(
                r.Attribute("status")?.Value ?? "",
                r.Element("doi")?.Value ?? "",
                r.Element("msg")?.Value.Trim() ?? ""))
            .ToList();

        var failureCount = int.TryParse(root.Element("batch_data")?.Element("failure_count")?.Value, out var count)
            ? count
            : records.Count(r => r.Status == "Failure");

        return new DepositResult(root.Attribute("status")?.Value ?? "", records, failureCount);
    }

    private static string Snippet(string response) =>
        response.Length > 200 ? response[..200] + "…" : response;
}
