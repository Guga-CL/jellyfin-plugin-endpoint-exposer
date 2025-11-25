using System;
using System.IO;
using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.EndpointExposer
{
    public class Plugin : BasePlugin
    {
        // Required: implement Name (abstract on BasePlugin)
        public override string Name => "Endpoint Exposer";

        // Optional: short description
        public override string Description => "Expose a secure endpoint to write watchplanner config";

        // Keep constructor trivial and non-throwing.
        public Plugin() : base()
        {
            // Intentionally empty. Do not perform IO or network here.
            // If you need to initialize diagnostics or other services,
            // call InitializeIfNeeded() from a controller or a safe startup task.
        }

        // Safe explicit initializer you can call after the host is ready.
        public void InitializeIfNeeded()
        {
            try
            {
                var pluginDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? ".",
                    "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");

                Directory.CreateDirectory(pluginDir);

                // Keep any further initialization minimal and guarded with try/catch.
                // Example: PluginDiagnostics.InitFromSafeContext(pluginDir);
            }
            catch
            {
                // swallow: plugin construction must not throw
            }
        }
    }
}
