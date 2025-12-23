// Configuration\settings.js adapted for EndpointExposer
var EndpointExposerConfigurationPage = {
    pluginUniqueId: "f1530767-390f-475e-afa2-6610c933c29e",
    _currentConfig: null,

    loadConfiguration: function (view) {
        Dashboard.showLoadingMsg();

        ApiClient.getPluginConfiguration(EndpointExposerConfigurationPage.pluginUniqueId).then(function (config) {
            EndpointExposerConfigurationPage._currentConfig = config || {};

            const get = id => view.querySelector('#' + id);

            const serverEl = get('ServerBaseUrl');
            if (serverEl) serverEl.value = config.ServerBaseUrl || '';

            const apiKeyEl = get('ApiKey');
            if (apiKeyEl) apiKeyEl.value = config.ApiKey || '';

            const allowEl = get('AllowNonAdmin');
            if (allowEl) allowEl.checked = !!config.AllowNonAdmin;

            const outDirEl = get('OutputDirectory');
            if (outDirEl) outDirEl.value = config.OutputDirectory || '';

            const maxPayloadEl = get('MaxPayloadBytes');
            if (maxPayloadEl) maxPayloadEl.value = config.MaxPayloadBytes || 0;

            const maxBackupsEl = get('MaxBackups');
            if (maxBackupsEl) maxBackupsEl.value = config.MaxBackups || 0;

            // Legacy RegisteredFiles textarea (kept for compatibility)
            const regFilesEl = get('RegisteredFiles');
            if (regFilesEl) regFilesEl.value = JSON.stringify(config.RegisteredFiles || [], null, 2);

            // Populate the structured Exposed Folders UI and raw view
            if (typeof EndpointExposerConfigurationPage.loadConfigurationToUi === 'function') {
                try {
                    EndpointExposerConfigurationPage.loadConfigurationToUi(config);
                } catch (e) {
                    console.warn('Failed to populate Exposed Folders UI:', e);
                }
            }

            Dashboard.hideLoadingMsg();
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            console.error('Failed to load plugin configuration', err);
        });
    },

    saveConfiguration: function (view) {
        Dashboard.showLoadingMsg();

        ApiClient.getPluginConfiguration(EndpointExposerConfigurationPage.pluginUniqueId).then(function (config) {
            const get = id => view.querySelector('#' + id);

            config.ServerBaseUrl = (get('ServerBaseUrl') && get('ServerBaseUrl').value) || null;
            config.ApiKey = (get('ApiKey') && get('ApiKey').value) || null;
            config.AllowNonAdmin = !!(get('AllowNonAdmin') && get('AllowNonAdmin').checked);
            config.OutputDirectory = (get('OutputDirectory') && get('OutputDirectory').value) || null;

            const maxPayloadVal = (get('MaxPayloadBytes') && get('MaxPayloadBytes').value) || '0';
            config.MaxPayloadBytes = parseInt(maxPayloadVal, 10) || 0;

            const maxBackupsVal = (get('MaxBackups') && get('MaxBackups').value) || '0';
            config.MaxBackups = parseInt(maxBackupsVal, 10) || 0;

            // Gather structured UI values (ExposedFolders + legacy RegisteredFiles)
            try {
                if (typeof EndpointExposerConfigurationPage.gatherUiToConfiguration === 'function') {
                    config = EndpointExposerConfigurationPage.gatherUiToConfiguration(config);
                } else {
                    // Fallback: parse RegisteredFiles textarea
                    try {
                        const raw = (get('RegisteredFiles') && get('RegisteredFiles').value) || '[]';
                        config.RegisteredFiles = JSON.parse(raw);
                    } catch (e) {
                        config.RegisteredFiles = [];
                    }
                }
            } catch (e) {
                Dashboard.hideLoadingMsg();
                const statusEl = view.querySelector('#ee-status');
                if (statusEl) statusEl.textContent = 'Validation error: ' + e.message;
                return;
            }

            // Primary save via ApiClient (updates Jellyfin in-memory config and XML)
            ApiClient.updatePluginConfiguration(EndpointExposerConfigurationPage.pluginUniqueId, config).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);

                // Refresh UI with saved config
                EndpointExposerConfigurationPage._currentConfig = config;
                if (typeof EndpointExposerConfigurationPage.loadConfigurationToUi === 'function') {
                    try { EndpointExposerConfigurationPage.loadConfigurationToUi(config); } catch (e) { /* ignore */ }
                }

                // Secondary: call controller SaveConfiguration so server-side SaveConfiguration() runs
                // (this triggers JSON write and the folder-creation block you added).
                try {
                    fetch('/Plugins/EndpointExposer/SaveConfiguration', {
                        method: 'PUT',
                        credentials: 'same-origin',
                        headers: {
                            'Content-Type': 'application/json',
                            'X-Requested-With': 'XMLHttpRequest'
                        },
                        body: JSON.stringify(config)
                    }).then(function (resp) {
                        if (!resp.ok) {
                            console.warn('Controller SaveConfiguration returned non-OK status', resp.status);
                        } else {
                            // optional: you can log success for debugging
                            console.debug('Controller SaveConfiguration invoked successfully');
                        }
                    }).catch(function (err) {
                        console.warn('Controller SaveConfiguration request failed', err);
                    });
                } catch (ex) {
                    console.warn('Failed to invoke controller SaveConfiguration', ex);
                }
            }).catch(function (err) {
                console.error('Failed to save plugin configuration', err);
            }).finally(function () {
                Dashboard.hideLoadingMsg();
            });
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            console.error('Failed to fetch plugin configuration before save', err);
        });
    },


    fetchRawConfiguration: function (view) {
        Dashboard.showLoadingMsg();

        ApiClient.getPluginConfiguration(EndpointExposerConfigurationPage.pluginUniqueId).then(function (config) {
            const rawEl = view.querySelector('#ee-raw');
            if (rawEl) rawEl.textContent = JSON.stringify(config, null, 2);
            Dashboard.hideLoadingMsg();
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            console.error('Failed to fetch raw configuration', err);
        });
    }
};

