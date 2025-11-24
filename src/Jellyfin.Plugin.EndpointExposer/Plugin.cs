using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EndpointExposer
{
    public class Plugin : BasePlugin, IServerEntryPoint
    {
        private readonly ILogger<Plugin> _logger;
        private readonly IApplicationPaths? _appPaths;
        private readonly IServerConfigurationManager? _configManager;

        public Plugin(IApplicationPaths? appPaths = null, ILoggerFactory? loggerFactory = null, IServerConfigurationManager? configManager = null)
            : base()
        {
            // Subscribe early to capture first-chance exceptions
            try
            {
                AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;
            }
            catch { /* swallow */ }

            try
            {
                _appPaths = appPaths;
                _configManager = configManager;
                _logger = loggerFactory?.CreateLogger<Plugin>() ?? new Microsoft.Extensions.Logging.Abstractions.NullLogger<Plugin>();

                _logger.LogInformation("[EndpointExposer] Plugin constructor running...");
                // Keep constructor minimal. Defer heavy work to OnApplicationStarted.
            }
            catch (Exception ex)
            {
                DumpExceptionToFile("plugin-constructor-exception", ex);
                try { Console.Error.WriteLine($"[EndpointExposer] Constructor exception dumped to plugin folder: {ex.Message}"); } catch { }
                try { _logger?.LogError(ex, "[EndpointExposer] Exception in plugin constructor (caught and dumped)."); } catch { }
                // Do not rethrow
            }
        }

        private void CurrentDomain_FirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            try
            {
                // Dump first-chance exceptions to a file for later inspection
                DumpExceptionToFile("firstchance", e.Exception);
            }
            catch { /* swallow */ }
        }

        private void DumpExceptionToFile(string prefix, Exception ex)
        {
            try
            {
                var pluginDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? ".",
                    "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");

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
                // must not throw from here
            }
        }

        public void OnApplicationStarted()
        {
            try
            {
                _logger.LogInformation("[EndpointExposer] OnApplicationStarted called. Initializing plugin...");
                // safe initialization here
            }
            catch (Exception ex)
            {
                DumpExceptionToFile("onstarted-exception", ex);
                _logger?.LogError(ex, "[EndpointExposer] Initialization failed in OnApplicationStarted.");
            }
        }

        public void OnApplicationStopping()
        {
            _logger?.LogInformation("[EndpointExposer] OnApplicationStopping called.");
        }
    }
}
