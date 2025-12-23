// Controller/EndpointExposerController.cs
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EndpointExposer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Jellyfin.Plugin.EndpointExposer.Controller
{
    [ApiController]
    [Route("Plugins/EndpointExposer/[action]")]
    public class EndpointExposerController : ControllerBase
    {
        private readonly ILogger _logger;

        // Lazy cached instances created from Plugin.Instance when host DI is not available.
        private static EndpointExposerService? _fallbackService;
        private static string? _fallbackConfigSnapshotJson;

        private static object _fallbackLock = new object();

        public EndpointExposerController(ILogger<EndpointExposerController> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Determine the effective server base URL to use for token validation.
        /// Preference order:
        /// 1) PluginConfiguration.ServerBaseUrl
        /// 2) Request.Scheme + Request.Host (the host the client used)
        /// 3) provided defaultBase
        /// </summary>
        private static string GetEffectiveServerBaseUrl(HttpRequest? request, PluginConfiguration? cfg, string defaultBase)
        {
            if (!string.IsNullOrWhiteSpace(cfg?.ServerBaseUrl))
                return cfg.ServerBaseUrl.TrimEnd('/');

            if (request != null && request.Host.HasValue)
            {
                var scheme = string.IsNullOrWhiteSpace(request.Scheme) ? "http" : request.Scheme;
                return $"{scheme}://{request.Host.Value}".TrimEnd('/');
            }

            return defaultBase.TrimEnd('/');
        }

        /// <summary>
        /// Get a service instance. Prefer host DI if it provides EndpointExposerService,
        /// otherwise build a fallback instance from Plugin.Instance.
        /// </summary>
        private EndpointExposerService GetService()
        {
            // Try to resolve from request services (host DI) first
            try
            {
                var svc = HttpContext?.RequestServices?.GetService(typeof(EndpointExposerService)) as EndpointExposerService;
                if (svc != null)
                    return svc;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetService: host DI resolution attempt failed.");
            }

            // Fallback: build a cached instance using Plugin.Instance
            // If we already have a fallback service, check whether the plugin configuration has changed.
            // We keep a lightweight JSON snapshot to detect changes and recreate the fallback service when needed.
            try
            {
                var plugin = Plugin.Instance;
                if (plugin == null)
                {
                    _logger.LogError("EndpointExposerController: Plugin.Instance is null; cannot construct fallback service.");
                    throw new InvalidOperationException("Plugin instance not available");
                }

                var cfg = plugin.Configuration ?? new PluginConfiguration();

                // Serialize a compact snapshot of the configuration to detect changes
                var cfgJson = System.Text.Json.JsonSerializer.Serialize(cfg);

                // If we have a cached service and the config snapshot matches, reuse it
                if (_fallbackService != null && !string.IsNullOrEmpty(_fallbackConfigSnapshotJson) && string.Equals(_fallbackConfigSnapshotJson, cfgJson, StringComparison.Ordinal))
                {
                    return _fallbackService;
                }

                // Otherwise, (re)create the fallback service using the current configuration
                // Create loggers
                ILoggerFactory? loggerFactory = null;
                try
                {
                    loggerFactory = plugin.ServiceProvider?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                }
                catch { /* ignore */ }

                var svcLogger = loggerFactory != null ? loggerFactory.CreateLogger<EndpointExposerService>() : new Microsoft.Extensions.Logging.Abstractions.NullLogger<EndpointExposerService>();
                var fileLogger = loggerFactory != null ? loggerFactory.CreateLogger<FileWriteService>() : new Microsoft.Extensions.Logging.Abstractions.NullLogger<FileWriteService>();

                // Determine effective server base URL (prefer explicit ServerBaseUrl, then request host)
                var defaultBase = "http://127.0.0.1:8096";
                var serverBase = GetEffectiveServerBaseUrl(HttpContext?.Request, cfg, defaultBase);

                // Try to obtain IHttpClientFactory from plugin.ServiceProvider if available
                IHttpClientFactory? httpFactory = null;
                try
                {
                    httpFactory = plugin.ServiceProvider?.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
                }
                catch { /* ignore */ }

                HttpClient http;
                if (httpFactory != null)
                {
                    http = httpFactory.CreateClient("EndpointExposer");
                }
                else
                {
                    http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                }

                var loggerForAuth = loggerFactory?.CreateLogger<JellyfinAuth>();
                var auth = new JellyfinAuth(serverBase, http, loggerForAuth);

                // Create FileWriteService (non-hosted fallback)
                var fileWriter = new FileWriteService(cfg, auth, fileLogger);

                // Create EndpointExposerService
                var newSvc = new EndpointExposerService(svcLogger, cfg, fileWriter, auth);

                // Replace cached service and snapshot atomically
                lock (_fallbackLock)
                {
                    _fallbackService = newSvc;
                    _fallbackConfigSnapshotJson = cfgJson;
                }

                _logger.LogInformation("EndpointExposerController: constructed fallback service from Plugin.Instance. EffectiveServerBase={Base}", serverBase);
                return _fallbackService;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EndpointExposerController: failed to construct fallback service.");
                throw;
            }

        }

        [HttpGet]
        public ActionResult<PluginConfiguration> Configuration()
        {
            try
            {
                var svc = GetService();
                var cfg = svc.GetConfiguration();
                return Ok(cfg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Configuration: unexpected error");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut]
        [HttpPost]
        public async Task<IActionResult> Write([FromQuery] string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return BadRequest("Query parameter 'name' is required.");

                var svc = GetService();
                var result = await svc.HandleWriteAsync(Request, name).ConfigureAwait(false);

                if (result == null)
                    return StatusCode(500, "Internal server error");

                if (!result.Success)
                {
                    if (result.StatusCode.HasValue)
                        return StatusCode(result.StatusCode.Value, result.Error ?? "Error");
                    return BadRequest(result.Error ?? "Error");
                }

                return Ok(new { Saved = true, Name = result.Name, Path = result.Path });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Write: unexpected error for name={Name}", name);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        public IActionResult List()
        {
            try
            {
                var svc = GetService();
                var files = svc.ListFiles().ToArray();
                return Ok(files);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "List: unexpected error");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        public IActionResult File([FromQuery] string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return BadRequest("Query parameter 'name' is required.");

                var svc = GetService();
                var (Exists, Bytes, ContentType, FileName) = svc.GetFile(name);
                if (!Exists)
                    return NotFound();

                var ct = string.IsNullOrWhiteSpace(ContentType) ? "application/octet-stream" : ContentType;
                return File(Bytes, ct, FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File: unexpected error for name={Name}", name);
                return StatusCode(500, "Internal server error");
            }
        }

        // Add "using System.Reflection;" at the top of the file if not already present.
        [HttpPut]
        [HttpPost]
        public async Task<IActionResult> SaveConfiguration()
        {
            try
            {
                // Read raw body as text to avoid input formatter binding issues
                string incomingRawJson;
                using (var sr = new StreamReader(Request.Body, Encoding.UTF8))
                {
                    incomingRawJson = await sr.ReadToEndAsync().ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(incomingRawJson))
                    return BadRequest("Missing configuration body.");

                JObject incomingRaw;
                try
                {
                    incomingRaw = JObject.Parse(incomingRawJson);
                }
                catch (Exception exParse)
                {
                    _logger?.LogDebug(exParse, "SaveConfiguration: failed to parse incoming JSON.");
                    return BadRequest("Invalid JSON payload.");
                }

                PluginConfiguration incoming;
                try
                {
                    incoming = incomingRaw.ToObject<PluginConfiguration>() ?? new PluginConfiguration();
                }
                catch (Exception exConv)
                {
                    _logger?.LogDebug(exConv, "SaveConfiguration: failed to convert JSON to PluginConfiguration.");
                    return BadRequest("Invalid configuration JSON.");
                }

                // Compute effective base first (prefer explicit config, then request host, then default)
                var defaultBase = "http://127.0.0.1:8096";
                var effectiveBase = GetEffectiveServerBaseUrl(HttpContext?.Request, incoming, defaultBase);

                // If incoming.ServerBaseUrl is empty, set it to the effective base
                if (string.IsNullOrWhiteSpace(incoming.ServerBaseUrl))
                {
                    incoming.ServerBaseUrl = effectiveBase;
                }

                // Clear ModelState and re-validate the incoming model now that we've set ServerBaseUrl
                ModelState.Clear();
                if (!TryValidateModel(incoming))
                {
                    foreach (var kv in ModelState)
                    {
                        foreach (var err in kv.Value.Errors)
                        {
                            _logger?.LogDebug("Post-validate ModelState error: {Key} => {Error}", kv.Key, err.ErrorMessage);
                        }
                    }

                    return BadRequest(ModelState);
                }

                // 1) Save plugin data file (existing behavior)
                var svc = GetService();
                await svc.SaveConfigurationAsync(incoming).ConfigureAwait(false);

                // 2) Persist to server plugin configuration (preferred) or fallback to XML file
                var persistedToPlugin = false;
                try
                {
                    var plugin = Plugin.Instance;
                    if (plugin != null)
                    {
                        var saveMethod = plugin.GetType().GetMethod("SaveConfiguration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (saveMethod != null)
                        {
                            try
                            {
                                saveMethod.Invoke(plugin, new object[] { incoming });
                                _logger.LogDebug("SaveConfiguration: invoked plugin SaveConfiguration method. EffectiveServerBase={Base}", incoming.ServerBaseUrl);
                                persistedToPlugin = true;
                            }
                            catch (Exception exInvoke)
                            {
                                _logger.LogWarning(exInvoke, "SaveConfiguration: reflection invoke failed; falling back to writing XML file.");
                                throw;
                            }
                        }
                        else
                        {
                            _logger.LogDebug("SaveConfiguration: plugin SaveConfiguration method not found; using XML fallback.");
                            throw new InvalidOperationException("SaveConfiguration method not found on plugin instance.");
                        }
                    }
                    else
                    {
                        _logger.LogDebug("SaveConfiguration: Plugin.Instance is null; using XML fallback.");
                        throw new InvalidOperationException("Plugin.Instance is null");
                    }
                }
                catch (Exception)
                {
                    // Reflection fallback: write XML into plugins\configurations
                    try
                    {
                        var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "jellyfin", "plugins", "configurations");
                        Directory.CreateDirectory(configDir);

                        var pluginFileName = typeof(Plugin).Namespace ?? "Jellyfin.Plugin.EndpointExposer";
                        var filePath = Path.Combine(configDir, pluginFileName + ".xml");

                        var xs = new System.Xml.Serialization.XmlSerializer(typeof(PluginConfiguration));
                        using (var fs = System.IO.File.Create(filePath))
                        {
                            xs.Serialize(fs, incoming);
                        }

                        _logger.LogDebug("SaveConfiguration: persisted configuration to {Path}", filePath);
                    }
                    catch (Exception ex2)
                    {
                        _logger.LogWarning(ex2, "SaveConfiguration: failed to persist to server plugin configurations fallback.");
                    }
                }

                // Ensure each configured ExposedFolder directory exists (best-effort)
                _logger.LogDebug("SaveConfiguration: starting ExposedFolders ensure step. Count={Count}", incoming?.ExposedFolders?.Count ?? 0);
                try
                {
                    if (incoming?.ExposedFolders != null && incoming.ExposedFolders.Count > 0)
                    {
                        var svcForDirs = GetService();
                        foreach (var fe in incoming.ExposedFolders)
                        {
                            if (fe == null) continue;
                            var logicalName = !string.IsNullOrWhiteSpace(fe.Name) ? fe.Name : fe.RelativePath;
                            if (string.IsNullOrWhiteSpace(logicalName)) continue;

                            try
                            {
                                // ResolveFolderPathFromConfig validates and creates the folder on disk
                                svcForDirs.ResolveFolderPathFromConfig(logicalName);
                                _logger.LogDebug("SaveConfiguration: ensured folder exists for {Folder}", logicalName);
                            }
                            catch (ArgumentException aex)
                            {
                                _logger.LogWarning(aex, "SaveConfiguration: invalid folder entry skipped {Folder}", logicalName);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "SaveConfiguration: failed to ensure folder for {Folder} (non-fatal)", logicalName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SaveConfiguration: unexpected error while ensuring ExposedFolders exist (non-fatal).");
                }

                _logger.LogDebug("SaveConfiguration: finished ExposedFolders ensure step.");
                // Ensure Plugin.Instance.Configuration reflects the newly saved configuration (use reflection)
                try
                {
                    var plugin = Plugin.Instance;
                    if (plugin != null)
                    {
                        var prop = plugin.GetType().GetProperty("Configuration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (prop != null)
                        {
                            var setMethod = prop.GetSetMethod(nonPublic: true);
                            if (setMethod != null)
                            {
                                setMethod.Invoke(plugin, new object[] { incoming });
                                _logger.LogDebug("SaveConfiguration: updated Plugin.Instance.Configuration in memory via non-public setter.");
                            }
                            else
                            {
                                var field = plugin.GetType().GetField("_configuration", BindingFlags.Instance | BindingFlags.NonPublic)
                                            ?? plugin.GetType().GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic);
                                if (field != null)
                                {
                                    field.SetValue(plugin, incoming);
                                    _logger.LogDebug("SaveConfiguration: updated Plugin.Instance configuration via private backing field.");
                                }
                                else
                                {
                                    _logger.LogDebug("SaveConfiguration: could not find non-public setter or backing field for Plugin.Configuration; in-memory update skipped.");
                                }
                            }
                        }
                        else
                        {
                            _logger.LogDebug("SaveConfiguration: Plugin type does not expose a Configuration property via reflection.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SaveConfiguration: failed to update Plugin.Instance.Configuration in memory via reflection.");
                }

                return Ok(new { Saved = true, EffectiveServerBase = incoming.ServerBaseUrl, PersistedToPlugin = persistedToPlugin });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveConfiguration: failed to persist configuration");
                return StatusCode(500, "Internal server error");
            }


        }



        #region Folder endpoints

        [HttpGet]
        public IActionResult FolderFiles([FromQuery] string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder))
                    return BadRequest("Query parameter 'folder' is required.");

                var svc = GetService();
                var files = svc.ListFolderFiles(folder).ToArray();
                return Ok(files);
            }
            catch (ArgumentException aex)
            {
                return BadRequest(aex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FolderFiles: unexpected error for folder={Folder}", folder);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet]
        public IActionResult FolderFile([FromQuery] string folder, [FromQuery] string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder))
                    return BadRequest("Query parameter 'folder' is required.");
                if (string.IsNullOrWhiteSpace(name))
                    return BadRequest("Query parameter 'name' is required.");

                var svc = GetService();
                var (Exists, Bytes, ContentType, FileName) = svc.ReadFolderFile(folder, name);
                if (!Exists)
                    return NotFound();

                var ct = string.IsNullOrWhiteSpace(ContentType) ? "application/octet-stream" : ContentType;
                return File(Bytes, ct, FileName);
            }
            catch (ArgumentException aex)
            {
                return BadRequest(aex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FolderFile: unexpected error for folder={Folder} name={Name}", folder, name);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPut]
        [HttpPost]
        public async Task<IActionResult> FolderWrite([FromQuery] string folder, [FromQuery] string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder))
                    return BadRequest("Query parameter 'folder' is required.");
                if (string.IsNullOrWhiteSpace(name))
                    return BadRequest("Query parameter 'name' is required.");

                // Auth: reuse existing logic from HandleWriteAsync (token/api key/admin checks)
                // We'll reuse the same auth checks by calling HandleWriteAsync-like flow but for folder.
                // For simplicity, replicate minimal auth checks here using existing code paths.

                var svc = GetService();

                // Determine token/key and admin status
                string? token = null;
                if (Request.Headers.ContainsKey("Authorization"))
                {
                    var auth = Request.Headers["Authorization"].ToString();
                    if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        token = auth.Substring(7).Trim();
                }
                if (string.IsNullOrWhiteSpace(token))
                {
                    if (Request.Headers.ContainsKey("X-Emby-Token"))
                        token = Request.Headers["X-Emby-Token"].ToString();
                    else if (Request.Headers.ContainsKey("X-Jellyfin-Token"))
                        token = Request.Headers["X-Jellyfin-Token"].ToString();
                }
                if (string.IsNullOrWhiteSpace(token) && Request.Query.ContainsKey("api_key"))
                    token = Request.Query["api_key"].ToString();

                bool isAdmin = false;
                var svcConfig = svc.GetConfiguration();
                bool apiKeySet = !string.IsNullOrWhiteSpace(svcConfig?.ApiKey);
                bool allowNonAdminGlobal = svcConfig?.AllowNonAdmin ?? false;

                if (!string.IsNullOrWhiteSpace(token))
                {
                    // Use the public wrapper to validate token (returns user object or null)
                    var userObj = await svc.ValidateTokenWithFallbackPublicAsync(token, Request).ConfigureAwait(false);
                    if (userObj != null)
                    {
                        // Ask the service whether this user is admin
                        isAdmin = svc.IsUserAdminPublic(userObj);
                    }
                }

                if (!isAdmin)
                {
                    // Validate api key header or query param
                    var providedKey = Request.Headers.ContainsKey("X-EndpointExposer-Key") ? Request.Headers["X-EndpointExposer-Key"].ToString() : null;
                    if (string.IsNullOrWhiteSpace(providedKey) && Request.Query.ContainsKey("api_key"))
                        providedKey = Request.Query["api_key"].ToString();

                    if (!string.Equals(providedKey, svcConfig?.ApiKey, StringComparison.Ordinal))
                    {
                        // Check folder-level AllowNonAdmin
                        var folderEntry = svcConfig?.ExposedFolders?.FirstOrDefault(f => string.Equals(f.Name, folder, StringComparison.OrdinalIgnoreCase) || string.Equals(f.RelativePath, folder, StringComparison.OrdinalIgnoreCase));
                        var folderAllows = folderEntry?.AllowNonAdmin ?? false;
                        if (!allowNonAdminGlobal || !apiKeySet || !folderAllows)
                        {
                            _logger.LogWarning("FolderWrite: unauthorized attempt for folder={Folder} name={Name}", folder, name);
                            return Unauthorized("Unauthorized");
                        }
                    }
                }


                // Read body into bytes (respect MaxPayloadBytes)
                var maxBytes = svc.GetConfiguration()?.MaxPayloadBytes ?? 2 * 1024 * 1024;
                using var ms = new MemoryStream();
                var buffer = new byte[8192];
                long total = 0;
                int read;
                while ((read = await Request.Body.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        _logger.LogWarning("FolderWrite: payload exceeded max while reading for folder={Folder} name={Name}", folder, name);
                        return StatusCode(413, "Payload too large");
                    }
                    ms.Write(buffer, 0, read);
                }
                var bytes = ms.ToArray();

                var writeResult = await svc.WriteFileToFolderAsync(folder, name, bytes).ConfigureAwait(false);
                if (!writeResult.Success)
                {
                    return StatusCode(500, writeResult.Error ?? "Failed to write file");
                }

                return Ok(new { Saved = true, Name = name, Path = writeResult.Path });
            }
            catch (ArgumentException aex)
            {
                return BadRequest(aex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FolderWrite: unexpected error for folder={Folder} name={Name}", folder, name);
                return StatusCode(500, "Internal server error");
            }
        }

        #endregion



    }
}
