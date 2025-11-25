## Jellyfin Plugin Endpoint Exposer

Work in progress, not ready for use

### Information about Jellyfin Plugin Development

#### Objective
**expose a secure HTTP endpoint** that accepts a JSON payload and writes a watchplanner configuration file. 

#### Extra Files Information
The codebase contains defensive diagnostics helpers (`PluginDiagnostics`, `EarlyDiagnostics`) and a plain CLR service (`WatchplannerService`) that implements the endpoint logic.

#### What can break plugin loading:

- Jellyfin loads plugin assemblies and may instantiate plugin types synchronously or run plugin manager tasks that expect constructors and type initializers to be cheap and non‑blocking.

- Any network I/O (or other blocking/throwing work) performed in:
    - static field initializers,
    - tatic constructors,
    - top‑level code, or 
    - the plugin constructor itself

  Can throw or block and cause the plugin manager to fail creating the plugin instance. A 429 is an exception that bubbles up and disables the plugin.