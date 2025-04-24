using System.Text.Json;
using ConverterPoC;

try
{
    var config = Config.Load("config.json");

    if (args == null || args.Length == 0)
        throw new ArgumentException("Please provide record ID");
            
    Console.WriteLine("Invenio RDM URL: " + config.ApiUrl);
    Console.WriteLine("CrossRef API URL: " + config.CrossRefApiUrl);
    
    var instanceAddress = config.ApiUrl;
    var rdmClient = new InvenioRDMClient(instanceAddress, config.AccessToken);
    
    var crossrefClient = new CrossrefApiClient(
        username: config.CrossRefUser,
        password: config.CrossRefPassword,
        apiUrl: config.CrossRefApiUrl
    );

    foreach (var recordId in args)
    {
        await ProcessRecordAsync(recordId, rdmClient, crossrefClient);
        await Task.Delay(TimeSpan.FromSeconds(1));
    }
}
catch (Exception ex)
{
    Console.WriteLine("Exception: " + ex.Message);
}

async Task ProcessRecordAsync(string s, InvenioRDMClient invenioRdmClient,
    CrossrefApiClient crossrefApiClient)
{
    Console.WriteLine("*****************");
    Console.WriteLine("Record ID: " + s);
            
    var contents = await invenioRdmClient.LoadRecordAsync(s);
            
    var converted = FromJsonConverter.Convert(invenioRdmClient, crossrefApiClient, contents ?? "");

    using var doc = JsonDocument.Parse(contents);
    var formattedJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });

    var xmlFileName = $"{s}.xml";
            
    await File.WriteAllTextAsync($"{s}.json", formattedJson);
    await File.WriteAllTextAsync(xmlFileName, converted);

    var resp = await crossrefApiClient.SubmitMetadataAsync(xmlFileName, converted);
    
    Console.WriteLine("CrossRef response: " + resp?.Trim());
}
