using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.EndpointExposer.Controllers
{
    [ApiController]
    [Route("endpoint-exposer/watchplanner")]
    public class WatchplannerController : ControllerBase
    {
        private const int MAX_BYTES = 64 * 1024;
        private readonly string _pluginDir;
        private readonly string _configDir;
        private readonly string _secretFile;

        public WatchplannerController()
        {
            _pluginDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) ?? ".", "jellyfin", "plugins", "Jellyfin.Plugin.EndpointExposer");
            _configDir = Path.Combine(_pluginDir, "config");
            _secretFile = Path.Combine(_configDir, "secret.txt");
            System.IO.Directory.CreateDirectory(_configDir);
        }

        [HttpGet("ping")]
        public IActionResult Ping() => Ok(new { status = "ok" });

        [HttpPost("update-config")]
        public async Task<IActionResult> UpdateConfig()
        {
            try
            {
                // Validate Authorization header
                var auth = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    return Unauthorized(new { success = false, message = "Missing token" });

                var token = auth.Substring("Bearer ".Length).Trim();
                if (!IsValidToken(token)) return Unauthorized(new { success = false, message = "Invalid token" });

                // Limit body size
                using var ms = new MemoryStream();
                await Request.Body.CopyToAsync(ms);
                if (ms.Length == 0 || ms.Length > MAX_BYTES) return BadRequest(new { success = false, message = "Invalid size" });

                var json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                try { JsonDocument.Parse(json); } catch { return BadRequest(new { success = false, message = "Invalid JSON" }); }

                var target = Path.Combine(_configDir, "server-config.json");
                var tmp = Path.Combine(_configDir, $"server-config.json.{Guid.NewGuid():N}.tmp");

                await System.IO.File.WriteAllTextAsync(tmp, json);
                try
                {
                    System.IO.File.Replace(tmp, target, null, ignoreMetadataErrors: true);
                }
                catch
                {
                    // Fallback for platforms that don't support File.Replace
                    try
                    {
                        if (System.IO.File.Exists(target))
                        {
                            System.IO.File.Delete(target);
                        }
                    }
                    catch { /* ignore */ }

                    System.IO.File.Move(tmp, target);
                }

                return Ok(new { success = true, message = "Configuration updated" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        private bool IsValidToken(string token)
        {
            try
            {
                if (!System.IO.File.Exists(_secretFile)) return false;
                var expected = System.IO.File.ReadAllText(_secretFile).Trim();
                return !string.IsNullOrEmpty(expected) && CryptographicEquals(expected, token);
            }
            catch { return false; }
        }

        private static bool CryptographicEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            var xa = System.Text.Encoding.UTF8.GetBytes(a);
            var xb = System.Text.Encoding.UTF8.GetBytes(b);
            if (xa.Length != xb.Length) return false;
            var diff = 0;
            for (int i = 0; i < xa.Length; i++) diff |= xa[i] ^ xb[i];
            return diff == 0;
        }
    }
}
