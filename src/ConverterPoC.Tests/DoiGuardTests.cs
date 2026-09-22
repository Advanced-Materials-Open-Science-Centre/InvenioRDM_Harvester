using System.Text.Json;

namespace ConverterPoC.Tests;

public class DoiGuardTests
{
    [Fact]
    public void GetRecordDois_ReadsPidAndDoiIdentifiers()
    {
        using var doc = JsonDocument.Parse("""
            {
              "pids": { "doi": { "identifier": "10.15330/dataset.26.07.01", "provider": "external" } },
              "metadata": {
                "identifiers": [
                  { "identifier": "https://doi.org/10.5281/zenodo.123", "scheme": "doi" },
                  { "identifier": "978-966-668-664-3", "scheme": "isbn" },
                  { "identifier": "10.15330/DATASET.26.07.01", "scheme": "doi" }
                ]
              }
            }
            """);

        Assert.Equal(["10.15330/dataset.26.07.01", "10.5281/zenodo.123"], DoiGuard.GetRecordDois(doc.RootElement));
    }

    [Fact]
    public void GetRecordDois_NoDois_IsEmpty()
    {
        using var doc = JsonDocument.Parse(TestRecords.Json());

        Assert.Empty(DoiGuard.GetRecordDois(doc.RootElement));
    }

    [Theory]
    [InlineData("10.15330/dataset.26.07.01")]
    [InlineData("10.15330/DATASET.26.07.01")]
    [InlineData("https://doi.org/10.15330/dataset.26.07.01")]
    public void CheckRecordDois_SameDoi_IsAccepted(string doi)
    {
        Assert.Empty(DoiGuard.CheckRecordDois(["10.15330/dataset.26.07.01"], doi));
    }

    [Fact]
    public void CheckRecordDois_NoRecordDois_IsAccepted()
    {
        Assert.Empty(DoiGuard.CheckRecordDois([], "10.15330/dataset.26.09.01"));
    }

    // e.g. tks6t-8t124 lists its journal DOI 10.15330/pcss.26.1.132-139
    [Fact]
    public void CheckRecordDois_OtherDoiWithSamePrefix_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DoiGuard.CheckRecordDois(["10.15330/pcss.26.1.132-139"], "10.15330/dataset.26.09.01"));

        Assert.Contains("10.15330/pcss.26.1.132-139", exception.Message);
    }

    [Fact]
    public void CheckRecordDois_DoiWithOtherPrefix_IsWarning()
    {
        var warning = Assert.Single(DoiGuard.CheckRecordDois(["10.5281/zenodo.123"], "10.15330/dataset.26.09.01"));

        Assert.Contains("10.5281/zenodo.123", warning);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://dataset.cnu.edu.ua/records/ej8xg-c9628")]
    [InlineData("https://dataset.cnu.edu.ua/records/ej8xg-c9628/")]
    [InlineData("https://dataset.pnu.edu.ua/records/ej8xg-c9628")]
    public void CheckRegistration_UnregisteredOrSameRecord_IsAccepted(string? registeredUrl)
    {
        DoiGuard.CheckRegistration("10.15330/dataset.26.09.01", registeredUrl, "ej8xg-c9628");
    }

    [Theory]
    [InlineData("https://dataset.cnu.edu.ua/records/kej9f-w3t43")]
    [InlineData("https://journals.pnu.edu.ua/index.php/pcss/article/view/123")]
    [InlineData("not a url")]
    public void CheckRegistration_RegisteredForSomethingElse_Throws(string registeredUrl)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DoiGuard.CheckRegistration("10.15330/dataset.26.09.01", registeredUrl, "ej8xg-c9628"));

        Assert.Contains(registeredUrl, exception.Message);
    }
}
