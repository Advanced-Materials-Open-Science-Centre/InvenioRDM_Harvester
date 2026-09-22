namespace ConverterPoC.Tests;

public class EdtfDateTests
{
    [Theory]
    [InlineData("2026-09-22", 2026, 9, 22)]
    [InlineData("2026-09", 2026, 9, null)]
    [InlineData("2026", 2026, null, null)]
    [InlineData("2024-02-29", 2024, 2, 29)]
    [InlineData("2020/2021", 2020, null, null)]
    [InlineData("2020-05/2021-06-01", 2020, 5, null)]
    [InlineData(" 2026-09-22 ", 2026, 9, 22)]
    public void Parse_ValidEdtf_KeepsPrecision(string value, int year, int? month, int? day)
    {
        Assert.Equal<EdtfDate?>(new EdtfDate(year, month, day), EdtfDate.Parse(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("2026-02-30")]
    [InlineData("2023-02-29")]
    [InlineData("0000")]
    [InlineData("26")]
    [InlineData("2026-9-22")]
    [InlineData("22.09.2026")]
    [InlineData("September 2026")]
    [InlineData("/2020")]
    public void Parse_InvalidValue_ReturnsNull(string? value)
    {
        Assert.Null(EdtfDate.Parse(value));
    }

    [Theory]
    [InlineData("2026-09-02", "month=09 day=02 year=2026")]
    [InlineData("2026-09", "month=09 year=2026")]
    [InlineData("2026", "year=2026")]
    public void ToCrossref_EmitsMonthDayYearInSchemaOrder(string value, string expected)
    {
        var elements = EdtfDate.Parse(value)!.Value.ToCrossref(TestRecords.Crossref);

        Assert.Equal(expected, string.Join(" ", elements.Select(e => $"{e.Name.LocalName}={e.Value}")));
        Assert.All(elements, e => Assert.Equal(TestRecords.Crossref, e.Name.Namespace));
    }
}
