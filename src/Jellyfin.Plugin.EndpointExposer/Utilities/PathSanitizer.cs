// Utilities/PathSanitizer.cs
using System;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.EndpointExposer.Utilities
{
    /// <summary>
    /// Utility for path and name validation and sanitization.
    /// Provides regex patterns and validation methods for safe file/folder handling.
    /// </summary>
    public static class PathSanitizer
    {
        // Regex patterns for validation
        private static readonly Regex FolderTokenRegex = new Regex("^[a-z0-9_-]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FileNameRegex = new Regex(@"^[\w\-.]+$", RegexOptions.Compiled);

        /// <summary>
        /// Validate a folder token/name against safe naming rules.
        /// Allows: alphanumeric, underscore, dash (no spaces, no special chars).
        /// </summary>
        public static bool IsFolderTokenValid(string? folderName)
        {
            return !string.IsNullOrWhiteSpace(folderName) && FolderTokenRegex.IsMatch(folderName);
        }

        /// <summary>
        /// Validate a file name against safe naming rules.
        /// Allows: word characters, dash, dot (but not path separators).
        /// </summary>
        public static bool IsFileNameValid(string? fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName) && FileNameRegex.IsMatch(fileName);
        }

        /// <summary>
        /// Check if a path is safe and doesn't escape the target directory via .. or absolute paths.
        /// </summary>
        public static bool IsPathSafe(string basePath, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(relativePath))
                return false;

            try
            {
                var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(basePath, relativePath));
                var baseFullPath = System.IO.Path.GetFullPath(basePath);

                // Ensure the resolved path is within the base directory
                return fullPath.StartsWith(baseFullPath, StringComparison.OrdinalIgnoreCase) &&
                       (fullPath.Length == baseFullPath.Length || fullPath[baseFullPath.Length] == System.IO.Path.DirectorySeparatorChar);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sanitize a folder name by removing invalid characters and enforcing max length.
        /// </summary>
        public static string SanitizeFolderName(string? folderName, int maxLength = 50)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                return string.Empty;

            // Keep only valid characters
            var sanitized = Regex.Replace(folderName.Trim(), @"[^a-z0-9_-]", "_", RegexOptions.IgnoreCase);

            // Limit length
            if (sanitized.Length > maxLength)
                sanitized = sanitized.Substring(0, maxLength);

            return sanitized;
        }

        /// <summary>
        /// Sanitize a file name by removing invalid characters and enforcing max length.
        /// </summary>
        public static string SanitizeFileName(string? fileName, int maxLength = 255)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            // Use Path.GetFileName to remove directory separators
            var baseName = System.IO.Path.GetFileName(fileName ?? string.Empty) ?? string.Empty;

            // Keep only valid characters
            var sanitized = Regex.Replace(baseName, @"[^\w\-.]", "_");

            // Limit length (accounting for extension)
            if (sanitized.Length > maxLength)
            {
                var ext = System.IO.Path.GetExtension(sanitized);
                var name = System.IO.Path.GetFileNameWithoutExtension(sanitized);
                var maxNameLength = maxLength - ext.Length - 1;
                if (maxNameLength > 0)
                {
                    sanitized = name.Substring(0, Math.Min(name.Length, maxNameLength)) + ext;
                }
            }

            return sanitized;
        }
    }
}
// END - Utilities/PathSanitizer.cs