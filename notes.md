# Note 1 - we don't need script.json / manual copy to pluginpages script.json proof:

but wait, if you don't see config.json in the other plugins repos, then how do they serve theses files to the configuration folder ?
because this is what the configuration folder looks like:

```
PS C:\Users\Gustavo\AppData\Local\jellyfin\plugins\configurations> Get-ChildItem -recurse
    Directory: C:\Users\Gustavo\AppData\Local\jellyfin\plugins\configurations

Mode                 LastWriteTime         Length Name
----                 -------------         ------ ----
d----          26/11/2025    23:30                Jellyfin.Plugin.HomeScreenSections
d----          26/11/2025    23:30                Jellyfin.Plugin.JellyfinEnhanced
d----          05/12/2025    23:41                Jellyfin.Plugin.PluginPages
-a---          15/02/2023    00:39           1153 ConfusedPolarBear.Plugin.IntroSkipper.xml
-a---          28/08/2025    01:21           4290 jellyfin-ani-sync.xml
-a---          02/08/2023    01:06            647 Jellyfin.Plugin.AniDB.xml
-a---          01/10/2022    23:25            596 Jellyfin.Plugin.AniList.xml
-a---          26/07/2022    16:26            203 Jellyfin.Plugin.AnilistSync.xml
-a---          31/10/2025    00:59            460 Jellyfin.Plugin.CollectionSections.xml
-a---          29/11/2025    03:27            683 Jellyfin.Plugin.EndpointExposer.xml
-a---          21/10/2025    00:43            253 Jellyfin.Plugin.Fanart.xml
-a---          22/04/2025    18:29            234 Jellyfin.Plugin.FileTransformation.xml
-a---          28/11/2025    13:44          12646 Jellyfin.Plugin.HomeScreenSections.xml
-a---          05/12/2025    01:17          25083 Jellyfin.Plugin.JavaScriptInjector.xml
-a---          19/11/2025    03:30           5449 Jellyfin.Plugin.JellyfinEnhanced.xml
-a---          11/11/2025    21:10            162 Jellyfin.Plugin.Kitsu.xml
-a---          31/05/2024    00:02            307 Jellyfin.Plugin.MusicBrainz.xml
-a---          02/10/2023    15:57            220 Jellyfin.Plugin.Omdb.xml
-a---          15/07/2022    22:26            311 Jellyfin.Plugin.OpenSubtitles.xml
-a---          19/07/2022    23:57            202 Jellyfin.Plugin.SessionCleaner.xml
-a---          15/07/2022    17:00            278 Jellyfin.Plugin.StudioImages.xml
-a---          19/08/2025    19:34            252 Jellyfin.Plugin.SubtitleExtract.xml
-a---          15/07/2022    16:59            403 Jellyfin.Plugin.Tmdb.xml
-a---          16/10/2023    01:14            221 Jellyfin.Plugin.Tvdb.xml
-a---          25/06/2024    17:47           3023 Jellyfin.Plugin.Webhook.xml
-a---          04/11/2022    23:21           1980 Shokofin.xml
-a---          09/03/2023    00:34           2263 Trakt.xml

    Directory: C:\Users\Gustavo\AppData\Local\jellyfin\plugins\configurations\Jellyfin.Plugin.HomeScreenSections

Mode                 LastWriteTime         Length Name
----                 -------------         ------ ----
-a---          28/11/2025    13:44            410 ModularHomeSettings.json

    Directory: C:\Users\Gustavo\AppData\Local\jellyfin\plugins\configurations\Jellyfin.Plugin.JellyfinEnhanced

Mode                 LastWriteTime         Length Name
----                 -------------         ------ ----
d----          26/11/2025    23:30                4e5df7e2e7ce4afd97a0b4ab0836d697
d----          26/11/2025    23:30                9ac7b5ba28dd44fc8d8666da627cbce7

    Directory: C:\Users\Gustavo\AppData\Local\jellyfin\plugins\configurations\Jellyfin.Plugin.JellyfinEnhanced\4e5df7e2
e7ce4afd97a0b4ab0836d697

Mode                 LastWriteTime         Length Name
----                 -------------         ------ ----
-a---          19/11/2025    01:04            871 settings.json

    Directory: C:\Users\Gustavo\AppData\Local\jellyfin\plugins\configurations\Jellyfin.Plugin.JellyfinEnhanced\9ac7b5ba
28dd44fc8d8666da627cbce7

Mode                 LastWriteTime         Length Name
----                 -------------         ------ ----
-a---          18/11/2025    16:22            872 settings.json

    Directory: C:\Users\Gustavo\AppData\Local\jellyfin\plugins\configurations\Jellyfin.Plugin.PluginPages

Mode                 LastWriteTime         Length Name
----                 -------------         ------ ----
-a---          05/12/2025    23:41            215 config.json

```

