using System.Net;

namespace ConverterPoC;

// Retries requests that are safe to repeat (reads) on network errors, timeouts, 429 and 5xx.
// Deposit uploads don't go through here: repeating one could register the same batch twice.
public static class HttpRetry
{
    public const int Attempts = 3;

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> createRequest,
        TimeSpan delay)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await client.SendAsync(createRequest());

                if (attempt == Attempts || !IsTransient(response.StatusCode))
                    return response;

                response.Dispose();
            }
            catch (Exception ex) when (attempt < Attempts && ex is HttpRequestException or TaskCanceledException)
            {
                // Network error or timeout: try again
            }

            await Task.Delay(delay * attempt);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;
}
