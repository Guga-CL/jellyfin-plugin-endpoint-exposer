// Utilities/ValidationHelper.cs
using System;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.EndpointExposer.Utilities
{
    /// <summary>
    /// Utility for common validation checks.
    /// Provides helpers for base64 detection, URL validation, and other checks.
    /// </summary>
    public static class ValidationHelper
    {
        /// <summary>
        /// Determine if a string appears to be base64 encoded.
        /// Uses heuristic: all characters in [A-Za-z0-9+/=] and length is multiple of 4.
        /// </summary>
        public static bool IsBase64String(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            try
            {
                // Remove whitespace
                var cleaned = Regex.Replace(input, @"\s", string.Empty);

                // Check length is multiple of 4
                if (cleaned.Length % 4 != 0)
                    return false;

                // Check all characters are valid base64
                if (!Regex.IsMatch(cleaned, @"^[A-Za-z0-9+/]*={0,2}$"))
                    return false;

                // Try to decode to verify
                try
                {
                    Convert.FromBase64String(cleaned);
                    return true;
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

        /// <summary>
        /// Validate a URL format using Uri parsing.
        /// </summary>
        public static bool IsValidUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            try
            {
                var uri = new Uri(url);
                return uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "ftp";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validate an email address format (basic check).
        /// </summary>
        public static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validate a GUID format.
        /// </summary>
        public static bool IsValidGuid(string? guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
                return false;

            return Guid.TryParse(guid, out _);
        }

        /// <summary>
        /// Validate that a value is within a specific range.
        /// </summary>
        public static bool IsInRange(int value, int min, int max)
        {
            return value >= min && value <= max;
        }

        /// <summary>
        /// Validate that a value is within a specific range (for long).
        /// </summary>
        public static bool IsInRange(long value, long min, long max)
        {
            return value >= min && value <= max;
        }

        /// <summary>
        /// Check if a string matches a specific pattern (regex).
        /// </summary>
        public static bool MatchesPattern(string? input, string pattern)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            try
            {
                return Regex.IsMatch(input, pattern);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validate payload size against max allowed bytes.
        /// </summary>
        public static bool IsPayloadSizeValid(long payloadBytes, long maxBytes)
        {
            return payloadBytes > 0 && payloadBytes <= maxBytes;
        }

        /// <summary>
        /// Validate configuration port number.
        /// </summary>
        public static bool IsValidPort(int port)
        {
            return port > 0 && port <= 65535;
        }

        /// <summary>
        /// Normalize a URL by trimming slashes and ensuring scheme.
        /// </summary>
        public static string? NormalizeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            url = url.Trim();

            // Add http:// if no scheme specified
            if (!url.Contains("://"))
            {
                url = "http://" + url;
            }

            // Remove trailing slashes
            return url.TrimEnd('/');
        }
    }
}
// END - Utilities/ValidationHelper.cs