notice how most plugin have only xml files?

Example: Jellyfin.Plugin.JavaScriptInjector
That plugin also have a plugin settings page but only a xml file and no "page" in pluginpages config.json
Actually if you open PluginPages config.json it's mostly empty, only 1 plugin is using that file.
So I think this proves that you don't need to have a config.json in your repo/project nor copy stuff to pluginpages config.json

# Note 2 - explaining the csproj and usings issue and the fix

**Problem Observed**  
There was a mismatch between the Plugin.cs and csproj files.

**What We Changed in Plugin.cs**  
Updated the code to align with the correct references.

**What We Changed in csproj**  
Added the ItemGroup snippet with the necessary package references. The csproj now uses Jellyfin package references, while the code uses MediaBrowser usings.

**Build Steps Run**  
The build steps were executed to ensure the changes were applied correctly.

**One-Line Reminder**  
Ensure the csproj and Plugin.cs are aligned with the correct references.

### Fix summary for Endpoint Exposer

**Problem observed**  
CS0234 / CS0246 errors because code `using` namespaces and the assemblies the project compiles against were out of sync.

---

### What we changed in Plugin.cs

- Replaced ambiguous/useless usings with the MediaBrowser pattern used by the working plugin:

```csharp
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
```

- Matched the working plugin's DI constructor shape and stored `IApplicationPaths` and `ILogger<Plugin>` in fields:

```csharp
public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger, IServiceProvider serviceProvider)
    : base(applicationPaths, xmlSerializer)
{
    Instance = this;
    _appPaths = applicationPaths;
    _logger = logger;
    ServiceProvider = serviceProvider;
}
```

- Added `Instance`, `_appPaths`, `_logger`, and `ServiceProvider` members to mirror the working pattern.

---

### What we changed in the csproj

- Ensured `RootNamespace` and `AssemblyName` are `Jellyfin.Plugin.EndpointExposer`.
- Added embedded resource logical names that match `GetType().Namespace`:

```xml
<EmbeddedResource Include="Configuration\settings.html">
  <LogicalName>$(RootNamespace).Configuration.settings.html</LogicalName>
</EmbeddedResource>
<EmbeddedResource Include="Configuration\settings.js">
  <LogicalName>$(RootNamespace).Configuration.settings.js</LogicalName>
</EmbeddedResource>
```

- Kept the three Jellyfin package references that the project uses:

```xml
<ItemGroup>
  <PackageReference Include="Jellyfin.Common" Version="10.11.3" />
  <PackageReference Include="Jellyfin.Controller" Version="10.11.3" />
  <PackageReference Include="Jellyfin.Model" Version="10.11.3" />
</ItemGroup>
```

**Note about the apparent mismatch**  
The project file uses the three `Jellyfin.*` package references above while the source files use `MediaBrowser.*` `using` directives. That is intentional for this repository: the code uses the `MediaBrowser.*` namespaces at compile time while the project restores the `Jellyfin.*` packages. We aligned `Plugin.cs` to the working plugin pattern so the compiler resolves the expected types in your environment.

---

### Build housekeeping performed

- Cleared NuGet locals and removed `bin`/`obj`.
- Ran `dotnet restore` and `dotnet build` to verify the project compiles.

---

