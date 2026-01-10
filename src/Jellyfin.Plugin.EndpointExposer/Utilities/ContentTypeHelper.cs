// Utilities/ContentTypeHelper.cs
namespace Jellyfin.Plugin.EndpointExposer.Utilities
{
    /// <summary>
    /// Utility for MIME type detection based on file extensions.
    /// Provides a comprehensive mapping of common file extensions to content types.
    /// </summary>
    public static class ContentTypeHelper
    {
        /// <summary>
        /// Get content type (MIME type) based on file extension.
        /// Returns null if extension is not recognized.
        /// </summary>
        public static string? GetContentTypeByExtension(string? extension)
        {
            if (string.IsNullOrEmpty(extension))
                return null;

            return extension.ToLowerInvariant() switch
            {
                // Text
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".md" or ".markdown" => "text/markdown",

                // Web
                ".html" or ".htm" => "text/html",
                ".css" => "text/css",
                ".js" or ".mjs" => "application/javascript",
                ".ts" => "application/typescript",

                // Data
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".yaml" or ".yml" => "application/x-yaml",
                ".toml" => "application/toml",

                // Images
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".bmp" => "image/bmp",
                ".tiff" or ".tif" => "image/tiff",

                // Archives
                ".zip" => "application/zip",
                ".tar" => "application/x-tar",
                ".tar.gz" or ".tgz" => "application/gzip",
                ".gzip" or ".gz" => "application/gzip",
                ".rar" => "application/x-rar-compressed",
                ".7z" => "application/x-7z-compressed",

                // Documents
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",

                // Fonts
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                ".otf" => "font/otf",
                ".eot" => "application/vnd.ms-fontobject",

                // Audio
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" or ".oga" => "audio/ogg",
                ".m4a" => "audio/mp4",
                ".flac" => "audio/flac",
                ".aac" => "audio/aac",

                // Video
                ".mp4" => "video/mp4",
                ".mpeg" or ".mpg" => "video/mpeg",
                ".webm" => "video/webm",
                ".ogv" => "video/ogg",
                ".mkv" => "video/x-matroska",
                ".mov" => "video/quicktime",
                ".flv" => "video/x-flv",
                ".avi" => "video/x-msvideo",

                // Default
                _ => null
            };
        }

        /// <summary>
        /// Get content type with a fallback if primary extension is not recognized.
        /// </summary>
        public static string GetContentTypeByExtensionOrDefault(string? extension, string defaultContentType = "application/octet-stream")
        {
            return GetContentTypeByExtension(extension) ?? defaultContentType;
        }

        /// <summary>
        /// Determine if the given extension represents a text-based file type.
        /// </summary>
        public static bool IsTextContentType(string? extension)
        {
            var contentType = GetContentTypeByExtension(extension);
            if (string.IsNullOrEmpty(contentType))
                return false;

            // Check if it's a text type
            return contentType.StartsWith("text/", System.StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("json", System.StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("xml", System.StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("javascript", System.StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("yaml", System.StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("markdown", System.StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains("toml", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determine if the given extension represents a binary file type.
        /// </summary>
        public static bool IsBinaryContentType(string? extension)
        {
            var contentType = GetContentTypeByExtension(extension);
            if (string.IsNullOrEmpty(contentType))
                return true; // Unknown types treated as binary

            // Anything not text is binary
            return !IsTextContentType(extension);
        }
    }
}
// END - Utilities/ContentTypeHelper.cs