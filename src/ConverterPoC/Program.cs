using System.Text.Json;
using ConverterPoC;

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

    foreach (var mapping in config.DoiMappings ?? [])
    {
        var recordUrl = config.ApiUrl + "records/" + mapping.DepositoryRecordId;
        
        await ProcessRecordAsync(mapping, rdmClient, crossrefClient, depositor, recordUrl);
        await Task.Delay(TimeSpan.FromSeconds(1));
    }
}
catch (Exception ex)
{
    Console.WriteLine("Exception: " + ex.Message);
}

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
            
    var contents = await invenioRdmClient.LoadRecordAsync(mapping.DepositoryRecordId);

    var converted = FromJsonConverter.Convert(crossrefApiClient,
        depositor,
        contents ?? "",
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