// Exposed Folders UI helpers (integrated into the page object)
(function (page) {
    const folderTokenRegex = /^[a-z0-9_-]+$/i;

    function createFolderRow(entry) {
        const tr = document.createElement('tr');

        // Name
        const nameTd = document.createElement('td');
        const nameInput = document.createElement('input');
        nameInput.type = 'text';
        nameInput.className = 'ee-input';
        nameInput.value = entry?.Name ?? '';
        nameTd.appendChild(nameInput);

        // Relative Path
        const relTd = document.createElement('td');
        const relInput = document.createElement('input');
        relInput.type = 'text';
        relInput.className = 'ee-input';
        relInput.value = entry?.RelativePath ?? '';
        relTd.appendChild(relInput);

        // Allow Non-Admin (use Jellyfin checkbox structure)
        const allowTd = document.createElement('td');
        allowTd.style.textAlign = 'center';
        const wrapper = document.createElement('label');
        wrapper.className = 'emby-checkbox-label';

        const allowInput = document.createElement('input');
        allowInput.type = 'checkbox';
        allowInput.checked = !!entry?.AllowNonAdmin;
        allowInput.setAttribute('is', 'emby-checkbox');
        allowInput.classList.add('emby-checkbox');
        allowInput.setAttribute('data-embycheckbox', 'true');

        const labelSpan = document.createElement('span');
        labelSpan.className = 'checkboxLabel';
        labelSpan.textContent = ''; // no visible text in table cell

        const outline = document.createElement('span');
        outline.className = 'checkboxOutline';
        outline.innerHTML = '<span class="material-icons checkboxIcon checkboxIcon-checked check" aria-hidden="true"></span><span class="material-icons checkboxIcon checkboxIcon-unchecked" aria-hidden="true"></span>';

        wrapper.appendChild(allowInput);
        wrapper.appendChild(labelSpan);
        wrapper.appendChild(outline);
        allowTd.appendChild(wrapper);

        // Description
        const descTd = document.createElement('td');
        const descInput = document.createElement('input');
        descInput.type = 'text';
        descInput.className = 'ee-input';
        descInput.value = entry?.Description ?? '';
        descTd.appendChild(descInput);

        // Actions
        const actionsTd = document.createElement('td');
        const removeBtn = document.createElement('button');
        removeBtn.className = 'btn btn-small';
        removeBtn.type = 'button';
        removeBtn.textContent = 'Remove';
        removeBtn.addEventListener('click', () => tr.remove());
        actionsTd.appendChild(removeBtn);

        tr.appendChild(nameTd);
        tr.appendChild(relTd);
        tr.appendChild(allowTd);
        tr.appendChild(descTd);
        tr.appendChild(actionsTd);

        return tr;
    }


    function renderFoldersTable(entries) {
        const tbody = document.querySelector('#ee-folders-table tbody');
        if (!tbody) return;
        tbody.innerHTML = '';
        (entries || []).forEach(e => tbody.appendChild(createFolderRow(e)));
    }

    function readFoldersFromUi() {
        const rows = Array.from(document.querySelectorAll('#ee-folders-table tbody tr'));
        const out = [];
        for (const r of rows) {
            const inputs = r.querySelectorAll('input');
            const name = (inputs[0] && inputs[0].value) ? inputs[0].value.trim() : '';
            const rel = (inputs[1] && inputs[1].value) ? inputs[1].value.trim() : '';
            const allow = inputs[2] ? inputs[2].checked : false;
            const desc = (inputs[3] && inputs[3].value) ? inputs[3].value.trim() : '';
            out.push({ Name: name, RelativePath: rel, AllowNonAdmin: allow, Description: desc });
        }
        return out;
    }

    function validateFolders(entries) {
        for (const e of entries) {
            if (!e.Name || !folderTokenRegex.test(e.Name)) return `Invalid folder Name: "${e.Name}"`;
            if (!e.RelativePath || !folderTokenRegex.test(e.RelativePath)) return `Invalid RelativePath for "${e.Name}": "${e.RelativePath}"`;
        }
        // ensure no duplicate Names or RelativePaths
        const names = new Set();
        const rels = new Set();
        for (const e of entries) {
            const n = e.Name.toLowerCase();
            const r = e.RelativePath.toLowerCase();
            if (names.has(n)) return `Duplicate folder Name: "${e.Name}"`;
            if (rels.has(r)) return `Duplicate RelativePath: "${e.RelativePath}"`;
            names.add(n);
            rels.add(r);
        }
        return null;
    }

    // Hook up Add / Import buttons (delegated)
    document.addEventListener('click', (ev) => {
        if (!ev.target) return;
        if (ev.target.id === 'ee-add-folder') {
            ev.preventDefault();
            const tbody = document.querySelector('#ee-folders-table tbody');
            if (tbody) tbody.appendChild(createFolderRow({ Name: '', RelativePath: '', AllowNonAdmin: false, Description: '' }));
        }
        if (ev.target.id === 'ee-import-folders') {
            ev.preventDefault();
            const raw = prompt('Paste JSON array of FolderEntry objects (Name, RelativePath, AllowNonAdmin, Description):');
            if (!raw) return;
            try {
                const parsed = JSON.parse(raw);
                if (!Array.isArray(parsed)) throw new Error('Not an array');
                renderFoldersTable(parsed);
            } catch (ex) {
                alert('Invalid JSON: ' + ex.message);
            }
        }
    });

    // Expose functions on the page object so existing handlers can call them
    page.loadConfigurationToUi = function (config) {
        // populate ExposedFolders
        renderFoldersTable(config?.ExposedFolders || []);
        // populate legacy RegisteredFiles textarea if present
        const rf = document.getElementById('ee-registered-files');
        if (rf) rf.value = JSON.stringify(config?.RegisteredFiles || [], null, 2);
        // populate raw view
        const raw = document.getElementById('ee-raw');
        if (raw) raw.textContent = JSON.stringify(config || {}, null, 2);
    };

    page.gatherUiToConfiguration = function (baseConfig) {
        const cfg = baseConfig || {};
        // read ExposedFolders
        const folders = readFoldersFromUi();
        const err = validateFolders(folders);
        if (err) throw new Error(err);
        cfg.ExposedFolders = folders;

        // read RegisteredFiles textarea (legacy) and validate JSON
        const rf = document.getElementById('ee-registered-files');
        if (rf) {
            const txt = rf.value.trim();
            if (txt) {
                try {
                    cfg.RegisteredFiles = JSON.parse(txt);
                } catch (ex) {
                    throw new Error('RegisteredFiles JSON is invalid: ' + ex.message);
                }
            } else {
                cfg.RegisteredFiles = [];
            }
        }

        return cfg;
    };

    // UI polish: ensure folder row inputs use emby-input and wire details toggle
    (function () {
        function applyEmbyInputs() {
            const rows = document.querySelectorAll('#ee-folders-table tbody tr');
            rows.forEach(r => {
                // Text inputs -> emby-input
                const inputs = r.querySelectorAll('input[type="text"], input[type="number"]');
                inputs.forEach(i => {
                    if (!i.hasAttribute('is')) i.setAttribute('is', 'emby-input');
                    i.classList.add('emby-input');
                });

                // Textareas -> emby-textarea
                const textareas = r.querySelectorAll('textarea');
                textareas.forEach(t => {
                    if (!t.hasAttribute('is')) t.setAttribute('is', 'emby-textarea');
                    t.classList.add('emby-input');
                });

                // Checkboxes -> ensure proper wrapper and attributes (idempotent)
                const checkboxes = r.querySelectorAll('input[type="checkbox"]');
                checkboxes.forEach(c => {
                    // mark as emby checkbox
                    if (!c.hasAttribute('is')) c.setAttribute('is', 'emby-checkbox');
                    c.classList.add('emby-checkbox');
                    if (!c.hasAttribute('data-embycheckbox')) c.setAttribute('data-embycheckbox', 'true');

                    // ensure wrapper exists
                    if (!c.closest('label.emby-checkbox-label')) {
                        const wrapper = document.createElement('label');
                        wrapper.className = 'emby-checkbox-label';

                        // move checkbox into wrapper
                        c.parentNode.insertBefore(wrapper, c);
                        wrapper.appendChild(c);

                        // visible label (empty)
                        const labelSpan = document.createElement('span');
                        labelSpan.className = 'checkboxLabel';
                        wrapper.appendChild(labelSpan);

                        // outline/icons
                        const outline = document.createElement('span');
                        outline.className = 'checkboxOutline';
                        outline.innerHTML = '<span class="material-icons checkboxIcon checkboxIcon-checked check" aria-hidden="true"></span><span class="material-icons checkboxIcon checkboxIcon-unchecked" aria-hidden="true"></span>';
                        wrapper.appendChild(outline);
                    }
                });
            });

            // Try to trigger Jellyfin's element upgrade/init so custom controls render immediately
            try {
                if (window.Emby && Emby.Elements && typeof Emby.Elements.upgrade === 'function') {
                    Emby.Elements.upgrade(document.querySelectorAll('input[is="emby-checkbox"], input[is="emby-input"], textarea[is="emby-textarea"]'));
                } else if (window.Emby && Emby.Controls && typeof Emby.Controls.init === 'function') {
                    Emby.Controls.init();
                }
            } catch (e) {
                // non-fatal; host may not expose these helpers in all builds
                console.debug('Emby upgrade/init not available', e);
            }
        }

        const tbody = document.querySelector('#ee-folders-table tbody');
        if (tbody) {
            const observer = new MutationObserver((mutations) => {
                for (const m of mutations) {
                    if (m.type === 'childList' && m.addedNodes.length) applyEmbyInputs();
                }
            });
            observer.observe(tbody, { childList: true, subtree: false });
        }

        document.addEventListener('DOMContentLoaded', applyEmbyInputs);
        document.addEventListener('viewshow', applyEmbyInputs);

        document.querySelectorAll('details').forEach(d => {
            d.addEventListener('toggle', () => {
                if (d.open) d.classList.add('open'); else d.classList.remove('open');
            });
        });

        window.EndpointExposerUiHelpers = window.EndpointExposerUiHelpers || {};
        window.EndpointExposerUiHelpers.setRawConfig = function (jsonText) {
            const el = document.getElementById('ee-raw');
            if (el) el.textContent = jsonText;
        };
    })();


})(EndpointExposerConfigurationPage);

// Export default initializer called by Jellyfin when the controller is injected.
export default function (view) {
    if (!view) return;

    // Load config when the view is shown
    view.addEventListener('viewshow', function () {
        EndpointExposerConfigurationPage.loadConfiguration(view);
    });

    // Form submit
    const form = view.querySelector('#ee-config-form');
    if (form) {
        form.addEventListener('submit', function (e) {
            e.preventDefault();
            EndpointExposerConfigurationPage.saveConfiguration(view);
        });
    }

    // Buttons
    const loadBtn = view.querySelector('#ee-load');
    if (loadBtn) {
        loadBtn.addEventListener('click', function () {
            EndpointExposerConfigurationPage.loadConfiguration(view);
        });
    }

    const fetchBtn = view.querySelector('#ee-fetch');
    if (fetchBtn) {
        fetchBtn.addEventListener('click', function () {
            EndpointExposerConfigurationPage.fetchRawConfiguration(view);
        });
    }
}
