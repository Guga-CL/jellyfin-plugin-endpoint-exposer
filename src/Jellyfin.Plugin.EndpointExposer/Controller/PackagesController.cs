// PackagesController.cs
using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EndpointExposer.Controllers
{
    [ApiController]
    // Give this route a higher precedence (lower Order) so it is selected quickly.
    [Route("Packages", Order = -1)]
    public class PackagesController : ControllerBase
    {
        private readonly ILogger<PackagesController> _logger;

        public PackagesController(ILogger<PackagesController> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("{name}")]
        [AllowAnonymous]
        public IActionResult GetPackage(string name, [FromQuery] string assemblyGuid = null)
        {
            try
            {
                static string NormalizeGuid(string g) =>
                    string.IsNullOrWhiteSpace(g) ? string.Empty : g.Replace("-", "").ToLowerInvariant();

                // Canonical GUID from meta.json (keep this in sync)
                var fallbackGuidRaw = "f1530767-390f-475e-afa2-6610c933c29e";

                // Use the fallback GUID; attempt to read assembly GUID attribute if available but do not log on failure
                var myGuidRaw = fallbackGuidRaw;
                try
                {
                    var pluginInstance = Plugin.Instance;
                    if (pluginInstance != null)
                    {
                        var asm = pluginInstance.GetType().Assembly;
                        var attrs = asm.GetCustomAttributes(typeof(System.Runtime.InteropServices.GuidAttribute), false);
                        if (attrs != null && attrs.Length > 0)
                        {
                            var guidAttr = attrs[0] as System.Runtime.InteropServices.GuidAttribute;
                            if (guidAttr != null && !string.IsNullOrWhiteSpace(guidAttr.Value))
                            {
                                myGuidRaw = guidAttr.Value;
                            }
                        }
                    }
                }
                catch
                {
                    // Intentionally silent; fall back to the canonical GUID
                }

                var myGuid = NormalizeGuid(myGuidRaw);
                var requestedGuid = NormalizeGuid(assemblyGuid);

                // If a GUID was provided and it does not match this plugin, return 404
                if (!string.IsNullOrEmpty(requestedGuid) && !string.Equals(requestedGuid, myGuid, StringComparison.OrdinalIgnoreCase))
                {
                    return NotFound();
                }

                var pkg = new
                {
                    name = name,
                    assemblyGuid = assemblyGuid ?? myGuidRaw,
                    version = "1.0.1.0",
                    description = "Endpoint Exposer plugin",
                    downloadUrl = string.Empty
                };

                _logger?.LogInformation("PackagesController: returning package metadata for {Name} assemblyGuid={Guid}", name, pkg.assemblyGuid);
                return Ok(pkg);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "PackagesController: error while returning package metadata for {Name}", name);
                return StatusCode(500);
            }
        }





    }
}
