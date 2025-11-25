using System;
using System.IO;
using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.EndpointExposer
{
    public class Plugin : BasePlugin
    {
        public override string Name => "Endpoint Exposer";
        public override string Description => "Expose a secure endpoint to write watchplanner config";

        public Plugin() : base()
        {
            try
            {
                // Intentionally minimal. Do not perform IO or network here.
                // This constructor is kept trivial to avoid throwing during plugin creation.
            }
            catch (Exception ex)
            {
                try
                {
                    var pluginDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? ".",
                        "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");
                    Directory.CreateDirectory(pluginDir);
                    var path = Path.Combine(pluginDir, "ctor-exception.txt");
                    File.WriteAllText(path, DateTime.UtcNow.ToString("o") + " - " + ex.ToString());
                }
                catch
                {
                    // swallow - must not throw from constructor
                }
            }
        }

        // Safe explicit initializer to be called later from a controller or admin action
        public void InitializeIfNeeded()
        {
            try
            {
                var pluginDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? ".",
                    "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");
                Directory.CreateDirectory(pluginDir);
                PluginDiagnostics.Initialize(pluginDir);
            }
            catch
            {
                // swallow
            }
        }
    }
}