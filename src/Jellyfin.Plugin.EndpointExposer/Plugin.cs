// Plugin.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.EndpointExposer.Services;


namespace Jellyfin.Plugin.EndpointExposer
{
    public partial class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages

    {
        private readonly IApplicationPaths _appPaths;
        private readonly ILogger<Plugin> _logger;

        private void EnsureServicesRegisteredAtRuntime()
        {
            try
            {
                // Some hosts expose IServiceCollection via ServiceProvider (rare).
                // If available, register plugin services into host DI. Otherwise, continue — controllers
                // and fallback service construction handle runtime behavior.
                var services = ServiceProvider?.GetService(typeof(IServiceCollection)) as IServiceCollection;
                if (services != null)
                {
                    // Prefer persisted ServerBaseUrl if present, otherwise use a sensible default.
                    var serverBaseUrl = !string.IsNullOrWhiteSpace(this.Configuration?.ServerBaseUrl)
                        ? this.Configuration.ServerBaseUrl
                        : null;

                    if (!string.IsNullOrWhiteSpace(serverBaseUrl))
                    {
                        RegisterServices(services, serverBaseUrl);
                        _logger?.LogInformation("EndpointExposer: registered services into host IServiceCollection at runtime.");
                        return;
                    }

                    _logger?.LogInformation("EndpointExposer: IServiceCollection available but no ServerBaseUrl configured; skipping runtime service registration.");
                }

                // If IServiceCollection is not available, do not attempt fragile reflection hacks.
                _logger?.LogWarning("EndpointExposer: host IServiceCollection not available at runtime; services were not registered. " +
                                    "This is expected on many hosts. Controllers will construct fallback services as needed.");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "EndpointExposer: EnsureServicesRegisteredAtRuntime encountered an error while attempting to register services.");
            }
        }





        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger, IServiceProvider serviceProvider)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _appPaths = applicationPaths;
            _logger = logger;
            ServiceProvider = serviceProvider;

            EnsureServicesRegisteredAtRuntime();
        }

        public static Plugin? Instance { get; private set; }

        public IServiceProvider ServiceProvider { get; }

        public override string Name => "EndpointExposer";

        public override string Description => "Expose a secure endpoint to write to disk";

        public override Guid Id => Guid.Parse("f1530767-390f-475e-afa2-6610c933c29e");

        public string SomePath => Path.Combine(_appPaths.WebPath, "somefile.json");

        public string GetPluginDataDir()
        {
            // Try to discover a configuration/plugin-configs path from IApplicationPaths via reflection.
            string? cfgDir = null;

            try
            {
                if (_appPaths != null)
                {
                    var t = _appPaths.GetType();
                    // Common candidate property names observed across hosts/versions
                    var candidates = new[] {
                "ConfigurationPath",
                "PluginConfigurationsPath",
                "PluginConfigurationPath",
                "ConfigurationDirectory",
                "PluginConfigurationDirectory",
                "ConfigurationFolder"
            };

                    foreach (var name in candidates)
                    {
                        var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        if (prop != null)
                        {
                            var val = prop.GetValue(_appPaths) as string;
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                cfgDir = val;
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore reflection errors and fall back below
            }

            // If we couldn't find a host-provided configuration path, fall back to LocalApplicationData
            if (string.IsNullOrWhiteSpace(cfgDir))
            {
                cfgDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "jellyfin", "plugins", "configurations");
            }

            // Build plugin-specific data directory under the configurations folder
            var pluginFolder = this.GetType().Namespace ?? "Jellyfin.Plugin.EndpointExposer";
            var dataDir = Path.Combine(cfgDir, pluginFolder, "data");

            // Ensure directory exists
            try
            {
                Directory.CreateDirectory(dataDir);
            }
            catch
            {
                // If creation fails, try a safer fallback under cfgDir\pluginFolder
                try
                {
                    var safe = Path.Combine(cfgDir, pluginFolder);
                    Directory.CreateDirectory(safe);
                    dataDir = Path.Combine(safe, "data");
                    Directory.CreateDirectory(dataDir);
                }
                catch
                {
                    // Last resort: use a temp folder
                    dataDir = Path.Combine(Path.GetTempPath(), pluginFolder, "data");
                    try { Directory.CreateDirectory(dataDir); } catch { /* swallow */ }
                }
            }

            return dataDir;
        }


        public override void OnUninstalling()
        {
            try
            {
                // cleanup logic if needed
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during uninstall cleanup.");
            }

            base.OnUninstalling();
        }
        // ---------------------------------------------------------------------
        // Service registration helper
        // ---------------------------------------------------------------------
        //
        // Call this method from your host startup / DI registration code so the
        // plugin's services are available via the application's IServiceCollection.
        //
        // Example (in Startup or wherever you configure services):
        //
        //   serverBaseUrl should be the base URL of the Jellyfin/Emby server,
        //   e.g. "http://localhost:8096"
        //   Plugin.Instance?.RegisterServices(services, serverBaseUrl);
        //
        // Notes:
        // - This method registers PluginConfiguration (the plugin's persisted config),
        //   JellyfinAuth, FileWriteService (as a hosted service), and EndpointExposerService.
        // - If the environment already registers equivalents, we can skip or adapt this.
        // ---------------------------------------------------------------------
        public void RegisterServices(IServiceCollection services, string serverBaseUrl)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (string.IsNullOrWhiteSpace(serverBaseUrl)) throw new ArgumentNullException(nameof(serverBaseUrl));

            // Register the plugin configuration instance so services can consume it.
            // BasePlugin<T> persists configuration; use the current Configuration instance.
            services.AddSingleton<PluginConfiguration>(sp => this.Configuration ?? new PluginConfiguration());

            // Register HttpClient factory for robust HTTP usage
            services.AddHttpClient("EndpointExposer")
                .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));

            // Determine effective base URL for logging and registration
            var cfgForLog = this.Configuration ?? new PluginConfiguration();
            var effectiveBase = !string.IsNullOrWhiteSpace(cfgForLog.ServerBaseUrl) ? cfgForLog.ServerBaseUrl : serverBaseUrl;
            _logger?.LogInformation("EndpointExposer: RegisterServices using ServerBaseUrl={Base}", effectiveBase);

            // Register JellyfinAuth using IHttpClientFactory and prefer explicit ServerBaseUrl from config
            services.AddSingleton<JellyfinAuth>(sp =>
            {
                var cfg = sp.GetRequiredService<PluginConfiguration>();
                var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                var http = httpFactory.CreateClient("EndpointExposer");

                var baseUrl = !string.IsNullOrWhiteSpace(cfg?.ServerBaseUrl) ? cfg.ServerBaseUrl : serverBaseUrl;
                var logger = sp.GetService<ILogger<JellyfinAuth>>();
                return new JellyfinAuth(baseUrl, http, logger);
            });

            // Register FileWriteService as a singleton and also as a hosted service so its background listener (if any)
            // will be started by the host. The FileWriteService in the project is a BackgroundService.
            services.AddSingleton<FileWriteService>(sp =>
            {
                var cfg = sp.GetRequiredService<PluginConfiguration>();
                var auth = sp.GetRequiredService<JellyfinAuth>();
                var logger = sp.GetRequiredService<ILogger<FileWriteService>>();
                return new FileWriteService(cfg, auth, logger);
            });

            // Add the background service wrapper so the host starts/stops it.
            services.AddHostedService(sp => sp.GetRequiredService<FileWriteService>());

            // Register AuthService for centralized token/auth logic
            services.AddSingleton<AuthService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<AuthService>>();
                var jellyfinAuth = sp.GetRequiredService<JellyfinAuth>();
                var cfg = sp.GetRequiredService<PluginConfiguration>();
                return new AuthService(logger, jellyfinAuth, cfg);
            });

            // Register FolderOperationService for folder-based file operations
            services.AddSingleton<FolderOperationService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<FolderOperationService>>();
                var cfg = sp.GetRequiredService<PluginConfiguration>();
                var fileWriter = sp.GetRequiredService<FileWriteService>();
                return new FolderOperationService(logger, cfg, fileWriter);
            });

            // Register ConfigurationHandler for configuration persistence
            services.AddSingleton<ConfigurationHandler>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ConfigurationHandler>>();
                var folderOps = sp.GetRequiredService<FolderOperationService>();
                return new ConfigurationHandler(logger, folderOps);
            });

            // Register FileOperationService for common file I/O utilities
            services.AddSingleton<FileOperationService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<FileOperationService>>();
                return new FileOperationService(logger);
            });

            // Register EndpointExposerService which contains the core file I/O logic used by the controller.
            services.AddSingleton<EndpointExposerService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<EndpointExposerService>>();
                var cfg = sp.GetRequiredService<PluginConfiguration>();
                var fileWriter = sp.GetRequiredService<FileWriteService>();
                return new EndpointExposerService(logger, cfg, fileWriter);
            });

            // Register controller dependencies if your host requires explicit registration.
            // Controllers are usually discovered automatically, but ensure logging is available.
            services.AddLogging();
            services.AddControllers().AddApplicationPart(typeof(Controller.EndpointExposerController).Assembly);
        }
    }
}
// END - Plugin.cs