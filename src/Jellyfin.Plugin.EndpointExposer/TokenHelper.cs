// TokenHelper.cs
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Jellyfin.Plugin.EndpointExposer
{
    public static class TokenHelper
    {
        /// <summary>
        /// Extract a token from an ASP.NET Core HttpRequest.
        /// Checks X-Emby-Token, X-Jellyfin-Token, Authorization (Bearer or MediaBrowser),
        /// then looks for api_key in the query string.
        /// </summary>
        public static string? ExtractTokenFromRequest(HttpRequest request)
        {
            if (request == null) return null;

            // 1) Header shortcuts
            if (request.Headers.TryGetValue("X-Emby-Token", out var xEmby) && !string.IsNullOrWhiteSpace(xEmby))
                return xEmby.ToString();

            if (request.Headers.TryGetValue("X-Jellyfin-Token", out var xJelly) && !string.IsNullOrWhiteSpace(xJelly))
                return xJelly.ToString();

            // 2) Authorization header
            if (request.Headers.TryGetValue("Authorization", out var authValues))
            {
                var auth = authValues.ToString();
                if (!string.IsNullOrWhiteSpace(auth))
                {
                    if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        return auth.Substring("Bearer ".Length).Trim();

                    const string mbPrefix = "MediaBrowser Token=\"";
                    if (auth.StartsWith(mbPrefix, StringComparison.OrdinalIgnoreCase) && auth.EndsWith("\""))
                        return auth.Substring(mbPrefix.Length, auth.Length - mbPrefix.Length - 1);
                }
            }

            // 3) Query string: api_key
            if (request.Query.TryGetValue("api_key", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey))
                return apiKey.ToString();

            // 4) Fallback: parse full query string (defensive)
            var qs = request.QueryString.HasValue ? request.QueryString.Value : null;
            if (!string.IsNullOrEmpty(qs))
            {
                var parsed = QueryHelpers.ParseQuery(qs);
                if (parsed.TryGetValue("api_key", out var apiKey2) && !string.IsNullOrWhiteSpace(apiKey2))
                    return apiKey2.ToString();
            }

            return null;
        }

        /// <summary>
        /// Optional helper for non-HttpRequest contexts: provide headers and an optional Uri.
        /// Useful if we will still have legacy code that provides NameValueCollection or similar.
        /// </summary>
        public static string? ExtractTokenFromHeaders(IDictionary<string, string?> headers, Uri? url = null)
        {
            if (headers == null) return null;

            if (headers.TryGetValue("X-Emby-Token", out var xEmby) && !string.IsNullOrWhiteSpace(xEmby))
                return xEmby;

            if (headers.TryGetValue("X-Jellyfin-Token", out var xJelly) && !string.IsNullOrWhiteSpace(xJelly))
                return xJelly;

            if (headers.TryGetValue("Authorization", out var auth) && !string.IsNullOrWhiteSpace(auth))
            {
                if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return auth.Substring("Bearer ".Length).Trim();

                const string mbPrefix = "MediaBrowser Token=\"";
                if (auth.StartsWith(mbPrefix, StringComparison.OrdinalIgnoreCase) && auth.EndsWith("\""))
                    return auth.Substring(mbPrefix.Length, auth.Length - mbPrefix.Length - 1);
            }

            if (url != null)
            {
                var parsed = QueryHelpers.ParseQuery(url.Query);
                if (parsed.TryGetValue("api_key", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey))
                    return apiKey.ToString();
            }

            return null;
        }
    }
}
// END - TokenHelper.cs