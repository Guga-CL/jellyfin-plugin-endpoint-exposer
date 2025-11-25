using System;
using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.EndpointExposer
{
    public class Plugin : BasePlugin
    {
        public override string Name => "Endpoint Exposer";
        public override string Description => "Expose a secure endpoint to write watchplanner config";

        // Minimal, guaranteed non-throwing constructor
        public Plugin() : base()
        {
            // Intentionally empty — no diagnostics, no IO, no network.
        }
    }
}