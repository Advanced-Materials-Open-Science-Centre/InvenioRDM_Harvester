using System.Text;

namespace ConverterPoC;

public class InvenioRDMClient
{
    private readonly string _apiUrl;

    public InvenioRDMClient(string apiUrl, string token)
    {
        _apiUrl = apiUrl;
        _client = new HttpClient();

        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        _client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    }

    readonly HttpClient _client;

    public async Task<string?> LoadRecordAsync(string recordId)
    {
        if(File.Exists(recordId))
           return await File.ReadAllTextAsync(recordId);
        
        var load = await LoadRecordInternalAsync(recordId);
        await File.WriteAllTextAsync(recordId, load);
        return load;
    }

    private async Task<string?> LoadRecordInternalAsync(string recordId)
    {
        var apiUrl = $"{_apiUrl}api/records/{recordId}";
        
        var response = await _client.GetAsync(apiUrl);
        
        if (response.IsSuccessStatusCode)
        {
            var a = await response.Content.ReadAsByteArrayAsync();
            var txt = Encoding.UTF8.GetString(a)
                .Replace("\r\n", "\n")
                .Replace("\n", "\r\n");
            return txt;
        }
        else
        {
            Console.WriteLine($"Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
            return null;
        }
    }

    public async Task<byte[]?> GetAsync(string pdfLink)
    {
        var response = await _client.GetAsync(pdfLink);

        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsByteArrayAsync();

        return null;
    }
}
