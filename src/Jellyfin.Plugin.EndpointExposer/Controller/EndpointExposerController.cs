// Controller/EndpointExposerController.cs
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.EndpointExposer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.EndpointExposer.Controller
{
    [ApiController]
    [Route("Plugins/EndpointExposer/[action]")]
    public class EndpointExposerController : ControllerBase
    {
        private readonly ILogger<EndpointExposerController> _logger;
        private readonly EndpointExposerService _service;
        private readonly AuthService _authService;
        private readonly FolderOperationService _folderService;
        private readonly ConfigurationHandler _configHandler;

        public EndpointExposerController(
            ILogger<EndpointExposerController> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));

            var cfg = serviceProvider.GetService<PluginConfiguration>() ?? Plugin.Instance?.Configuration ?? new PluginConfiguration();

            // Ensure we have a FileWriteService available (either registered or constructed fallback)
            var fileWriter = serviceProvider.GetService<FileWriteService>();
            if (fileWriter == null)
            {
                // Prefer an explicit ServerBaseUrl from config; otherwise construct JellyfinAuth with an empty base
                // so that runtime validation can derive the effective base from incoming requests when necessary.
                var jellyfinAuth = serviceProvider.GetService<JellyfinAuth>() ?? new JellyfinAuth(cfg.ServerBaseUrl ?? string.Empty, new HttpClient(), serviceProvider.GetService<ILogger<JellyfinAuth>>());
                var fwLogger = serviceProvider.GetService<ILogger<FileWriteService>>() ?? serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<FileWriteService>();
                fileWriter = new FileWriteService(cfg, jellyfinAuth, fwLogger);
            }

            // Resolve or fallback EndpointExposerService
            _service = serviceProvider.GetService<EndpointExposerService>() ?? new EndpointExposerService(serviceProvider.GetService<ILogger<EndpointExposerService>>() ?? serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<EndpointExposerService>(), cfg, fileWriter);

            // Resolve or construct other services with safe defaults
            _authService = serviceProvider.GetService<AuthService>() ?? new AuthService(serviceProvider.GetService<ILogger<AuthService>>(), serviceProvider.GetService<JellyfinAuth>() ?? new JellyfinAuth(cfg.ServerBaseUrl ?? string.Empty, new HttpClient(), serviceProvider.GetService<ILogger<JellyfinAuth>>()), cfg);

            _folderService = serviceProvider.GetService<FolderOperationService>() ?? new FolderOperationService(serviceProvider.GetService<ILogger<FolderOperationService>>(), cfg, fileWriter);

            _configHandler = serviceProvider.GetService<ConfigurationHandler>() ?? new ConfigurationHandler(serviceProvider.GetService<ILogger<ConfigurationHandler>>(), _folderService);
        }

        /// <summary>
        /// GET: /Plugins/EndpointExposer/DataBasePath
        /// Returns the plugin's data base directory path for client-side previews.
        /// </summary>
        [HttpGet]
        public ActionResult<object> DataBasePath()
        {
            try
            {
                var cfg = _service.GetConfiguration();
                var service = _folderService;
                // Use the configured output directory or default plugin data path
                string basePath = cfg.OutputDirectory ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "jellyfin",
                    "plugins",
                    "configurations",
                    "Jellyfin.Plugin.EndpointExposer",
                    "data"
                );
                return Ok(new { basePath = basePath });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DataBasePath: unexpected error");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// GET: /Plugins/EndpointExposer/Configuration
        /// Returns current plugin configuration.
        /// </summary>
        [HttpGet]
        public ActionResult<PluginConfiguration> Configuration()
        {
            try
            {
                var cfg = _service.GetConfiguration();
                return Ok(cfg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Configuration: unexpected error");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// POST/PUT: /Plugins/EndpointExposer/SaveConfiguration
        /// Saves plugin configuration with validation and folder creation.
        /// </summary>
        [HttpPut]
        [HttpPost]
        public async Task<IActionResult> SaveConfiguration()
        {
            try
            {
                string incomingRawJson;
                using (var sr = new StreamReader(Request.Body, Encoding.UTF8))
                {
                    incomingRawJson = await sr.ReadToEndAsync().ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(incomingRawJson))
                    return BadRequest("Missing configuration body.");

                PluginConfiguration incoming;
                try
                {
                    var incomingRaw = JObject.Parse(incomingRawJson);
                    incoming = incomingRaw.ToObject<PluginConfiguration>() ?? new PluginConfiguration();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "SaveConfiguration: failed to parse/convert JSON");
                    return BadRequest("Invalid configuration JSON.");
                }

                // Apply effective server base if not set
                var effectiveBase = _authService.GetEffectiveServerBase(HttpContext?.Request);
                _configHandler.ApplyEffectiveServerBase(incoming, effectiveBase);

                // Validate configuration
                var (isValid, error) = _configHandler.ValidateConfiguration(incoming);
                if (!isValid)
                {
                    _logger.LogWarning("SaveConfiguration: validation failed - {Error}", error);
                    return BadRequest(new { error = error });
                }

                // Save configuration
                await _configHandler.SaveConfigurationAsync(incoming).ConfigureAwait(false);

                _logger.LogInformation("SaveConfiguration: configuration saved with ServerBase={Base}", incoming.ServerBaseUrl);
                return Ok(new
                {
                    Saved = true,
                    EffectiveServerBase = incoming.ServerBaseUrl ?? effectiveBase,
                    FolderCount = incoming.ExposedFolders?.Count ?? 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveConfiguration: unexpected error");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// POST/PUT: /Plugins/EndpointExposer/Write
        /// Write JSON payload to a file in the default output directory.
        /// Requires admin or valid API key (based on AllowNonAdmin setting).
        /// </summary>
        [HttpPut]
        [HttpPost]
        public async Task<IActionResult> Write([FromQuery] string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return BadRequest("Query parameter 'name' is required.");

                // Extract and validate token
                var token = _authService.ExtractTokenFromRequest(Request);
                JObject? user = null;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    user = await _authService.ValidateTokenAsync(token, Request).ConfigureAwait(false);
                }

                // Check authorization
                var (isAuthorized, reason) = _authService.CheckWriteAuthorization(Request, user);
                if (!isAuthorized)
                {
                    _logger.LogWarning("Write: unauthorized attempt for {Name} - {Reason}", name, reason);
                    return Unauthorized(new { error = reason });
                }

                // Perform write operation
                var result = await _service.HandleWriteAsync(Request, name).ConfigureAwait(false);

                if (result == null)
                    return StatusCode(500, "Internal server error");

                if (!result.Success)
                {
                    if (result.StatusCode.HasValue)
                        return StatusCode(result.StatusCode.Value, result.Error ?? "Error");
                    return BadRequest(result.Error ?? "Error");
                }

                _logger.LogInformation("Write: successfully wrote {Name}", name);
                return Ok(new { Saved = true, Name = result.Name, Path = result.Path });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Write: unexpected error for name={Name}", name);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// GET: /Plugins/EndpointExposer/List
        /// List all files in the default output directory.
        /// </summary>
        [HttpGet]
        public IActionResult List()
        {
            try
            {
                var files = _service.ListFiles().ToArray();
                return Ok(files);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "List: unexpected error");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// GET: /Plugins/EndpointExposer/File
        /// Read a file from the default output directory.
        /// </summary>
        [HttpGet]
        public IActionResult File([FromQuery] string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return BadRequest("Query parameter 'name' is required.");

                var (Exists, Bytes, ContentType, FileName) = _service.GetFile(name);
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

        #region Folder Endpoints

        /// <summary>
        /// GET: /Plugins/EndpointExposer/FolderFiles
        /// List files in a configured folder.
        /// </summary>
        [HttpGet]
        public IActionResult FolderFiles([FromQuery] string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder))
                    return BadRequest("Query parameter 'folder' is required.");

                var files = _folderService.ListFolderFiles(folder).ToArray();
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

        /// <summary>
        /// GET: /Plugins/EndpointExposer/FolderFile
        /// Read a file from a configured folder.
        /// </summary>
        [HttpGet]
        public IActionResult FolderFile([FromQuery] string folder, [FromQuery] string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder))
                    return BadRequest("Query parameter 'folder' is required.");
                if (string.IsNullOrWhiteSpace(name))
                    return BadRequest("Query parameter 'name' is required.");

                var (Exists, Bytes, ContentType, FileName) = _folderService.ReadFolderFile(folder, name);
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

        /// <summary>
        /// POST/PUT: /Plugins/EndpointExposer/FolderWrite
        /// Write file to a configured folder.
        /// Requires admin or valid API key (with folder-specific permissions).
        /// </summary>
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

                // Use the injected/fallback service instance stored on the controller
                var svc = _service;

                // Extract and validate token
                var token = _authService.ExtractTokenFromRequest(Request);
                Newtonsoft.Json.Linq.JObject? user = null;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    _logger.LogDebug("FolderWrite: extracted token (length={Length}), attempting validation", token?.Length ?? 0);
                    // token is checked for null/whitespace above; use null-forgiving to satisfy analyzer
                    user = await _authService.ValidateTokenAsync(token!, Request).ConfigureAwait(false);
                    if (user == null)
                    {
                        _logger.LogWarning("FolderWrite: token validation returned null - token may be invalid or base URL incorrect. Request PathBase={PathBase}, Host={Host}",
                            Request?.PathBase.HasValue == true ? Request.PathBase.Value : "none",
                            Request?.Host.HasValue == true ? Request.Host.Value : "none");
                    }
                    else
                    {
                        _logger.LogDebug("FolderWrite: token validated successfully, user Id={UserId}, IsAdmin={IsAdmin}",
                            user["Id"]?.ToString() ?? "unknown", _authService.IsUserAdmin(user));
                    }
                }
                else
                {
                    _logger.LogDebug("FolderWrite: no token extracted from request. Headers: Authorization={HasAuth}, X-Emby-Token={HasEmby}, X-Jellyfin-Token={HasJellyfin}",
                        Request?.Headers?.ContainsKey("Authorization") == true,
                        Request?.Headers?.ContainsKey("X-Emby-Token") == true,
                        Request?.Headers?.ContainsKey("X-Jellyfin-Token") == true);
                }

                // Check folder-specific authorization
                var (isAuthorized, reason) = _authService.CheckFolderWriteAuthorization(Request, folder, user);
                if (!isAuthorized)
                {
                    _logger.LogWarning("FolderWrite: unauthorized attempt for folder={Folder} name={Name} - {Reason}", folder, name, reason);
                    return Unauthorized(new { error = reason });
                }

                // After authorization checks, before writing the file:
                // Log incoming content-length and some headers for diagnostics
                _logger.LogDebug("FolderWrite: incoming request Content-Type={ContentType}, Content-Length={ContentLength}, Headers: Authorization={HasAuth}, X-EndpointExposer-Key={HasKey}, X-Emby-Token={HasEmby}, X-Jellyfin-Token={HasJellyfin}",
                    Request?.ContentType ?? "(none)",
                    Request?.ContentLength?.ToString() ?? "(null)",
                    Request?.Headers?.ContainsKey("Authorization") == true,
                    Request?.Headers?.ContainsKey("X-EndpointExposer-Key") == true,
                    Request?.Headers?.ContainsKey("X-Emby-Token") == true,
                    Request?.Headers?.ContainsKey("X-Jellyfin-Token") == true);

                // Read payload into bytes, but support JSON wrapper { "content": "..." } as well
                byte[] bytes;
                string contentType = Request?.ContentType ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(contentType) && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    string raw;
                    using (var sr = new System.IO.StreamReader(Request.Body, Encoding.UTF8))
                    {
                        raw = await sr.ReadToEndAsync().ConfigureAwait(false);
                    }

                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        _logger.LogWarning("FolderWrite: empty JSON body for folder={Folder} name={Name}", folder, name);
                        return BadRequest("Missing body");
                    }

                    try
                    {
                        var j = Newtonsoft.Json.Linq.JObject.Parse(raw);

                        if (j.TryGetValue("content", StringComparison.OrdinalIgnoreCase, out var contentToken) && contentToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                        {
                            var contentStr = contentToken.ToString();
                            if (string.IsNullOrEmpty(contentStr))
                            {
                                _logger.LogWarning("FolderWrite: 'content' property present but empty for folder={Folder} name={Name}", folder, name);
                                return BadRequest("Missing body");
                            }

                            // Try base64 decode, fallback to UTF-8
                            try
                            {
                                var maybe = contentStr.Trim();
                                if (maybe.Length % 4 == 0)
                                {
                                    bytes = Convert.FromBase64String(maybe);
                                }
                                else
                                {
                                    bytes = Encoding.UTF8.GetBytes(contentStr);
                                }
                            }
                            catch
                            {
                                bytes = Encoding.UTF8.GetBytes(contentStr);
                            }
                        }
                        else
                        {
                            // No "content" property: treat the whole JSON as the payload
                            var compact = j.ToString(Newtonsoft.Json.Formatting.None);
                            if (string.IsNullOrWhiteSpace(compact))
                            {
                                _logger.LogWarning("FolderWrite: parsed JSON is empty for folder={Folder} name={Name}", folder, name);
                                return BadRequest("Missing body");
                            }
                            bytes = Encoding.UTF8.GetBytes(compact);
                        }
                    }
                    catch (Newtonsoft.Json.JsonException jex)
                    {
                        _logger.LogWarning(jex, "FolderWrite: invalid JSON payload for folder={Folder} name={Name}", folder, name);
                        return BadRequest("Invalid JSON");
                    }
                }
                else
                {
                    // Non-JSON: read raw bytes up to configured limit
                    var maxBytes = _service.GetConfiguration()?.MaxPayloadBytes ?? 2 * 1024 * 1024;
                    using var ms = new System.IO.MemoryStream();
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
                    bytes = ms.ToArray();

                    if (bytes.Length == 0)
                    {
                        _logger.LogWarning("FolderWrite: empty raw body for folder={Folder} name={Name} - refusing to write empty file", folder, name);
                        return BadRequest("Missing body");
                    }
                }

                // Log final byte length before write
                _logger.LogDebug("FolderWrite: read {Len} bytes for folder={Folder} name={Name}", bytes.Length, folder, name);

                // Enforce MaxPayloadBytes for JSON-derived bytes as well
                var configuredMax = _service.GetConfiguration()?.MaxPayloadBytes ?? 2 * 1024 * 1024;
                if (bytes.Length > configuredMax)
                {
                    _logger.LogWarning("FolderWrite: payload too large ({Len} > {Max}) for folder={Folder} name={Name}", bytes.Length, configuredMax, folder, name);
                    return StatusCode(413, "Payload too large");
                }


                // Write file
                var writeResult = await _folderService.WriteFolderFileAsync(folder, name, bytes).ConfigureAwait(false);
                if (!writeResult.Success)
                {
                    _logger.LogWarning("FolderWrite: write failed for folder={Folder} name={Name} - {Error}", folder, name, writeResult.Error);
                    return StatusCode(500, writeResult.Error ?? "Failed to write file");
                }

                _logger.LogInformation("FolderWrite: successfully wrote {Name} to folder {Folder} (path={Path})", name, folder, writeResult.Path);
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

        /// <summary>
        /// GET: /Plugins/EndpointExposer/ResolvePath?relative=foldername
        /// Resolves a relative folder name to its absolute path.
        /// Used by the settings page for client-side path preview.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public IActionResult ResolvePath([FromQuery] string relative)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relative))
                    return BadRequest(new { error = "Query parameter 'relative' is required." });

                // Resolve the folder path
                var resolvedPath = _folderService.ResolveFolderPath(relative);
                return Ok(new { resolvedPath = resolvedPath, relative = relative });
            }
            catch (ArgumentException aex)
            {
                return NotFound(new { error = "Folder not configured or invalid", detail = aex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ResolvePath: unexpected error for relative={Relative}", relative);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// POST: /Plugins/EndpointExposer/CreateFolder
        /// Verify that a folder exists and is accessible.
        /// Requires admin privileges or valid API key.
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> CreateFolder()
        {
            try
            {
                string raw;
                using (var sr = new StreamReader(Request.Body, Encoding.UTF8))
                    raw = await sr.ReadToEndAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(raw))
                    return BadRequest(new { error = "Missing request body" });

                JObject payload;
                try
                {
                    payload = JObject.Parse(raw);
                }
                catch
                {
                    return BadRequest(new { error = "Invalid JSON payload" });
                }

                var folderName = (string?)payload["RelativePath"] ?? (string?)payload["relative"] ?? (string?)payload["folder"];
                if (string.IsNullOrWhiteSpace(folderName))
                    return BadRequest(new { error = "RelativePath is required" });

                // Extract and validate token
                var token = _authService.ExtractTokenFromRequest(Request);
                JObject? user = null;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    user = await _authService.ValidateTokenAsync(token, Request).ConfigureAwait(false);
                }

                // Check authorization (admin or API key required)
                var (isAuthorized, reason) = _authService.CheckWriteAuthorization(Request, user);
                if (!isAuthorized)
                {
                    _logger.LogWarning("CreateFolder: unauthorized attempt for {Folder}", folderName);
                    return Unauthorized(new { error = reason });
                }

                // Resolve folder path
                try
                {
                    var resolvedPath = _folderService.ResolveFolderPath(folderName);
                    return Ok(new { success = true, resolvedPath = resolvedPath, folder = folderName });
                }
                catch (ArgumentException aex)
                {
                    return NotFound(new { error = "Folder not configured or invalid", detail = aex.Message });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateFolder: unexpected error");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// OPTIONS: /Plugins/EndpointExposer/CreateFolder
        /// CORS preflight support.
        /// </summary>
        [HttpOptions]
        [AllowAnonymous]
        public IActionResult CreateFolderOptions() => Ok();

        #endregion
    }
}
// END - Controller/EndpointExposerController.cs