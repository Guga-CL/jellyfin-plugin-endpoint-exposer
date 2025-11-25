using System;
using System.IO;
using System.Threading;

namespace Jellyfin.Plugin.EndpointExposer
{
    internal static class PluginDiagnostics
    {
        private const string FirstChanceFile = "firstchance.txt";
        private const string InitFailedFile = "diagnostics-init-failed.txt";

        private static int _initialized;
        private static string? _pluginDir;

        public static void Initialize(string pluginDir)
        {
            if (Interlocked.Exchange(ref _initialized, 1) == 1) return;

            try
            {
                _pluginDir = pluginDir ?? GetDefaultPluginDir();
                Directory.CreateDirectory(_pluginDir);
                AppDomain.CurrentDomain.FirstChanceException += FirstChanceHandler;
            }
            catch (Exception ex)
            {
                try
                {
                    var path = Path.Combine(GetDefaultPluginDir(), InitFailedFile);
                    File.WriteAllText(path, $"{DateTime.UtcNow:O} - Init failed: {ex.GetType().FullName}: {ex.Message}");
                }
                catch { }
            }
        }

        private static void FirstChanceHandler(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_pluginDir))
                {
                    _pluginDir = GetDefaultPluginDir();
                    try { Directory.CreateDirectory(_pluginDir); } catch { }
                }

                var target = Path.Combine(_pluginDir, FirstChanceFile);
                var temp = Path.Combine(_pluginDir, $"{FirstChanceFile}.{Guid.NewGuid():N}.tmp");
                var content = $"{DateTime.UtcNow:O} | {e.Exception.GetType().FullName} | {Truncate(e.Exception.Message, 1000)}{Environment.NewLine}";
                File.WriteAllText(temp, content);

                try
                {
                    if (!File.Exists(target)) File.Move(temp, target);
                    else
                    {
                        var toAppend = File.ReadAllText(temp);
                        File.AppendAllText(target, toAppend);
                        try { File.Delete(temp); } catch { }
                    }
                }
                catch
                {
                    try
                    {
                        var toAppend = File.ReadAllText(temp);
                        File.AppendAllText(target, toAppend);
                        try { File.Delete(temp); } catch { }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string GetDefaultPluginDir()
        {
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? ".";
                return Path.Combine(local, "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");
            }
            catch { return Path.Combine(".", "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer"); }
        }

        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
