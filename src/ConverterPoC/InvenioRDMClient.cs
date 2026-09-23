using System.Net.Http.Headers;
using System.Text;

namespace ConverterPoC;

public class InvenioRDMClient
{
    private readonly string _apiUrl;
    private readonly HttpClient _client;

    public InvenioRDMClient(string apiUrl, string? token)
    {
        _apiUrl = apiUrl;
        _client = new HttpClient();

        // Public records can be read anonymously
        if (!string.IsNullOrEmpty(token))
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // Always fetches the current record: a local copy would go stale after edits in InvenioRDM
    public async Task<string?> LoadRecordAsync(string recordId)
    {
        using var response = await _client.GetAsync($"{_apiUrl}api/records/{recordId}");

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Error: {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
            return null;
        }

        return Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
    }
}
