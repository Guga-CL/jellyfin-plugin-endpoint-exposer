using System;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.EndpointExposer
{
    internal static class PluginDiagnostics
    {
        private const string AssemblyMarker = "assembly-loaded.txt";
        private const string FirstChanceFile = "firstchance.txt";
        private const string InitFailedFile = "diagnostics-init-failed.txt";

        // Called explicitly from Plugin constructor (safe place).
        public static void InitFromPluginConstructor()
        {
            try
            {
                var pluginDir = GetPluginDir();
                Directory.CreateDirectory(pluginDir);

                // Remove old diagnostics files (best-effort)
                TryDeleteFiles(pluginDir, AssemblyMarker);
                TryDeleteFiles(pluginDir, FirstChanceFile);
                TryDeleteFiles(pluginDir, "*-exception-*.txt");
                TryDeleteFiles(pluginDir, InitFailedFile);

                // Register a lightweight FirstChance handler that only writes a small text file.
                AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
                {
                    try
                    {
                        DumpFirstChance(pluginDir, e.Exception);
                    }
                    catch
                    {
                        // swallow everything; diagnostics must not throw
                    }
                };

                TryWriteText(pluginDir, AssemblyMarker, $"Assembly loaded at {DateTime.UtcNow:O}");
            }
            catch (Exception ex)
            {
                // If diagnostics initialization fails, write a tiny file so we can see it.
                try
                {
                    var pluginDir = GetPluginDir();
                    Directory.CreateDirectory(pluginDir);
                    TryWriteText(pluginDir, InitFailedFile, ex.ToString());
                }
                catch
                {
                    // swallow
                }
            }
        }

        private static void DumpFirstChance(string pluginDir, Exception ex)
        {
            try
            {
                var content = BuildDumpContent(ex);
                var target = Path.Combine(pluginDir, FirstChanceFile);

                // Write atomically: write to temp then replace/move
                var temp = Path.Combine(pluginDir, $"{FirstChanceFile}.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(temp, content);

                try
                {
                    if (File.Exists(target))
                    {
                        var backup = Path.Combine(pluginDir, $"{FirstChanceFile}.bak");
                        try
                        {
                            File.Replace(temp, target, backup, ignoreMetadataErrors: true);
                            if (File.Exists(backup))
                            {
                                try { File.Delete(backup); } catch { }
                            }
                        }
                        catch
                        {
                            try { File.Delete(target); } catch { }
                            File.Move(temp, target);
                        }
                    }
                    else
                    {
                        File.Move(temp, target);
                    }
                }
                catch
                {
                    try
                    {
                        File.WriteAllText(target, content);
                        if (File.Exists(temp)) try { File.Delete(temp); } catch { }
                    }
                    catch
                    {
                        // swallow
                    }
                }
            }
            catch
            {
                // swallow
            }
        }

        private static string BuildDumpContent(Exception ex)
        {
            try
            {
                var sw = new System.Text.StringBuilder();
                sw.AppendLine($"Timestamp: {DateTime.UtcNow:O}");
                sw.AppendLine("Exception:");
                sw.AppendLine(ex?.ToString() ?? "<null>");
                sw.AppendLine();
                sw.AppendLine("Environment.StackTrace:");
                sw.AppendLine(Environment.StackTrace ?? "<no stack>");
                sw.AppendLine();
                sw.AppendLine("Loaded assemblies:");
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name))
                {
                    try
                    {
                        var name = a.GetName();
                        sw.AppendLine($"{name.Name}, Version={name.Version}, Location={a.Location}");
                    }
                    catch
                    {
                        sw.AppendLine($"<assembly info unavailable> {a.FullName}");
                    }
                }
                return sw.ToString();
            }
            catch
            {
                return $"Timestamp: {DateTime.UtcNow:O}\nException: <failed to build dump>\n";
            }
        }

        private static void TryWriteText(string pluginDir, string fileName, string text)
        {
            try
            {
                var path = Path.Combine(pluginDir, fileName);
                File.WriteAllText(path, text);
            }
            catch
            {
                // swallow
            }
        }

        private static void TryDeleteFiles(string pluginDir, string searchPattern)
        {
            try
            {
                var files = Directory.GetFiles(pluginDir, searchPattern);
                foreach (var f in files)
                {
                    try { File.Delete(f); } catch { /* ignore */ }
                }
            }
            catch
            {
                // swallow
            }
        }

        private static string GetPluginDir()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? ".";
            return Path.Combine(local, "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");
        }
    }
}
