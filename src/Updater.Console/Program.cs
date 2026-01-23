using ConverterPoC;

var outputFolder = Path.Combine(Directory.GetCurrentDirectory(), "output");
Directory.CreateDirectory(outputFolder);

var files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.xml");

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
    var replaced = xml.Replace("dataset.pnu.edu.ua", "dataset.cnu.edu.ua");

    var fileName = Path.GetFileName(path);
    var resp = await crossrefClient.SubmitMetadataAsync(fileName, replaced);
    
    Console.WriteLine("CrossRef response: " + resp?.Trim());
    Console.WriteLine(fileName);
}

Console.WriteLine();