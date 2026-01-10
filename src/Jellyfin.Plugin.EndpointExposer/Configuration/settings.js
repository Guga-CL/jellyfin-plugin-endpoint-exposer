// Configuration/settings.js
var EndpointExposerConfigurationPage = {
    pluginUniqueId: "f1530767-390f-475e-afa2-6610c933c29e",
    _currentConfig: null,

    loadConfiguration: function (view) {
        console.debug('EE: loadConfiguration start', { ApiClient: typeof ApiClient, Dashboard: typeof Dashboard });
        Dashboard.showLoadingMsg();

        ApiClient.getPluginConfiguration(EndpointExposerConfigurationPage.pluginUniqueId)
            .then(function (config) {
                // Always keep a non-null current config
                EndpointExposerConfigurationPage._currentConfig = config || {};

                // Populate basic inputs (defensive)
                const get = id => view.querySelector('#' + id);
                try {
                    if (get('ServerBaseUrl')) get('ServerBaseUrl').value = (config && config.ServerBaseUrl) || '';
                    if (get('ApiKey')) get('ApiKey').value = (config && config.ApiKey) || '';
                    if (get('OutputDirectory')) get('OutputDirectory').value = (config && config.OutputDirectory) || '';
                    if (get('MaxPayloadBytes')) get('MaxPayloadBytes').value = (config && config.MaxPayloadBytes) || 0;
                    if (get('MaxBackups')) get('MaxBackups').value = (config && config.MaxBackups) || 0;
                } catch (e) {
                    console.warn('EE: input population failed', e);
                }

                // Ensure raw configuration is always visible for debugging
                try {
                    const rawEl = view.querySelector('#ee-raw');
                    if (rawEl) rawEl.textContent = JSON.stringify(config || {}, null, 2);
                } catch (e) { /* ignore */ }

                // Wait for the page-scoped factory to be available before rendering UI that depends on it.
                (function waitForFactoryAndRender(cfg, attempts = 20, delayMs = 100) {
                    const ready = !!(window.EndpointExposerConfigurationPage && typeof window.EndpointExposerConfigurationPage.createFolderCard === 'function');
                    if (ready) {
                        try {
                            if (typeof EndpointExposerConfigurationPage.loadConfigurationToUi === 'function') {
                                EndpointExposerConfigurationPage.loadConfigurationToUi(cfg);
                            } else {
                                // Fallback: call global loader if present
                                if (typeof page !== 'undefined' && typeof page.loadConfigurationToUi === 'function') page.loadConfigurationToUi(cfg);
                            }
                        } catch (e) {
                            console.warn('EE: loadConfigurationToUi failed', e);
                        }

                        // Trigger base path fetch and refresh previews if available
                        try {
                            const fetchFn = (window.EndpointExposerConfigurationPage && window.EndpointExposerConfigurationPage.fetchPluginBasePathOnce) || (typeof page !== 'undefined' && page.fetchPluginBasePathOnce);
                            if (typeof fetchFn === 'function') {
                                Promise.resolve(fetchFn()).then(() => {
                                    document.querySelectorAll('.ee-folder').forEach(card => {
                                        const rel = card.querySelector('input[type="text"].ee-input:nth-of-type(2)') || card.querySelector('input[type="text"].ee-input');
                                        if (rel) rel.dispatchEvent(new Event('input', { bubbles: true }));
                                    });
                                }).catch(() => { });
                            }
                        } catch (e) { /* ignore */ }

                        Dashboard.hideLoadingMsg();
                        return;
                    }

                    if (attempts <= 0) {
                        console.debug('EE: factory not available after retries; rendering minimal UI');
                        try {
                            if (typeof EndpointExposerConfigurationPage.loadConfigurationToUi === 'function') {
                                EndpointExposerConfigurationPage.loadConfigurationToUi(cfg);
                            }
                        } catch (e) { /* ignore */ }
                        Dashboard.hideLoadingMsg();
                        return;
                    }

                    setTimeout(() => waitForFactoryAndRender(cfg, attempts - 1, delayMs), delayMs);
                })(config || {});
            })
            .catch(function (err) {
                Dashboard.hideLoadingMsg();
                console.error('Failed to load plugin configuration', err);
                // Ensure raw area shows error for debugging
                try {
                    const rawEl = view.querySelector('#ee-raw');
                    if (rawEl) rawEl.textContent = 'Failed to load configuration: ' + (err && err.message ? err.message : String(err));
                } catch (e) { /* ignore */ }
            });
    },

    saveConfiguration: function (view) {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(EndpointExposerConfigurationPage.pluginUniqueId)
            .then(function (config) {
                const get = id => view.querySelector('#' + id);
                config.ServerBaseUrl = (get('ServerBaseUrl') && get('ServerBaseUrl').value) || null;
                config.ApiKey = (get('ApiKey') && get('ApiKey').value) || null;
                config.OutputDirectory = (get('OutputDirectory') && get('OutputDirectory').value) || null;
                const maxPayloadVal = (get('MaxPayloadBytes') && get('MaxPayloadBytes').value) || '0';
                config.MaxPayloadBytes = parseInt(maxPayloadVal, 10) || 0;
                const maxBackupsVal = (get('MaxBackups') && get('MaxBackups').value) || '0';
                config.MaxBackups = parseInt(maxBackupsVal, 10) || 0;

                try {
                    if (typeof EndpointExposerConfigurationPage.gatherUiToConfiguration === 'function') {
                        config = EndpointExposerConfigurationPage.gatherUiToConfiguration(config);
                    } else {
                        config.RegisteredFiles = config.RegisteredFiles || [];
                        config.ExposedFolders = config.ExposedFolders || [];
                    }
                } catch (e) {
                    Dashboard.hideLoadingMsg();
                    const statusEl = view.querySelector('#ee-status');
                    if (statusEl) statusEl.textContent = 'Validation error: ' + e.message;
                    return;
                }

                ApiClient.updatePluginConfiguration(EndpointExposerConfigurationPage.pluginUniqueId, config)
                    .then(function (result) {
                        Dashboard.processPluginConfigurationUpdateResult(result);
                        EndpointExposerConfigurationPage._currentConfig = config;
                        if (typeof EndpointExposerConfigurationPage.loadConfigurationToUi === 'function') {
                            try { EndpointExposerConfigurationPage.loadConfigurationToUi(config); } catch (e) { }
                        }
                    })
                    .catch(function (err) { console.error('Failed to save plugin configuration', err); })
                    .finally(function () { Dashboard.hideLoadingMsg(); });
            })
            .catch(function (err) {
                Dashboard.hideLoadingMsg();
                console.error('Failed to fetch plugin configuration before save', err);
            });
    },

    fetchRawConfiguration: function (view) {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(EndpointExposerConfigurationPage.pluginUniqueId)
            .then(function (config) {
                const rawEl = view.querySelector('#ee-raw');
                if (rawEl) rawEl.textContent = JSON.stringify(config, null, 2);
                Dashboard.hideLoadingMsg();
            })
            .catch(function (err) {
                Dashboard.hideLoadingMsg();
                console.error('Failed to fetch raw configuration', err);
            });
    }
};

