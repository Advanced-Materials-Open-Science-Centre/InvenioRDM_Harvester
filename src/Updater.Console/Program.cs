using System.Text.RegularExpressions;
using ConverterPoC;

var outputFolder = Path.Combine("");

var files = Directory.GetFiles(outputFolder, "*.xml");

var read = files.Select(r => (r, File.ReadAllText(r))).ToList();

var config = Config.Load("config.json");

Console.WriteLine("CrossRef API URL: " + config.CrossRefApiUrl);
    
var crossrefClient = new CrossrefApiClient(
    username: config.CrossRefUser,
    password: config.CrossRefPassword,
    apiUrl: config.CrossRefApiUrl
);

foreach (var (path, xml) in read)
{
    var replaced = PostProcess(xml);

    var fileName = Path.GetFileName(path);
    var resp = await crossrefClient.SubmitMetadataAsync(fileName, replaced);
    
    Console.WriteLine("CrossRef response: " + resp?.Trim());
    Console.WriteLine(fileName);
}

Console.WriteLine();

string PostProcess(string s)
{
    var withUpdateLinks = s.Replace("dataset.pnu.edu.ua", "dataset.cnu.edu.ua");

    var pattern = @"<timestamp>\d{14}</timestamp>";
    
    var newTimestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    
    var replacement = $"<timestamp>{newTimestamp}</timestamp>";

    var updatedXml = Regex.Replace(withUpdateLinks, pattern, replacement);
    
    Thread.Sleep(5000);
    
    return updatedXml;
}