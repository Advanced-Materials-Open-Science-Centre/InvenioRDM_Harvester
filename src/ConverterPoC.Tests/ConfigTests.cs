namespace ConverterPoC.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("config-tests-").FullName;

    private string ConfigPath => Path.Combine(_directory, "config.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void LocalFile_OverridesSettings()
    {
        File.WriteAllText(ConfigPath, """
            {
              "ApiUrl": "https://example.org/",
              "CrossRefUser": "",
              "CrossRefPassword": "",
              "DoiMappings": [{ "Doi": "10.xxxxx/example", "DepositoryRecordId": "abcde-12345" }]
            }
            """);
        File.WriteAllText(Path.Combine(_directory, "config.local.json"), """
            {
              "CrossRefUser": "user",
              "CrossRefPassword": "secret",
              "ResultTimeoutMinutes": 0,
              "DoiMappings": [{ "Doi": "10.15330/dataset.26.09.01", "DepositoryRecordId": "ej8xg-c9628" }]
            }
            """);

        var config = Config.Load(ConfigPath);

        Assert.Equal("https://example.org/", config.ApiUrl);
        Assert.Equal("user", config.CrossRefUser);
        Assert.Equal("secret", config.CrossRefPassword);
        Assert.Equal(0, config.ResultTimeoutMinutes);
        Assert.Equal("ej8xg-c9628", Assert.Single(config.DoiMappings).DepositoryRecordId);
    }

    [Fact]
    public void WithoutLocalFile_UsesConfigJsonAndDefaults()
    {
        File.WriteAllText(ConfigPath, """{ "ApiUrl": "https://example.org/" }""");

        var config = Config.Load(ConfigPath);

        Assert.Equal("https://example.org/", config.ApiUrl);
        Assert.Equal(5, config.ResultTimeoutMinutes);
        Assert.Empty(config.DoiMappings);
    }

    [Fact]
    public void MissingConfig_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => Config.Load(ConfigPath));
    }
}
