(function () {
    'use strict';

    var cfg         = window.JellyEmuConfig || {};
    var userId      = cfg.userId || '';
    var token       = cfg.token || '';
    var platformTag = cfg.platformTag || window.EJS_platformTag || '';

    var _sysPrefs = {};
    var _globPrefs = {};

    function emu() {
        return window.EJS_emulator || window.emulator || (window.EJS && window.EJS.emulator) || null;
    }

    function gm() {
        var e = emu();
        return (e && e.gameManager) ? e.gameManager : (window.gameManager || window.EJS_gameManager || (e && e.Module ? e : null));
    }

    function jeFetch(url, options) {
        options = options || {};
        if (token) {
            options.headers = options.headers || {};
            options.headers['Authorization'] = 'MediaBrowser Token="' + token + '"';
        }
        return fetch(url, options);
    }

    // Helper to calculate effective setting value: Global -> Initial Config -> Default
    function getEffectivePref(key, defaultVal) {
        if (_globPrefs && _globPrefs[key] !== undefined && _globPrefs[key] !== null && _globPrefs[key] !== '') {
            return _globPrefs[key];
        }
        if (cfg) {
            if (key === 'shader' && cfg.activeShader) return cfg.activeShader;
            if (cfg[key] !== undefined && cfg[key] !== null && cfg[key] !== '') {
                return cfg[key];
            }
        }
        return defaultVal;
    }

    // Setting Application Handlers

    function normalizeShader(val) {
        if (!val || val === 'none' || val === 'disabled' || val === '0') return 'disabled';
        var s = String(val).trim();
        if (s.toLowerCase() === 'crt-easymode') return 'crt-easymode.glslp';
        if (s.toLowerCase() === '2xscalehq') return '2xScaleHQ.glslp';
        if (s.toLowerCase() === '4xscalehq') return '4xScaleHQ.glslp';
        if (s.toLowerCase() === 'crt-aperture') return 'crt-aperture.glslp';
        if (s.toLowerCase() === 'crt-geom') return 'crt-geom.glslp';
        if (s.toLowerCase() === 'crt-mattias') return 'crt-mattias.glslp';
        return s;
    }

    function applyLiveShader() {
        var raw = getEffectivePref('shader', 'crt-easymode.glslp');
        var s = normalizeShader(raw);
        var e = emu();
        var g = (e && e.gameManager) ? e.gameManager : gm();
        if (!e && !g) return;

        if (g) {
            if (s === 'disabled' || s === 'none') {
                if (typeof g.disableShader === 'function') {
                    try { g.disableShader(); } catch (ex) { console.warn('[JellyEmu] disableShader error:', ex); }
                } else if (typeof g.enableShader === 'function') {
                    try { g.enableShader(null); } catch (ex) { }
                } else if (typeof g.loadShader === 'function') {
                    try { g.loadShader('disabled'); } catch (ex) { }
                }
            } else {
                if (typeof g.enableShader === 'function') {
                    try { g.enableShader(s); } catch (ex) { console.warn('[JellyEmu] enableShader error:', ex); }
                } else if (typeof g.loadShader === 'function') {
                    try { g.loadShader(s); } catch (ex) { }
                } else if (typeof g.setShader === 'function') {
                    try { g.setShader(s); } catch (ex) { }
                }
            }
        }

        if (e) {
            if (e.settings) e.settings['shader'] = s;
            if (typeof e.changeSettingOption === 'function') {
                try { e.changeSettingOption('shader', s); } catch (ex) { }
            }
        }
    }

    function applyLiveRotation() {
        var r = getEffectivePref('videoRotation', '0');
        var num = parseInt(r) || 0;
        var rotIndex = 0;
        if (num === 90 || num === 1) rotIndex = 1;
        else if (num === 180 || num === 2) rotIndex = 2;
        else if (num === 270 || num === 3) rotIndex = 3;

        var e = emu();
        if (e && typeof e.changeSettingOption === 'function') {
            if (e.settings) delete e.settings['videoRotation'];
            try { e.changeSettingOption('videoRotation', rotIndex); } catch (ex) { }
        }
    }

    function applyLiveFfRate() {
        var ff = getEffectivePref('ffrate', '3');
        var e = emu();
        if (e && typeof e.changeSettingOption === 'function') {
            if (e.settings) delete e.settings['ff-ratio'];
            try { e.changeSettingOption('ff-ratio', ff); } catch (ex) { }
        }
    }

    function applyLiveSmRate() {
        var sm = getEffectivePref('smrate', '3');
        var e = emu();
        if (e && typeof e.changeSettingOption === 'function') {
            if (e.settings) delete e.settings['sm-ratio'];
            try { e.changeSettingOption('sm-ratio', sm); } catch (ex) { }
        }
    }

    function applyLiveScreenSize() {
        var size = getEffectivePref('scale', 'fit');

        // Normalize legacy aliases
        if (size === 'aspect') size = 'fit';
        if (size === 'native') size = '1';
        if (size === '2x') size = '2';
        if (size === '3x') size = '3';
        if (size === '4x') size = '4';

        var gameEl = document.getElementById('game');
        var ejsParent = document.querySelector('.ejs_parent');
        var canvasParent = document.querySelector('.ejs_canvas_parent');
        var canvas = document.querySelector('canvas.ejs_canvas') || document.querySelector('#game canvas') || document.querySelector('canvas');

        if (gameEl) gameEl.setAttribute('data-scale', size);
        if (ejsParent) ejsParent.setAttribute('data-scale', size);

        if (canvasParent) {
            canvasParent.style.setProperty('display', 'flex', 'important');
            canvasParent.style.setProperty('align-items', 'center', 'important');
            canvasParent.style.setProperty('justify-content', 'center', 'important');
            canvasParent.style.setProperty('width', '100%', 'important');
            canvasParent.style.setProperty('height', '100%', 'important');
            canvasParent.style.setProperty('overflow', 'hidden', 'important');
        }

        if (!canvas) return;

        if (size === 'fit') {
            canvas.style.setProperty('width', '100%', 'important');
            canvas.style.setProperty('height', '100%', 'important');
            canvas.style.setProperty('max-width', '100vw', 'important');
            canvas.style.setProperty('max-height', '100vh', 'important');
            canvas.style.setProperty('object-fit', 'contain', 'important');
            canvas.style.setProperty('image-rendering', 'auto', 'important');
        } else if (size === 'stretch') {
            canvas.style.setProperty('width', '100vw', 'important');
            canvas.style.setProperty('height', '100vh', 'important');
            canvas.style.setProperty('max-width', '100vw', 'important');
            canvas.style.setProperty('max-height', '100vh', 'important');
            canvas.style.setProperty('object-fit', 'fill', 'important');
            canvas.style.setProperty('image-rendering', 'auto', 'important');
        } else {
            var mult = parseInt(size) || 1;
            var e = emu();
            var nativeW = (e && e.gameManager && typeof e.gameManager.getVideoDimensions === 'function')
                ? e.gameManager.getVideoDimensions('width')
                : (canvas.width || 256);
            var nativeH = (e && e.gameManager && typeof e.gameManager.getVideoDimensions === 'function')
                ? e.gameManager.getVideoDimensions('height')
                : (canvas.height || 224);

            if (!nativeW || nativeW <= 0) nativeW = 256;
            if (!nativeH || nativeH <= 0) nativeH = 224;

            canvas.style.setProperty('width', (nativeW * mult) + 'px', 'important');
            canvas.style.setProperty('height', (nativeH * mult) + 'px', 'important');
            canvas.style.setProperty('max-width', 'none', 'important');
            canvas.style.setProperty('max-height', 'none', 'important');
            canvas.style.setProperty('object-fit', 'contain', 'important');
            canvas.style.setProperty('image-rendering', 'pixelated', 'important');
        }
    }

    var fpsEl = document.getElementById('je-fps');
    var fpsMode = '0';
    var _fpsRafId = null;
    var fpsFrames = 0;
    var fpsLast = performance.now();
    var _prevFrameTime = performance.now();
    var _frameDeltas = [];
    var _stutterCount = 0;

    function fpsLoop(now) {
        if (fpsMode === '0') {
            _fpsRafId = null;
            return;
        }

        var delta = now - _prevFrameTime;
        _prevFrameTime = now;

        if (delta > 0 && delta < 500) {
            _frameDeltas.push(delta);
            if (delta > 24) { // Stutter detection (>24ms on ~16.6ms target)
                _stutterCount++;
            }
        }
        fpsFrames++;

        if (now - fpsLast >= 1000) {
            var elapsed = now - fpsLast;
            var currentFps = ((fpsFrames * 1000) / elapsed).toFixed(1);

            if (fpsEl) {
                if (fpsMode === '1') {
                    fpsEl.innerHTML = '<div class="je-fps-row"><span class="material-icons">speed</span><span>' + Math.round(currentFps) + ' FPS</span></div>';
                } else if (fpsMode === '2') {
                    var avgMs = (elapsed / fpsFrames).toFixed(1);
                    var minMs = _frameDeltas.length ? Math.min.apply(null, _frameDeltas).toFixed(1) : avgMs;
                    var maxMs = _frameDeltas.length ? Math.max.apply(null, _frameDeltas).toFixed(1) : avgMs;

                    var canvas = document.querySelector('canvas.ejs_canvas') || document.querySelector('#game canvas') || document.querySelector('canvas');
                    var bufInfo = canvas ? (canvas.width + '×' + canvas.height) : 'N/A';
                    var dispInfo = 'N/A';
                    if (canvas) {
                        var r = canvas.getBoundingClientRect();
                        dispInfo = Math.round(r.width) + '×' + Math.round(r.height);
                    }

                    var rows = [
                        '<div class="je-fps-row"><span class="material-icons">speed</span><span>' + currentFps + ' FPS (' + avgMs + ' ms)</span></div>',
                        '<div class="je-fps-row"><span class="material-icons">timer</span><span>Timing: ' + minMs + ' - ' + maxMs + ' ms' + (_stutterCount > 0 ? ' (' + _stutterCount + ' stutters)' : '') + '</span></div>',
                        '<div class="je-fps-row"><span class="material-icons">tv</span><span>Canvas: ' + bufInfo + ' → ' + dispInfo + '</span></div>'
                    ];

                    var coreName = window.EJS_core || (window.JellyEmuConfig && window.JellyEmuConfig.platformTag);
                    if (coreName) {
                        rows.push('<div class="je-fps-row"><span class="material-icons">sports_esports</span><span>Core: ' + coreName + '</span></div>');
                    }

                    if (window.performance && window.performance.memory && window.performance.memory.usedJSHeapSize) {
                        var mb = Math.round(window.performance.memory.usedJSHeapSize / (1024 * 1024));
                        rows.push('<div class="je-fps-row"><span class="material-icons">memory</span><span>Heap: ' + mb + ' MB</span></div>');
                    }

                    fpsEl.innerHTML = rows.join('');
                }
            }

            fpsFrames = 0;
            fpsLast = now;
            _frameDeltas = [];
            _stutterCount = 0;
        }

        _fpsRafId = requestAnimationFrame(fpsLoop);
    }

    function applyLiveFps() {
        var raw = getEffectivePref('showFps', '0');
        var v = String(raw).trim().toLowerCase();
        if (!fpsEl) fpsEl = document.getElementById('je-fps');

        if (v === '1' || v === 'fps' || v === 'true') {
            fpsMode = '1';
        } else if (v === '2' || v === 'detailed') {
            fpsMode = '2';
        } else {
            fpsMode = '0';
        }

        if (fpsMode !== '0') {
            if (fpsEl) {
                fpsEl.classList.add('je-active');
                if (fpsMode === '2') {
                    fpsEl.classList.add('je-detailed');
                } else {
                    fpsEl.classList.remove('je-detailed');
                }
            }
            if (!_fpsRafId) {
                fpsFrames = 0;
                fpsLast = performance.now();
                _prevFrameTime = performance.now();
                _frameDeltas = [];
                _stutterCount = 0;
                _fpsRafId = requestAnimationFrame(fpsLoop);
            }
        } else {
            if (fpsEl) {
                fpsEl.classList.remove('je-active');
                fpsEl.classList.remove('je-detailed');
            }
            if (_fpsRafId) {
                cancelAnimationFrame(_fpsRafId);
                _fpsRafId = null;
            }
        }
    }

    document.addEventListener('visibilitychange', function () {
        if (!document.hidden && fpsMode !== '0') {
            fpsFrames = 0;
            fpsLast = performance.now();
            _prevFrameTime = performance.now();
            _frameDeltas = [];
            _stutterCount = 0;
        }
    });

    function applyAllLiveSettings() {
        applyLiveShader();
        applyLiveRotation();
        applyLiveFfRate();
        applyLiveSmRate();
        applyLiveScreenSize();
        applyLiveFps();
    }

    // Apply when emulator boots
    window.addEventListener('jellyemu:gamestart', function () {
        setTimeout(applyAllLiveSettings, 100);
        setTimeout(applyAllLiveSettings, 500);
    });

    // Tab Management
    var tabSysBtn  = document.getElementById('je-set-tab-sys');
    var tabGlobBtn = document.getElementById('je-set-tab-glob');
    var panelSys   = document.getElementById('je-panel-sys-settings');
    var panelGlob  = document.getElementById('je-panel-glob-settings');

    if (tabSysBtn && tabGlobBtn && panelSys && panelGlob) {
        tabSysBtn.addEventListener('click', function () {
            tabSysBtn.classList.add('je-tab-active');
            tabGlobBtn.classList.remove('je-tab-active');
            panelSys.style.display = 'flex';
            panelGlob.style.display = 'none';
        });
        tabGlobBtn.addEventListener('click', function () {
            tabGlobBtn.classList.add('je-tab-active');
            tabSysBtn.classList.remove('je-tab-active');
            panelGlob.style.display = 'flex';
            panelSys.style.display = 'none';
        });
    }

    // Scoped Data Fetch & UI Synchronization
    function updateOverrideBadge() {
        var badge = document.getElementById('je-sys-override-badge');
        if (!badge) return;
        if (_sysPrefs && _sysPrefs.core) {
            badge.style.display = 'inline-block';
            badge.textContent = 'Custom Core';
        } else {
            badge.style.display = 'none';
        }
    }

    function populateCoreSelect() {
        var coreSelect = document.getElementById('je-set-core');
        if (!coreSelect) return;

        jeFetch('/jellyemu/systems')
            .then(function (r) { return r.ok ? r.json() : {}; })
            .then(function (data) {
                if (!data || !data.systems) return;
                var sys = data.systems.find(function (s) { return s.name.toLowerCase() === platformTag.toLowerCase(); });
                if (!sys || !sys.cores || sys.cores.length === 0) return;

                var currentVal = _sysPrefs.core || '';
                coreSelect.innerHTML = '';

                if (sys.cores.length === 1) {
                    var opt = document.createElement('option');
                    opt.value = sys.cores[0].id;
                    opt.textContent = (sys.cores[0].name || sys.cores[0].id) + ' (Default)';
                    opt.selected = true;
                    coreSelect.appendChild(opt);
                    coreSelect.disabled = true;
                } else {
                    coreSelect.disabled = false;
                    sys.cores.forEach(function (c, index) {
                        var opt = document.createElement('option');
                        opt.value = c.id;
                        opt.textContent = (c.name || c.id) + (index === 0 ? ' (Default)' : '');
                        if (currentVal ? c.id === currentVal : index === 0) opt.selected = true;
                        coreSelect.appendChild(opt);
                    });
                }
            })
            .catch(function (e) { console.warn('[JellyEmu] Failed loading core options:', e); });
    }

    function populateSelect(elId, optionsList, activeVal) {
        var el = document.getElementById(elId);
        if (!el || !Array.isArray(optionsList) || optionsList.length === 0) return;
        el.innerHTML = '';
        optionsList.forEach(function (opt) {
            var o = document.createElement('option');
            o.value = opt.id;
            o.textContent = opt.label;
            if (String(opt.id) === String(activeVal)) o.selected = true;
            el.appendChild(o);
        });
    }

    function populateSettingOptions() {
        return jeFetch('/jellyemu/setting-options')
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (opts) {
                if (!opts) return;
                var defaultShader = normalizeShader(cfg.activeShader) || 'crt-easymode.glslp';
                if (defaultShader === 'disabled' && cfg.activeShader !== 'disabled' && cfg.activeShader !== 'none') defaultShader = 'crt-easymode.glslp';

                populateSelect('je-glob-shader', opts.shaders, _globPrefs.shader ? normalizeShader(_globPrefs.shader) : defaultShader);
                populateSelect('je-glob-screensize', opts.scaling, _globPrefs.scale ? normalizeScale(_globPrefs.scale) : (normalizeScale(cfg.scale) || 'fit'));
                populateSelect('je-glob-rotation', opts.rotation, _globPrefs.videoRotation !== undefined ? String(_globPrefs.videoRotation) : (cfg.videoRotation !== undefined ? String(cfg.videoRotation) : '0'));
                populateSelect('je-glob-fps', opts.fps, _globPrefs.showFps !== undefined ? String(_globPrefs.showFps) : (cfg.showFps !== undefined ? String(cfg.showFps) : '0'));
                populateSelect('je-glob-ffrate', opts.fastForwardRates, _globPrefs.ffrate || cfg.ffrate || '3');
                populateSelect('je-glob-smrate', opts.slowMotionRates, _globPrefs.smrate || cfg.smrate || '3');
                populateSelect('je-glob-autosave', opts.autosave, _globPrefs.autosave || '0');
                populateSelect('je-glob-haptics', opts.haptics, _globPrefs.haptics !== undefined ? String(_globPrefs.haptics) : '1');
            })
            .catch(function (e) { console.warn('[JellyEmu] Failed loading setting options:', e); });
    }

    function normalizeScale(val) {
        if (!val) return '';
        var s = String(val).trim().toLowerCase();
        if (s === 'aspect') return 'fit';
        if (s === 'native') return '1';
        if (s === '2x') return '2';
        if (s === '3x') return '3';
        if (s === '4x') return '4';
        return s;
    }

    function loadSettingsData() {
        // Fetch System Scoped Settings
        var pSys = jeFetch('/jellyemu/prefs/' + userId + '?scope=system&targetId=' + encodeURIComponent(platformTag))
            .then(function (r) { return r.ok ? r.json() : {}; })
            .then(function (data) {
                _sysPrefs = (data && data.preferences) || {};
                updateOverrideBadge();
                populateCoreSelect();
            }).catch(function (e) { console.warn('[JellyEmu] Failed loading system prefs:', e); });

        // Fetch Global Settings
        var pGlob = jeFetch('/jellyemu/prefs/' + userId + '?scope=global')
            .then(function (r) { return r.ok ? r.json() : {}; })
            .then(function (data) {
                _globPrefs = (data && data.preferences) || {};
                return populateSettingOptions();
            }).catch(function (e) { console.warn('[JellyEmu] Failed loading global prefs:', e); });

        return Promise.all([pSys, pGlob]);
    }

    // Pre-load prefs on initial page load so effective defaults are ready
    loadSettingsData();

    var settingsBtn = document.getElementById('je-btn-settings');
    if (settingsBtn) {
        settingsBtn.addEventListener('click', function () {
            loadSettingsData().then(function () {
                if (window._jeOpenPopup) window._jeOpenPopup('je-pop-settings');
            });
        });
    }

    // Save System Settings
    var btnSaveSys = document.getElementById('je-btn-save-sys');
    if (btnSaveSys) {
        btnSaveSys.addEventListener('click', function () {
            var btn = this;
            btn.disabled = true;
            btn.textContent = 'Saving...';

            var coreEl = document.getElementById('je-set-core');
            var coreVal = (coreEl && coreEl.value) ? coreEl.value : null;
            var currentCore = window.EJS_core || '';
            var coreChanged = (coreVal && currentCore && coreVal !== currentCore);
            var payload = { core: coreVal };

            if (coreVal) {
                _sysPrefs.core = coreVal;
            } else {
                delete _sysPrefs.core;
            }
            updateOverrideBadge();

            jeFetch('/jellyemu/prefs/' + userId, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    scope: 'system',
                    targetId: platformTag,
                    preferences: payload
                })
            })
            .then(function (r) { return r.json(); })
            .then(function (savedData) {
                if (savedData && savedData.preferences) _sysPrefs = savedData.preferences;
                btn.textContent = 'Saved!';
                updateOverrideBadge();

                if (coreChanged) {
                    btn.textContent = 'Saved! Applying...';
                    setTimeout(function () {
                        var reloadUrl = new URL(window.location.href);
                        reloadUrl.searchParams.delete('core');
                        window.location.href = reloadUrl.toString();
                    }, 500);
                } else {
                    setTimeout(function () {
                        btn.disabled = false;
                        btn.textContent = 'Save ' + platformTag + ' Settings';
                    }, 1000);
                }
            })
            .catch(function (err) {
                console.error('[JellyEmu] Save system settings failed:', err);
                alert('Failed to save system settings.');
                btn.disabled = false;
                btn.textContent = 'Save ' + platformTag + ' Settings';
            });
        });
    }

    // Clear System Overrides Button
    var btnClearSys = document.getElementById('je-btn-clear-sys');
    if (btnClearSys) {
        btnClearSys.addEventListener('click', function () {
            if (!confirm('Reset custom core for ' + platformTag + ' back to default?')) return;
            var btn = this;
            btn.disabled = true;
            btn.textContent = 'Resetting...';

            var hadCustomCore = !!_sysPrefs.core;
            delete _sysPrefs.core;
            updateOverrideBadge();

            jeFetch('/jellyemu/prefs/' + userId + '?scope=system&targetId=' + encodeURIComponent(platformTag), {
                method: 'DELETE'
            })
            .then(function (r) { return r.json(); })
            .then(function () {
                _sysPrefs = {};
                if (hadCustomCore) {
                    btn.textContent = 'Reset! Applying...';
                    setTimeout(function () {
                        var reloadUrl = new URL(window.location.href);
                        reloadUrl.searchParams.delete('core');
                        window.location.href = reloadUrl.toString();
                    }, 500);
                } else {
                    loadSettingsData().then(function () {
                        btn.disabled = false;
                        btn.textContent = 'Reset Factory Defaults';
                    });
                }
            })
            .catch(function (err) {
                console.error('[JellyEmu] Reset core failed:', err);
                alert('Failed to reset core.');
                btn.disabled = false;
                btn.textContent = 'Reset Factory Defaults';
            });
        });
    }

    // Save Global Settings Button
    var btnSaveGlob = document.getElementById('je-btn-save-glob');
    if (btnSaveGlob) {
        btnSaveGlob.addEventListener('click', function () {
            var btn = this;
            btn.disabled = true;
            btn.textContent = 'Saving...';

            var getVal = function (id, fallback) {
                var el = document.getElementById(id);
                return el ? el.value : (fallback !== undefined ? fallback : '');
            };

            var payload = {
                shader: getVal('je-glob-shader', 'crt-easymode.glslp'),
                scale: getVal('je-glob-screensize', 'fit'),
                videoRotation: getVal('je-glob-rotation', '0'),
                showFps: getVal('je-glob-fps', '0'),
                ffrate: getVal('je-glob-ffrate', '3'),
                smrate: getVal('je-glob-smrate', '3'),
                autosave: getVal('je-glob-autosave', '0'),
                haptics: getVal('je-glob-haptics', '1')
            };

            // Instantly apply in memory
            _globPrefs = Object.assign({}, _globPrefs, payload);
            applyAllLiveSettings();

            jeFetch('/jellyemu/prefs/' + userId, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    scope: 'global',
                    targetId: '',
                    preferences: payload
                })
            })
            .then(function (r) { return r.json(); })
            .then(function (savedData) {
                if (savedData && savedData.preferences) _globPrefs = savedData.preferences;
                btn.textContent = 'Saved!';
                setTimeout(function () {
                    btn.disabled = false;
                    btn.textContent = 'Save Global Settings';
                }, 1000);
            })
            .catch(function (err) {
                console.error('[JellyEmu] Save global settings failed:', err);
                alert('Failed to save global settings.');
                btn.disabled = false;
                btn.textContent = 'Save Global Settings';
            });
        });
    }

    // Reset Factory Defaults Button
    var btnResetFactory = document.getElementById('je-btn-reset-factory');
    if (btnResetFactory) {
        btnResetFactory.addEventListener('click', function () {
            if (!confirm('Are you sure you want to reset ALL emulator settings and system overrides back to factory defaults?')) return;
            var btn = this;
            btn.disabled = true;

            _sysPrefs = {};
            _globPrefs = {};
            applyAllLiveSettings();

            jeFetch('/jellyemu/prefs/' + userId + '/reset', {
                method: 'DELETE'
            })
            .then(function (r) { return r.json(); })
            .then(function () {
                alert('All settings reset to factory defaults.');
                _sysPrefs = {};
                _globPrefs = {};
                loadSettingsData().then(function () {
                    btn.disabled = false;
                    applyAllLiveSettings();
                });
            })
            .catch(function (err) {
                console.error('[JellyEmu] Reset failed:', err);
                alert('Failed to reset settings.');
                btn.disabled = false;
            });
        });
    }

    // Game Cache Deletion (Targeting EmulatorJS-roms IndexedDB database)
    function deleteGameCache() {
        var gameUrl = window.EJS_gameUrl || '';
        var filename = gameUrl ? gameUrl.split('/').pop() : '';
        var decFilename = decodeURIComponent(filename);
        var bareName = filename.split('?')[0];
        var decBare = decFilename.split('?')[0];
        var itemId = (cfg && cfg.itemId) || '';

        function matches(k) {
            if (!k || typeof k !== 'string') return false;
            var s = k.toLowerCase();
            return (filename && s.indexOf(filename.toLowerCase()) !== -1) ||
                   (decFilename && s.indexOf(decFilename.toLowerCase()) !== -1) ||
                   (bareName && s.indexOf(bareName.toLowerCase()) !== -1) ||
                   (decBare && s.indexOf(decBare.toLowerCase()) !== -1) ||
                   (itemId && s.indexOf(itemId.toLowerCase()) !== -1);
        }

        return new Promise(function (resolve) {
            if (!window.indexedDB) return resolve(0);
            var req = indexedDB.open('EmulatorJS-roms');
            req.onerror = req.onblocked = function () { resolve(0); };
            req.onsuccess = function (ev) {
                var db = ev.target.result;
                if (!db.objectStoreNames.contains('rom')) { db.close(); return resolve(0); }

                var tx = db.transaction('rom', 'readwrite');
                var store = tx.objectStore('rom');
                var count = 0;

                // Clean ?EJS_KEYS! index array
                var indexReq = store.get('?EJS_KEYS!');
                indexReq.onsuccess = function () {
                    var keys = indexReq.result;
                    if (Array.isArray(keys)) {
                        var filtered = keys.filter(function (k) { return !matches(k); });
                        if (filtered.length !== keys.length) {
                            count += (keys.length - filtered.length);
                            store.put(filtered, '?EJS_KEYS!');
                        }
                    }
                };

                // Scan and delete matching ROM entries
                var curReq = store.openCursor();
                curReq.onsuccess = function (e) {
                    var cursor = e.target.result;
                    if (cursor) {
                        if (cursor.key !== '?EJS_KEYS!' && matches(cursor.key)) {
                            cursor.delete();
                            count++;
                        }
                        cursor.continue();
                    }
                };

                tx.oncomplete = tx.onerror = function () {
                    db.close();
                    resolve(count);
                };
            };
        });
    }

    // Delete Game Cache Button
    var btnClearCache = document.getElementById('je-btn-clear-cache');
    if (btnClearCache) {
        btnClearCache.addEventListener('click', function () {
            var btn = this;
            if (!confirm('Delete this game\'s downloaded cache from browser storage?\n\nIt will be re-downloaded on next launch.')) return;
            btn.disabled = true;
            btn.textContent = 'Deleting...';

            deleteGameCache().then(function (count) {
                btn.textContent = 'Cache Deleted!';
                btn.style.color = '#52B54B';
                btn.style.borderColor = 'rgba(82, 181, 75, 0.4)';
                alert('Local game cache deleted (' + count + ' entry removed).\n\nThe ROM will re-download on next start.');
                setTimeout(function () {
                    btn.disabled = false;
                    btn.textContent = 'Delete Cache';
                    btn.style.color = '#ff6b6b';
                    btn.style.borderColor = 'rgba(255,100,100,0.3)';
                }, 2500);
            }).catch(function (err) {
                console.error('[JellyEmu] Error clearing cache:', err);
                alert('Failed to clear cache: ' + err);
                btn.disabled = false;
                btn.textContent = 'Delete Cache';
            });
        });
    }
})();
