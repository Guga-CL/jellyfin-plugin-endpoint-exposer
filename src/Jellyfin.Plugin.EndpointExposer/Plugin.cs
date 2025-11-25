using System;
using System.IO;
using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.EndpointExposer
{
    public class Plugin : BasePlugin
    {
        public override string Name => "Endpoint Exposer";

        public Plugin()
            : base()
        {
            // Keep constructor trivial and non-throwing.
            // Defer any network or heavy initialization to OnApplicationStarted or to controllers/services.
            try
            {
                var pluginDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? ".",
                    "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");

                Directory.CreateDirectory(pluginDir);
            }
            catch
            {
                // swallow: plugin construction must not throw
            }
        }
    }
}
