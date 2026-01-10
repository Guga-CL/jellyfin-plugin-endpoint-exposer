// Services/AuthService.cs
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.EndpointExposer.Services
{
    /// <summary>
    /// Centralized authentication and authorization service.
    /// Handles token extraction, validation, and authorization decisions.
    /// </summary>
    public class AuthService
    {
        private readonly ILogger<AuthService> _logger;
        private readonly JellyfinAuth _jellyfinAuth;
        private readonly PluginConfiguration _config;

        /// <summary>
        /// Get the current configuration, preferring Plugin.Instance.Configuration if available.
        /// This ensures we always use the most up-to-date configuration.
        /// </summary>
        private PluginConfiguration GetCurrentConfig()
        {
            // Prefer Plugin.Instance.Configuration if available (most up-to-date)
            var pluginConfig = Plugin.Instance?.Configuration;
            if (pluginConfig != null)
                return pluginConfig;

            // Fallback to injected config
            return _config;
        }

        public AuthService(ILogger<AuthService> logger, JellyfinAuth jellyfinAuth, PluginConfiguration config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jellyfinAuth = jellyfinAuth ?? throw new ArgumentNullException(nameof(jellyfinAuth));
            _config = config ?? new PluginConfiguration();
        }

        /// <summary>
        /// Extract authentication token from HttpRequest in priority order:
        /// 1. Authorization header (Bearer or MediaBrowser)
        /// 2. X-Emby-Token header
        /// 3. X-Jellyfin-Token header
        /// 4. api_key query parameter
        /// Returns null if no token found.
        /// </summary>
        public string? ExtractTokenFromRequest(HttpRequest request)
        {
            if (request == null)
                return null;

            // 1. Authorization header: Bearer or MediaBrowser
            if (request.Headers.TryGetValue("Authorization", out var authValues))
            {
                var auth = authValues.Count > 0 ? authValues[0] : authValues.ToString();
                if (!string.IsNullOrWhiteSpace(auth))
                {
                    // Existing Bearer/MediaBrowser checks
                    if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        return auth.Substring(7).Trim();
                    const string mbPrefix = "MediaBrowser Token=\"";
                    if (auth.StartsWith(mbPrefix, StringComparison.OrdinalIgnoreCase) && auth.EndsWith("\""))
                        return auth.Substring(mbPrefix.Length, auth.Length - mbPrefix.Length - 1);

                    // **NEW: Extract Token="<TOKEN>" from anywhere in the header**
                    var tokenIndex = auth.IndexOf("Token=\"", StringComparison.Ordinal);
                    if (tokenIndex >= 0)
                    {
                        tokenIndex += 7;
                        var after = auth.IndexOf("\"", tokenIndex, StringComparison.Ordinal);
                        if (after > tokenIndex)
                        {
                            var token = auth.Substring(tokenIndex, after - tokenIndex).Trim();
                            _logger.LogDebug("ExtractTokenFromRequest: found Token in MediaBrowser header, extracted (length={Length})", token.Length);
                            return token;
                        }
                    }
                }
            }

            // 2. X-Emby-Token header
            if (request.Headers.TryGetValue("X-Emby-Token", out var xEmby) && !string.IsNullOrWhiteSpace(xEmby))
                return xEmby.ToString();

            // 3. X-Jellyfin-Token header
            if (request.Headers.TryGetValue("X-Jellyfin-Token", out var xJelly) && !string.IsNullOrWhiteSpace(xJelly))
                return xJelly.ToString();

            // 4. api_key query parameter
            if (request.Query.TryGetValue("api_key", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey))
                return apiKey.ToString();

            return null;
        }

        /// <summary>
        /// Extract optional API key from X-EndpointExposer-Key header or api_key query parameter.
        /// </summary>
        public string? ExtractApiKeyFromRequest(HttpRequest request)
        {
            if (request == null)
                return null;

            // Header first
            if (request.Headers.TryGetValue("X-EndpointExposer-Key", out var headerKey) && !string.IsNullOrWhiteSpace(headerKey))
                return headerKey.ToString();

            // Query parameter fallback
            if (request.Query.TryGetValue("api_key", out var queryKey) && !string.IsNullOrWhiteSpace(queryKey))
                return queryKey.ToString();

            return null;
        }

        /// <summary>
        /// Validate a token against the Jellyfin server with automatic fallback to derived base URL.
        /// Returns user object (JObject) on success, null on failure.
        /// </summary>
        public async Task<JObject?> ValidateTokenAsync(string token, HttpRequest? request)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            // 1. Try primary auth using the base the JellyfinAuth was constructed with
            try
            {
                var primaryBase = _jellyfinAuth?.BaseUrl ?? string.Empty;
                _logger.LogDebug("ValidateTokenAsync: attempting primary validation with base={Base}", primaryBase);
                if (!string.IsNullOrWhiteSpace(primaryBase) && _jellyfinAuth != null)
                {
                    var user = await _jellyfinAuth.GetUserFromTokenAsync(token, primaryBase).ConfigureAwait(false);
                    if (user != null)
                    {
                        _logger.LogInformation("ValidateTokenAsync: token validated using primary auth base ({Base})", primaryBase);
                        return user;
                    }
                    else
                    {
                        _logger.LogDebug("ValidateTokenAsync: primary validation returned null for base={Base}", primaryBase);
                    }
                }
                else
                {
                    _logger.LogDebug("ValidateTokenAsync: skipping primary validation (primaryBase empty or _jellyfinAuth null)");
                }
            }
            catch (System.Net.Http.HttpRequestException hre)
            {
                _logger.LogWarning(hre, "ValidateTokenAsync: primary auth call failed (will attempt fallback). Base={Base}", _jellyfinAuth?.BaseUrl ?? "null");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ValidateTokenAsync: primary auth call failed with unexpected error (will attempt fallback). Base={Base}", _jellyfinAuth?.BaseUrl ?? "null");
            }

            // 2. Build fallback base URL with preference order (matches backup logic):
            // 1) PluginConfiguration.ServerBaseUrl
            // 2) Request-derived host/scheme (including PathBase like /jellyfin)
            // 3) Default 127.0.0.1:8096 (but try to preserve PathBase from request)
            var config = GetCurrentConfig();
            string fallbackBase;
            string? requestPathBase = null;

            // Try to preserve PathBase from request for the default fallback
            if (request != null && request.PathBase.HasValue)
            {
                requestPathBase = request.PathBase.Value.TrimEnd('/');
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(config?.ServerBaseUrl))
                {
                    fallbackBase = config.ServerBaseUrl.TrimEnd('/');
                }
                else
                {
                    var derived = DeriveBaseUrlFromRequest(request);
                    if (!string.IsNullOrWhiteSpace(derived))
                    {
                        fallbackBase = derived;
                    }
                    else
                    {
                        // Default fallback, but include PathBase if available
                        fallbackBase = "http://127.0.0.1:8096";
                        if (!string.IsNullOrWhiteSpace(requestPathBase))
                        {
                            fallbackBase = $"{fallbackBase}{requestPathBase}";
                        }
                    }
                }
            }
            catch
            {
                // Ignore and use default (with PathBase if available)
                fallbackBase = "http://127.0.0.1:8096";
                if (!string.IsNullOrWhiteSpace(requestPathBase))
                {
                    fallbackBase = $"{fallbackBase}{requestPathBase}";
                }
            }

            // If fallbackBase equals the primary auth base, no point retrying
            var primary = _jellyfinAuth?.BaseUrl ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(primary) && string.Equals(fallbackBase, primary, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("ValidateTokenAsync: fallback base equals primary base ({Base}), skipping fallback.", fallbackBase);
                return null;
            }

            // 3. Attempt validation against fallback base
            try
            {
                _logger.LogInformation("ValidateTokenAsync: attempting token validation against fallback base {Base}", fallbackBase);
                if (_jellyfinAuth != null)
                {
                    var user = await _jellyfinAuth.GetUserFromTokenAsync(token, fallbackBase).ConfigureAwait(false);
                    if (user != null)
                    {
                        _logger.LogInformation("ValidateTokenAsync: token validated against fallback base {Base}", fallbackBase);
                        return user;
                    }
                }
            }
            catch (System.Net.Http.HttpRequestException hre2)
            {
                _logger.LogWarning(hre2, "ValidateTokenAsync: fallback auth call failed for base {Base}", fallbackBase);
            }
            catch (Exception ex2)
            {
                _logger.LogWarning(ex2, "ValidateTokenAsync: fallback auth call failed for base {Base}", fallbackBase);
            }

            _logger.LogWarning("ValidateTokenAsync: token validation failed for both primary and fallback bases.");
            return null;
        }

        /// <summary>
        /// Determine if a user object has admin privileges.
        /// </summary>
        public bool IsUserAdmin(JObject? user)
        {
            if (user == null)
                return false;

            return _jellyfinAuth.IsAdmin(user);
        }

        /// <summary>
        /// Determine effective server base URL from request in priority order:
        /// 1. Explicit ServerBaseUrl from config (most authoritative)
        /// 2. Derived from incoming request (scheme + host + PathBase)
        /// 3. Provided default fallback
        /// </summary>
        public string GetEffectiveServerBase(HttpRequest? request, string defaultBase = "")
        {
            var config = GetCurrentConfig();
            // Prefer explicit ServerBaseUrl if configured
            if (!string.IsNullOrWhiteSpace(config?.ServerBaseUrl))
                return config.ServerBaseUrl.TrimEnd('/');

            // Derive from request
            var derived = DeriveBaseUrlFromRequest(request);
            if (!string.IsNullOrWhiteSpace(derived))
                return derived.TrimEnd('/');

            // Fallback
            return defaultBase.TrimEnd('/');
        }

        /// <summary>
        /// Derive base URL from HttpRequest (scheme + host + PathBase if present).
        /// This is critical for plugins running behind reverse proxies or with path bases like /jellyfin.
        /// </summary>
        private string? DeriveBaseUrlFromRequest(HttpRequest? request)
        {
            if (request == null || !request.Host.HasValue)
                return null;

            try
            {
                // Check X-Forwarded-Proto for true scheme in proxy setups
                var scheme = request.Headers.TryGetValue("X-Forwarded-Proto", out var proto)
                    ? proto.ToString()
                    : (string.IsNullOrWhiteSpace(request.Scheme) ? "http" : request.Scheme);

                var host = request.Host.Value;
                var pathBase = request.PathBase.HasValue ? request.PathBase.Value.TrimEnd('/') : string.Empty;
                var derived = $"{scheme}://{host}{pathBase}".TrimEnd('/');
                _logger.LogDebug("DeriveBaseUrlFromRequest: derived base URL {Base} (scheme={Scheme}, host={Host}, pathBase={PathBase})",
                    derived, scheme, host, pathBase);
                return derived;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DeriveBaseUrlFromRequest: failed to derive base URL from request");
                return null;
            }
        }

        /// <summary>
        /// Comprehensive authorization check: determine if request is authorized to perform write operations.
        /// Returns (isAuthorized, reason).
        /// </summary>
        public (bool IsAuthorized, string? Reason) CheckWriteAuthorization(HttpRequest request, JObject? validatedUser = null)
        {
            if (request == null)
                return (false, "No request context");

            var config = GetCurrentConfig();
            var isAdmin = validatedUser != null && IsUserAdmin(validatedUser);
            var globalAllowNonAdmin = config?.AllowNonAdmin ?? false;
            var apiKeyConfigured = !string.IsNullOrWhiteSpace(config?.ApiKey);
            var providedApiKey = ExtractApiKeyFromRequest(request);
            var apiKeyValid = !string.IsNullOrWhiteSpace(providedApiKey) && string.Equals(providedApiKey, config?.ApiKey, StringComparison.Ordinal);

            // Admin always authorized
            if (isAdmin)
                return (true, null);

            // Non-admin: check API key if global allowNonAdmin is true
            if (globalAllowNonAdmin && apiKeyConfigured)
            {
                if (apiKeyValid)
                    return (true, null);
                else
                    return (false, "Invalid API key");
            }

            // Fallback: deny
            return (false, "Unauthorized: requires admin or valid API key");
        }

        /// <summary>
        /// Authorization check for folder operations.
        /// Returns (isAuthorized, reason).
        /// </summary>
        public (bool IsAuthorized, string? Reason) CheckFolderWriteAuthorization(HttpRequest request, string folderName, JObject? validatedUser = null)
        {
            if (request == null)
                return (false, "No request context");

            var config = GetCurrentConfig();
            var isAdmin = validatedUser != null && IsUserAdmin(validatedUser);

            // Admin always authorized
            if (isAdmin)
            {
                _logger.LogDebug("CheckFolderWriteAuthorization: admin user authorized for folder={Folder}", folderName);
                return (true, null);
            }

            // Get folder-specific settings
            var folderEntry = config?.ExposedFolders?.Find(f =>
                string.Equals(f.Name, folderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.RelativePath, folderName, StringComparison.OrdinalIgnoreCase));

            var folderAllowsNonAdmin = folderEntry?.AllowNonAdmin ?? false;
            var globalAllowNonAdmin = config?.AllowNonAdmin ?? false;
            var apiKeyConfigured = !string.IsNullOrWhiteSpace(config?.ApiKey);
            var providedApiKey = ExtractApiKeyFromRequest(request);
            var apiKeyValid = !string.IsNullOrWhiteSpace(providedApiKey) && string.Equals(providedApiKey, config?.ApiKey, StringComparison.Ordinal);

            _logger.LogDebug("CheckFolderWriteAuthorization: folder={Folder}, isAdmin={IsAdmin}, folderAllowsNonAdmin={FolderAllows}, globalAllowNonAdmin={GlobalAllows}, apiKeyConfigured={ApiKeySet}, apiKeyValid={ApiKeyValid}, folderEntryFound={FolderFound}",
                folderName, isAdmin, folderAllowsNonAdmin, globalAllowNonAdmin, apiKeyConfigured, apiKeyValid, folderEntry != null);

            // If API key is valid, allow (implicit - we don't return Unauthorized)
            // If API key is NOT valid (or not provided), check folder settings
            if (!apiKeyValid)
            {
                // API key doesn't match or wasn't provided
                // Deny if: !allowNonAdminGlobal || !apiKeySet || !folderAllows
                // Allow if: allowNonAdminGlobal && apiKeySet && folderAllows
                if (!globalAllowNonAdmin || !apiKeyConfigured || !folderAllowsNonAdmin)
                {
                    var reason = $"Unauthorized: requires admin, valid API key, or folder allows non-admin with proper settings (globalAllow={globalAllowNonAdmin}, apiKeySet={apiKeyConfigured}, folderAllows={folderAllowsNonAdmin})";
                    _logger.LogWarning("CheckFolderWriteAuthorization: {Reason}", reason);
                    return (false, reason);
                }
            }

            // If we get here, either:
            // 1. API key is valid, OR
            // 2. API key is not valid but (allowNonAdminGlobal && apiKeySet && folderAllows) is true
            _logger.LogDebug("CheckFolderWriteAuthorization: authorized for folder={Folder}", folderName);
            return (true, null);
        }
    }
}
// END - Services/AuthService.cs