**One‑line reminder to keep in your notes**  
Kept `Plugin.cs` aligned with the working JavaScript Injector pattern (MediaBrowser usings and DI constructor), set `RootNamespace` to `Jellyfin.Plugin.EndpointExposer`, added the three `Jellyfin.*` PackageReference entries to the csproj, fixed embedded resource logical names, cleared caches, then restored and rebuilt.

# Note 3

## plugin page dashboard experienced a noticeable delay to fully load the page

### Summary

**Symptom:** Plugin dashboard experienced a noticeable delay (~4–5s) when opening the plugin page; server logs earlier showed route collisions. The settings page still needs separate attention.

### Root cause

The Jellyfin SPA probes `GET /Packages/{name}` early. If that probe is slow or missing, the SPA waits while it falls back to other lookups, causing the UI delay. Adding a plugin route that matched the core `/Packages` route created a routing collision and produced `AmbiguousMatchException`.

### Fix applied

- **Handled the SPA probe quickly** by adding a minimal package endpoint that returns a small JSON package object immediately.
- **Eliminated route collisions** by moving plugin endpoints to a plugin‑scoped path (or by setting route `Order` when intentionally overriding core) so the router no longer sees multiple matching endpoints.
- **Added PluginPages compatibility endpoints** so the SPA can resolve the plugin page URL deterministically.
- **Served minimal embedded settings HTML and defensive settings JS** (embedded as resources with explicit logical names) so the configuration page can be loaded reliably.

### Why this resolves the delay

The SPA now receives a fast `200` for the package probe and does not block on fallback lookups. Namespacing or ordering routes prevents ambiguous matches in the router, removing server exceptions and long waits.

### Quick checklist to avoid this in future

- Ensure a **fast `/Packages/{name}` response** exists for plugins the SPA probes, or intentionally override it with a plugin endpoint using `Order` if you need to.
- **Avoid registering top‑level routes** that collide with Jellyfin core unless you explicitly control precedence.
- Provide `/PluginPages/ByName` compatibility returning the plugin page `Url`.
- Embed `settings.html` and `settings.js` with logical names matching your controller, and make the JS defensive about DOM lookups.
- Rebuild, restart Jellyfin, and verify with DevTools Network that `/Packages/...` returns quickly and no ambiguous routing errors appear.

# Note 4

## Settings Page - Cause & Fix for the console error "Uncaught (in promise) TypeError: can't access property "classList", v is undefined"

**What happened**  
The Jellyfin SPA expects a very specific page wrapper when it injects plugin fragments. The original fragment either lacked that exact wrapper structure or the script ran before the SPA had injected the wrapper, so the code attempted to access an element that did not exist. The runtime error `can't access property "classList", v is undefined` means the script tried to use `.classList` on `undefined` (the DOM node was not found).

**Root reasons**

- The fragment did not include the exact wrapper classes/attributes the SPA looks for (`.page.type-interior.pluginConfigurationPage` and `.page-content`), so the SPA's DOM plumbing didn't attach as expected.
- The script executed at the wrong time (before the wrapper existed) and did not defensively check for `null` before touching `.classList`.
- Potential duplicate DOM-manipulating code (inline script + settings.js) can race and cause intermittent failures.

---

### How to fix it

**Concrete fixes applied**

- Replaced the fragment with the exact wrapper the SPA expects: a `div` with classes `page type-interior pluginConfigurationPage` containing a `div.page-content`. This ensures the SPA finds and uses the fragment correctly.
- Added a defensive inline script that **always** checks for the element before using `.classList` and retries safely if needed. The script uses a small helper like `safeAddClass(el, cls)` and a short retry/observer loop so it never calls `.classList` on `undefined`.
- Removed the duplicate `settings.js` from the project to avoid conflicting DOM access and timing races, a separated settings.js is not necessary.

**Why this works**

- Providing the exact wrapper guarantees the SPA will treat the fragment as a configuration page and inject it into the expected place.
- Defensive checks and delayed/retry logic prevent attempts to mutate DOM nodes that aren't present yet, eliminating the `v is undefined` error.
- Removing duplicate scripts removes competing mutations and timing races.

---

### Minimal defensive pattern

**Safe access pattern**

