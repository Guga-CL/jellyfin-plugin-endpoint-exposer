// src/Jellyfin.Plugin.EndpointExposer/PluginConfiguration.cs
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.EndpointExposer
{
    /// <summary>
    /// Plugin configuration persisted by Jellyfin.
    /// Inherits BasePluginConfiguration so BasePlugin<T> accepts it.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        // explicit server base URL for token validation (e.g. "http://yourhost:8096")
        public string ServerBaseUrl { get; set; }

        /// <summary>
        /// Optional explicit output directory. If empty, controller will use app data path.
        /// </summary>
        public string? OutputDirectory { get; set; }

        /// <summary>
        /// If true, non-admin users are allowed to write files (global fallback).
        /// </summary>
        public bool AllowNonAdmin { get; set; } = false;

        /// <summary>
        /// Maximum payload size in bytes for incoming writes. Default 2 MiB.
        /// </summary>
        public int MaxPayloadBytes { get; set; } = 2 * 1024 * 1024;

        /// <summary>
        /// Maximum Backups. Default 5.
        /// </summary>
        public int MaxBackups { get; set; } = 5;

        /// <summary>
        /// Optional list of registered logical files exposed by the plugin.
        /// Kept for backward compatibility and fine-grained control.
        /// </summary>
        public List<FileEntry>? RegisteredFiles { get; set; }

        /// <summary>
        /// Optional list of exposed folders. Each entry maps a logical name to a subfolder
        /// under the plugin data directory and controls folder-level permissions.
        /// </summary>
        public List<FolderEntry>? ExposedFolders { get; set; }

        /// <summary>
        /// Public reads (legacy/optional).
        /// </summary>
        public List<string> PublicReads { get; set; } = new List<string>();

        /// <summary>
        /// Listen prefix used by the optional HttpListener service (if you use it).
        /// Example: "http://localhost:5001/".
        /// </summary>
        public string ListenPrefix { get; set; } = "http://localhost:8096/";

        /// <summary>
        /// Optional API key that allows header-based writes when provided in X-EndpointExposer-Key.
        /// If null/empty, header-based writes are ignored and normal auth is required.
        /// </summary>
        public string? ApiKey { get; set; }
    }

    /// <summary>
    /// FolderEntry describes a logical folder exposed by the plugin.
    /// RelativePath must be a single folder token (no slashes, no ..).
    /// </summary>
    public class FolderEntry
    {
        /// <summary>
        /// Logical name used in API calls (e.g., "watchplanner").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Relative folder name under the plugin data directory (single token).
        /// Example: "watchplanner"
        /// </summary>
        public string RelativePath { get; set; } = string.Empty;

        /// <summary>
        /// If true, non-admin users are allowed to write files in this folder (subject to global AllowNonAdmin).
        /// </summary>
        public bool AllowNonAdmin { get; set; } = false;

        /// <summary>
        /// Optional human description shown in the UI.
        /// </summary>
        public string? Description { get; set; }
    }
}
