using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ConverterPoC;

public class CrossrefApiClient
{
    private readonly HttpClient _client;
    private readonly string _username;
    private readonly string _password;
    private readonly string _depositUrl;

    public CrossrefApiClient(string username, string password, string apiUrl)
    {
        _username = username;
        _password = password;
        _depositUrl = apiUrl;
        _client = new HttpClient();
    }

    // Returns null when the DOI isn't registered with Crossref; other failures throw so an
    // unreachable API isn't mistaken for "not registered"
    public async Task<CrossrefWork?> GetWorkAsync(string doi)
    {
        if (string.IsNullOrWhiteSpace(doi))
            throw new ArgumentException("DOI cannot be null or empty.");

        var url = $"https://api.crossref.org/works/{Uri.EscapeDataString(doi)}";

        using var response = await _client.GetAsync(url);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var result = JsonSerializer.Deserialize<CrossrefApiResponse>(await response.Content.ReadAsStringAsync());
        return result?.Message;
    }

    // Deposit log for an uploaded file (doi_batch_diagnostic XML once processed). Crossref recommends
    // tracking by file name, which must be unique because only the first match is returned.
    public async Task<string> GetSubmissionResultAsync(string fileName)
    {
        EnsureCredentialsSet();

        // Same servlet as the deposit endpoint (doi.crossref.org or test.crossref.org)
        var url = new Uri(new Uri(_depositUrl), "submissionDownload");

        // POST keeps the password out of URLs and logs
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["usr"] = _username,
            ["pwd"] = _password,
            ["file_name"] = fileName,
            ["type"] = "result"
        });

        using var response = await _client.PostAsync(url, content);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
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

        var response = await _client.PostAsync(_depositUrl, content);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private void EnsureCredentialsSet()
    {
        if(string.IsNullOrEmpty(_password))
            throw new ArgumentException("Provide CrossRef password");
        
        if(string.IsNullOrEmpty(_username))
            throw new ArgumentException("Provide CrossRef username");
    }
}
