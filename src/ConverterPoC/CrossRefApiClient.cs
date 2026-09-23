using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConverterPoC;

// A Crossref request that failed, with Crossref's own explanation (e.g. "Wrong credentials")
public class CrossrefException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public bool IsAuthenticationFailure =>
        StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}

public class CrossrefApiClient
{
    private readonly HttpClient _client;
    private readonly string _username;
    private readonly string _password;
    private readonly string _depositUrl;
    private readonly TimeSpan _retryDelay;

    public CrossrefApiClient(
        string username,
        string password,
        string apiUrl,
        HttpClient? httpClient = null,
        TimeSpan? retryDelay = null)
    {
        _username = username;
        _password = password;
        _depositUrl = apiUrl;
        _client = httpClient ?? new HttpClient();
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
    }

    // Returns null when the DOI isn't registered with Crossref; other failures throw so an
    // unreachable API isn't mistaken for "not registered"
    public async Task<CrossrefWork?> GetWorkAsync(string doi)
    {
        if (string.IsNullOrWhiteSpace(doi))
            throw new ArgumentException("DOI cannot be null or empty.");

        var url = $"https://api.crossref.org/works/{Uri.EscapeDataString(doi)}";

        using var response = await HttpRetry.SendAsync(_client, () => new HttpRequestMessage(HttpMethod.Get, url), _retryDelay);

        if (response.StatusCode == HttpStatusCode.NotFound)
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

        // POST keeps the password out of URLs and logs; it only reads, so it can be retried
        using var response = await HttpRetry.SendAsync(_client, () => new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["usr"] = _username,
                ["pwd"] = _password,
                ["file_name"] = fileName,
                ["type"] = "result"
            })
        }, _retryDelay);

        return await ReadOrThrowAsync(response, "Could not fetch the deposit result");
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

        // Not retried: a repeated upload could be queued twice
        using var response = await _client.PostAsync(_depositUrl, content);

        return await ReadOrThrowAsync(response, "Crossref rejected the upload");
    }

    private static async Task<string> ReadOrThrowAsync(HttpResponseMessage response, string error)
    {
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new CrossrefException(response.StatusCode,
                $"{error}: {(int)response.StatusCode} {response.ReasonPhrase} {ToText(body)}".TrimEnd());

        return body;
    }

    // Crossref answers with small HTML pages, e.g. "FAILURE ... [Wrong credentials. Incorrect username or password.]"
    private static string ToText(string html)
    {
        var text = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();
        return text.Length > 300 ? text[..300] + "…" : text;
    }

    private void EnsureCredentialsSet()
    {
        if(string.IsNullOrEmpty(_password))
            throw new ArgumentException("Provide CrossRef password");

        if(string.IsNullOrEmpty(_username))
            throw new ArgumentException("Provide CrossRef username");
    }
}
