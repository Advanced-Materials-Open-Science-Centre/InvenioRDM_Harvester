using System.Text.Json;
using ConverterPoC;

var failed = new List<string>();

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

    foreach (var mapping in mappings)
    {
        var recordUrl = config.ApiUrl + "records/" + mapping.DepositoryRecordId;

        try
        {
            await ProcessRecordAsync(mapping, rdmClient, crossrefClient, depositor, recordUrl);
        }
        catch (Exception ex)
        {
            // One bad record shouldn't block the rest of the batch
            Console.WriteLine($"Record {mapping.DepositoryRecordId} failed: {ex.Message}");
            failed.Add(mapping.DepositoryRecordId);
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    Console.WriteLine("*****************");
    Console.WriteLine($"Submitted {mappings.Length - failed.Count} of {mappings.Length} record(s)" +
                      (failed.Count > 0 ? "; failed: " + string.Join(", ", failed) : ""));
}
catch (Exception ex)
{
    Console.WriteLine("Exception: " + ex.Message);
    return 1;
}

return failed.Count == 0 ? 0 : 1;

async Task ProcessRecordAsync(
    DoiMapping mapping, 
    InvenioRDMClient invenioRdmClient,
    CrossrefApiClient crossrefApiClient,
    Depositor depositor,
    string recordUrl)
{
    Console.WriteLine("*****************");
    Console.WriteLine("Record ID: " + mapping.DepositoryRecordId);
    Console.WriteLine("DOI: " + mapping.Doi);
            
    var contents = await invenioRdmClient.LoadRecordAsync(mapping.DepositoryRecordId)
                   ?? throw new InvalidOperationException("Could not load the record from InvenioRDM");

    var converted = FromJsonConverter.Convert(crossrefApiClient,
        depositor,
        contents,
        mapping.Doi,
        recordUrl
    );

    using var doc = JsonDocument.Parse(contents);
    var formattedJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });

    var xmlFileName = $"{mapping.DepositoryRecordId}.xml";
            
    await File.WriteAllTextAsync($"{mapping.DepositoryRecordId}.json", formattedJson);
    await File.WriteAllTextAsync(xmlFileName, converted);

    var resp = await crossrefApiClient.SubmitMetadataAsync(xmlFileName, converted);
    
    Console.WriteLine("CrossRef response: " + resp?.Trim());
}
