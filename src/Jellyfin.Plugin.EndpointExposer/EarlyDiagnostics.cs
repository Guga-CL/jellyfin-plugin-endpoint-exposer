using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Jellyfin.Plugin.EndpointExposer
{
    internal static class EarlyDiagnostics
    {
        // ModuleInitializer runs as soon as the assembly is loaded (before most type initializers).
        [ModuleInitializer]
        internal static void Init()
        {
            try
            {
                AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
                {
                    try
                    {
                        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? ".";
                        var pluginDir = Path.Combine(local, "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");
                        Directory.CreateDirectory(pluginDir);
                        var path = Path.Combine(pluginDir, "early-exception.txt");
                        var line = $"{DateTime.UtcNow:O} | {e.Exception.GetType().FullName} | {Truncate(e.Exception.Message, 2000)}{Environment.NewLine}{e.Exception}{Environment.NewLine}---{Environment.NewLine}";
                        // Append so we keep multiple events
                        File.AppendAllText(path, line);
                    }
                    catch
                    {
                        // swallow - diagnostics must not throw
                    }
                };
            }
            catch
            {
                // swallow
            }
        }

        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