```js
const el = document.querySelector(
  ".page.type-interior.pluginConfigurationPage"
);
if (!el) return; // never touch classList if element missing
el.classList.add("endpoint-exposer-loaded");
```

**Retry/observer pattern (short)**

```js
function waitFor(selector, cb, attempts = 10, delay = 100) {
  const el = document.querySelector(selector);
  if (el) return cb(el);
  if (attempts-- > 0)
    setTimeout(() => waitFor(selector, cb, attempts, delay), delay);
}
waitFor(".page.type-interior.pluginConfigurationPage", (el) =>
  el.classList.add("endpoint-exposer-loaded")
);
```

---

### Quick checklist to avoid this again

- **Always** include the exact wrapper classes/IDs the SPA expects.
- **Always** guard DOM access with `if (!el) return` or use a retry/observer.
- Keep DOM-manipulating code in **one** place (either inline defensive script or a single external file).
- Test with browser cache disabled and watch the console for `null` access errors after deploy.

---

# Note 5

## meta.json doesn't need to be included in jellyfin plugin projects nor deployed:

- shutdown the server (all normal build processes I usually do, etc.)

- I removed meta.json from the deployment process, so no meta.json gets copied to "C:\Users\Gustavo\AppData\Local\jellyfin\plugins\Endpoint Exposer_1.0.0.1"
  or just remove the file before restarting the server.

- restart the jellyfin server without the meta.json file, jellyfin automatically creates it:

```
{
  "category": "",
  "changelog": "",
  "description": "Expose a secure endpoint to write to disk",
  "guid": "f1530767-390f-475e-afa2-6610c933c29e",
  "name": "Endpoint Exposer",
  "overview": "",
  "owner": "",
  "targetAbi": "10.11.4.0",
  "timestamp": "0001-01-01T00:00:00.0000000Z",
  "version": "1.0.0.0",
  "status": "Active",
  "autoUpdate": false,
  "assemblies": []
}
```

server load is the same as before:

```
--- Jellyfin Log file: log_20251214.log (LastWrite: 12/14/2025 13:41:19) ---

[Line:32-34] [2025-12-14 13:41:08.735 -03:00]
[INF] [11] Emby.Server.Implementations.AppBase.BaseConfigurationManager: Setting cache path: "D:\Temp\Jellyfin\Cache"
[INF] [11] Emby.Server.Implementations.Plugins.PluginManager: Registering whitelisted assemblies for plugin ""Endpoint Exposer""...
[INF] [11] Emby.Server.Implementations.ApplicationHost: Loading assemblies

[Line:38-40] [2025-12-14 13:41:08.779 -03:00]
[INF] [11] Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly "Jellyfin.Plugin.CollectionSections, Version=2.3.4.0, Culture=neutral, PublicKeyToken=null" from "C:\Users\Gustavo\AppData\Local\jellyfin\plugins\Collection Sections_2.3.4.0\Jellyfin.Plugin.CollectionSections.dll"
[INF] [11] Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly "Jellyfin.Plugin.EndpointExposer, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" from "C:\Users\Gustavo\AppData\Local\jellyfin\plugins\Endpoint Exposer_1.0.0.1\Jellyfin.Plugin.EndpointExposer.dll"
[INF] [11] Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly "Jellyfin.Plugin.Fanart, Version=12.0.0.0, Culture=neutral, PublicKeyToken=null" from "C:\Users\Gustavo\AppData\Local\jellyfin\plugins\Fanart_12.0.0.0\Jellyfin.Plugin.Fanart.dll"

[Line:85-89] [2025-12-14 13:41:09.595 -03:00]
[INF] [11] Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: "Collection Sections" "2.3.4.0"
[INF] [11] Jellyfin.Plugin.EndpointExposer.Plugin: EndpointExposer: RegisterServices using ServerBaseUrl="http://127.0.0.1:8096"
[INF] [11] Jellyfin.Plugin.EndpointExposer.Plugin: EndpointExposer: registered services into host IServiceCollection at runtime.
[INF] [11] Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: "Endpoint Exposer" "1.0.1.0"
[INF] [11] Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: "Fanart" "12.0.0.0"

[Line:173-176] [2025-12-14 13:41:13.629 -03:00]
[INF] [22] Emby.Server.Implementations.ScheduledTasks.TaskManager: "HomeScreenSections Startup" Completed after 0 minute(s) and 0 seconds
[INF] [11] Jellyfin.Plugin.EndpointExposer.Controllers.PackagesController: PackagesController: returning package metadata for "Endpoint Exposer" assemblyGuid="f1530767390f475eafa26610c933c29e"
[INF] [19] Jellyfin.Plugin.EndpointExposer.Controller.EndpointExposerController: EndpointExposerController: using fallback service instances constructed from Plugin.Instance. EffectiveServerBase="http://127.0.0.1:8096"
[INF] [19] Emby.Server.Implementations.ScheduledTasks.TaskManager: "Update Plugins" Completed after 0 minute(s) and 6 seconds
```

