// Services/ConfigurationHandler.cs
using System;
using System.IO;
using System.Xml.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.EndpointExposer.Services
{
    /// <summary>
    /// Handles plugin configuration persistence, loading, and folder creation.
    /// Manages reading/writing configuration to XML and in-memory plugin state.
    /// </summary>
    public class ConfigurationHandler
    {
        private readonly ILogger<ConfigurationHandler> _logger;
        private readonly FolderOperationService _folderOperationService;

        public ConfigurationHandler(ILogger<ConfigurationHandler> logger, FolderOperationService folderOperationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _folderOperationService = folderOperationService ?? throw new ArgumentNullException(nameof(folderOperationService));
        }

        /// <summary>
        /// Save configuration to XML file and plugin instance.
        /// Also ensures all ExposedFolders exist on disk.
        /// </summary>
        public async Task SaveConfigurationAsync(PluginConfiguration config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            try
            {
                _logger.LogDebug("ConfigurationHandler: saving configuration");

                // Update plugin instance with new configuration (via reflection)
                await UpdatePluginInstanceConfigurationAsync(config).ConfigureAwait(false);

                // Persist to XML file (fallback mechanism)
                await PersistConfigurationToXmlAsync(config).ConfigureAwait(false);

                // Ensure all configured folders exist
                await EnsureConfiguredFoldersExistAsync(config).ConfigureAwait(false);

                _logger.LogInformation("ConfigurationHandler: configuration saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConfigurationHandler: failed to save configuration");
                throw;
            }
        }

        /// <summary>
        /// Ensure all configured folders exist on disk.
        /// Non-fatal: logs warnings for failures but does not throw.
        /// </summary>
        private Task EnsureConfiguredFoldersExistAsync(PluginConfiguration config)
        {
            if (config?.ExposedFolders == null || config.ExposedFolders.Count == 0)
                return Task.CompletedTask;

            _logger.LogDebug("ConfigurationHandler: ensuring {Count} configured folders exist", config.ExposedFolders.Count);

            foreach (var folderEntry in config.ExposedFolders)
            {
                if (folderEntry == null)
                    continue;

                var logicalName = !string.IsNullOrWhiteSpace(folderEntry.Name) ? folderEntry.Name : folderEntry.RelativePath;
                if (string.IsNullOrWhiteSpace(logicalName))
                    continue;

                try
                {
                    _folderOperationService.ResolveFolderPath(logicalName);
                    _logger.LogDebug("ConfigurationHandler: ensured folder exists for {Folder}", logicalName);
                }
                catch (ArgumentException aex)
                {
                    _logger.LogWarning(aex, "ConfigurationHandler: invalid folder entry skipped {Folder}", logicalName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ConfigurationHandler: failed to ensure folder for {Folder} (non-fatal)", logicalName);
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Update the Plugin.Instance.Configuration property via reflection.
        /// This is necessary because the plugin uses a private backing field.
        /// </summary>
        private async Task UpdatePluginInstanceConfigurationAsync(PluginConfiguration config)
        {
            await Task.Run(() =>
            {
                try
                {
                    var plugin = Plugin.Instance;
                    if (plugin == null)
                    {
                        _logger.LogWarning("ConfigurationHandler: Plugin.Instance is null; cannot update in-memory configuration");
                        return;
                    }

                    var pluginType = plugin.GetType();

                    // Try public property setter first
                    var prop = pluginType.GetProperty("Configuration",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);

                    if (prop != null)
                    {
                        var setMethod = prop.GetSetMethod(nonPublic: true);
                        if (setMethod != null)
                        {
                            setMethod.Invoke(plugin, new object[] { config });
                            _logger.LogDebug("ConfigurationHandler: updated Plugin.Instance.Configuration via property setter");
                            return;
                        }
                    }

                    // Try backing field as fallback
                    var backingFieldCandidates = new[] { "_configuration", "configuration", "_config", "config" };
                    foreach (var fieldName in backingFieldCandidates)
                    {
                        var field = pluginType.GetField(fieldName,
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic);

                        if (field != null)
                        {
                            field.SetValue(plugin, config);
                            _logger.LogDebug("ConfigurationHandler: updated Plugin.Instance configuration via private field {FieldName}", fieldName);
                            return;
                        }
                    }

                    _logger.LogWarning("ConfigurationHandler: could not find property or backing field for Plugin.Configuration");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ConfigurationHandler: failed to update Plugin.Instance.Configuration via reflection");
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Persist configuration to XML file as a fallback mechanism.
        /// </summary>
        private async Task PersistConfigurationToXmlAsync(PluginConfiguration config)
        {
            await Task.Run(() =>
            {
                try
                {
                    var configDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "jellyfin", "plugins", "configurations");

                    Directory.CreateDirectory(configDir);

                    var pluginFileName = typeof(Plugin).Namespace ?? "Jellyfin.Plugin.EndpointExposer";
                    var filePath = Path.Combine(configDir, pluginFileName + ".xml");

                    var xs = new XmlSerializer(typeof(PluginConfiguration));
                    using (var fs = File.Create(filePath))
                    {
                        xs.Serialize(fs, config);
                    }

                    _logger.LogDebug("ConfigurationHandler: persisted configuration to {Path}", filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ConfigurationHandler: failed to persist configuration to XML file");
                    throw;
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Apply effective server base URL to configuration if not explicitly set.
        /// </summary>
        public void ApplyEffectiveServerBase(PluginConfiguration config, string effectiveBase)
        {
            if (config == null)
                return;

            if (string.IsNullOrWhiteSpace(config.ServerBaseUrl))
            {
                config.ServerBaseUrl = effectiveBase;
                _logger.LogDebug("ConfigurationHandler: applied effective server base {Base}", effectiveBase);
            }
        }

        /// <summary>
        /// Validate configuration for common issues.
        /// Returns (IsValid, ErrorMessage).
        /// </summary>
        public (bool IsValid, string? ErrorMessage) ValidateConfiguration(PluginConfiguration config)
        {
            if (config == null)
                return (false, "Configuration is null");

            if (!string.IsNullOrWhiteSpace(config.ServerBaseUrl))
            {
                try
                {
                    var uri = new Uri(config.ServerBaseUrl);
                    if (uri.Scheme != "http" && uri.Scheme != "https")
                        return (false, "ServerBaseUrl must use http or https scheme");
                }
                catch
                {
                    return (false, "ServerBaseUrl is not a valid URI");
                }
            }

            if (config.MaxPayloadBytes < 1024)
                return (false, "MaxPayloadBytes must be at least 1024 bytes");

            if (config.MaxBackups < 0)
                return (false, "MaxBackups cannot be negative");

            return (true, null);
        }
    }
}
// END - Services/ConfigurationHandler.cs