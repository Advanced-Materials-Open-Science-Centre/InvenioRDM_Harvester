using System.Text.Json;

namespace ConverterPoC;

public class Config
{
    public string ApiUrl { get; set; }
    public string AccessToken { get; set; }
    public string CrossRefUser { get; set; }
    public string CrossRefPassword { get; set; }
    public string CrossRefApiUrl { get; set; }

    public static Config Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Configuration file not found.", filePath);

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<Config>(json);
    }
}
