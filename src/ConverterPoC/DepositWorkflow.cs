using System.Text.Json;
using System.Xml.Linq;

namespace ConverterPoC;

public record DepositSummary(IReadOnlyList<string> Deposited, IReadOnlyList<string> Failed, IReadOnlyList<string> Unconfirmed)
{
    // 0: everything deposited; 1: a record failed; 2: nothing failed, but Crossref hasn't
    // confirmed every deposit yet
    public int ExitCode => Failed.Count > 0 ? 1 : Unconfirmed.Count > 0 ? 2 : 0;
}

// Loads each mapped record, checks its DOI, converts and validates it, uploads it to Crossref,
// then waits for Crossref's deposit results
public class DepositWorkflow(
    ConversionSettings settings,
    string repositoryUrl,
    InvenioRDMClient invenioRdmClient,
    CrossrefApiClient crossrefApiClient,
    TextWriter log,
    string outputDirectory,
    Func<TimeSpan, Task>? delay = null)
{
    public TimeSpan RecordInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ResultPollInterval { get; init; } = TimeSpan.FromSeconds(15);

    private readonly Func<TimeSpan, Task> _delay = delay ?? Task.Delay;

    private record Submission(string RecordId, string FileName);

    public async Task<DepositSummary> RunAsync(IReadOnlyList<DoiMapping> mappings, TimeSpan resultTimeout)
    {
        var failed = new List<string>();
        var submissions = new List<Submission>();

        for (var i = 0; i < mappings.Count; i++)
        {
            var mapping = mappings[i];

            try
            {
                submissions.Add(await ProcessRecordAsync(mapping));
            }
            catch (CrossrefException ex) when (ex.IsAuthenticationFailure)
            {
                // Every other upload would be rejected the same way
                var notProcessed = mappings.Skip(i + 1).Select(m => m.DepositoryRecordId).ToList();
                log.WriteLine($"Record {mapping.DepositoryRecordId} failed: {ex.Message}");
                log.WriteLine("Stopping: Crossref rejected the credentials" +
                              (notProcessed.Count > 0 ? "; not processed: " + string.Join(", ", notProcessed) : ""));
                failed.Add(mapping.DepositoryRecordId);
                failed.AddRange(notProcessed);
                break;
            }
            catch (Exception ex)
            {
                // One bad record shouldn't block the rest of the batch
                log.WriteLine($"Record {mapping.DepositoryRecordId} failed: {ex.Message}");
                failed.Add(mapping.DepositoryRecordId);
            }

            await _delay(RecordInterval);
        }

        List<string> deposited = [];
        List<string> unconfirmed = [];

        if (submissions.Count > 0)
        {
            (deposited, var depositFailed, unconfirmed) = await WaitForResultsAsync(submissions, resultTimeout);
            failed.AddRange(depositFailed);
        }

        var summary = new DepositSummary(deposited, failed, unconfirmed);

        log.WriteLine("*****************");
        log.WriteLine($"{deposited.Count} of {mappings.Count} record(s) deposited" +
                      (failed.Count > 0 ? "; failed: " + string.Join(", ", failed) : "") +
                      (unconfirmed.Count > 0 ? "; not confirmed yet: " + string.Join(", ", unconfirmed) : ""));

        return summary;
    }

    private async Task<Submission> ProcessRecordAsync(DoiMapping mapping)
    {
        log.WriteLine("*****************");
        log.WriteLine("Record ID: " + mapping.DepositoryRecordId);
        log.WriteLine("DOI: " + mapping.Doi);

        if (string.IsNullOrWhiteSpace(mapping.Doi))
            throw new InvalidOperationException("The DoiMappings entry has no Doi");

        var contents = await invenioRdmClient.LoadRecordAsync(mapping.DepositoryRecordId)
                       ?? throw new InvalidOperationException("Could not load the record from InvenioRDM");

        using var doc = JsonDocument.Parse(contents);

        foreach (var warning in DoiGuard.CheckRecordDois(DoiGuard.GetRecordDois(doc.RootElement), mapping.Doi))
            log.WriteLine("Warning: " + warning);

        var registered = await crossrefApiClient.GetWorkAsync(mapping.Doi);
        DoiGuard.CheckRegistration(mapping.Doi, registered?.Resource?.Primary?.Url, mapping.DepositoryRecordId);

        CrossrefContentType? registeredType = null;

        if (registered != null)
        {
            // Crossref doesn't let a deposit change a DOI's content type, so updates keep the registered one
            registeredType = CrossrefContentTypes.FromRegisteredType(registered.Type) ??
                             throw new InvalidOperationException($"DOI is registered as {registered.Type}, which this tool can't deposit");

            log.WriteLine($"DOI is already registered for this record as {registered.Type}; the deposit updates its metadata");
        }

        var converted = FromJsonConverter.Convert(
            settings,
            contents,
            mapping.Doi,
            repositoryUrl + "records/" + mapping.DepositoryRecordId,
            registeredType);

        var formattedJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        var xmlFileName = $"{mapping.DepositoryRecordId}.xml";

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, $"{mapping.DepositoryRecordId}.json"), formattedJson);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, xmlFileName), converted);

        var schemaErrors = CrossrefSchema.Validate(converted);

        if (schemaErrors.Count > 0)
            throw new InvalidOperationException(
                $"{xmlFileName} doesn't match the Crossref schema, not submitted:{Environment.NewLine}  " +
                string.Join(Environment.NewLine + "  ", schemaErrors));

        // Crossref's deposit log is looked up by file name and returns the first match, so every
        // upload needs its own name
        var batchId = XDocument.Parse(converted).Descendants().First(e => e.Name.LocalName == "doi_batch_id").Value;
        var uploadFileName = $"{mapping.DepositoryRecordId}_{batchId}.xml";

        var response = await crossrefApiClient.SubmitMetadataAsync(uploadFileName, converted);

        log.WriteLine("CrossRef response: " + response.Trim());

        return new Submission(mapping.DepositoryRecordId, uploadFileName);
    }

    // Crossref processes deposits asynchronously; poll the deposit logs until each one is completed
    private async Task<(List<string> Deposited, List<string> Failed, List<string> Unconfirmed)> WaitForResultsAsync(
        List<Submission> submissions, TimeSpan timeout)
    {
        var deposited = new List<string>();
        var failed = new List<string>();
        var waiting = submissions.ToList();
        var lastStatus = new Dictionary<Submission, string>();
        var rounds = (int)Math.Ceiling(timeout / ResultPollInterval);

        log.WriteLine("*****************");

        if (rounds > 0)
            log.WriteLine($"Waiting up to {timeout.TotalMinutes:0.#} minute(s) for Crossref to process {waiting.Count} deposit(s)...");

        for (var round = 0; round < rounds && waiting.Count > 0; round++)
        {
            await _delay(ResultPollInterval);

            foreach (var submission in waiting.ToList())
            {
                DepositResult result;

                try
                {
                    result = DepositResult.Parse(await crossrefApiClient.GetSubmissionResultAsync(submission.FileName));
                }
                catch (CrossrefException ex) when (ex.IsAuthenticationFailure)
                {
                    log.WriteLine($"Stopped waiting for results: {ex.Message}");
                    round = rounds;
                    break;
                }
                catch (Exception ex) when (ex is FormatException or HttpRequestException or CrossrefException)
                {
                    // Not in the queue yet, or a transient error: try again on the next round
                    lastStatus[submission] = ex.Message;
                    continue;
                }

                if (!result.IsCompleted)
                {
                    lastStatus[submission] = $"status {result.Status}";
                    continue;
                }

                waiting.Remove(submission);

                foreach (var record in result.Records)
                    log.WriteLine($"Record {submission.RecordId}: {record.Status} {record.Doi} {record.Message}".TrimEnd());

                (result.Succeeded ? deposited : failed).Add(submission.RecordId);
            }
        }

        foreach (var submission in waiting)
        {
            log.WriteLine($"Record {submission.RecordId}: no result yet for {submission.FileName}" +
                          (lastStatus.TryGetValue(submission, out var status) ? $" ({status})" : "") +
                          "; check Crossref's submission queue later");
        }

        return (deposited, failed, waiting.Select(s => s.RecordId).ToList());
    }
}
