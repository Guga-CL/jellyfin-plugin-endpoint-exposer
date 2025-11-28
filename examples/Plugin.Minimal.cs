using System;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.EndpointExposer
{
    public class Plugin : BasePlugin<BasePluginConfiguration>
    {
        public override Guid Id => new Guid("f1530767-390f-475e-afa2-6610c933c29e");
        public override string Name => "Endpoint Exposer";
        public override string Description => "Expose a secure endpoint to write to local disc";

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            // No side effects, just a valid ctor
        }
    }
}
