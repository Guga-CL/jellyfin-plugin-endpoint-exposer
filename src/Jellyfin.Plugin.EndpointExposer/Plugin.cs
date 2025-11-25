// src/Jellyfin.Plugin.EndpointExposer/Plugin.cs
using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.EndpointExposer
{
    public class Plugin : BasePlugin
    {
        public Plugin()
        {
            // Keep constructor trivial. Do not access services, files, or network here.
            // If you need to initialize diagnostics, call a safe init method from a startup hook.
        }

        public override void OnApplicationStarted()
        {
            base.OnApplicationStarted();
            // Safe place to initialize diagnostics and other services.
            PluginDiagnostics.InitFromPluginConstructor(); // rename if needed
        }
    }
}
