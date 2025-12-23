// Services/EndpointExposerService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;


namespace Jellyfin.Plugin.EndpointExposer.Services
{
    /// <summary>
    /// Core logic for EndpointExposer: file operations, config persistence, write handling, backups.
    /// This version adds robust token validation fallback and avoids hardcoded server URLs.
    /// </summary>
    public class EndpointExposerService
    {
        private readonly ILogger<EndpointExposerService> _logger;
        private readonly PluginConfiguration _config;
        private readonly FileWriteService _fileWriteService;
        private readonly JellyfinAuth _auth;
        private readonly string _outDir;
        private readonly string[] _corsAllowedOrigins = new[] { "http://127.0.0.1:8096", "http://localhost:8096" };

        public EndpointExposerService(ILogger<EndpointExposerService> logger, PluginConfiguration config, FileWriteService fileWriteService, JellyfinAuth auth)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? new PluginConfiguration();
            _fileWriteService = fileWriteService ?? throw new ArgumentNullException(nameof(fileWriteService));
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));

            // Determine output directory preference:
            // 1) explicit OutputDirectory from config
            // 2) plugin data dir via Plugin.Instance.GetPluginDataDir() (if available)
            // 3) fallback to LocalApplicationData path (legacy)
            if (!string.IsNullOrWhiteSpace(_config.OutputDirectory))
            {
                _outDir = _config.OutputDirectory!;
            }
            else
            {
                try
                {
                    var pluginDir = Plugin.Instance?.GetPluginDataDir();
                    if (!string.IsNullOrWhiteSpace(pluginDir))
                    {
                        _outDir = pluginDir;
                    }
                    else
                    {
                        _outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "jellyfin", "plugins", "EndpointExposer_data");
                    }
                }
                catch
                {
                    _outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "jellyfin", "plugins", "EndpointExposer_data");
                }
            }

            try
            {
                Directory.CreateDirectory(_outDir);
                _logger.LogInformation("EndpointExposerService: using output directory {OutDir}", _outDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create output directory {OutDir}", _outDir);
            }
        }


        public PluginConfiguration GetConfiguration() => _config;

        public IEnumerable<string> ListFiles(string? ext = null)
        {
            if (!Directory.Exists(_outDir))
                return Array.Empty<string>();

            return Directory.EnumerateFiles(_outDir, "*", SearchOption.TopDirectoryOnly)
                .Where(f => string.IsNullOrEmpty(ext) || f.EndsWith(ext!, StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileName(f)!)
                .ToList();
        }



        #region Folder-based helpers

        private static readonly Regex FolderTokenRegex = new Regex("^[a-z0-9_-]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FileNameRegex = new Regex(@"^[\w\-.]+$", RegexOptions.Compiled);

        /// <summary>
        /// Resolve a configured folder entry by logical name and return the absolute folder path.
        /// Throws ArgumentException if folder is invalid or not configured.
        /// </summary>
        public string ResolveFolderPathFromConfig(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName) || !FolderTokenRegex.IsMatch(folderName))
                throw new ArgumentException("Invalid folder name", nameof(folderName));

            // Find matching FolderEntry in configuration (case-insensitive match on Name or RelativePath)
            var entry = _config?.ExposedFolders?.FirstOrDefault(f =>
                string.Equals(f.Name, folderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.RelativePath, folderName, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
                throw new ArgumentException("Folder not configured", nameof(folderName));

            // Ensure RelativePath is a single token and safe
            var rel = entry.RelativePath;
            if (string.IsNullOrWhiteSpace(rel) || !FolderTokenRegex.IsMatch(rel))
                throw new ArgumentException("Configured folder has invalid RelativePath", nameof(folderName));

            // Base plugin data dir (use Plugin.Instance helper if available)
            string baseDir;
            try
            {
                baseDir = Plugin.Instance?.GetPluginDataDir() ?? GetDefaultDataDir();
            }
            catch
            {
                baseDir = GetDefaultDataDir();
            }

            var folderDir = Path.Combine(baseDir, rel);
            Directory.CreateDirectory(folderDir);
            return folderDir;
        }

        private string GetDefaultDataDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "jellyfin", "plugins", "configurations", typeof(Plugin).Namespace ?? "Jellyfin.Plugin.EndpointExposer", "data");
        }

        /// <summary>
        /// List files in a configured folder (top-level only).
        /// </summary>
        public List<string> ListFolderFiles(string folderName)
        {
            var folderDir = ResolveFolderPathFromConfig(folderName);
            if (!Directory.Exists(folderDir))
                return new List<string>();

            // Ensure we return a non-nullable List<string> by filtering null/empty names
            return Directory.EnumerateFiles(folderDir, "*", SearchOption.TopDirectoryOnly)
                            .Select(Path.GetFileName)
                            .Where(n => !string.IsNullOrEmpty(n))
                            .Select(n => n!) // assert non-null after the filter
                            .ToList();
        }



        /// <summary>
        /// Read a file from a configured folder. Returns (Exists, Bytes, ContentType, FileName).
        /// </summary>
        public (bool Exists, byte[] Bytes, string? ContentType, string FileName) ReadFolderFile(string folderName, string fileName)
        {
            var folderDir = ResolveFolderPathFromConfig(folderName);
            var safeName = Path.GetFileName(fileName ?? string.Empty) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeName) || !FileNameRegex.IsMatch(safeName))
                return (false, Array.Empty<byte>(), null, safeName);

            var path = Path.Combine(folderDir, safeName);
            if (!File.Exists(path))
                return (false, Array.Empty<byte>(), null, safeName);

            var bytes = File.ReadAllBytes(path);
            var ct = GetContentTypeByExtension(Path.GetExtension(path));
            return (true, bytes, ct, safeName);
        }

        /// <summary>
        /// Write bytes/text into a configured folder using FileWriteService for atomic writes and backups.
        /// Returns (Success, Path, Error).
        /// </summary>
        public async Task<(bool Success, string? Path, string? Error)> WriteFileToFolderAsync(string folderName, string fileName, byte[] bytes, CancellationToken ct = default)
        {
            if (bytes == null) bytes = Array.Empty<byte>();

            var folderDir = ResolveFolderPathFromConfig(folderName);
            var safeName = Path.GetFileName(fileName ?? string.Empty) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeName) || !FileNameRegex.IsMatch(safeName))
                return (false, null, "Invalid file name");

            var path = Path.Combine(folderDir, safeName);

            // Enforce MaxPayloadBytes
            var maxBytes = _config?.MaxPayloadBytes ?? 2 * 1024 * 1024;
            if (bytes.Length > maxBytes)
            {
                _logger.LogWarning("WriteFileToFolderAsync: payload too large ({Len} > {Max}) for {File}", bytes.Length, maxBytes, safeName);
                return (false, null, "Payload too large");
            }

            try
            {
                await _fileWriteService.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                _logger.LogInformation("WriteFileToFolderAsync: saved {File} ({Bytes} bytes) in folder {Folder}", safeName, bytes.Length, folderName);
                return (true, path, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WriteFileToFolderAsync: failed to write {File} in folder {Folder}", safeName, folderName);
                return (false, null, "Failed to write file");
            }
        }

        #endregion





        /// <summary>
        /// Returns: (Exists, Bytes, ContentType (nullable), FileName)
        /// </summary>
        public (bool Exists, byte[] Bytes, string? ContentType, string FileName) GetFile(string name)
        {
            var safeName = Path.GetFileName(name ?? string.Empty) ?? string.Empty;
            var path = Path.Combine(_outDir, safeName);
            if (!File.Exists(path))
                return (false, Array.Empty<byte>(), null, safeName);

            var bytes = File.ReadAllBytes(path);
            var contentType = GetContentTypeByExtension(Path.GetExtension(path));
            return (true, bytes, contentType, safeName);
        }

        public async Task<(bool Success, string? Path, string? Error)> SaveFileFromRequestAsync(HttpRequest request)
        {
            if (request.ContentLength == null || request.ContentLength == 0)
                return (false, null, "Request body is empty.");

            try
            {
                if (request.ContentType != null && request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    using var sr = new StreamReader(request.Body, Encoding.UTF8);
                    var json = await sr.ReadToEndAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(json))
                        return (false, null, "Empty JSON payload.");

                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("name", out var nameProp))
                        return (false, null, "Missing 'name' property.");

                    var name = nameProp.GetString();
                    if (string.IsNullOrWhiteSpace(name))
                        return (false, null, "Invalid 'name' property.");

                    string? content = null;
                    if (doc.RootElement.TryGetProperty("content", out var contentProp))
                        content = contentProp.GetString();

                    if (content == null)
                        return (false, null, "Missing 'content' property.");

                    var safeName = Path.GetFileName(name);
                    var path = Path.Combine(_outDir, safeName);

                    byte[] bytes;
                    if (IsBase64String(content))
                        bytes = Convert.FromBase64String(content);
                    else
                        bytes = Encoding.UTF8.GetBytes(content);

                    await _fileWriteService.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                    return (true, path, null);
                }
                else
                {
                    var name = request.Query["name"].FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(name))
                        return (false, null, "Query parameter 'name' is required for raw body uploads.");

                    var safeName = Path.GetFileName(name);
                    var path = Path.Combine(_outDir, safeName);

                    using var ms = new MemoryStream();
                    await request.Body.CopyToAsync(ms).ConfigureAwait(false);
                    var bytes = ms.ToArray();

                    await _fileWriteService.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                    return (true, path, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveFileFromRequestAsync failed");
                return (false, null, "Failed to save file");
            }
        }

        public bool DeleteFile(string name)
        {
            var safeName = Path.GetFileName(name ?? string.Empty) ?? string.Empty;
            var path = Path.Combine(_outDir, safeName);
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }

        public async Task<string> FetchRemoteConfigAsync(string url)
        {
            using var client = new HttpClient();
            var res = await client.GetAsync(url).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        public string[] GetCorsAllowedOrigins() => _corsAllowedOrigins;

        /// <summary>
        /// Try to validate token using the primary _auth instance. If that fails due to connection issues,
        /// attempt a fallback validation using an alternate base URL derived from config, request, or default.
        /// Returns the user JObject or null if validation fails.
        /// </summary>
        private async Task<JObject?> ValidateTokenWithFallbackAsync(string token, HttpRequest? request)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            // First attempt: use injected _auth
            try
            {
                var user = await _auth.GetUserFromTokenAsync(token).ConfigureAwait(false);
                if (user != null)
                {
                    _logger.LogDebug("ValidateTokenWithFallbackAsync: token validated using primary auth base ({Base})", _auth.BaseUrl);
                    return user;
                }
            }
            catch (HttpRequestException hre)
            {
                _logger.LogWarning(hre, "ValidateTokenWithFallbackAsync: primary auth call failed (will attempt fallback). PrimaryBase may be unreachable: {Base}", _auth.BaseUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ValidateTokenWithFallbackAsync: primary auth call failed with unexpected error (will attempt fallback). PrimaryBase: {Base}", _auth.BaseUrl);
            }

            // Build fallback base URL preference order:
            // 1) PluginConfiguration.ServerBaseUrl
            // 2) Request-derived host/scheme
            // 3) Default 127.0.0.1:8096
            string fallbackBase = "http://127.0.0.1:8096";
            try
            {
                if (!string.IsNullOrWhiteSpace(_config?.ServerBaseUrl))
                {
                    fallbackBase = _config.ServerBaseUrl.TrimEnd('/');
                }
                else if (request != null && request.Host.HasValue)
                {
                    var scheme = string.IsNullOrWhiteSpace(request.Scheme) ? "http" : request.Scheme;
                    fallbackBase = $"{scheme}://{request.Host.Value}".TrimEnd('/');
                }
            }
            catch { /* ignore and use default */ }

            // If fallbackBase equals the primary auth base, no point retrying
            if (string.Equals(fallbackBase, _auth.BaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("ValidateTokenWithFallbackAsync: fallback base equals primary base ({Base}), skipping fallback.", fallbackBase);
                return null;
            }

            try
            {
                _logger.LogInformation("ValidateTokenWithFallbackAsync: attempting token validation against fallback base {Base}", fallbackBase);
                var user = await _auth.GetUserFromTokenAsync(token, fallbackBase).ConfigureAwait(false);
                if (user != null)
                {
                    _logger.LogInformation("ValidateTokenWithFallbackAsync: token validated against fallback base {Base}", fallbackBase);
                    return user;
                }
            }
            catch (HttpRequestException hre2)
            {
                _logger.LogWarning(hre2, "ValidateTokenWithFallbackAsync: fallback auth call failed for base {Base}", fallbackBase);
            }
            catch (Exception ex2)
            {
                _logger.LogWarning(ex2, "ValidateTokenWithFallbackAsync: fallback auth call failed for base {Base}", fallbackBase);
            }

            _logger.LogWarning("ValidateTokenWithFallbackAsync: token validation failed for both primary and fallback bases.");
            return null;
        }

        /// <summary>
        /// Public wrapper that invokes the existing (possibly non-public) ValidateTokenWithFallbackAsync method via reflection.
        /// Returns the underlying user object (or null) as returned by the original method.
        /// </summary>
        public async Task<object?> ValidateTokenWithFallbackPublicAsync(string token, Microsoft.AspNetCore.Http.HttpRequest? req)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            try
            {
                var method = this.GetType().GetMethod("ValidateTokenWithFallbackAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (method == null)
                    return null;

                var result = method.Invoke(this, new object[] { token, req });
                if (result is System.Threading.Tasks.Task task)
                {
                    await task.ConfigureAwait(false);
                    var resultProp = task.GetType().GetProperty("Result");
                    return resultProp?.GetValue(task);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Public helper to check whether a user object is an admin using the internal JellyfinAuth instance.
        /// Returns false on error or if _auth is not available.
        /// </summary>
        public bool IsUserAdminPublic(object? user)
        {
            if (user == null) return false;
            try
            {
                // _auth is the JellyfinAuth instance already present in the service
                if (_auth == null) return false;

                // JellyfinAuth typically exposes IsAdmin(UserDto) or similar; call via reflection to avoid signature assumptions
                var authType = _auth.GetType();
                var isAdminMethod = authType.GetMethod("IsAdmin", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (isAdminMethod != null)
                {
                    var res = isAdminMethod.Invoke(_auth, new[] { user });
                    if (res is bool b) return b;
                }

                // If reflection failed, try dynamic invocation (best-effort)
                try
                {
                    dynamic dyn = _auth;
                    return (bool)dyn.IsAdmin((dynamic)user);
                }
                catch
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }


        public async Task<WriteOutcome> HandleWriteAsync(HttpRequest request, string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return WriteOutcome.CreateFail(400, "Name is required");

                // Validate name (sanitization)
                var safeFileName = Path.GetFileName(name) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(safeFileName))
                    return WriteOutcome.CreateFail(400, "Invalid name");

                // Auth: check user principal first (if present) else API key via header or query
                var apiKeyHeader = request.Headers.ContainsKey("X-EndpointExposer-Key") ? request.Headers["X-EndpointExposer-Key"].ToString() : null;
                bool isAdmin = false;
                bool apiKeySet = !string.IsNullOrWhiteSpace(_config.ApiKey);
                bool allowNonAdmin = _config.AllowNonAdmin;

                // If request has Authorization header, try to validate token via JellyfinAuth
                string? token = null;
                if (request.Headers.ContainsKey("Authorization"))
                {
                    var auth = request.Headers["Authorization"].ToString();
                    if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        token = auth.Substring(7).Trim();
                }

                // Also accept X-Emby-Token / X-Jellyfin-Token
                if (string.IsNullOrWhiteSpace(token))
                {
                    if (request.Headers.ContainsKey("X-Emby-Token"))
                        token = request.Headers["X-Emby-Token"].ToString();
                    else if (request.Headers.ContainsKey("X-Jellyfin-Token"))
                        token = request.Headers["X-Jellyfin-Token"].ToString();
                }

                // Query api_key fallback
                if (string.IsNullOrWhiteSpace(token) && request.Query.ContainsKey("api_key"))
                    token = request.Query["api_key"].ToString();

                JObject? user = null;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    user = await ValidateTokenWithFallbackAsync(token, request).ConfigureAwait(false);
                    if (user != null)
                        isAdmin = _auth.IsAdmin(user);
                }

                if (!isAdmin)
                {
                    if (!allowNonAdmin || !apiKeySet)
                    {
                        _logger.LogWarning("HandleWriteAsync: unauthorized attempt for {Name} (no admin, api key not allowed/set)", name);
                        return WriteOutcome.CreateFail(401, "Unauthorized");
                    }

                    // Validate api key header or query param
                    var providedKey = apiKeyHeader;
                    if (string.IsNullOrWhiteSpace(providedKey) && request.Query.ContainsKey("api_key"))
                        providedKey = request.Query["api_key"].ToString();

                    if (!string.Equals(providedKey, _config.ApiKey, StringComparison.Ordinal))
                    {
                        _logger.LogWarning("HandleWriteAsync: invalid api key for {Name}", name);
                        return WriteOutcome.CreateFail(401, "Unauthorized");
                    }
                }

                // Enforce MaxPayloadBytes
                var maxBytes = _config?.MaxPayloadBytes ?? 2 * 1024 * 1024;
                if (request.ContentLength.HasValue && request.ContentLength.Value > maxBytes)
                {
                    _logger.LogWarning("HandleWriteAsync: payload too large (ContentLength {Len} > {Max}) for {Name}", request.ContentLength.Value, maxBytes, name);
                    return WriteOutcome.CreateFail(413, "Payload too large");
                }

                // Read body safely up to limit
                string body;
                using (var ms = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    long total = 0;
                    int read;
                    while ((read = await request.Body.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > maxBytes)
                        {
                            _logger.LogWarning("HandleWriteAsync: payload exceeded max while reading for {Name}", name);
                            return WriteOutcome.CreateFail(413, "Payload too large");
                        }
                        ms.Write(buffer, 0, read);
                    }
                    body = Encoding.UTF8.GetString(ms.ToArray());
                }

                if (string.IsNullOrWhiteSpace(body))
                    return WriteOutcome.CreateFail(400, "Missing body");

                // Validate JSON
                JToken parsed;
                try
                {
                    parsed = JToken.Parse(body);
                }
                catch (Newtonsoft.Json.JsonException jex)
                {
                    _logger.LogWarning(jex, "HandleWriteAsync: invalid JSON for {Name}", name);
                    return WriteOutcome.CreateFail(400, "Invalid JSON");
                }

                // Prepare paths
                var safePath = Path.Combine(_outDir, safeFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(safePath) ?? _outDir);

                // Use FileWriteService to perform atomic write and create backups
                try
                {
                    var content = parsed.ToString(Newtonsoft.Json.Formatting.Indented);
                    await _fileWriteService.WriteAllTextAsync(safePath, content, Encoding.UTF8).ConfigureAwait(false);

                    _logger.LogInformation("HandleWriteAsync: saved {Name} ({Bytes} bytes)", name, Encoding.UTF8.GetByteCount(content));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "HandleWriteAsync: failed to write file for {Name}", name);
                    return WriteOutcome.CreateFail(500, "Failed to write file");
                }

                // Prune backups (defensive)
                try
                {
                    var backupsDir = Path.Combine(Path.GetDirectoryName(safePath) ?? _outDir, "backups");
                    var maxBackups = _config?.MaxBackups ?? 10;
                    if (Directory.Exists(backupsDir))
                    {
                        var prefix = Path.GetFileName(safePath) + ".";
                        var backups = Directory.GetFiles(backupsDir, prefix + "*.bak", SearchOption.TopDirectoryOnly)
                                               .Select(p => new FileInfo(p))
                                               .OrderByDescending(fi => fi.CreationTimeUtc)
                                               .ToList();
                        if (backups.Count > maxBackups)
                        {
                            foreach (var toDelete in backups.Skip(maxBackups))
                            {
                                try { toDelete.Delete(); }
                                catch (Exception ex) { _logger.LogWarning(ex, "HandleWriteAsync: failed to delete old backup {File}", toDelete.FullName); }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "HandleWriteAsync: backup pruning failed for {Name}", name);
                }

                return WriteOutcome.CreateSuccess(name, safePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HandleWriteAsync: unexpected error");
                return WriteOutcome.CreateFail(500, "Internal server error");
            }
        }

        public async Task SaveConfigurationAsync(PluginConfiguration incoming)
        {
            // Persist JSON into the server plugin configurations folder so it matches the XML fallback
            var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "jellyfin", "plugins", "configurations", typeof(Plugin).Namespace ?? "Jellyfin.Plugin.EndpointExposer");
            Directory.CreateDirectory(configDir);

            var fileName = "configuration.json";
            var path = Path.Combine(configDir, fileName);
            var json = JsonSerializer.Serialize(incoming, new JsonSerializerOptions { WriteIndented = true });

            try
            {
                await _fileWriteService.WriteAllTextAsync(path, json, Encoding.UTF8).ConfigureAwait(false);
                _logger.LogInformation("SaveConfigurationAsync: persisted configuration JSON to {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveConfigurationAsync: failed to persist configuration to {Path}", path);
                throw;
            }
        }


        private static string? GetContentTypeByExtension(string? ext)
        {
            if (string.IsNullOrEmpty(ext))
                return null;

            ext = ext.ToLowerInvariant();
            return ext switch
            {
                ".json" => "application/json",
                ".txt" => "text/plain",
                ".html" => "text/html",
                ".xml" => "application/xml",
                ".bin" => "application/octet-stream",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                _ => null
            };
        }

        private static bool IsBase64String(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;

            if (s!.Length % 4 != 0) return false;
            try
            {
                Convert.FromBase64String(s);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Result type for write operations.
        /// </summary>
        public class WriteOutcome
        {
            public bool Success { get; set; }
            public int? StatusCode { get; set; }
            public string? Error { get; set; }
            public string? Name { get; set; }
            public string? Path { get; set; }

            public static WriteOutcome CreateSuccess(string name, string? path = null) => new WriteOutcome { Success = true, Name = name, Path = path };
            public static WriteOutcome CreateFail(int statusCode, string error) => new WriteOutcome { Success = false, StatusCode = statusCode, Error = error };
            public static WriteOutcome CreateFail(string error) => new WriteOutcome { Success = false, Error = error };
        }
    }
}
