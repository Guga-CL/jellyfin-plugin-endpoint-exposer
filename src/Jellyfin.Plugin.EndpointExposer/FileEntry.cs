// src/Jellyfin.Plugin.EndpointExposer/FileEntry.cs
using System;

namespace Jellyfin.Plugin.EndpointExposer
{
    /// <summary>
    /// Simple DTO describing a registered logical file exposed by the plugin.
    /// Matches the JSON shape used by the configuration UI and controller.
    /// </summary>
    public class FileEntry
    {
        /// <summary>
        /// Machine name (lowercase, a-z0-9-_). Used as filename (name.json).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Human friendly label shown in UI.
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// Optional description for the UI.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// If true, non-admin users are allowed to write this file (subject to plugin-level AllowNonAdmin).
        /// </summary>
        public bool AllowNonAdmin { get; set; } = false;
    }
}