function getAccessToken() {
    try {
        const creds = localStorage.getItem('jellyfin_credentials');
        if (!creds) return null;
        const parsed = JSON.parse(creds);
        const servers = parsed.Servers || [];
        if (servers.length && servers[0].AccessToken) return servers[0].AccessToken;
    } catch (e) { console.warn('Could not parse jellyfin_credentials', e); }
    return null;
}

//#region IIFE
(function (page) {
    const folderTokenRegex = /^[a-z0-9_-]+$/i;
    let _cachedPluginBasePath = null;

    // helper: extract resolvedPath from various ApiClient.ajax return shapes
    async function extractResolvedPathFromAjaxResult(result) {
        try {
            // If it's a Fetch Response (has json() function), try to parse it
            if (result && typeof result === 'object' && typeof result.json === 'function') {
                try {
                    const parsed = await result.json();
                    if (parsed && (parsed.resolvedPath || parsed.ResolvedPath)) return parsed.resolvedPath || parsed.ResolvedPath;
                } catch (e) {
                    // If json() fails, try text() and parse
                    try {
                        const txt = await result.text();
                        const parsed = JSON.parse(txt);
                        if (parsed && (parsed.resolvedPath || parsed.ResolvedPath)) return parsed.resolvedPath || parsed.ResolvedPath;
                    } catch (e2) {
                        console.debug('extractResolvedPathFromAjaxResult: failed to parse Fetch Response body', e, e2);
                    }
                }
            }

            // Common case: already-parsed JSON object
            if (result && typeof result === 'object') {
                if (result.resolvedPath || result.ResolvedPath) return result.resolvedPath || result.ResolvedPath;

                // Some clients wrap the parsed body under 'response' or 'data'
                if (result.response && typeof result.response === 'object') {
                    if (result.response.resolvedPath || result.response.ResolvedPath) return result.response.resolvedPath || result.response.ResolvedPath;
                    // If response is a Fetch-like object, try its json()
                    if (typeof result.response.json === 'function') {
                        try {
                            const parsed = await result.response.json();
                            if (parsed && (parsed.resolvedPath || parsed.ResolvedPath)) return parsed.resolvedPath || parsed.ResolvedPath;
                        } catch (e) { /* ignore */ }
                    }
                }

                if (result.data && (result.data.resolvedPath || result.data.ResolvedPath)) return result.data.resolvedPath || result.data.ResolvedPath;
            }

            // If result is a string, try to parse JSON
            if (typeof result === 'string') {
                try {
                    const parsed = JSON.parse(result);
                    if (parsed && (parsed.resolvedPath || parsed.ResolvedPath)) return parsed.resolvedPath || parsed.ResolvedPath;
                } catch (e) { /* ignore */ }
            }

            return null;
        } catch (e) {
            console.debug('extractResolvedPathFromAjaxResult error', e, result);
            return null;
        }
    }

    // Prefill ServerBaseUrl from jellyfin_credentials for better UX (client-side only)
    (function prefillServerBaseFromLocalStorage() {
        try {
            const raw = localStorage.getItem('jellyfin_credentials');
            if (!raw) return;
            const parsed = JSON.parse(raw);
            const servers = parsed?.Servers;
            if (!Array.isArray(servers) || !servers.length) return;
            const s = servers[0];
            // Prefer ManualAddress then LocalAddress
            const candidate = (s?.ManualAddress || s?.LocalAddress || '').trim();
            if (!candidate) return;

            // Only set the input if the user hasn't already configured a ServerBaseUrl
            const input = document.querySelector('#ServerBaseUrl');
            if (input && !input.value) {
                // sanitize: remove trailing slash
                input.value = candidate.replace(/\/+$/, '');
            }

            // Also set ApiClient._serverInfo fallback if ApiClient supports it (optional)
            if (window.ApiClient && ApiClient._serverInfo && !ApiClient._serverInfo.BaseUrl) {
                ApiClient._serverInfo.BaseUrl = candidate.replace(/\/+$/, '');
            }
        } catch (e) {
            console.debug('prefillServerBaseFromLocalStorage: failed', e);
        }
    })();

    // Client helper: prefer jellyfin_credentials ManualAddress/LocalAddress, fallback to window.location
    function getClientBaseUrl() {
        try {
            const raw = localStorage.getItem('jellyfin_credentials');
            if (raw) {
                const parsed = JSON.parse(raw);
                const servers = parsed?.Servers;
                if (Array.isArray(servers) && servers.length) {
                    const s = servers[0];
                    const candidate = (s?.ManualAddress || s?.LocalAddress || '').trim();
                    if (candidate) return normalizeBase(candidate);
                }
            }
        } catch (e) {
            console.debug('getClientBaseUrl: failed to read jellyfin_credentials', e);
        }

        // Fallback to the page origin (preserves scheme + host + port)
        if (window && window.location && window.location.origin) return normalizeBase(window.location.origin);

        return 'http://<host>';
    }

    function normalizeBase(u) {
        try {
            // If it's already a full URL, new URL() will parse it and preserve any path (virtual dir)
            const parsed = new URL(u, window.location.origin);
            // Remove trailing slash
            return parsed.origin + parsed.pathname.replace(/\/+$/, '');
        } catch (e) {
            // If parsing fails, fallback to raw trimmed string without trailing slash
            return (u || '').replace(/\/+$/, '');
        }
    }

    // Try ResolvePath with retries (client-side resilience)
    async function tryResolvePath(relative, attempts = 3, delayMs = 350) {
        const token = getAccessToken() || (ApiClient && ApiClient._serverInfo && ApiClient._serverInfo.AccessToken) || null;
        const url = ApiClient.getUrl('Plugins/EndpointExposer/ResolvePath') + '?relative=' + encodeURIComponent(relative);

        for (let i = 0; i < attempts; i++) {
            try {
                const result = await ApiClient.ajax({ url: url, type: 'GET', headers: token ? { 'X-Emby-Token': token } : {} });
                const resolved = await extractResolvedPathFromAjaxResult(result);
                if (resolved) return resolved;
                // If no resolved path, treat as transient and retry
            } catch (e) {
                console.debug('tryResolvePath attempt failed', i + 1, e);
            }
            // small backoff
            await new Promise(r => setTimeout(r, delayMs * (i + 1)));
        }
        return null;
    }

    const fetchPluginBasePathOnce = async function fetchPluginBasePathOnce() {
        if (_cachedPluginBasePath !== null) return _cachedPluginBasePath;
        try {
            const token = getAccessToken();
            const url = ApiClient.getUrl('Plugins/EndpointExposer/DataBasePath');
            const res = await ApiClient.ajax({ url: url, type: 'GET', headers: token ? { 'X-Emby-Token': token } : {} });
            _cachedPluginBasePath = (res && (res.basePath || res.basepath)) ? (res.basePath || res.basepath) : null;
        } catch (e) {
            _cachedPluginBasePath = null;
            console.debug('fetchPluginBasePathOnce failed', e);
        }
        return _cachedPluginBasePath;
    };

    // Attach to page object for external access
    page.fetchPluginBasePathOnce = fetchPluginBasePathOnce;

    function createFolderCard(entry) {
        const card = document.createElement('div');
        card.className = 'ee-folder';

        // Name Row (with Remove button on the right)
        const nameRow = document.createElement('div');
        nameRow.className = 'ee-row';
        nameRow.style.justifyContent = 'space-between';
        nameRow.style.alignItems = 'center';

        const nameLeftDiv = document.createElement('div');
        nameLeftDiv.style.display = 'flex';
        nameLeftDiv.style.gap = '12px';
        nameLeftDiv.style.flex = '1';
        nameLeftDiv.style.alignItems = 'center';

        const nameLabel = document.createElement('label');
        nameLabel.textContent = 'Name';
        const nameInput = document.createElement('input');
        nameInput.type = 'text';
        nameInput.className = 'ee-input';
        nameInput.value = entry?.Name ?? '';
        nameInput.placeholder = 'token (a-z0-9_-)';
        nameInput.style.flex = '1';

        const removeBtn = document.createElement('button');
        removeBtn.className = 'btn';
        removeBtn.type = 'button';
        removeBtn.textContent = 'Remove';
        removeBtn.style.whiteSpace = 'nowrap';

        nameLeftDiv.appendChild(nameLabel);
        nameLeftDiv.appendChild(nameInput);
        nameRow.appendChild(nameLeftDiv);
        nameRow.appendChild(removeBtn);
        card.appendChild(nameRow);

        // Relative Path
        const relRow = document.createElement('div');
        relRow.className = 'ee-row';
        const relLabel = document.createElement('label');
        relLabel.textContent = 'Relative Path';
        const relInput = document.createElement('input');
        relInput.type = 'text';
        relInput.className = 'ee-input';
        relInput.value = entry?.RelativePath ?? '';
        relInput.placeholder = 'relative folder token';
        relInput.style.flex = '1';
        relRow.appendChild(relLabel);
        relRow.appendChild(relInput);
        card.appendChild(relRow);

        // Description
        const descRow = document.createElement('div');
        descRow.className = 'ee-row';
        const descLabel = document.createElement('label');
        descLabel.textContent = 'Description';
        const descInput = document.createElement('textarea');
        descInput.className = 'ee-input';
        descInput.rows = 3;
        descInput.value = entry?.Description ?? '';
        descInput.placeholder = 'Optional description';
        descInput.style.flex = '1';
        descRow.appendChild(descLabel);
        descRow.appendChild(descInput);
        card.appendChild(descRow);

        // Allow Non-Admin
        const allowRow = document.createElement('div');
        allowRow.className = 'ee-row';
        allowRow.style.alignItems = 'center';
        const allowLabel = document.createElement('label');
        allowLabel.textContent = 'Allow Non-Admin';
        const allowInput = document.createElement('input');
        allowInput.type = 'checkbox';
        allowInput.className = 'ee-checkbox';
        allowInput.checked = !!entry?.AllowNonAdmin;
        allowRow.appendChild(allowLabel);
        allowRow.appendChild(allowInput);
        card.appendChild(allowRow);

        // Preview
        const previewRow = document.createElement('div');
        previewRow.className = 'ee-row';
        previewRow.style.flexDirection = 'column';
        previewRow.style.alignItems = 'flex-start';
        previewRow.style.gap = '6px';
        const previewCol = document.createElement('div');
        previewCol.className = 'previews';
        const resolvedLine = document.createElement('div');
        resolvedLine.className = 'ee-resolved-path';
        resolvedLine.textContent = 'Resolved path: [resolving...]';
        resolvedLine.style.width = '100%';
        const exampleLine = document.createElement('div');
        exampleLine.className = 'ee-example-url';
        exampleLine.textContent = 'Example POST URL: [generating...]';
        exampleLine.style.width = '100%';
        previewCol.appendChild(resolvedLine);
        previewCol.appendChild(exampleLine);
        previewRow.appendChild(previewCol);
        card.appendChild(previewRow);

        // Create row
        const createRow = document.createElement('div');
        createRow.className = 'ee-row';
        createRow.style.alignItems = 'center';
        createRow.style.justifyContent = 'space-between';
        const createBtnDiv = document.createElement('div');
        createBtnDiv.style.display = 'flex';
        createBtnDiv.style.gap = '12px';
        createBtnDiv.style.alignItems = 'center';
        const createBtn = document.createElement('button');
        createBtn.className = 'btn';
        createBtn.type = 'button';
        createBtn.textContent = 'Create folder on server';
        const createStatus = document.createElement('span');
        createStatus.style.marginLeft = '8px';
        createStatus.style.color = 'var(--secondaryText)';
        createStatus.style.fontSize = '0.9em';
        createBtnDiv.appendChild(createBtn);
        createBtnDiv.appendChild(createStatus);
        createRow.appendChild(createBtnDiv);
        createRow.appendChild(removeBtn);
        card.appendChild(createRow);

        // #region setPreviewText
        function setPreviewText(el, labelText, pathOnly) {
            el.textContent = labelText;
            el.setAttribute('data-path', pathOnly || '');
            el.setAttribute('title', pathOnly || labelText);
        }

        // Initialize preview with fallback (no undefined variable)
        setPreviewText(resolvedLine, 'Resolved path: ' + buildPreviewPath(''), buildPreviewPath(''));

        // Click-to-copy: copy only the raw path (data-path), fall back to visible text if missing
        resolvedLine.addEventListener('click', () => {
            try {
                const raw = resolvedLine.getAttribute('data-path') || resolvedLine.textContent || '';
                navigator.clipboard.writeText(raw);
                const prev = resolvedLine.textContent;
                resolvedLine.textContent = 'Copied to clipboard';
                setTimeout(() => setPreviewText(resolvedLine, prev, raw), 900);
            } catch (e) { /* ignore */ }
        });

        // #endregion  setPreviewText
        function debounce(fn, wait) { let t = null; return function (...args) { clearTimeout(t); t = setTimeout(() => fn.apply(this, args), wait); }; }

        function buildPreviewPath(rel) {
            rel = (rel || '<relative-path>').replace(/^[\\/]+|[\\/]+$/g, '');
            if (_cachedPluginBasePath) return _cachedPluginBasePath.replace(/[\\/]+$/, '') + '/' + rel;
            const pluginName = 'Jellyfin.Plugin.EndpointExposer';
            const win = `C:\\Users\\<user>\\AppData\\Local\\jellyfin\\plugins\\configurations\\${pluginName}\\data\\${rel}`;
            const nix = `/var/lib/jellyfin/plugins/configurations/${pluginName}/data/${rel}`;
            return `${win}  (Windows)  —  ${nix}  (Linux)`;
        }

        //#region updatePreviews
        async function updatePreviews() {
            const nameVal = (nameInput.value || '<name>').trim();
            const relVal = (relInput.value || '').trim();
            const origin = getClientBaseUrl();
            exampleLine.textContent = 'Example POST URL: ' + origin + '/Plugins/EndpointExposer/write/' + encodeURIComponent(nameVal) + '/file.json';

            if (!relVal) {
                setPreviewText(resolvedLine, 'Resolved path: ' + buildPreviewPath(''), buildPreviewPath(''));
                return;
            }

            // If we already have the authoritative base path, show it immediately and skip ResolvePath
            if (_cachedPluginBasePath) {
                setPreviewText(resolvedLine, 'Resolved path: ' + (_cachedPluginBasePath.replace(/[\\/]+$/, '') + '/' + relVal), (_cachedPluginBasePath.replace(/[\\/]+$/, '') + '/' + relVal));
                return;
            }

            // Otherwise show an optimistic resolving state and call ResolvePath
            setPreviewText(resolvedLine, 'Resolved path: [resolving...]', '');

            try {
                const resolved = await tryResolvePath(relVal);
                if (resolved) {
                    setPreviewText(resolvedLine, 'Resolved path: ' + resolved, resolved);
                } else {
                    const p = buildPreviewPath(relVal);
                    setPreviewText(resolvedLine, 'Resolved path: ' + p, p);
                }
            } catch (e) {
                console.debug('ResolvePath failed (updatePreviews)', e);
                const p = buildPreviewPath(relVal);
                setPreviewText(resolvedLine, 'Resolved path: ' + p, p);
            }
        }

        const debouncedUpdate = debounce(updatePreviews, 250);
        nameInput.addEventListener('input', debouncedUpdate);
        relInput.addEventListener('input', debouncedUpdate);

        // Fetch base path (non-blocking) and refresh preview when ready
        fetchPluginBasePathOnce().then(() => { updatePreviews(); }).catch(() => { });

        // Show fallback immediately
        updatePreviews();

        // Create folder handler (auto-save then CreateFolder)
        createBtn.addEventListener('click', async () => {
            const relVal = (relInput.value || '').trim();
            if (!relVal) { createStatus.textContent = 'Relative path required'; return; }
            createStatus.textContent = 'Saving configuration...'; createBtn.disabled = true;
            try {
                const pluginId = EndpointExposerConfigurationPage.pluginUniqueId;
                const cfg = await ApiClient.getPluginConfiguration(pluginId);
                cfg.ExposedFolders = cfg.ExposedFolders || [];
                const logicalName = (nameInput.value || relVal).trim();
                const existingIndex = cfg.ExposedFolders.findIndex(f =>
                    (f.Name && f.Name.toLowerCase() === logicalName.toLowerCase()) ||
                    (f.RelativePath && f.RelativePath.toLowerCase() === relVal.toLowerCase())
                );
                const folderObj = { Name: logicalName, RelativePath: relVal, AllowNonAdmin: !!allowInput.checked, Description: descInput.value ? descInput.value.trim() : '' };
                if (existingIndex >= 0) cfg.ExposedFolders[existingIndex] = Object.assign(cfg.ExposedFolders[existingIndex], folderObj);
                else cfg.ExposedFolders.push(folderObj);
                await ApiClient.updatePluginConfiguration(pluginId, cfg);
                createStatus.textContent = 'Configuration saved. Creating folder...';
                const token = getAccessToken();
                const createResult = await ApiClient.ajax({
                    url: ApiClient.getUrl('Plugins/EndpointExposer/CreateFolder'),
                    type: 'POST',
                    data: JSON.stringify({ RelativePath: relVal }),
                    contentType: 'application/json',
                    headers: token ? { 'X-Emby-Token': token } : {}
                });
                const resolvedPath = (createResult && (createResult.resolvedPath || createResult.ResolvedPath)) ? (createResult.resolvedPath || createResult.ResolvedPath) : null;
                createStatus.textContent = 'Created: ' + (resolvedPath || relVal);
                setPreviewText(resolvedLine, 'Resolved path: ' + (resolvedPath || buildPreviewPath(relVal)), (resolvedPath || buildPreviewPath(relVal)));

            } catch (err) {
                console.error('CreateFolder flow failed', err);
                let msg = 'request failed';
                try { if (err && err.message) msg = err.message; else if (err && err.response && err.response.statusText) msg = err.response.statusText; } catch (e) { }
                createStatus.textContent = 'Error: ' + msg;
            } finally { createBtn.disabled = false; }
        });

        // Remove handler
        removeBtn.addEventListener('click', () => {
            const list = document.getElementById('ee-folders-list');
            if (list && card.parentNode) list.removeChild(card);
        });

        // ResolvePath on blur (single server validation)
        relInput.addEventListener('blur', async () => {
            const relVal = (relInput.value || '').trim();
            if (!relVal) return;

            try {
                const resolved = await tryResolvePath(relVal);
                if (resolved) {
                    setPreviewText(resolvedLine, 'Resolved path: ' + resolved, resolved);
                    return;
                }
            } catch (e) {
                console.debug('ResolvePath failed (blur)', e);
            }

            // fallback to cached base path or local guess
            if (_cachedPluginBasePath) {
                setPreviewText(resolvedLine, 'Resolved path: ' + (_cachedPluginBasePath.replace(/[\\/]+$/, '') + '/' + relVal), (_cachedPluginBasePath.replace(/[\\/]+$/, '') + '/' + relVal));
            } else {
                const p = buildPreviewPath(relVal);
                setPreviewText(resolvedLine, 'Resolved path: ' + p, p);
            }
        });

        return {
            element: card,
            appendTo: function (container) { container.appendChild(card); },
            getData: function () {
                return { Name: nameInput.value.trim(), RelativePath: relInput.value.trim(), AllowNonAdmin: allowInput.checked, Description: descInput.value.trim() };
            }
        };
    }
    //#endregion updatePreviews

    page.createFolderCard = createFolderCard;

    //#endregion IIFE

    function renderFoldersList(entries) {
        const list = document.getElementById('ee-folders-list');
        if (!list) return;
        list.innerHTML = '';
        (entries || []).forEach(e => { const card = createFolderCard(e); if (card && typeof card.appendTo === 'function') card.appendTo(list); });
    }

    function readFoldersFromUi() {
        const list = document.getElementById('ee-folders-list');
        const out = [];
        if (!list) return out;
        const cards = Array.from(list.querySelectorAll('.ee-folder'));
        for (const c of cards) {
            const nameInput = c.querySelector('input[type="text"].ee-input');
            const relInput = c.querySelector('input[type="text"].ee-input:nth-of-type(2)') || c.querySelector('input[type="text"].ee-input');
            const descInput = c.querySelector('textarea.ee-input') || c.querySelector('input[type="text"].ee-input[placeholder="Optional description"]');
            const allowInput = c.querySelector('input[type="checkbox"], input.ee-checkbox');
            out.push({
                Name: nameInput ? nameInput.value.trim() : '',
                RelativePath: relInput ? relInput.value.trim() : '',
                AllowNonAdmin: allowInput ? allowInput.checked : false,
                Description: descInput ? descInput.value.trim() : ''
            });
        }
        return out;
    }

    page.loadConfigurationToUi = function (config) {
        renderFoldersList(config?.ExposedFolders || []);
        const raw = document.getElementById('ee-raw');
        if (raw) raw.textContent = JSON.stringify(config || {}, null, 2);
    };

    page.gatherUiToConfiguration = function (baseConfig) {
        const cfg = baseConfig || {};
        const folders = readFoldersFromUi();
        for (const f of folders) {
            if (!f.Name || !folderTokenRegex.test(f.Name)) throw new Error('Invalid folder Name: "' + f.Name + '"');
            if (!f.RelativePath || !folderTokenRegex.test(f.RelativePath)) throw new Error('Invalid RelativePath for "' + f.Name + '": "' + f.RelativePath + '"');
        }
        const names = new Set(); const rels = new Set();
        for (const f of folders) {
            const n = f.Name.toLowerCase(); const r = f.RelativePath.toLowerCase();
            if (names.has(n)) throw new Error('Duplicate folder Name: "' + f.Name + '"');
            if (rels.has(r)) throw new Error('Duplicate RelativePath: "' + f.RelativePath + '"');
            names.add(n); rels.add(r);
        }
        cfg.ExposedFolders = folders; cfg.RegisteredFiles = cfg.RegisteredFiles || [];
        return cfg;
    };

    (function () {
        function applyEmbyInputs() {
            const inputs = document.querySelectorAll('.ee-folder input[type="text"], .ee-folder input[type="checkbox"], .ee-folder textarea');
            inputs.forEach(i => {
                if (!i.hasAttribute('is') && i.type === 'text') i.setAttribute('is', 'emby-input');
                if (!i.hasAttribute('is') && i.type === 'checkbox') i.setAttribute('is', 'emby-checkbox');
                i.classList.add('emby-input');
                if (i.type === 'checkbox' && !i.hasAttribute('data-embycheckbox')) i.setAttribute('data-embycheckbox', 'true');
            });
            try {
                if (window.Emby && Emby.Elements && typeof Emby.Elements.upgrade === 'function') {
                    Emby.Elements.upgrade();
                }
            } catch (e) { /* ignore */ }
        }

        // Wire detected base UI (requires the HTML snippet to be present)
        (function wireDetectedBaseUi() {
            const valEl = document.getElementById('ee-detected-base-value');
            const useBtn = document.getElementById('ee-use-detected');
            const testBtn = document.getElementById('ee-test-base');
            const testRes = document.getElementById('ee-test-result');
            const input = document.getElementById('ServerBaseUrl');

            const detected = getClientBaseUrl();
            if (valEl) valEl.textContent = detected;

            if (useBtn && input) {
                useBtn.addEventListener('click', () => { input.value = detected; input.dispatchEvent(new Event('input')); });
            }

            if (testBtn) {
                testBtn.addEventListener('click', async () => {
                    if (testRes) testRes.textContent = 'Testing...';
                    try {
                        const token = getAccessToken();
                        const url = ApiClient.getUrl('Plugins/EndpointExposer/DataBasePath');
                        const res = await ApiClient.ajax({ url: url, type: 'GET', headers: token ? { 'X-Emby-Token': token } : {} });
                        if (testRes) testRes.textContent = res ? 'Reachable / JSON OK' : 'No JSON response';
                    } catch (e) {
                        if (testRes) testRes.textContent = 'Failed: ' + (e && e.message ? e.message : 'network/error');
                    }
                    setTimeout(() => { if (testRes) testRes.textContent = ''; }, 3000);
                });
            }
        })();

        // Attach handlers for page-level controls
        document.getElementById('ee-add-folder')?.addEventListener('click', () => {
            const list = document.getElementById('ee-folders-list');
            if (!list) return;
            const card = createFolderCard({});
            if (card && typeof card.appendTo === 'function') card.appendTo(list);
            applyEmbyInputs();
        });

        document.getElementById('ee-load')?.addEventListener('click', () => {
            EndpointExposerConfigurationPage.loadConfiguration(document.getElementById('endpointExposerConfigurationPage'));
        });

        document.getElementById('ee-fetch')?.addEventListener('click', () => {
            EndpointExposerConfigurationPage.fetchRawConfiguration(document.getElementById('endpointExposerConfigurationPage'));
        });

        document.getElementById('ee-save')?.addEventListener('click', (e) => {
            e.preventDefault();
            EndpointExposerConfigurationPage.saveConfiguration(document.getElementById('endpointExposerConfigurationPage'));
        });

        // initial load
        EndpointExposerConfigurationPage.loadConfiguration(document.getElementById('endpointExposerConfigurationPage'));
    })();

})(EndpointExposerConfigurationPage);
// END - Configuration/settings.js