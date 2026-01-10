// Services/FolderOperationService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EndpointExposer.Services
{
    /// <summary>
    /// Service for folder-based file operations.
    /// Handles folder resolution, listing, reading, and writing files within configured folders.
    /// </summary>
    public class FolderOperationService
    {
        private readonly ILogger<FolderOperationService> _logger;
        private readonly PluginConfiguration _config;
        private readonly FileWriteService _fileWriteService;

        private static readonly Regex FolderTokenRegex = new Regex("^[a-z0-9_-]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FileNameRegex = new Regex(@"^[\w\-.]+$", RegexOptions.Compiled);

        public FolderOperationService(ILogger<FolderOperationService> logger, PluginConfiguration config, FileWriteService fileWriteService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? new PluginConfiguration();
            _fileWriteService = fileWriteService ?? throw new ArgumentNullException(nameof(fileWriteService));
        }

        /// <summary>
        /// Validate a folder token/name against safe naming rules.
        /// </summary>
        public bool IsFolderTokenValid(string? folderName)
        {
            return !string.IsNullOrWhiteSpace(folderName) && FolderTokenRegex.IsMatch(folderName);
        }

        /// <summary>
        /// Validate a file name against safe naming rules.
        /// </summary>
        public bool IsFileNameValid(string? fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName) && FileNameRegex.IsMatch(fileName);
        }

        /// <summary>
        /// Resolve a configured folder entry by logical name and return the absolute folder path.
        /// Throws ArgumentException if folder is invalid or not configured.
        /// Creates the folder if it doesn't exist.
        /// </summary>
        public string ResolveFolderPath(string folderName)
        {
            if (!IsFolderTokenValid(folderName))
                throw new ArgumentException("Invalid folder name format", nameof(folderName));

            // Find matching FolderEntry in configuration (case-insensitive match on Name or RelativePath)
            var entry = _config?.ExposedFolders?.FirstOrDefault(f =>
                string.Equals(f.Name, folderName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.RelativePath, folderName, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
                throw new ArgumentException($"Folder '{folderName}' not configured", nameof(folderName));

            // Ensure RelativePath is a single token and safe
            var rel = entry.RelativePath;
            if (!IsFolderTokenValid(rel))
                throw new ArgumentException($"Configured folder has invalid RelativePath: {rel}", nameof(folderName));

            // Get base plugin data dir
            string baseDir = GetPluginDataDir();

            var folderDir = Path.Combine(baseDir, rel);
            try
            {
                Directory.CreateDirectory(folderDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create folder directory {FolderDir}", folderDir);
                throw new InvalidOperationException($"Failed to create folder directory: {folderDir}", ex);
            }

            return folderDir;
        }

        /// <summary>
        /// List all files in a configured folder (top-level only, no subdirectories).
        /// </summary>
        public List<string> ListFolderFiles(string folderName)
        {
            try
            {
                var folderDir = ResolveFolderPath(folderName);

                if (!Directory.Exists(folderDir))
                    return new List<string>();

                return Directory.EnumerateFiles(folderDir, "*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Cast<string>()
                    .ToList();
            }
            catch (ArgumentException)
            {
                // Folder is not configured or invalid — treat as empty result rather than an error.
                _logger.LogInformation("ListFolderFiles: folder '{FolderName}' not configured", folderName);
                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list files in folder {FolderName}", folderName);
                return new List<string>();
            }
        }

        /// <summary>
        /// Read a file from a configured folder.
        /// Returns (Exists, Bytes, ContentType, FileName).
        /// </summary>
        public (bool Exists, byte[] Bytes, string? ContentType, string FileName) ReadFolderFile(string folderName, string fileName)
        {
            if (!IsFileNameValid(fileName))
                return (false, Array.Empty<byte>(), null, fileName ?? string.Empty);

            try
            {
                var folderDir = ResolveFolderPath(folderName);
                var safeName = Path.GetFileName(fileName);
                var path = Path.Combine(folderDir, safeName);

                if (!File.Exists(path))
                    return (false, Array.Empty<byte>(), null, safeName);

                var bytes = File.ReadAllBytes(path);
                var contentType = GetContentTypeByExtension(Path.GetExtension(path));
                return (true, bytes, contentType, safeName);
            }
            catch (ArgumentException)
            {
                // Folder not configured — not an error for callers; report as not found.
                _logger.LogInformation("ReadFolderFile: folder '{FolderName}' not configured", folderName);
                return (false, Array.Empty<byte>(), null, fileName ?? string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read file {FileName} from folder {FolderName}", fileName, folderName);
                return (false, Array.Empty<byte>(), null, fileName ?? string.Empty);
            }
        }

        /// <summary>
        /// Write bytes to a file in a configured folder using atomic writes and backups.
        /// Returns (Success, Path, Error).
        /// </summary>
        public async Task<(bool Success, string? Path, string? Error)> WriteFolderFileAsync(string folderName, string fileName, byte[] bytes, CancellationToken ct = default)
        {
            if (bytes == null)
                bytes = Array.Empty<byte>();

            if (!IsFileNameValid(fileName))
                return (false, null, "Invalid file name");

            try
            {
                var folderDir = ResolveFolderPath(folderName);
                var safeName = Path.GetFileName(fileName);
                var path = Path.Combine(folderDir, safeName);

                // Enforce MaxPayloadBytes
                var maxBytes = _config?.MaxPayloadBytes ?? 2 * 1024 * 1024;
                if (bytes.Length > maxBytes)
                {
                    _logger.LogWarning("WriteFolderFileAsync: payload too large ({Len} > {Max}) for {File} in {Folder}",
                        bytes.Length, maxBytes, safeName, folderName);
                    return (false, null, "Payload too large");
                }

                await _fileWriteService.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                _logger.LogInformation("WriteFolderFileAsync: saved {File} ({Bytes} bytes) in folder {Folder}",
                    safeName, bytes.Length, folderName);
                return (true, path, null);
            }
            catch (ArgumentException)
            {
                _logger.LogInformation("WriteFolderFileAsync: folder '{FolderName}' not configured", folderName);
                return (false, null, "Folder not configured");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WriteFolderFileAsync: failed to write {File} in folder {FolderName}", fileName, folderName);
                return (false, null, "Failed to write file");
            }
        }

        /// <summary>
        /// Delete a file from a configured folder.
        /// Returns true if successful, false if file doesn't exist or error occurs.
        /// </summary>
        public bool DeleteFolderFile(string folderName, string fileName)
        {
            if (!IsFileNameValid(fileName))
                return false;

            try
            {
                var folderDir = ResolveFolderPath(folderName);
                var safeName = Path.GetFileName(fileName);
                var path = Path.Combine(folderDir, safeName);

                if (!File.Exists(path))
                    return false;

                File.Delete(path);
                _logger.LogInformation("DeleteFolderFile: deleted {File} from folder {Folder}", safeName, folderName);
                return true;
            }
            catch (ArgumentException)
            {
                _logger.LogInformation("DeleteFolderFile: folder '{FolderName}' not configured", folderName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteFolderFile: failed to delete {File} from folder {FolderName}", fileName, folderName);
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

        /// <summary>
        /// Get the plugin data directory.
        /// </summary>
        private string GetPluginDataDir()
        {
            try
            {
                return Plugin.Instance?.GetPluginDataDir() ?? GetDefaultDataDir();
            }
            catch
            {
                return GetDefaultDataDir();
            }
        }

        /// <summary>
        /// Get default plugin data directory path.
        /// </summary>
        private string GetDefaultDataDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "jellyfin", "plugins", "configurations",
                typeof(Plugin).Namespace ?? "Jellyfin.Plugin.EndpointExposer",
                "data");
        }
    }
}
// END - Services/FolderOperationService.cs