so this confirms the meta.json is not really relevant, that's why we don't see meta.json in any github repo from other jellyfin plugins, it's 100% unnecessary to have that file, jellyfin already creates the file automatically probably based on information in the project files.

# Note 6

## **Issue:** Jellyfin webcomponents threw `TypeError: this.labelElement is undefined` because labels were not in the structure the components expect;

Copying Webhook’s exact markup (input `label` attributes and `<label class="checkboxContainer">` wrappers) fixed initialization, removed console errors, and restored interactive UI.

### Summary of the problem

Your page used Jellyfin custom elements (`is="emby-input"`, `is="emby-checkbox"`) but the DOM structure and timing didn’t match what those webcomponents expect. During upgrade the components tried to access a `labelElement` that didn’t exist, which threw `TypeError: can't access property "htmlFor", this.labelElement is undefined` and later caused other initialization failures (`classList` on `null`). The checkbox rendered visually but had no event wiring, so clicks did nothing.

---

### Root causes

- **Label lookup mismatch** — Jellyfin webcomponents expect a specific label pattern (e.g., a sibling `<label for="id">` or a `<label class="checkboxContainer">` wrapper). If they can’t find it, `this.labelElement` stays `undefined` and `attachedCallback` throws.
- **Upgrade timing** — the component upgrade can run before the label is discoverable if markup order or lookup strategy differs.
- **Markup differences from Webhook** — small differences (label placement, missing `label` attribute, wrapper element type) break the component’s internal wiring.
- **Resulting symptoms** — console `TypeError` stack traces, checkboxes stuck visually checked, no click/keyboard interaction.

---

### Why copying Webhook’s structure fixed it

- **Exact markup expectations met** — Webhook uses the patterns the webcomponents were written for: inputs with `label="..."` and explicit `<label for="...">` or checkbox inputs wrapped in `<label class="checkboxContainer">`. That guarantees the component finds `labelElement` during `attachedCallback`.
- **Consistent DOM order** — Webhook’s layout places labels where the upgrade code searches, avoiding timing issues.
- **No defensive hacks required** — once the expected structure is used, the webcomponents initialize cleanly, wire events, and update visuals correctly.

---

### Practical checklist to avoid this again

- **Use Webhook-style patterns** for Jellyfin UI components:
  - For inputs: include `label="..."` and an explicit `<label id="label-Id" for="Id">` sibling (or follow the exact Webhook layout).
  - For checkboxes: wrap them in a `<label class="checkboxContainer">` with the input inside.
- **Avoid duplicate IDs** anywhere on the page.
- **Verify upgrade state** in DevTools:
  ```js
  const el = document.getElementById("AllowNonAdmin");
  console.log(el.labelElement, el.labels, el.checked);
  ```
- **Test in incognito** to avoid stale bundles and cache.
- **If you must fallback**, use native inputs (no `is="emby-*"`) or add a small defensive shim that sets `el.labelElement` and re-runs initialization — but treat that as temporary.

---

# Note 7

### Jellyfin SPA injection race condition

A concise reference for mod authors and integrators: when a Jellyfin extension injects UI into the home screen, the app’s SPA navigation and DOM swaps can cause the injected UI to be inserted but **not** initialized. The result is an empty placeholder (`#your-root`) that only populates after a delayed retry or manual refresh. This note explains why it happens and how to fix it reliably.

