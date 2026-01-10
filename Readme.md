# Jellyfin Plugin Endpoint Exposer

## Status

**Work in progress:** The plugin already works in Jellyfin 10.11.x (Windows 11), in the sense that other plugins/mods can use Endpoint Exposer to write to one or more folders.

This was built for personal use, I was not super familiar with C# and jellyfin plugin structure, so there was a lot of trial and error and I didn't had the time/will to properly clean/ refactor the project to remove all the useless, legacy, test stuff.

**Warning:** This was only tested in Windows 11, and I'm not sure if it's secure enough, so use this at your own risk.

---

## Overview

**Objective:** Tries to provide a secure, simple HTTP interface that lets Jellyfin plugins, web mods, etc. read and write files inside a controlled plugin data directory without requiring direct filesystem access.
This replaces the need for external tooling (Caddy + reverse proxy + PS1/Node scripts) to write files to the server.

I like to create stupid/very specific mods, and some of them need to write direct to the server to allow global changes, example custom sections that may update content with actions directly in the browser/section. So this is a little helper for those scenarios.

Maybe there are better solutions now, so do you research before trying this.

---

## How to test locally

- **Build**: `dotnet build -c Release` in the plugin project.
- **Deploy**: copy `Jellyfin.Plugin.EndpointExposer.dll` into `%LOCALAPPDATA%\jellyfin\plugins\Endpoint Exposer`.
- **Configuration**: quick test to see it working, open the plugin settings page, try default settings first, just make sure to create one Exposed Folder, ex:

```
Name: My-Test
Relative Path: MY_FOLDER
```

Click Create Folder then check the default user data plugin configuration folder.
The path should appear at the bottom part of the folder card but if it doesn't work you can manually create it with the same folder name:

`C:\Users\<user_name_here>\AppData\Local\jellyfin\plugins\configurations\Jellyfin.Plugin.EndpointExposer\data\MY_FOLDER`

Click Save

Open the home page or reload open the console and paste this:

```
(async () => {
  // if you changed the folder in "Relative Path" change it here too
  const rel = 'Plugins/EndpointExposer/FolderWrite?folder=MY_FOLDER&name=TEST-CONFIG.json';
  const url = window.ApiClient && typeof window.ApiClient.getUrl === 'function'
    ? window.ApiClient.getUrl(rel)
    : (window.location.origin + '/' + rel.replace(/^\//, ''));
  const payload = JSON.stringify({ test: 'quick-check', ts: Date.now() }, null, 2);

  // Prefer ApiClient.fetch with data if available
  if (window.ApiClient && typeof window.ApiClient.fetch === 'function') {
    try {
      const opts = { url, type: 'PUT', dataType: 'text', data: payload, contentType: 'application/json' };
      const text = await window.ApiClient.fetch(opts);
      console.log('ApiClient.fetch result:', text);
      return;
    } catch (e) {
      console.warn('ApiClient.fetch failed, falling back to native fetch', e);
    }
  }

  // Native fetch fallback
  const r = await fetch(url, {
    method: 'PUT',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: payload
  });
  const text = await r.text().catch(()=>null);
  console.log('native fetch status', r.status, r.statusText, 'body:', text);
})();

```

You should see some information in the console, including a: `PUT 200`, and a: `fetch...`
Access the local folder and check if the json file was created and that there is content written inside it.
If it doesn't work or if you have an odd reverse proxy configuration try to add your external IP/URL in the "Server Base URL" field, and redo the test.

---
