using System;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.EndpointExposer
{
    internal static class PluginDiagnostics
    {
        public static void InitFromPluginConstructor()
        {
            try
            {
                var pluginDir = GetPluginDir();
                Directory.CreateDirectory(pluginDir);
                AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
                {
                    try
                    {
                        // Keep this extremely small and robust
                        var file = Path.Combine(pluginDir, "firstchance.txt");
                        var content = $"{DateTime.UtcNow:O} - {e.Exception.GetType().FullName}: {e.Exception.Message}";
                        File.AppendAllText(file, content + Environment.NewLine);
                    }
                    catch
                    {
                        // swallow: diagnostics must never throw
                    }
                };
            }
            catch
            {
                // swallow
            }
        }

        private static string GetPluginDir()
        {
            // Return the plugin folder under LOCALAPPDATA/jellyfin/plugins/YourPlugin
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");
        }
    }

}
