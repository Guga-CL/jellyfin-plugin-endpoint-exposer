// PluginPages.cs
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.EndpointExposer
{
    // This file provides the GetPages implementation only.
    // Do not re-declare base classes or interfaces here; Plugin.cs already does that.
    public partial class Plugin
    {
        public IEnumerable<PluginPageInfo> GetPages()
        {
            var prefix = GetType().Namespace;

            yield return new PluginPageInfo
            {
                Name = Name,
                DisplayName = "Endpoint Exposer",
                EnableInMainMenu = true,
                EmbeddedResourcePath = prefix + ".Configuration.settings.html"
            };

            yield return new PluginPageInfo
            {
                Name = $"{Name}.js",
                EmbeddedResourcePath = prefix + ".Configuration.settings.js"
            };


        }
    }
}
// END - PluginPages.cs