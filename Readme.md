## Jellyfin Plugin Endpoint Exposer

Work in progress, not ready for use

### Information about Jellyfin Plugin Development
- Plugin.cs Skeleton loads with no issues, this is the starting point.

#### Objective
**Expose a secure HTTP endpoint** that accepts a JSON payload and writes a file in the local disc.
With this we will replace the need of using: caddy + reverse proxy + ps1/node script to write to the disc.

#### What makes Plugin.cs not load in Jellyfin
- Wrong base class: inherit from BasePlugin without a configuration type. Jellyfin's plugin loader expects most plugins to derive from BasePlugin<TConfig> (usually BasePluginConfiguration). That mismatch can cause PluginManager.CreatePluginInstance to throw a NullReferenceException when it tries to wire up configuration paths and serializers.

- Missing constructor signature: Jellyfin calls the plugin constructor with IApplicationPaths and IXmlSerializer (and sometimes an ILogger). My earlier minimal version only had a parameterless constructor, so the loader couldn't find a matching ctor and failed.

- Static initialization: Adding a static constructor that registered encodings was unnecessary and introduced complexity. Jellyfin doesn't need that for plugin loading, and it can confuse the loader if the static ctor throws or runs too early.

- Jellyfin loads plugin assemblies and may instantiate plugin types synchronously or run plugin manager tasks that expect constructors and type initializers to be cheap and non‑blocking.