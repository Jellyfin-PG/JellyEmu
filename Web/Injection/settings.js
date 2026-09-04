(function() {
    window.JellyEmu = window.JellyEmu || {};
    const JE = window.JellyEmu;

    let _systemsData = [];
    let _systemCoreMap = {};
    let _knownSystems = [];

    let _settingOptions = {
        shaders: [],
        scaling: [],
        rotation: [],
        fastForwardRates: [],
        slowMotionRates: [],
        volume: [],
        mute: [],
        fps: [],
        autosave: [],
        haptics: [],
        virtualGamepad: [],
        virtualGamepadLefty: []
    };

    function normalizeShaderId(id) {
        if (!id || id === 'none' || id === 'disabled' || id === '0') return 'disabled';
        var s = String(id).trim();
        if (s.toLowerCase() === 'crt-easymode') return 'crt-easymode.glslp';
        if (s.toLowerCase() === '2xscalehq') return '2xScaleHQ.glslp';
        if (s.toLowerCase() === '4xscalehq') return '4xScaleHQ.glslp';
        if (s.toLowerCase() === 'crt-aperture') return 'crt-aperture.glslp';
        if (s.toLowerCase() === 'crt-geom') return 'crt-geom.glslp';
        if (s.toLowerCase() === 'crt-mattias') return 'crt-mattias.glslp';
        return s;
    }

    function normalizeScaleId(id) {
        if (!id) return 'fit';
        var s = String(id).trim().toLowerCase();
        if (s === 'aspect') return 'fit';
        if (s === 'native') return '1';
        if (s === '2x') return '2';
        if (s === '3x') return '3';
        if (s === '4x') return '4';
        return s;
    }

    let _activeTab = "global";
    let _selectedSystem = "";

    function showToast(msg) {
        const existing = document.querySelector('.je-toast');
        if (existing) existing.remove();

        const toast = document.createElement('div');
        toast.className = 'je-toast';
        toast.innerHTML = `<span class="material-icons" style="font-size:18px">check_circle</span> ${msg}`;
        document.body.appendChild(toast);
        setTimeout(() => toast.remove(), 3000);
    }

    async function ensureSystemsLoaded(token) {
        try {
            const headers = {};
            if (token) headers['Authorization'] = `MediaBrowser Token="${token}"`;
            
            const [sysRes, optsRes] = await Promise.all([
                _knownSystems.length > 0 ? Promise.resolve(null) : fetch('/jellyemu/systems', { headers }).catch(() => null),
                fetch('/jellyemu/setting-options', { headers }).catch(() => null)
            ]);

            if (optsRes && optsRes.ok) {
                const optsData = await optsRes.json();
                if (optsData) {
                    _settingOptions = {
                        shaders: optsData.shaders || [],
                        scaling: optsData.scaling || [],
                        rotation: optsData.rotation || [],
                        fastForwardRates: optsData.fastForwardRates || [],
                        slowMotionRates: optsData.slowMotionRates || [],
                        volume: optsData.volume || [],
                        mute: optsData.mute || [],
                        fps: optsData.fps || [],
                        autosave: optsData.autosave || [],
                        haptics: optsData.haptics || [],
                        virtualGamepad: optsData.virtualGamepad || [],
                        virtualGamepadLefty: optsData.virtualGamepadLefty || []
                    };
                }
            }

            if (sysRes && sysRes.ok) {
                const data = await sysRes.json();
                if (data && data.systems) {
                    _systemsData = data.systems;
                    _systemCoreMap = {};
                    _knownSystems = [];
                    data.systems.forEach(s => {
                        _knownSystems.push(s.name);
                        _systemCoreMap[s.name] = (s.cores || []).map(c => ({
                            id: c.id,
                            name: c.name || c.id
                        }));
                    });
                    if (!_selectedSystem && _knownSystems.length > 0) {
                        _selectedSystem = _knownSystems[0];
                    }
                }
            }
        } catch (e) {
            console.warn('[JellyEmu] Failed to load systems/options metadata:', e);
        }
    }

    JE.hijackJellyEmuSettings = async function() {
        const activePage = document.querySelector('.page:not(.hide):not(#myPreferencesMenuPage)');
        if (!activePage) return;

        const isAlreadyHijacked = activePage.hasAttribute('data-jellyemu-settings-hijacked');
        if (!isAlreadyHijacked) {
            activePage.setAttribute('data-jellyemu-settings-hijacked', '1');
            activePage.className = 'page libraryPage noSecondaryNavPage mainAnimatedPage';
            activePage.setAttribute('data-title', 'JellyEmu Settings');
            activePage.setAttribute('data-backbutton', 'true');
        }

        document.title = 'JellyEmu Settings';
        const headerTitle = document.querySelector('.skinHeader .pageTitle');
        if (headerTitle) headerTitle.textContent = 'JellyEmu Settings';

        const userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : null;
        const token  = window.ApiClient ? window.ApiClient.accessToken() : '';

        if (!userId) {
            activePage.innerHTML = `
                <div class="je-settings-page">
                    <div class="je-empty-state">Please sign in to Jellyfin to manage emulator settings.</div>
                </div>`;
            return;
        }

        await ensureSystemsLoaded(token);

        function renderContainer() {
            activePage.innerHTML = `
                <div class="je-settings-page">
                    <div class="je-settings-header">
                        <h1 class="je-settings-heading">
                            <span class="material-icons" style="color:var(--accent, #00a4dc)">sports_esports</span>
                            JellyEmu Settings
                        </h1>
                        <button id="je-btn-reset-all" class="je-btn je-btn-danger">
                            <span class="material-icons" style="font-size:16px">restart_alt</span>
                            Reset to Factory Defaults
                        </button>
                    </div>

                    <div class="je-settings-tabs">
                        <button class="je-settings-tab ${_activeTab === 'global' ? 'active' : ''}" data-tab="global">
                            <span class="material-icons" style="font-size:18px">public</span>
                            Global Settings
                        </button>
                        <button class="je-settings-tab ${_activeTab === 'system' ? 'active' : ''}" data-tab="system">
                            <span class="material-icons" style="font-size:18px">devices</span>
                            System Settings
                        </button>
                        <button class="je-settings-tab ${_activeTab === 'ra' ? 'active' : ''}" data-tab="ra">
                            <span class="material-icons" style="font-size:18px">emoji_events</span>
                            RetroAchievements
                        </button>
                    </div>

                    <div id="je-settings-content"></div>
                </div>`;

            // Tab listeners
            activePage.querySelectorAll('.je-settings-tab').forEach(btn => {
                btn.addEventListener('click', () => {
                    _activeTab = btn.getAttribute('data-tab');
                    renderContainer();
                });
            });

            // Factory reset button listener
            const resetBtn = activePage.querySelector('#je-btn-reset-all');
            if (resetBtn) {
                resetBtn.addEventListener('click', () => {
                    if (confirm('Are you sure you want to reset ALL your JellyEmu settings and custom system overrides back to factory defaults?')) {
                        resetBtn.disabled = true;
                        fetch(`/jellyemu/prefs/${userId}/reset`, {
                            method: 'DELETE',
                            headers: { 'Authorization': `MediaBrowser Token="${token}"` }
                        })
                        .then(r => r.json())
                        .then(() => {
                            showToast('All settings reset to factory defaults.');
                            renderContainer();
                        })
                        .catch(err => {
                            console.error('[JellyEmu] Reset failed:', err);
                            alert('Failed to reset settings.');
                        })
                        .finally(() => { resetBtn.disabled = false; });
                    }
                });
            }

            loadTabContent();
        }

        function loadTabContent() {
            const container = activePage.querySelector('#je-settings-content');
            if (!container) return;

            if (_activeTab === 'global') {
                renderGlobalTab(container);
            } else if (_activeTab === 'system') {
                renderSystemTab(container);
            } else if (_activeTab === 'ra') {
                renderRaTab(container);
            }
        }

        // Global Settings Tab
        function renderGlobalTab(container) {
            container.innerHTML = `<div class="je-empty-state">Loading global settings...</div>`;

            fetch(`/jellyemu/prefs/${userId}?scope=global`, {
                headers: { 'Authorization': `MediaBrowser Token="${token}"` }
            })
            .then(r => r.ok ? r.json() : {})
            .then(data => {
                const p = (data && data.preferences) || {};
                const activeShader = normalizeShaderId(p.shader || 'crt-easymode.glslp');
                const activeScale = normalizeScaleId(p.scale || 'fit');
                const activeVsync = (p.vsync === undefined || p.vsync === null || p.vsync === '' || p.vsync === '1' || p.vsync === true) ? '1' : '0';

                const renderOptions = (list, active) => {
                    if (!Array.isArray(list)) return '';
                    return list.map(opt => `<option value="${opt.id}" ${String(opt.id) === String(active) ? 'selected' : ''}>${opt.label}</option>`).join('');
                };

                container.innerHTML = `
                    <div class="je-settings-section">
                        <h2 class="je-settings-section-heading">
                            <span class="material-icons" style="color:var(--accent, #00a4dc)">display_settings</span>
                            Display & Audio Defaults
                        </h2>
                        <div class="je-settings-section-desc">Default visual, audio, and scaling settings applied to all games across every system.</div>

                        <div class="je-settings-grid">
                            <div class="je-input-container">
                                <label class="je-input-label">Default Shader Filter</label>
                                <select id="je-pref-shader" class="je-select">
                                    ${renderOptions(_settingOptions.shaders, activeShader)}
                                </select>
                                <div class="je-field-desc">Post-processing shader applied to the game canvas.</div>
                            </div>

                            <div class="je-input-container">
                                <label class="je-input-label">Screen Scaling Mode</label>
                                <select id="je-pref-scale" class="je-select">
                                    ${renderOptions(_settingOptions.scaling, activeScale)}
                                </select>
                                <div class="je-field-desc">How emulator frames scale inside the player viewport.</div>
                            </div>

                            <div class="je-input-container">
                                <label class="je-input-label">Default Screen Rotation</label>
                                <select id="je-pref-rotation" class="je-select">
                                    ${renderOptions(_settingOptions.rotation, p.videoRotation || '0')}
                                </select>
                                <div class="je-field-desc">Orientation angle for the game display.</div>
                            </div>

                            <div class="je-input-container">
                                <label class="je-input-label">Default Audio Volume</label>
                                <select id="je-pref-volume" class="je-select">
                                    ${renderOptions(_settingOptions.volume, p.volume || '1')}
                                </select>
                                <div class="je-field-desc">Default sound volume level when launching games.</div>
                            </div>

                            <div class="je-input-container">
                                <label class="je-input-label">Default Audio Mute</label>
                                <select id="je-pref-mute" class="je-select">
                                    ${renderOptions(_settingOptions.mute, p.mute || '0')}
                                </select>
                                <div class="je-field-desc">Initial mute state when launching games.</div>
                            </div>

                            <div class="je-input-container">
                                <label class="je-input-label">Performance Overlay</label>
                                <select id="je-pref-fps" class="je-select">
                                    ${renderOptions(_settingOptions.fps, p.showFps || '0')}
                                </select>
                                <div class="je-field-desc">Real-time framerate and timing metrics overlay.</div>
                            </div>
                        </div>
                    </div>

                    <div class="je-settings-section">
                        <h2 class="je-settings-section-heading">
                            <span class="material-icons" style="color:var(--accent, #00a4dc)">sports_esports</span>
                            Performance, Emulation & Controls
                        </h2>
                        <div class="je-settings-section-desc">Fast forward speeds, state persistence, controller vibration, and touch gamepad preferences.</div>

                        <div class="je-settings-grid">
                            <div class="je-input-container">
                                <label class="je-input-label">Fast Forward Speed</label>
                                <select id="je-pref-ffrate" class="je-select">
                                    ${renderOptions(_settingOptions.fastForwardRates, p.ffrate || '3')}
                                </select>
                                <div class="je-field-desc">Speed multiplier when fast-forward is triggered.</div>
                            </div>

                            <div class="je-input-container">
                                <label class="je-input-label">Slow Motion Speed</label>
                                <select id="je-pref-smrate" class="je-select">
                                    ${renderOptions(_settingOptions.slowMotionRates, p.smrate || '3')}
                                </select>
                                <div class="je-field-desc">Speed reduction factor for slow-motion gameplay.</div>
                            </div>

                            <div class="je-input-container">
                                <label class="je-input-label">Autosave on Exit</label>
                                <select id="je-pref-autosave" class="je-select">
                                    ${renderOptions(_settingOptions.autosave, p.autosave || '0')}
                                </select>
                                <div class="je-field-desc">Automatically saves state upon closing the emulator.</div>
                            </div>

                            <div class="je-input-container">
                                <label class="je-input-label">Gamepad Haptics</label>
                                <select id="je-pref-haptics" class="je-select">
                                    ${renderOptions(_settingOptions.haptics, p.haptics !== undefined ? String(p.haptics) : '1')}
                                </select>
                                <div class="je-field-desc">Physical gamepad rumble vibration feedback.</div>
                            </div>

                            <div class="je-input-container">
                                <label class="je-input-label">On-Screen Mobile Gamepad</label>
                                <select id="je-pref-vg" class="je-select">
                                    ${renderOptions(_settingOptions.virtualGamepad, p.virtualGamepad || '0')}
                                </select>
                                <div class="je-field-desc">Touchscreen controls overlay for mobile devices.</div>
                            </div>

                            <div class="je-input-container">
                                <label class="je-input-label">Mobile Gamepad Layout</label>
                                <select id="je-pref-vg-lefty" class="je-select">
                                    ${renderOptions(_settingOptions.virtualGamepadLefty, p.virtualGamepadLefty || '0')}
                                </select>
                                <div class="je-field-desc">D-pad and action button orientation on mobile.</div>
                            </div>
                        </div>

                        <div class="je-actions">
                            <button id="je-save-global" class="je-btn je-btn-primary">
                                <span class="material-icons" style="font-size:18px">save</span>
                                Save Global Settings
                            </button>
                        </div>
                    </div>`;

                const saveBtn = container.querySelector('#je-save-global');
                saveBtn.addEventListener('click', () => {
                    saveBtn.disabled = true;
                    saveBtn.innerHTML = `<span class="material-icons" style="font-size:18px">sync</span> Saving...`;

                    const payload = {
                        scope: 'global',
                        targetId: '',
                        preferences: {
                            shader: container.querySelector('#je-pref-shader').value,
                            scale: container.querySelector('#je-pref-scale').value,
                            videoRotation: container.querySelector('#je-pref-rotation').value,
                            volume: container.querySelector('#je-pref-volume').value,
                            mute: container.querySelector('#je-pref-mute').value,
                            showFps: container.querySelector('#je-pref-fps').value,
                            ffrate: container.querySelector('#je-pref-ffrate').value,
                            smrate: container.querySelector('#je-pref-smrate').value,
                            autosave: container.querySelector('#je-pref-autosave').value,
                            haptics: container.querySelector('#je-pref-haptics').value,
                            virtualGamepad: container.querySelector('#je-pref-vg').value,
                            virtualGamepadLefty: container.querySelector('#je-pref-vg-lefty').value
                        }
                    };

                    fetch(`/jellyemu/prefs/${userId}`, {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'Authorization': `MediaBrowser Token="${token}"`
                        },
                        body: JSON.stringify(payload)
                    })
                    .then(r => r.json())
                    .then(() => {
                        showToast('Global settings saved.');
                        loadTabContent();
                    })
                    .catch(err => {
                        console.error('[JellyEmu] Failed to save global settings:', err);
                        alert('Failed to save settings.');
                        saveBtn.disabled = false;
                        saveBtn.innerHTML = `<span class="material-icons" style="font-size:18px">save</span> Save Global Settings`;
                    });
                });
            });
        }

        // System Settings Tab
        function renderSystemTab(container) {
            container.innerHTML = `
                <div class="je-settings-section">
                    <h2 class="je-settings-section-heading">
                        <span class="material-icons" style="color:var(--accent, #00a4dc)">devices</span>
                        Platform & Console Settings
                    </h2>
                    <div class="je-settings-section-desc">Configure emulation core, shader, rotation, performance, audio, and controls for a specific system.</div>

                    <div class="je-input-container" style="margin-bottom:2em;">
                        <label class="je-input-label">Select System / Platform</label>
                        <select id="je-select-system" class="je-select">
                            ${_knownSystems.map(sys => `<option value="${sys}" ${sys === _selectedSystem ? 'selected' : ''}>${sys}</option>`).join('')}
                        </select>
                    </div>

                    <div id="je-system-form-container"></div>
                </div>`;

            const sysSelect = container.querySelector('#je-select-system');
            sysSelect.addEventListener('change', () => {
                _selectedSystem = sysSelect.value;
                loadSystemSettings(container);
            });

            loadSystemSettings(container);
        }

        function loadSystemSettings(container) {
            const formContainer = container.querySelector('#je-system-form-container');
            if (!formContainer) return;

            formContainer.innerHTML = `<div class="je-empty-state">Loading ${_selectedSystem} settings...</div>`;

            fetch(`/jellyemu/prefs/${userId}?scope=system&targetId=${encodeURIComponent(_selectedSystem)}`, {
                headers: { 'Authorization': `MediaBrowser Token="${token}"` }
            })
            .then(r => r.ok ? r.json() : {})
            .then(data => {
                const sp = (data && data.preferences) || {};
                const hasCustomCore = !!sp.core;
                const availableCores = _systemCoreMap[_selectedSystem] || [];

                formContainer.innerHTML = `
                    <div style="display:flex;align-items:center;margin-bottom:1.5em;border-bottom:1px solid rgba(255,255,255,0.08);padding-bottom:0.8em;">
                        <span style="font-weight:500;font-size:1.1em;color:#fff;">${_selectedSystem} Configuration</span>
                        <span class="je-badge ${hasCustomCore ? 'je-badge-active' : 'je-badge-inherit'}">
                            ${hasCustomCore ? 'Custom Core Selected' : 'Default Core'}
                        </span>
                    </div>

                    <div class="je-settings-grid">
                        ${availableCores.length > 1 ? `
                        <div class="je-input-container" style="grid-column: 1 / -1;">
                            <label class="je-input-label" style="color:var(--accent, #00a4dc);font-weight:600;">Emulation Core</label>
                            <select id="je-sys-core" class="je-select">
                                ${availableCores.map((c, index) => `<option value="${c.id}" ${(sp.core ? c.id === sp.core : index === 0) ? 'selected' : ''}>${c.name || c.id}${index === 0 ? ' (Default)' : ''}</option>`).join('')}
                            </select>
                            <div class="je-field-desc">Select the Libretro core used to emulate ${_selectedSystem} games. All display, control, and performance options are managed globally.</div>
                        </div>` : availableCores.length === 1 ? `
                        <div class="je-input-container" style="grid-column: 1 / -1;">
                            <label class="je-input-label" style="color:var(--accent, #00a4dc);font-weight:600;">Emulation Core</label>
                            <select id="je-sys-core" class="je-select" disabled>
                                <option value="${availableCores[0].id}" selected>${availableCores[0].name || availableCores[0].id} (Default)</option>
                            </select>
                            <div class="je-field-desc">Default Libretro core for ${_selectedSystem}. All display, control, and performance options are managed globally.</div>
                        </div>` : `
                        <div style="color:rgba(255,255,255,0.6);padding:1em 0;">No alternative cores available for ${_selectedSystem}. All other preferences follow your Global Settings.</div>
                        `}
                    </div>

                    <div class="je-actions" style="margin-top:24px">
                        <button id="je-save-sys" class="je-btn je-btn-primary">
                            <span class="material-icons" style="font-size:18px">save</span>
                            Save ${_selectedSystem} Settings
                        </button>
                        ${hasCustomCore ? `
                        <button id="je-clear-sys" class="je-btn je-btn-danger" style="margin-left:12px">
                            <span class="material-icons" style="font-size:18px">delete_sweep</span>
                            Reset to Default Core
                        </button>` : ''}
                    </div>`;

                const saveBtn = formContainer.querySelector('#je-save-sys');
                if (saveBtn) {
                    saveBtn.addEventListener('click', () => {
                        saveBtn.disabled = true;

                        const coreEl  = formContainer.querySelector('#je-sys-core');
                        const coreVal = coreEl ? coreEl.value : '';

                        const prefsObj = {};
                        if (coreVal) prefsObj.core = coreVal;
                        else prefsObj.core = null;

                        fetch(`/jellyemu/prefs/${userId}`, {
                            method: 'POST',
                            headers: {
                                'Content-Type': 'application/json',
                                'Authorization': `MediaBrowser Token="${token}"`
                            },
                            body: JSON.stringify({
                                scope: 'system',
                                targetId: _selectedSystem,
                                preferences: prefsObj
                            })
                        })
                        .then(r => r.json())
                        .then(() => {
                            showToast(`${_selectedSystem} settings updated.`);
                            loadSystemSettings(container);
                        })
                        .catch(err => {
                            console.error('[JellyEmu] Failed to save system settings:', err);
                            alert('Failed to save system settings.');
                            saveBtn.disabled = false;
                        });
                    });
                }

                const clearBtn = formContainer.querySelector('#je-clear-sys');
                if (clearBtn) {
                    clearBtn.addEventListener('click', () => {
                        if (confirm(`Reset ${_selectedSystem} core back to default?`)) {
                            clearBtn.disabled = true;
                            fetch(`/jellyemu/prefs/${userId}?scope=system&targetId=${encodeURIComponent(_selectedSystem)}`, {
                                method: 'DELETE',
                                headers: { 'Authorization': `MediaBrowser Token="${token}"` }
                            })
                            .then(r => r.json())
                            .then(() => {
                                showToast(`${_selectedSystem} core reset to default.`);
                                loadSystemSettings(container);
                            })
                            .catch(err => {
                                console.error('[JellyEmu] Failed to reset system core:', err);
                                alert('Failed to reset core.');
                            });
                        }
                    });
                }
            });
        }

        // Retroachievements Tab
        function renderRaTab(container) {
            container.innerHTML = `
                <div class="je-settings-section">
                    <h2 class="je-settings-section-heading">
                        <span class="material-icons" style="color:#f0c040">emoji_events</span>
                        RetroAchievements Account
                    </h2>
                    <div class="je-settings-section-desc">Connect your RetroAchievements account to unlock achievements and track game progress.</div>

                    <div class="je-input-container">
                        <label class="je-input-label">Username</label>
                        <input type="text" id="je-ra-user" class="je-input" placeholder="Enter RetroAchievements Username">
                    </div>

                    <div class="je-input-container">
                        <label class="je-input-label">Web API Key</label>
                        <input type="password" id="je-ra-key" class="je-input" placeholder="Enter RetroAchievements Web API Key">
                        <div class="je-field-desc">Find your API key at <a href="https://retroachievements.org/settings" target="_blank" style="color:var(--accent, #00a4dc)">retroachievements.org/settings</a> (under Web API Key)</div>
                    </div>

                    <div class="je-actions">
                        <button id="je-save-ra" class="je-btn je-btn-primary">
                            <span class="material-icons" style="font-size:18px">save</span>
                            Save Credentials
                        </button>
                    </div>
                </div>`;

            const userInp = container.querySelector('#je-ra-user');
            const keyInp  = container.querySelector('#je-ra-key');
            const saveBtn = container.querySelector('#je-save-ra');

            fetch(`/jellyemu/retroachievements/${userId}`, {
                headers: { 'Authorization': `MediaBrowser Token="${token}"` }
            })
            .then(r => r.ok ? r.json() : null)
            .then(data => {
                if (data) {
                    userInp.value = data.raUsername || '';
                    keyInp.value  = data.raApiKey   || '';
                }
            });

            saveBtn.addEventListener('click', () => {
                saveBtn.disabled = true;
                saveBtn.innerHTML = `<span class="material-icons" style="font-size:18px">sync</span> Saving...`;

                fetch(`/jellyemu/retroachievements/${userId}`, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Authorization': `MediaBrowser Token="${token}"`
                    },
                    body: JSON.stringify({
                        raUsername: userInp.value.trim(),
                        raApiKey: keyInp.value.trim()
                    })
                })
                .then(r => r.ok ? r.json() : Promise.reject())
                .then(() => {
                    showToast('RetroAchievements credentials saved.');
                    saveBtn.disabled = false;
                    saveBtn.innerHTML = `<span class="material-icons" style="font-size:18px">save</span> Save Credentials`;
                })
                .catch(() => {
                    alert('Failed to save RetroAchievements credentials.');
                    saveBtn.disabled = false;
                    saveBtn.innerHTML = `<span class="material-icons" style="font-size:18px">save</span> Save Credentials`;
                });
            });
        }

        renderContainer();
    };
})();
