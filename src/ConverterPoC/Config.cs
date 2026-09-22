using System.Text.Json;

namespace ConverterPoC;

public class Config
{
    public string ApiUrl { get; set; }
    public string AccessToken { get; set; }
    public string CrossRefUser { get; set; }
    public string CrossRefPassword { get; set; }
    public string CrossRefApiUrl { get; set; }
    public string DepositorName { get; set; }
    public string DepositorEmail { get; set; }
    public string Registrant { get; set; }
    public DoiMapping[] DoiMappings { get; set; } = [];

    public static Config Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Configuration file not found.", filePath);

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<Config>(json);
    }

    public Depositor GetDepositor()
    {
        if (string.IsNullOrWhiteSpace(DepositorName) ||
            string.IsNullOrWhiteSpace(DepositorEmail) ||
            string.IsNullOrWhiteSpace(Registrant))
            throw new ArgumentException("Provide DepositorName, DepositorEmail and Registrant in config.json");

        return new Depositor(DepositorName, DepositorEmail, Registrant);
    }
}

public class DoiMapping
{
    public string Doi { get; set; }
    public string DepositoryRecordId { get; set; }
}
