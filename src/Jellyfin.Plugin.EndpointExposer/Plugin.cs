using System;
using System.IO;
using Microsoft.Extensions.Logging;
using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.EndpointExposer
{
    public class Plugin : BasePlugin
    {
        // Required override from BasePlugin
        public override string Name => "Endpoint Exposer";

        // Keep constructor trivial and non-throwing
        public Plugin()
            : base()
        {
            // Minimal logging via NullLogger to avoid depending on ILoggerFactory at compile time
            var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<Plugin>();
            try
            {
                logger.LogInformation("[EndpointExposer] Plugin constructed (minimal).");
            }
            catch
            {
                // Swallow any logging errors; constructor must not throw
            }
        }

        // Optional helper you can call from a controller or later when server services are available.
        // This method intentionally avoids compile-time references to server interfaces.
        public void InitializeIfNeeded()
        {
            try
            {
                // Example: write a marker file in plugin folder to indicate initialization ran.
                var pluginDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? ".",
                    "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");

                Directory.CreateDirectory(pluginDir);
                var marker = Path.Combine(pluginDir, "initialized.txt");
                File.WriteAllText(marker, $"Initialized at {DateTime.UtcNow:O}");
            }
            catch
            {
                // Do not throw; keep initialization best-effort
            }
        }
    }
}
