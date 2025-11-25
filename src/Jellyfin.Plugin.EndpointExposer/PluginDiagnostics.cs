using System;
using System.IO;
using System.Threading;

namespace Jellyfin.Plugin.EndpointExposer
{
    internal static class PluginDiagnostics
    {
        private const string FirstChanceFile = "firstchance.txt";
        private const string InitFailedFile = "diagnostics-init-failed.txt";

        // Thread-safe lazy init
        private static int _initialized;
        private static string? _pluginDir;

        /// <summary>
        /// Call this from a safe context after the host is ready.
        /// Example: call from Plugin.InitializeIfNeeded() or from a controller action.
        /// </summary>
        public static void Initialize(string pluginDir)
        {
            if (Interlocked.Exchange(ref _initialized, 1) == 1)
            {
                // already initialized
                return;
            }

            try
            {
                _pluginDir = pluginDir ?? GetDefaultPluginDir();
                Directory.CreateDirectory(_pluginDir);

                // Register a minimal first-chance handler that is extremely defensive.
                AppDomain.CurrentDomain.FirstChanceException += FirstChanceHandler;
            }
            catch (Exception ex)
            {
                // Best-effort: write a tiny init-failed file so you can inspect it later.
                try
                {
                    var path = Path.Combine(GetDefaultPluginDir(), InitFailedFile);
                    File.WriteAllText(path, $"{DateTime.UtcNow:O} - Init failed: {ex.GetType().FullName}: {ex.Message}");
                }
                catch
                {
                    // swallow
                }
            }
        }

        private static void FirstChanceHandler(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_pluginDir))
                {
                    _pluginDir = GetDefaultPluginDir();
                    try { Directory.CreateDirectory(_pluginDir); } catch { /* ignore */ }
                }

                var target = Path.Combine(_pluginDir, FirstChanceFile);
                var temp = Path.Combine(_pluginDir, $"{FirstChanceFile}.{Guid.NewGuid():N}.tmp");

                // Keep content extremely small to avoid heavy operations
                var content = $"{DateTime.UtcNow:O} | {e.Exception.GetType().FullName} | {Truncate(e.Exception.Message, 1000)}{Environment.NewLine}";

                // Write temp then append/replace in a best-effort, non-throwing way
                File.WriteAllText(temp, content);

                try
                {
                    // If target exists, append atomically by moving temp to a unique file and then appending
                    if (!File.Exists(target))
                    {
                        File.Move(temp, target);
                    }
                    else
                    {
                        // Append: open target for append and write the temp content, then delete temp
                        var toAppend = File.ReadAllText(temp);
                        File.AppendAllText(target, toAppend);
                        try { File.Delete(temp); } catch { /* ignore */ }
                    }
                }
                catch
                {
                    // Fallback: try to append directly and ignore any errors
                    try
                    {
                        var toAppend = File.ReadAllText(temp);
                        File.AppendAllText(target, toAppend);
                        try { File.Delete(temp); } catch { /* ignore */ }
                    }
                    catch
                    {
                        // swallow everything
                    }
                }
            }
            catch
            {
                // absolutely do not throw from the handler
            }
        }

        private static string GetDefaultPluginDir()
        {
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? ".";
                return Path.Combine(local, "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");
            }
            catch
            {
                return Path.Combine(".", "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");
            }
        }

        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
