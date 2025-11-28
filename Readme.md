# Jellyfin Plugin Endpoint Exposer

**Work in progress — not ready for production**

---

## Status
**Working baseline:** a minimal `Plugin.cs` skeleton that inherits `BasePlugin<BasePluginConfiguration>` and exposes the required constructor loads cleanly in Jellyfin 10.11.3.

---

## Overview
**Objective:** expose a secure HTTP endpoint that accepts a JSON payload and writes a file to the server disk.  
This replaces the current external tooling (Caddy + reverse proxy + PS1/Node scripts) used to write files to the server.

---

## Why plugins sometimes fail to load
- **Wrong base class** — Plugins must inherit `BasePlugin<TConfig>` (commonly `BasePluginConfiguration`). Using plain `BasePlugin` can break the loader.  
- **Missing constructor signature** — Jellyfin expects constructors that accept `IApplicationPaths` and `IXmlSerializer` (and optionally `ILogger<T>`). A parameterless ctor will not match the loader's reflection calls.  
- **Static initialization** — Static ctors or heavy type initializers can run too early or throw; keep type initializers minimal and side‑effect free.  
- **Constructor side effects** — PluginManager may instantiate plugins synchronously; constructors and initializers should be cheap and non‑blocking.

---

## How to test locally
- **Build**: `dotnet build -c Release` in the plugin project.  
- **Deploy**: copy `Jellyfin.Plugin.EndpointExposer.dll` and `meta.json` into `%LOCALAPPDATA%\jellyfin\plugins\Jellyfin.Plugin.EndpointExposer`.  
- **Verify**: check Jellyfin logs for `Loaded plugin: "Endpoint Exposer"` and for any `Error creating` entries.  
- **Quick reflection test**: run a small PowerShell snippet to `Assembly.LoadFrom(...)`, `GetType(...)`, `Activator.CreateInstance(...)` and read `Name`/`Description`/`Id` to reproduce the loader path outside Jellyfin.

---

## Notes and next steps
- Start from the minimal working `Plugin.cs` that inherits `BasePlugin<BasePluginConfiguration>` and exposes the expected ctor.  
- Add features incrementally (logging, delayed registration, endpoint wiring). After each change, rebuild and redeploy to confirm the plugin still loads.  
- Keep constructors and static initializers side‑effect free; move work to background tasks started after construction if needed.

---
