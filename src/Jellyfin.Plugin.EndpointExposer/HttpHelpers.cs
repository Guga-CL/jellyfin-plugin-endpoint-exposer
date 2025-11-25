using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EndpointExposer
{
    internal static class HttpHelpers
    {
        // Simple retry with 429 handling and exponential backoff
        public static async Task<HttpResponseMessage> SendWithRetryAsync(
            Func<HttpClient, Task<HttpResponseMessage>> action,
            IHttpClientFactory httpFactory,
            string? clientName = null,
            int maxRetries = 3,
            CancellationToken cancellationToken = default)
        {
            if (httpFactory == null) throw new ArgumentNullException(nameof(httpFactory));

            var client = string.IsNullOrEmpty(clientName)
                ? httpFactory.CreateClient()
                : httpFactory.CreateClient(clientName);

            var attempt = 0;
            var delay = TimeSpan.FromSeconds(1);

            while (true)
            {
                attempt++;
                try
                {
                    var resp = await action(client).ConfigureAwait(false);

                    if ((int)resp.StatusCode == 429 && attempt <= maxRetries)
                    {
                        // Respect Retry-After header if present
                        if (resp.Headers.RetryAfter != null)
                        {
                            var retryAfter = resp.Headers.RetryAfter.Delta ?? TimeSpan.FromSeconds(5);
                            await Task.Delay(retryAfter, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
                        }

                        resp.Dispose();
                        continue;
                    }

                    return resp;
                }
                catch (HttpRequestException) when (attempt <= maxRetries)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
                    if (attempt == maxRetries) throw;
                }
            }
        }
    }
}