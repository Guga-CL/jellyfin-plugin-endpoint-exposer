// Services/FileOperationService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EndpointExposer.Services
{
    /// <summary>
    /// Utility service for common file operations and content type detection.
    /// Provides helpers for MIME type inference and base64 detection.
    /// </summary>
    public class FileOperationService
    {
        private readonly ILogger<FileOperationService> _logger;

        public FileOperationService(ILogger<FileOperationService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Get content type (MIME type) based on file extension.
        /// Returns null if extension is not recognized.
        /// </summary>
        public string? GetContentTypeByExtension(string? extension)
        {
            if (string.IsNullOrEmpty(extension))
                return null;

            return extension.ToLowerInvariant() switch
            {
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".txt" => "text/plain",
                ".html" or ".htm" => "text/html",
                ".css" => "text/css",
                ".js" or ".mjs" => "application/javascript",
                ".ts" => "application/typescript",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".pdf" => "application/pdf",
                ".csv" => "text/csv",
                ".yaml" or ".yml" => "application/x-yaml",
                ".zip" => "application/zip",
                ".tar" => "application/x-tar",
                ".gzip" or ".gz" => "application/gzip",
                ".md" or ".markdown" => "text/markdown",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                ".otf" => "font/otf",
                ".eot" => "application/vnd.ms-fontobject",
                _ => null
            };
        }

        /// <summary>
        /// Determine if a string appears to be base64 encoded.
        /// Uses heuristic: all characters in [A-Za-z0-9+/=] and length is multiple of 4.
        /// </summary>
        public bool IsBase64String(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            // Remove whitespace
            var cleaned = System.Text.RegularExpressions.Regex.Replace(input, @"\s", string.Empty);

            // Check length is multiple of 4
            if (cleaned.Length % 4 != 0)
                return false;

            // Check all characters are valid base64
            return System.Text.RegularExpressions.Regex.IsMatch(cleaned, @"^[A-Za-z0-9+/]*={0,2}$");
        }

        /// <summary>
        /// Sanitize a file name by removing/replacing problematic characters.
        /// Allows: alphanumeric, dash, underscore, dot.
        /// </summary>
        public string SanitizeFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            // Use Path.GetFileName to remove directory separators
            var baseName = Path.GetFileName(fileName);

            // Remove any remaining problematic characters
            var sanitized = System.Text.RegularExpressions.Regex.Replace(baseName, @"[^\w\-.]", "_");
            return sanitized;
        }

        /// <summary>
        /// Check if a path is safe (doesn't escape the target directory via .. or absolute paths).
        /// </summary>
        public bool IsPathSafe(string basePath, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(relativePath))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(basePath, relativePath));
                var baseFullPath = Path.GetFullPath(basePath);

                // Ensure the resolved path is within the base directory
                return fullPath.StartsWith(baseFullPath, StringComparison.OrdinalIgnoreCase) &&
                       (fullPath.Length == baseFullPath.Length || fullPath[baseFullPath.Length] == Path.DirectorySeparatorChar);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get the size of a directory recursively.
        /// </summary>
        public long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path))
                return 0;

            try
            {
                var directoryInfo = new DirectoryInfo(path);
                return directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate size of directory {Path}", path);
                return 0;
            }
        }

        /// <summary>
        /// Ensure a directory exists, creating it if necessary.
        /// Returns true if directory exists after call, false on failure.
        /// </summary>
        public bool EnsureDirectoryExists(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory {Path}", path);
                return false;
            }
        }

        /// <summary>
        /// List files in a directory matching a pattern.
        /// </summary>
        public List<string> ListFilesInDirectory(string path, string pattern = "*")
        {
            if (!Directory.Exists(path))
                return new List<string>();

            try
            {
                return Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Cast<string>()
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list files in directory {Path}", path);
                return new List<string>();
            }
        }

        /// <summary>
        /// Try to delete a file, returning success status without throwing.
        /// </summary>
        public bool TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete file {Path}", path);
                return false;
            }
        }

        /// <summary>
        /// Try to delete a directory recursively, returning success status without throwing.
        /// </summary>
        public bool TryDeleteDirectory(string path, bool recursive = true)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete directory {Path}", path);
                return false;
            }
        }
    }
}
// END - Services/FileOperationService.cs