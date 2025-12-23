// JellyfinAuth.cs
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EndpointExposer
{
    public class JellyfinAuth
    {
        private readonly HttpClient _http;
        private readonly ILogger<JellyfinAuth>? _logger;

        // Exposed so callers can log or inspect the configured base
        public string BaseUrl { get; private set; }

        public JellyfinAuth(string serverBaseUrl, HttpClient? http = null, ILogger<JellyfinAuth>? logger = null)
        {
            BaseUrl = serverBaseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(serverBaseUrl));
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _logger = logger;
        }

        /// <summary>
        /// Validate a token against the configured base URL or an optional overrideBase.
        /// Returns a JObject representing the user on success, or null on failure.
        /// Throws HttpRequestException on network-level failures.
        /// </summary>
        public async Task<JObject?> GetUserFromTokenAsync(string token, string? overrideBase = null)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            var baseUrl = (overrideBase ?? BaseUrl).TrimEnd('/');

            // First attempt: Users/Me with token in header and Bearer
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/Users/Me");
                req.Headers.Add("X-Emby-Token", token);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var res = await _http.SendAsync(req).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (res.IsSuccessStatusCode)
                {
                    _logger?.LogDebug("JellyfinAuth: token validated via Users/Me at {Base}", baseUrl);
                    return JObject.Parse(body);
                }

                _logger?.LogDebug("JellyfinAuth: Users/Me returned {Status} for base {Base}", (int)res.StatusCode, baseUrl);
            }
            catch (HttpRequestException)
            {
                // Bubble up network errors so callers can decide to retry with fallback
                _logger?.LogWarning("JellyfinAuth: network error contacting Users/Me at {Base}", baseUrl);
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "JellyfinAuth: unexpected error contacting Users/Me at {Base}", baseUrl);
            }

            // Second attempt: System/Info/Public with api_key query (some setups accept API key here)
            try
            {
                var res2 = await _http.GetAsync($"{baseUrl}/System/Info/Public?api_key={token}").ConfigureAwait(false);
                var body2 = await res2.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (res2.IsSuccessStatusCode)
                {
                    _logger?.LogDebug("JellyfinAuth: API key validated via System/Info/Public at {Base}", baseUrl);
                    return JObject.FromObject(new
                    {
                        Id = "ApiKey",
                        Policy = new { IsAdministrator = true }
                    });
                }

                _logger?.LogDebug("JellyfinAuth: System/Info/Public returned {Status} for base {Base}", (int)res2.StatusCode, baseUrl);
            }
            catch (HttpRequestException)
            {
                _logger?.LogWarning("JellyfinAuth: network error contacting System/Info/Public at {Base}", baseUrl);
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "JellyfinAuth: unexpected error contacting System/Info/Public at {Base}", baseUrl);
            }

            _logger?.LogInformation("JellyfinAuth: token validation failed for base {Base}", baseUrl);
            return null;
        }

        public bool IsAdmin(JObject user)
        {
            if (user == null) return false;

            var policy = user["Policy"];
            if (policy != null && policy["IsAdministrator"] != null)
            {
                return policy.Value<bool>("IsAdministrator");
            }

            if (user["HasAdministrativeRole"] != null)
            {
                return user.Value<bool>("HasAdministrativeRole");
            }

            var roles = user["Roles"];
            if (roles != null && roles.Type == JTokenType.Array)
            {
                foreach (var r in roles)
                {
                    if (string.Equals(r.ToString(), "Administrator", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
    }
}
