using System;
using System.IO;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EndpointExposer
{
    public class Plugin : BasePlugin
    {
        // Minimal required metadata
        public override string Name => "Endpoint Exposer";
        public override string Description => "Expose a secure endpoint to write watchplanner config";
        public override Guid Id => new Guid("d3b9f8a6-0000-4000-8000-000000000001");

        // Keep constructor trivial and non-throwing.
        // Do not perform network I/O or heavy file I/O here.
        public Plugin()
            : base()
        {
            try
            {
                // Initialize diagnostics in a safe, explicit way (no ModuleInitializer).
                // This will swallow any exceptions internally and write a small marker file if it fails.
                PluginDiagnostics.InitFromPluginConstructor();
            }
            catch
            {
                // absolutely do not rethrow; plugin construction must not throw
            }
        }
    }
}
