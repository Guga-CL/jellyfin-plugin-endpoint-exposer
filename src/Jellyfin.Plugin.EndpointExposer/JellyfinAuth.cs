// JellyfinAuth.cs
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EndpointExposer
{
    public class JellyfinAuth
    {
        private readonly HttpClient _http;
        private readonly ILogger? _logger;
        private readonly string _baseCandidate;

        // Exposed so callers can log or inspect the configured base
        public string BaseUrl { get; private set; }

        public JellyfinAuth(string baseCandidate, HttpClient httpClient, ILogger? logger)
        {
            _baseCandidate = baseCandidate?.TrimEnd('/') ?? string.Empty;
            BaseUrl = _baseCandidate;
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
        }


        /// <summary>
        /// Validate a token against the configured base URL or an optional overrideBase.
        /// Returns a JObject representing the user on success, or null on failure.
        /// Throws HttpRequestException on network-level failures.
        /// </summary>
        public async Task<JObject?> GetUserFromTokenAsync(string token, string baseCandidate)
        {
            if (string.IsNullOrWhiteSpace(baseCandidate))
                return null;

            // Build candidate list (start with the authoritative candidate)
            var candidates = new List<string> { baseCandidate.TrimEnd('/') };

            // Optional fallback: if candidate has no path segment, try a common virtual path
            try
            {
                var uri = new Uri(candidates[0]);
                if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
                {
                    candidates.Add(candidates[0].TrimEnd('/') + "/jellyfin");
                }
            }
            catch
            {
                // ignore URI parse errors; we'll still try the raw candidate
            }

            foreach (var candidate in candidates)
            {
                var usersMeUrl = new Uri(new Uri(candidate + "/"), "Users/Me").ToString();

                var req = new HttpRequestMessage(HttpMethod.Get, usersMeUrl);
                req.Headers.Accept.Clear();
                req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                if (!string.IsNullOrWhiteSpace(token))
                {
                    req.Headers.Add("X-Emby-Token", token);
                }

                HttpResponseMessage resp;
                string body = string.Empty;
                try
                {
                    resp = await _http.SendAsync(req).ConfigureAwait(false);
                    body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "GetUserFromTokenAsync: request failed for {Url}", usersMeUrl);
                    continue;
                }

                // Quick guard: HTML likely starts with '<'
                if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith("<"))
                {
                    _logger?.LogDebug("GetUserFromTokenAsync: candidate {Candidate} returned HTML; skipping", candidate);
                    _logger?.LogTrace("GetUserFromTokenAsync: raw response (truncated): {Raw}", body.Length > 2000 ? body.Substring(0, 2000) + "..." : body);
                    continue;
                }

                // If Content-Type indicates JSON, try parse; otherwise still attempt parse defensively
                var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 || !string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        var j = JObject.Parse(body);
                        return j;
                    }
                    catch (Newtonsoft.Json.JsonReaderException jex)
                    {
                        _logger?.LogWarning(jex, "GetUserFromTokenAsync: failed to parse JSON from {Url}", usersMeUrl);
                        _logger?.LogDebug("GetUserFromTokenAsync: raw response (truncated): {Raw}", body.Length > 2000 ? body.Substring(0, 2000) + "..." : body);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "GetUserFromTokenAsync: unexpected parse error for {Url}", usersMeUrl);
                        continue;
                    }
                }
            }

            // Nothing worked
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
// END - JellyfinAuth.cs