---

### Problem summary

- **Symptom**: Section wrapper and root element appear in the DOM, but the mod’s UI code does not run immediately after in‑app navigation (logo click, header button, history API), leaving the root empty.
- **Observed pattern**: Works on full page load, fails intermittently on hash/history navigation or DOM swaps. Eventually recovers after retries or timeouts.
- **Impact**: Poor UX, confusing blank sections, and hard-to-reproduce bugs across different navigation flows.

---

### Root causes

- **Timing race** between Jellyfin’s DOM updates and the injector’s mount attempt. The injector may run too early or be throttled and miss the moment the view is ready.
- **Throttling logic** that prevents repeated attempts can block a fresh mount immediately after navigation.
- **Assets loaded on demand** (scripts/CSS) add latency when the injector tries to mount and must wait for network loads.
- **Multiple navigation entry points** (history.pushState, hash anchors, header buttons) have different timing semantics, so a single detection method misses some flows.

---

### Practical fixes applied

Use these patterns in any Jellyfin mod injector to make injection robust.

**1. Expose a promise-based mount API in the UI module**

```js
// UI module
function mount(root) {
  buildShell(root);
  if (hasCachedData()) {
    renderSync();
    return Promise.resolve(true);
  }
  return loadDataAndRender(); // returns a Promise
}
```

**Why**: Injector can `await` the mount and know when the DOM is populated.

**2. Preload critical assets early in the bootstrap**

```js
// bootstrap
ensureClientScripts().catch((e) => console.warn("preload failed", e));
```

**Why**: Avoids network waits during the navigation path.

**3. Make throttle logic tolerant when root is missing**

```js
// throttle guard
if (!document.getElementById("your-root")) {
  delete STATE.lastAttempt[routeKey]; // allow immediate retry
} else if (Date.now() - last < COOLDOWN) {
  return; // throttle only when root exists
}
```

**Why**: Allows immediate retry when the UI is absent.

**4. Hook multiple navigation signals and clear cooldowns**

- Wrap `history.pushState` and `history.replaceState` to emit a custom event.
- Listen for `popstate`, `hashchange`, and targeted anchor clicks (logo/home).
- On navigation, clear per-route cooldown and trigger injection.

**5. Fast-visibility trigger**

- Observe the injected wrapper; when it becomes visible, call `mount()` immediately (small delay to let DOM settle).  
  **Why**: Handles cases where wrapper is inserted by Jellyfin but not yet visible or ready.

**6. Short poll with bounded retries**

- Poll the root for children with a short interval (e.g., 80ms) and limited attempts (~10–15) rather than long timeouts.

---

### Quick troubleshooting checklist

- **Console checks**

  - Confirm bootstrap logs run on navigation (enable debug flag).
  - Verify `window.__your_state.loadedScripts` lists client files.
  - Check `document.getElementById('your-root').childElementCount` after navigation.

- **If root is empty**

  - Ensure `mount()` is exposed and returns a Promise.
  - Confirm bootstrap cleared per-route cooldown on navigation.
  - Verify scripts/CSS are reachable and preloaded.
  - Add a fast-visibility observer to trigger mount when wrapper becomes visible.

- **Useful quick commands**

```js
// in console
window.__your_state && console.log(Object.keys(window.__your_state));
console.log("root exists", !!document.getElementById("your-root"));
console.log(
  "root children",
  document.getElementById("your-root") &&
    document.getElementById("your-root").childElementCount
);
```

---

### Best practices for mod authors

- **Design mount as a promise** so external injectors can await readiness.
- **Preload critical JS/CSS** in the bootstrap rather than on first interaction.
- **Detect multiple navigation flows**: history hooks, hashchange, anchor clicks, and header buttons.
- **Keep injector idempotent**: safe to call repeatedly without duplicating DOM.
- **Use short, bounded polling and a visibility observer** instead of long timeouts.
- **Gate verbose logs behind a debug flag** so users can enable diagnostics when needed.

---
