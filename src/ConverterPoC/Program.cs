using System.Text.Json;
using System.Xml.Linq;
using ConverterPoC;

var resultPollInterval = TimeSpan.FromSeconds(15);

var failed = new List<string>();
var unconfirmed = new List<string>();

try
{
    var config = Config.Load("config.json");
    var depositor = config.GetDepositor();

    Console.WriteLine("Invenio RDM URL: " + config.ApiUrl);
    Console.WriteLine("CrossRef API URL: " + config.CrossRefApiUrl);
    Console.WriteLine("Depositor: " + depositor.Name + " <" + depositor.Email + ">");

    var instanceAddress = config.ApiUrl;
    var rdmClient = new InvenioRDMClient(instanceAddress, config.AccessToken);

    var crossrefClient = new CrossrefApiClient(
        username: config.CrossRefUser,
        password: config.CrossRefPassword,
        apiUrl: config.CrossRefApiUrl
    );

    var mappings = config.DoiMappings ?? [];
    var submissions = new List<Submission>();

    foreach (var mapping in mappings)
    {
        var recordUrl = config.ApiUrl + "records/" + mapping.DepositoryRecordId;

        try
        {
            submissions.Add(await ProcessRecordAsync(mapping, rdmClient, crossrefClient, depositor, recordUrl));
        }
        catch (Exception ex)
        {
            // One bad record shouldn't block the rest of the batch
            Console.WriteLine($"Record {mapping.DepositoryRecordId} failed: {ex.Message}");
            failed.Add(mapping.DepositoryRecordId);
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    if (submissions.Count > 0)
        await WaitForResultsAsync(crossrefClient, submissions, TimeSpan.FromMinutes(config.ResultTimeoutMinutes));

    Console.WriteLine("*****************");
    Console.WriteLine($"{mappings.Length - failed.Count - unconfirmed.Count} of {mappings.Length} record(s) deposited" +
                      (failed.Count > 0 ? "; failed: " + string.Join(", ", failed) : "") +
                      (unconfirmed.Count > 0 ? "; not confirmed yet: " + string.Join(", ", unconfirmed) : ""));
}
catch (Exception ex)
{
    Console.WriteLine("Exception: " + ex.Message);
    return 1;
}

return failed.Count == 0 ? 0 : 1;

async Task<Submission> ProcessRecordAsync(
    DoiMapping mapping,
    InvenioRDMClient invenioRdmClient,
    CrossrefApiClient crossrefApiClient,
    Depositor depositor,
    string recordUrl)
{
    Console.WriteLine("*****************");
    Console.WriteLine("Record ID: " + mapping.DepositoryRecordId);
    Console.WriteLine("DOI: " + mapping.Doi);

    if (string.IsNullOrWhiteSpace(mapping.Doi))
        throw new InvalidOperationException("The DoiMappings entry has no Doi");

    var contents = await invenioRdmClient.LoadRecordAsync(mapping.DepositoryRecordId)
                   ?? throw new InvalidOperationException("Could not load the record from InvenioRDM");

    using var doc = JsonDocument.Parse(contents);

    foreach (var warning in DoiGuard.CheckRecordDois(DoiGuard.GetRecordDois(doc.RootElement), mapping.Doi))
        Console.WriteLine("Warning: " + warning);

    var registered = await crossrefApiClient.GetWorkAsync(mapping.Doi);
    DoiGuard.CheckRegistration(mapping.Doi, registered?.Resource?.Primary?.Url, mapping.DepositoryRecordId);

    if (registered != null)
        Console.WriteLine("DOI is already registered for this record; the deposit updates its metadata");

    var converted = FromJsonConverter.Convert(
        depositor,
        contents,
        mapping.Doi,
        recordUrl
    );

    var formattedJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });

    var xmlFileName = $"{mapping.DepositoryRecordId}.xml";

    await File.WriteAllTextAsync($"{mapping.DepositoryRecordId}.json", formattedJson);
    await File.WriteAllTextAsync(xmlFileName, converted);

    var schemaErrors = CrossrefSchema.Validate(converted);

    if (schemaErrors.Count > 0)
        throw new InvalidOperationException(
            $"{xmlFileName} doesn't match the Crossref schema, not submitted:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", schemaErrors));

    // Crossref's deposit log is looked up by file name and returns the first match, so every
    // upload needs its own name
    var batchId = XDocument.Parse(converted).Descendants().First(e => e.Name.LocalName == "doi_batch_id").Value;
    var uploadFileName = $"{mapping.DepositoryRecordId}_{batchId}.xml";

    var resp = await crossrefApiClient.SubmitMetadataAsync(uploadFileName, converted);

    Console.WriteLine("CrossRef response: " + resp.Trim());

    return new Submission(mapping.DepositoryRecordId, uploadFileName);
}

// Crossref processes deposits asynchronously; poll the deposit logs until each one is completed
async Task WaitForResultsAsync(CrossrefApiClient crossrefApiClient, List<Submission> submissions, TimeSpan timeout)
{
    var waiting = submissions.ToList();
    var lastStatus = new Dictionary<Submission, string>();
    var deadline = DateTime.UtcNow + timeout;

    Console.WriteLine("*****************");

    if (timeout > TimeSpan.Zero)
        Console.WriteLine($"Waiting up to {timeout.TotalMinutes:0} minute(s) for Crossref to process {waiting.Count} deposit(s)...");

    while (waiting.Count > 0 && DateTime.UtcNow < deadline)
    {
        await Task.Delay(resultPollInterval);

        foreach (var submission in waiting.ToList())
        {
            DepositResult result;

            try
            {
                result = DepositResult.Parse(await crossrefApiClient.GetSubmissionResultAsync(submission.FileName));
            }
            catch (Exception ex) when (ex is FormatException or HttpRequestException)
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
                Console.WriteLine($"Record {submission.RecordId}: {record.Status} {record.Doi} {record.Message}".TrimEnd());

            if (!result.Succeeded)
                failed.Add(submission.RecordId);
        }
    }

    foreach (var submission in waiting)
    {
        Console.WriteLine($"Record {submission.RecordId}: no result yet for {submission.FileName}" +
                          (lastStatus.TryGetValue(submission, out var status) ? $" ({status})" : "") +
                          "; check Crossref's submission queue later");
        unconfirmed.Add(submission.RecordId);
    }
}

record Submission(string RecordId, string FileName);
