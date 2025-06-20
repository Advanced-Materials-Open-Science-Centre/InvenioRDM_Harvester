using UglyToad.PdfPig;

namespace ConverterPoC;

public static class DatasetDoiNumberProvider
{
    private static Lock _lock = new Lock();
    private static string FileName = "DataSetDoiNumber.txt";

    public static int GetCurrentAndSaveNewDoiNumber(DateTime utcNow)
    {
        var current = GetCurrentDoiNumber(utcNow);
        SaveCurrentDoiNumber(utcNow, current + 1);
        return current;
    }
    
    public static int GetCurrentDoiNumber(DateTime utcNow)
    {
        lock (_lock)
        {
            try
            {
                var text = File.ReadAllText(FileName);
                var parts = text.Split(',');

                if (parts.Length != 2)
                    throw new InvalidOperationException("Wrong contents");
                
                var timestamp = DateTime.Parse(parts[0]);

                var newMonth = timestamp.Month < utcNow.Month;

                if (newMonth)
                    return 0;
                
                return int.Parse(parts[1]);
            }
            catch (Exception ex)
            {
                return 0;
            }
        }
    }
    
    public static void SaveCurrentDoiNumber(DateTime utcNow, int? currentDoiNumber)
    {
        lock (_lock)
        {
            try
            {
                var line = utcNow.ToString("O") + "," + (currentDoiNumber ?? 0);
                File.WriteAllText(FileName, line);
            }
            catch (Exception ex)
            {
               Console.WriteLine(ex);
            }
        }
    }
}