using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Jellyfin.Plugin.EndpointExposer
{
    internal static class PluginDiagnostics
    {
        [ModuleInitializer]
        public static void InitModule()
        {
            try
            {
                AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
                {
                    try
                    {
                        DumpException("firstchance", e.Exception);
                    }
                    catch { /* swallow */ }
                };

                // Write a quick "assembly loaded" marker so we know the initializer ran
                TryWriteText("assembly-loaded.txt", $"Assembly loaded at {DateTime.UtcNow:O}");
            }
            catch (Exception ex)
            {
                TryWriteText("diagnostics-init-failed.txt", ex.ToString());
            }
        }

        private static void DumpException(string prefix, Exception ex)
        {
            try
            {
                var pluginDir = GetPluginDir();
                Directory.CreateDirectory(pluginDir);

                var file = Path.Combine(pluginDir, $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.log");
                using var sw = new StreamWriter(file, append: false);
                sw.WriteLine($"Timestamp: {DateTime.UtcNow:O}");
                sw.WriteLine("Exception:");
                sw.WriteLine(ex.ToString());
                sw.WriteLine();
                sw.WriteLine("Environment.StackTrace:");
                sw.WriteLine(Environment.StackTrace);
                sw.WriteLine();
                sw.WriteLine("Loaded assemblies:");
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name))
                {
                    try
                    {
                        var name = a.GetName();
                        sw.WriteLine($"{name.Name}, Version={name.Version}, Location={a.Location}");
                    }
                    catch
                    {
                        sw.WriteLine($"<assembly info unavailable> {a.FullName}");
                    }
                }
                sw.Flush();
            }
            catch
            {
                // must not throw
            }
        }

        private static void TryWriteText(string fileName, string text)
        {
            try
            {
                var pluginDir = GetPluginDir();
                Directory.CreateDirectory(pluginDir);
                File.WriteAllText(Path.Combine(pluginDir, fileName), text);
            }
            catch { }
        }

        private static string GetPluginDir()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? ".";
            return Path.Combine(local, "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");
        }
    }
}
