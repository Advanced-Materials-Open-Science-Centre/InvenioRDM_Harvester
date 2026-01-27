using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ConverterPoC;

public class CrossrefApiClient
{
    private readonly HttpClient _client;
    private readonly string _username;
    private readonly string _password;
    private readonly string _testApiUrl;

    public CrossrefApiClient(string username, string password, string apiUrl)
    {
        _username = username;
        _password = password;
        _testApiUrl = apiUrl;
        _client = new HttpClient();
    }

    public async Task<CrossrefApiResponse?> DoiExistsAsync(string doi)
    {
        if (string.IsNullOrWhiteSpace(doi))
            throw new ArgumentException("DOI cannot be null or empty.");

        var url = $"https://api.crossref.org/works/{Uri.EscapeDataString(doi)}";

        try
        {
            var response = await _client.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<CrossrefApiResponse>(response);
            return result;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP error: {ex.Message}");
            return null;
        }
    }
    
    public async Task<string> SubmitMetadataAsync(string fileName, string metadataXml)
    {
        EnsureCredentialsSet();
        
        using var content = new MultipartFormDataContent();

        content.Add(new StringContent(_username), "login_id");
        content.Add(new StringContent(_password), "login_passwd");

        content.Add(new StringContent("doMDUpload"), "operation");

        var xmlContent = new ByteArrayContent(Encoding.UTF8.GetBytes(metadataXml));
        xmlContent.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        content.Add(xmlContent, "fname", fileName);

        var response = await _client.PostAsync(_testApiUrl, content);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    public async Task<CrossrefWork?> SearchCrossrefByIsbnAsync(string isbn)
    {
        string query = $"https://api.crossref.org/works?query={isbn}&rows=10";
        var json = await _client.GetStringAsync(query);

        var response = JsonSerializer.Deserialize<CrossrefSearchResponse>(json);
    
        return response?.Message?.Items?.FirstOrDefault(w =>
            w.Isbn != null && w.Isbn.Any(i => i.Replace("-", "") == isbn.Replace("-", ""))
        );
    }
    
    public async Task<string?> GetJournalTitleByISSN(string issn)
    {
        var url = $"https://api.crossref.org/journals/{issn}";

        try
        {
            var response = await _client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var title = doc.RootElement.GetProperty("message").GetProperty("title").GetString();
            return title;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return null;
        }
    }
    
    private void EnsureCredentialsSet()
    {
        if(string.IsNullOrEmpty(_password))
            throw new ArgumentException("Provide CrossRef password");
        
        if(string.IsNullOrEmpty(_username))
            throw new ArgumentException("Provide CrossRef username");
    }
}
