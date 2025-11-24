using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EndpointExposer
{
    public class Plugin : BasePlugin
    {
        private readonly ILogger<Plugin> _logger;

        public Plugin(ILogger<Plugin> logger)
        {
            _logger = logger;
            _logger.LogInformation("[EndpointExposer] Plugin constructor running...");
        }

        public override string Name => "Endpoint Exposer";
        public override string Description => "Exposes watchplanner endpoints using Jellyfin auth.";
    }
}
