using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using MediaBrowser.Controller.Users;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Server;

namespace Jellyfin.Plugin.EndpointExposer.Controllers
{
    [ApiController]
    [Route("Watchplanner")]
    public class WatchplannerController : ControllerBase
    {
        private readonly ILogger<WatchplannerController> _logger;
        private readonly IUserManager _userManager;
        private readonly IJsonSerializer _json;
        private readonly IServerApplicationPaths _paths;
        private static readonly object _fileLock = new object();

        public WatchplannerController(
            ILogger<WatchplannerController> logger,
            IUserManager userManager,
            IJsonSerializer json,
            IServerApplicationPaths paths)
        {
            _logger = logger;
            _userManager = userManager;
            _json = json;
            _paths = paths;

            _logger.LogInformation("[EndpointExposer] WatchplannerController constructed.");
            _logger.LogInformation("[EndpointExposer] ApplicationDataPath: {Path}", _paths.ApplicationDataPath);
        }

        // GET /Watchplanner/config
        [HttpGet("config")]
        public IActionResult GetConfig()
        {
            try
            {
                var cfgPath = ConfigFilePath;
                EnsureDirectoryExists(Path.GetDirectoryName(cfgPath));

                if (!System.IO.File.Exists(cfgPath))
                {
                    var defaultObj = new Dictionary<string, object>
                    {
                        ["serverWeekGrid"] = new Dictionary<string, object>()
                    };

                    var defaultJson = _json.SerializeToString(defaultObj);
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

        // POST /Watchplanner/config
        [HttpPost("config")]
        public async Task<IActionResult> PostConfig()
        {
            string remoteIp = HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
            try
            {
                // Authenticate user
                var userIdClaim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim))
                {
                    _logger.LogWarning("[EndpointExposer] Unauthorized POST attempt (no user) from {IP}", remoteIp);
                    return Unauthorized(new { success = false, message = "Not authenticated" });
                }

                var user = _userManager.GetUserById(userIdClaim);
                if (user == null || !user.IsAdministrator)
                {
                    _logger.LogWarning("[EndpointExposer] Forbidden POST attempt by user {UserId} from {IP}", userIdClaim, remoteIp);
                    return Forbid();
                }

                // Read request body
                using var sr = new StreamReader(Request.Body);
                var body = await sr.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body))
                {
                    _logger.LogWarning("[EndpointExposer] Empty POST body by user {UserId} from {IP}", userIdClaim, remoteIp);
                    return BadRequest(new { success = false, message = "Empty body" });
                }

                // Deserialize incoming JSON into a dictionary
                Dictionary<string, object> incoming;
                try
                {
                    incoming = _json.DeserializeFromString<Dictionary<string, object>>(body);
                }
                catch (Exception dex)
                {
                    _logger.LogWarning(dex, "[EndpointExposer] Invalid JSON from user {UserId} from {IP}", userIdClaim, remoteIp);
                    return BadRequest(new { success = false, message = "Invalid JSON payload" });
                }

                if (!IsValidSchedule(incoming, out var validationMessage))
                {
                    _logger.LogWarning("[EndpointExposer] Payload validation failed for user {UserId} from {IP}: {Reason}", userIdClaim, remoteIp, validationMessage);
                    return BadRequest(new { success = false, message = $"Invalid payload: {validationMessage}" });
                }

                // Write atomically with a lock
                var cfgPath = ConfigFilePath;
                lock (_fileLock)
                {
                    EnsureDirectoryExists(Path.GetDirectoryName(cfgPath));

                    // Build the object to persist: { "serverWeekGrid": <incoming> }
                    var toWrite = new Dictionary<string, object>
                    {
                        ["serverWeekGrid"] = incoming
                    };

                    var outJson = _json.SerializeToString(toWrite);

                    // Atomic write: write to temp file then replace
                    var tempPath = cfgPath + ".tmp";
                    System.IO.File.WriteAllText(tempPath, outJson);
                    // If target exists, replace; otherwise move
                    if (System.IO.File.Exists(cfgPath))
                    {
                        System.IO.File.Replace(tempPath, cfgPath, null);
                    }
                    else
                    {
                        System.IO.File.Move(tempPath, cfgPath);
                    }
                }

                _logger.LogInformation("[EndpointExposer] Config updated by user {UserId} from {IP}", userIdClaim, remoteIp);
                return Ok(new { success = true, message = "Configuration updated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EndpointExposer] Error writing config");
                return StatusCode(500, new { success = false, message = "Error writing config" });
            }
        }

        #region Helpers

        // Prefer configurations folder, fallback to plugin folder; migrate if needed
        private string GetConfigDirectory()
        {
            var appData = _paths.ApplicationDataPath;

            var cfgDirCandidate = Path.Combine(appData, "plugins", "configurations", "Jellyfin.Plugin.EndpointExposer");
            var pluginDirCandidate = Path.Combine(appData, "plugins", "Jellyfin.Plugin.EndpointExposer", "config");

            var cfgFileCandidate = Path.Combine(cfgDirCandidate, "server-config.json");
            var pluginFileCandidate = Path.Combine(pluginDirCandidate, "server-config.json");

            // If configurations folder exists, use it
            if (Directory.Exists(cfgDirCandidate))
            {
                return cfgDirCandidate;
            }

            // If plugin folder has an existing file but configurations doesn't exist, migrate it
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
                    // fall through to use plugin folder
                }
            }

            // Ensure plugin folder exists
            if (!Directory.Exists(pluginDirCandidate))
            {
                Directory.CreateDirectory(pluginDirCandidate);
            }

            return pluginDirCandidate;
        }

        private string ConfigFilePath => Path.Combine(GetConfigDirectory(), "server-config.json");

        private void EnsureDirectoryExists(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        // Basic validation: incoming must be an object/dictionary with keys that are days or arbitrary strings,
        // and values should be arrays or simple values. This is intentionally permissive but prevents non-object payloads.
        private bool IsValidSchedule(Dictionary<string, object> incoming, out string message)
        {
            message = null;
            if (incoming == null)
            {
                message = "Payload is not an object";
                return false;
            }

            // Accept either a top-level serverWeekGrid object or the direct mapping (days -> items)
            // If the payload looks like { "serverWeekGrid": { ... } } then unwrap it
            if (incoming.Count == 1 && incoming.ContainsKey("serverWeekGrid") && incoming["serverWeekGrid"] is Newtonsoft.Json.Linq.JObject jObj)
            {
                try
                {
                    var dict = jObj.ToObject<Dictionary<string, object>>();
                    incoming.Clear();
                    foreach (var kv in dict) incoming[kv.Key] = kv.Value;
                }
                catch
                {
                    // leave as-is; validation below will catch issues
                }
            }

            // Validate keys and values lightly
            foreach (var kv in incoming)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                {
                    message = "Empty key found";
                    return false;
                }

                // Accept arrays or single values; if it's a JArray or JObject, it's fine
                var val = kv.Value;
                if (val == null) continue;

                var typeName = val.GetType().Name;
                // Allow common JSON types: JArray, JObject, List<>, Dictionary<>, string, long, double, bool
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

        #endregion
    }
}
