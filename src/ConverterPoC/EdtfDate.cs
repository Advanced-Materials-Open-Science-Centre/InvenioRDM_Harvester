using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ConverterPoC;

// InvenioRDM publication dates are EDTF level 0: YYYY, YYYY-MM, YYYY-MM-DD, or an interval of
// those ("2020/2021-06"). Crossref dates allow month and day to be omitted, so precision is kept
// instead of inventing a day or month.
public readonly record struct EdtfDate(int Year, int? Month, int? Day)
{
    private static readonly Regex Pattern = new(@"^([0-9]{4})(?:-([0-9]{2})(?:-([0-9]{2}))?)?$");

    // Returns null for anything that isn't a valid EDTF level 0 date; intervals use their start
    public static EdtfDate? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = Pattern.Match(value.Split('/')[0].Trim());

        if (!match.Success)
            return null;

        var year = int.Parse(match.Groups[1].Value);
        int? month = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : null;
        int? day = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : null;

        if (year < 1 ||
            month is < 1 or > 12 ||
            (day != null && (day < 1 || day > DateTime.DaysInMonth(year, month!.Value))))
            return null;

        return new EdtfDate(year, month, day);
    }

    // Content of a Crossref date_t element (publication_date, posted_date): month?, day?, year
    public IEnumerable<XElement> ToCrossref(XNamespace ns)
    {
        if (Month != null)
            yield return new XElement(ns + "month", Month.Value.ToString("D2"));

        if (Day != null)
            yield return new XElement(ns + "day", Day.Value.ToString("D2"));

        yield return new XElement(ns + "year", Year);
    }
}
