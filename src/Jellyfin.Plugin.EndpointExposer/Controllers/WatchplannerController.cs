using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.EndpointExposer.Controllers
{
    [ApiController]
    [Route("Watchplanner")]
    public class WatchplannerController : ControllerBase
    {
        private readonly ILogger<WatchplannerController> _logger;
        private readonly IServerApplicationPaths? _paths;
        private static readonly object _fileLock = new object();

        public WatchplannerController(
            ILogger<WatchplannerController> logger,
            IServerApplicationPaths? paths = null)
        {
            _logger = logger;
            _paths = paths;

            _logger.LogInformation("[EndpointExposer] WatchplannerController constructed. IServerApplicationPaths available: {HasPaths}", _paths != null);
            _logger.LogInformation("[EndpointExposer] Resolved ApplicationDataPath: {Path}", ResolveApplicationDataPath());
        }

        [HttpGet("config")]
        public IActionResult GetConfig()
        {
            try
            {
                if (User?.Identity?.IsAuthenticated != true)
                {
                    _logger.LogWarning("[EndpointExposer] Unauthenticated GET attempt");
                    return Unauthorized(new { success = false, message = "Not authenticated" });
                }

                var cfgPath = ConfigFilePath;
                EnsureDirectoryExists(Path.GetDirectoryName(cfgPath));

                if (!System.IO.File.Exists(cfgPath))
                {
                    var defaultObj = new Dictionary<string, object>
                    {
                        ["serverWeekGrid"] = new Dictionary<string, object>()
                    };

                    var defaultJson = JsonConvert.SerializeObject(defaultObj, Formatting.None);
                    _logger.LogInformation("[EndpointExposer] Config file not found, returning default config.");
                    return Content(defaultJson, "application/json");
                }

                var content = System.IO.File.ReadAllText(cfgPath);
                return Content(content, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EndpointExposer] Error reading config");
                return StatusCode(500, new { success = false, message = "Error reading config" });
            }
        }

        [HttpPost("config")]
        public async Task<IActionResult> PostConfig()
        {
            string remoteIp = HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
            try
            {
                if (User?.Identity?.IsAuthenticated != true)
                {
                    _logger.LogWarning("[EndpointExposer] Unauthorized POST attempt (no user) from {IP}", remoteIp);
                    return Unauthorized(new { success = false, message = "Not authenticated" });
                }

                bool isAdmin = false;
                try
                {
                    isAdmin = User.IsInRole("Administrator");
                }
                catch { }

                if (!isAdmin)
                {
                    var adminClaim = User.FindFirst("IsAdministrator") ?? User.FindFirst("is_admin") ?? User.FindFirst("IsAdmin");
                    if (adminClaim != null && bool.TryParse(adminClaim.Value, out var parsed) && parsed)
                    {
                        isAdmin = true;
                    }
                }

                if (!isAdmin)
                {
                    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
                    _logger.LogWarning("[EndpointExposer] Forbidden POST attempt by user {UserId} from {IP}", userId, remoteIp);
                    return Forbid();
                }

                using var sr = new StreamReader(Request.Body);
                var body = await sr.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body))
                {
                    _logger.LogWarning("[EndpointExposer] Empty POST body from {IP}", remoteIp);
                    return BadRequest(new { success = false, message = "Empty body" });
                }

                Dictionary<string, object> incoming;
                try
                {
                    incoming = JsonConvert.DeserializeObject<Dictionary<string, object>>(body) ?? new Dictionary<string, object>();
                }
                catch (JsonException dex)
                {
                    _logger.LogWarning(dex, "[EndpointExposer] Invalid JSON from {IP}", remoteIp);
                    return BadRequest(new { success = false, message = "Invalid JSON payload" });
                }

                if (!IsValidSchedule(incoming, out var validationMessage))
                {
                    _logger.LogWarning("[EndpointExposer] Payload validation failed from {IP}: {Reason}", remoteIp, validationMessage);
                    return BadRequest(new { success = false, message = $"Invalid payload: {validationMessage}" });
                }

                var cfgPath = ConfigFilePath;
                lock (_fileLock)
                {
                    EnsureDirectoryExists(Path.GetDirectoryName(cfgPath));

                    var toWrite = new Dictionary<string, object>
                    {
                        ["serverWeekGrid"] = incoming
                    };

                    var outJson = JsonConvert.SerializeObject(toWrite, Formatting.None);

                    var tempPath = cfgPath + ".tmp";
                    System.IO.File.WriteAllText(tempPath, outJson);
                    if (System.IO.File.Exists(cfgPath))
                    {
                        System.IO.File.Replace(tempPath, cfgPath, null);
                    }
                    else
                    {
                        System.IO.File.Move(tempPath, cfgPath);
                    }
                }

                var userIdLog = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
                _logger.LogInformation("[EndpointExposer] Config updated by user {UserId} from {IP}", userIdLog, remoteIp);
                return Ok(new { success = true, message = "Configuration updated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EndpointExposer] Error writing config");
                return StatusCode(500, new { success = false, message = "Error writing config" });
            }
        }

        #region Helpers

        private string GetConfigDirectory()
        {
            var appData = ResolveApplicationDataPath();

            var cfgDirCandidate = Path.Combine(appData, "plugins", "configurations", "Jellyfin.Plugin.EndpointExposer");
            var pluginDirCandidate = Path.Combine(appData, "plugins", "Jellyfin.Plugin.EndpointExposer", "config");

            var cfgFileCandidate = Path.Combine(cfgDirCandidate, "server-config.json");
            var pluginFileCandidate = Path.Combine(pluginDirCandidate, "server-config.json");

            if (Directory.Exists(cfgDirCandidate))
            {
                return cfgDirCandidate;
            }

            if (System.IO.File.Exists(pluginFileCandidate))
            {
                try
                {
                    Directory.CreateDirectory(cfgDirCandidate);
                    System.IO.File.Move(pluginFileCandidate, cfgFileCandidate);
                    _logger.LogInformation("[EndpointExposer] Migrated config from plugin folder to configurations folder: {Target}", cfgFileCandidate);
                    return cfgDirCandidate;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[EndpointExposer] Migration failed, falling back to plugin folder");
                }
            }

            if (!Directory.Exists(pluginDirCandidate))
            {
                Directory.CreateDirectory(pluginDirCandidate);
            }

            return pluginDirCandidate;
        }

        private string ConfigFilePath => Path.Combine(GetConfigDirectory(), "server-config.json");

        private void EnsureDirectoryExists(string? dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private bool IsValidSchedule(Dictionary<string, object> incoming, out string? message)
        {
            message = null;
            if (incoming == null)
            {
                message = "Payload is not an object";
                return false;
            }

            if (incoming.Count == 1 && incoming.ContainsKey("serverWeekGrid") && incoming["serverWeekGrid"] is Newtonsoft.Json.Linq.JObject jObj)
            {
                try
                {
                    var dict = jObj.ToObject<Dictionary<string, object>>();
                    incoming.Clear();
                    if (dict != null)
                    {
                        foreach (var kv in dict) incoming[kv.Key] = kv.Value;
                    }
                }
                catch { }
            }

            foreach (var kv in incoming)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                {
                    message = "Empty key found";
                    return false;
                }

                var val = kv.Value;
                if (val == null) continue;

                var typeName = val.GetType().Name;
                var allowed = typeName.Contains("JArray") || typeName.Contains("JObject") ||
                              typeName.Contains("List") || typeName.Contains("Dictionary") ||
                              typeName == "String" || typeName == "Int64" || typeName == "Int32" ||
                              typeName == "Double" || typeName == "Boolean" || typeName == "Object";

                if (!allowed)
                {
                    message = $"Unsupported value type for key '{kv.Key}': {typeName}";
                    return false;
                }
            }

            return true;
        }

        private string ResolveApplicationDataPath()
        {
            // Try to read common property names via reflection to be resilient across Jellyfin versions
            try
            {
                if (_paths != null)
                {
                    var pathsType = _paths.GetType();

                    // Direct property ApplicationDataPath
                    var prop = pathsType.GetProperty("ApplicationDataPath", BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null)
                    {
                        var val = prop.GetValue(_paths) as string;
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }

                    // Some versions expose ApplicationPaths property which itself has ApplicationDataPath
                    var appPathsProp = pathsType.GetProperty("ApplicationPaths", BindingFlags.Public | BindingFlags.Instance);
                    if (appPathsProp != null)
                    {
                        var appPathsObj = appPathsProp.GetValue(_paths);
                        if (appPathsObj != null)
                        {
                            var innerProp = appPathsObj.GetType().GetProperty("ApplicationDataPath", BindingFlags.Public | BindingFlags.Instance);
                            if (innerProp != null)
                            {
                                var val = innerProp.GetValue(appPathsObj) as string;
                                if (!string.IsNullOrWhiteSpace(val)) return val;
                            }
                        }
                    }

                    // Try other common names
                    var altNames = new[] { "ApplicationDataFolder", "AppDataPath", "DataPath" };
                    foreach (var name in altNames)
                    {
                        var p = pathsType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                        if (p != null)
                        {
                            var v = p.GetValue(_paths) as string;
                            if (!string.IsNullOrWhiteSpace(v)) return v;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[EndpointExposer] Reflection attempt to read IServerApplicationPaths failed; falling back to defaults.");
            }

            // Fallbacks by platform / environment
            if (OperatingSystem.IsWindows())
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "jellyfin");
            }

            var env = Environment.GetEnvironmentVariable("JELLYFIN_DATA");
            if (!string.IsNullOrWhiteSpace(env)) return env;

            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdg)) return Path.Combine(xdg, "jellyfin");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share", "jellyfin");
        }

        #endregion
    }
}
