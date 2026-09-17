/**
 * JellyEmu Core Options Manager
 *
 * Handles per-core options storage in localStorage and the Core Options configuration popup for EmulatorJS.
 * Core options are stored per core:
 *   Storage Key: 'jellyemu_coreopts_' + (window.EJS_core || 'default')
 *
 * Depends on:
 *   - window._jeOpenPopup
 *   - window._jeClosePopup
 *   - window.EJS_emulator / gameManager
 *   - window.EJS_core
 */
(function () {
    'use strict';

    function emu() {
        return window.EJS_emulator || window.emulator || (window.EJS && window.EJS.emulator) || null;
    }

    function gm() {
        var e = emu();
        return (e && e.gameManager) ? e.gameManager : (window.gameManager || window.EJS_gameManager || (e && e.Module ? e : null));
    }

    function getCoreName() {
        return window.EJS_core || (window.JellyEmuConfig && window.JellyEmuConfig.platformTag) || 'default';
    }

    function getStorageKey(coreName) {
        return 'jellyemu_coreopts_' + (coreName || getCoreName());
    }

    function loadSavedCoreOptions(coreName) {
        var key = getStorageKey(coreName);
        try {
            var raw = localStorage.getItem(key);
            if (raw) {
                var parsed = JSON.parse(raw);
                if (parsed && typeof parsed === 'object') {
                    return parsed;
                }
            }
        } catch (e) {
            console.warn('[JellyEmu] Failed to load core options from localStorage:', e);
        }
        return {};
    }

    function saveCoreOption(coreName, optKey, optVal) {
        var storageKey = getStorageKey(coreName);
        try {
            var opts = loadSavedCoreOptions(coreName);
            opts[optKey] = optVal;
            localStorage.setItem(storageKey, JSON.stringify(opts));
        } catch (e) {
            console.warn('[JellyEmu] Failed to save core option to localStorage:', e);
        }
    }

    function clearSavedCoreOptions(coreName) {
        var storageKey = getStorageKey(coreName);
        try {
            localStorage.removeItem(storageKey);
        } catch (e) {
            console.warn('[JellyEmu] Failed to clear core options from localStorage:', e);
        }
    }

    function applySavedCoreOptions() {
        var g = gm();
        if (!g || typeof g.setVariable !== 'function') return;
        var coreName = getCoreName();
        var saved = loadSavedCoreOptions(coreName);
        var keys = Object.keys(saved);
        if (keys.length === 0) return;

        console.log('[JellyEmu] Applying saved core options for core [' + coreName + ']:', saved);
        keys.forEach(function (k) {
            try {
                g.setVariable(k, saved[k]);
            } catch (err) {
                console.warn('[JellyEmu] Error setting core variable ' + k + ':', err);
            }
        });
    }

    function buildCoreOptions() {
        var body = document.getElementById('je-coreopts-body');
        if (!body) return;
        var g = gm();
        var coreName = getCoreName();

        if (!g || typeof g.getCoreOptions !== 'function') {
            body.innerHTML = '<div style="opacity:.4;font-size:13px;text-align:center;padding:20px;">Core options not available yet.</div>';
            return;
        }

        var optsRaw = g.getCoreOptions();
        if (!optsRaw || typeof optsRaw !== 'string') {
            body.innerHTML = '<div style="opacity:.4;font-size:13px;text-align:center;padding:20px;">No options available for this core.</div>';
            return;
        }

        var savedOpts = loadSavedCoreOptions(coreName);
        var lines = optsRaw.split('\n');
        body.innerHTML = '';
        var hasOpts = false;

        lines.forEach(function (line) {
            if (!line.trim()) return;

            // Splits "key|currentValue; val1|val2|val3"
            var parts = line.split(/;\s*/);
            if (parts.length < 2) return;

            var keyVal = parts[0].split('|');
            var key = keyVal[0];
            var currentVal = keyVal[1];
            var options = parts[1].split('|');

            hasOpts = true;

            // If we have a stored value in localStorage for this core, prioritize it
            var effectiveVal = (savedOpts && savedOpts[key] !== undefined) ? savedOpts[key] : currentVal;
            if (savedOpts && savedOpts[key] !== undefined && savedOpts[key] !== currentVal) {
                try {
                    g.setVariable(key, effectiveVal);
                } catch (e) {}
            }

            // Format pretty label (e.g. 'snes9x_overclock_cycles' -> 'Overclock cycles')
            var displayKey = key.replace(/^[^_]+_/, '').replace(/_/g, ' ');
            displayKey = displayKey.charAt(0).toUpperCase() + displayKey.slice(1);

            var row = document.createElement('div');
            row.className = 'je-setting';

            var label = document.createElement('span');
            label.className = 'je-setting-label';
            label.textContent = displayKey;

            var select = document.createElement('select');
            options.forEach(function (opt) {
                var option = document.createElement('option');
                option.value = opt;
                option.textContent = opt;
                if (opt === effectiveVal) option.selected = true;
                select.appendChild(option);
            });

            // Trigger the core change instantly on dropdown change and persist to localStorage
            select.addEventListener('change', function () {
                var newVal = this.value;
                try {
                    g.setVariable(key, newVal);
                } catch (e) {
                    console.warn('[JellyEmu] Failed to setVariable:', e);
                }
                saveCoreOption(coreName, key, newVal);
            });

            row.appendChild(label);
            row.appendChild(select);
            body.appendChild(row);
        });

        if (!hasOpts) {
            body.innerHTML = '<div style="opacity:.4;font-size:13px;text-align:center;padding:20px;">No options available for this core.</div>';
        }
    }

    function init() {
        var btnCoreOpts = document.getElementById('je-btn-coreopts');
        if (btnCoreOpts) {
            btnCoreOpts.addEventListener('click', function () {
                buildCoreOptions();
                if (typeof window._jeOpenPopup === 'function') {
                    window._jeOpenPopup('je-pop-coreopts');
                } else {
                    var el = document.getElementById('je-pop-coreopts');
                    if (el) el.classList.add('je-open');
                }
            });
        }

        // Apply saved core options as soon as game boots
        window.addEventListener('jellyemu:gamestart', function () {
            setTimeout(applySavedCoreOptions, 150);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window.JellyEmuCore = {
        buildCoreOptions: buildCoreOptions,
        applySavedCoreOptions: applySavedCoreOptions,
        loadSavedCoreOptions: loadSavedCoreOptions,
        saveCoreOption: saveCoreOption,
        clearSavedCoreOptions: clearSavedCoreOptions,
        getStorageKey: getStorageKey
    };
})();
