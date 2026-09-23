using System.Net;

namespace ConverterPoC.Tests;

// In-memory HTTP: routes requests to canned responses and records what was sent.
// Unmatched requests get 404.
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, string, bool> Match, Func<HttpResponseMessage> Respond)> _routes = [];

    public List<(HttpMethod Method, Uri Uri, string Body)> Requests { get; } = [];

    public FakeHttp On(Func<HttpRequestMessage, string, bool> match, params Func<HttpResponseMessage>[] responses)
    {
        // Successive matching requests get successive responses; the last one repeats
        var calls = 0;
        _routes.Add((match, () => responses[Math.Min(calls++, responses.Length - 1)]()));
        return this;
    }

    public IEnumerable<(HttpMethod Method, Uri Uri, string Body)> To(string urlPart) =>
        Requests.Where(r => r.Uri.AbsoluteUri.Contains(urlPart));

    public static Func<HttpResponseMessage> Respond(HttpStatusCode status, string body = "") =>
        () => new HttpResponseMessage(status) { Content = new StringContent(body) };

    public static Func<HttpResponseMessage> Fail(Exception exception) => () => throw exception;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
        Requests.Add((request.Method, request.RequestUri!, body));

        foreach (var (match, respond) in _routes)
        {
            if (match(request, body))
                return respond();
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
