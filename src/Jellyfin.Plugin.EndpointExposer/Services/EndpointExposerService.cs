// Services/EndpointExposerService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.EndpointExposer.Services
{
    /// <summary>
    /// Core file I/O service for EndpointExposer.
    /// Handles writing JSON payloads to files with atomic operations and backups.
    /// Folder operations, auth, and config persistence are delegated to specialized services.
    /// </summary>
    public class EndpointExposerService
    {
        private readonly ILogger<EndpointExposerService> _logger;
        private readonly PluginConfiguration _config;
        private readonly FileWriteService _fileWriteService;
        private readonly string _outDir;

        public EndpointExposerService(ILogger<EndpointExposerService> logger, PluginConfiguration config, FileWriteService fileWriteService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? new PluginConfiguration();
            _fileWriteService = fileWriteService ?? throw new ArgumentNullException(nameof(fileWriteService));

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

        /// <summary>
        /// Get the current plugin configuration.
        /// </summary>
        public PluginConfiguration GetConfiguration() => _config;

        /// <summary>
        /// List all files in the default output directory with optional extension filter.
        /// </summary>
        public IEnumerable<string> ListFiles(string? ext = null)
        {
            if (!Directory.Exists(_outDir))
                return Array.Empty<string>();

            try
            {
                return Directory.EnumerateFiles(_outDir, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => string.IsNullOrEmpty(ext) || f.EndsWith(ext!, StringComparison.OrdinalIgnoreCase))
                    .Select(f => Path.GetFileName(f)!)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list files in output directory");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Read a file from the default output directory.
        /// Returns (Exists, Bytes, ContentType, FileName).
        /// </summary>
        public (bool Exists, byte[] Bytes, string? ContentType, string FileName) GetFile(string name)
        {
            try
            {
                var safeName = Path.GetFileName(name ?? string.Empty) ?? string.Empty;
                var path = Path.Combine(_outDir, safeName);

                if (!File.Exists(path))
                    return (false, Array.Empty<byte>(), null, safeName);

                var bytes = File.ReadAllBytes(path);
                var contentType = GetContentTypeByExtension(Path.GetExtension(path));
                return (true, bytes, contentType, safeName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read file {Name}", name);
                return (false, Array.Empty<byte>(), null, name ?? string.Empty);
            }
        }

        /// <summary>
        /// Handle write request: parse JSON from HTTP request and save to file.
        /// Returns WriteOutcome with result details.
        /// NOTE: Authorization must be checked by caller before calling this method.
        /// </summary>
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

                // Enforce MaxPayloadBytes
                var maxBytes = _config?.MaxPayloadBytes ?? 2 * 1024 * 1024;
                if (request.ContentLength.HasValue && request.ContentLength.Value > maxBytes)
                {
                    _logger.LogWarning("HandleWriteAsync: payload too large (ContentLength {Len} > {Max}) for {Name}",
                        request.ContentLength.Value, maxBytes, name);
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

                    return WriteOutcome.CreateSuccess(name, safePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "HandleWriteAsync: failed to write file for {Name}", name);
                    return WriteOutcome.CreateFail(500, "Failed to write file");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HandleWriteAsync: unexpected error for {Name}", name);
                return WriteOutcome.CreateFail(500, "Internal server error");
            }
        }

        /// <summary>
        /// Delete a file from the default output directory.
        /// </summary>
        public bool DeleteFile(string name)
        {
            try
            {
                var safeName = Path.GetFileName(name ?? string.Empty) ?? string.Empty;
                var path = Path.Combine(_outDir, safeName);

                if (!File.Exists(path))
                    return false;

                File.Delete(path);
                _logger.LogInformation("DeleteFile: deleted {Name}", safeName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file {Name}", name);
                return false;
            }
        }

        /// <summary>
        /// Get content type (MIME type) based on file extension.
        /// </summary>
        private string? GetContentTypeByExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return null;

            return extension.ToLowerInvariant() switch
            {
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".txt" => "text/plain",
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".pdf" => "application/pdf",
                ".csv" => "text/csv",
                ".yaml" or ".yml" => "application/x-yaml",
                _ => null
            };
        }
    }

    /// <summary>
    /// Outcome of a write operation.
    /// </summary>
    public class WriteOutcome
    {
        public bool Success { get; set; }
        public string? Name { get; set; }
        public string? Path { get; set; }
        public string? Error { get; set; }
        public int? StatusCode { get; set; }

        public static WriteOutcome CreateSuccess(string name, string path)
        {
            return new WriteOutcome
            {
                Success = true,
                Name = name,
                Path = path,
                Error = null,
                StatusCode = null
            };
        }

        public static WriteOutcome CreateFail(int statusCode, string error)
        {
            return new WriteOutcome
            {
                Success = false,
                Name = null,
                Path = null,
                Error = error,
                StatusCode = statusCode
            };
        }
    }
}
// END - Services/EndpointExposerService.cs