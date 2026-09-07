(function () {
    var cfg = window.JellyEmuConfig || {};
    var itemId      = cfg.itemId      || '';
    var userId      = cfg.userId      || '';
    var savePostUrl = cfg.savePostUrl || '';
    var activeSlot  = cfg.activeSlot  || 1;
    var token       = cfg.token       || '';

    // - Helpers -
    function emu() { return window.EJS_emulator; }
    function gm()  { var e = emu(); return e ? e.gameManager : null; }
    function _jeEnsureBinary(data) { return window._jeEnsureBinary ? window._jeEnsureBinary(data) : data; }
    function openPopup(id)  { window._jeOpenPopup  && window._jeOpenPopup(id);  }
    function closePopup(id) { window._jeClosePopup && window._jeClosePopup(id); }
    function syncVGToggles(){ window._jeSyncVGToggles && window._jeSyncVGToggles(); }
    function _isInputWindowOpen() {
        var pop = document.getElementById('je-pop-inputmap');
        return !!(pop && (pop.classList.contains('je-open') || pop.classList.contains('je-popup-active')));
    }

    // ==========================================
    // - EMULATORJS SYSTEM INPUT SCHEMES -
    // Ref: https://github.com/EmulatorJS/EmulatorJS/blob/0b1c5e94d8df0db7509b211fb9ffc72d2805948a/data/src/emulator.js#L2817
    // ==========================================
    var HOTKEYS = [
        { id: 24, label: 'QUICK SAVE', description: 'Save state immediately' },
        { id: 25, label: 'QUICK LOAD', description: 'Load state immediately' },
        { id: 26, label: 'CHANGE SLOT', description: 'Cycle active save state slot' },
        { id: 27, label: 'FAST FORWARD', description: 'Toggle fast forward emulation' },
        { id: 28, label: 'REWIND', description: 'Rewind gameplay in real time' },
        { id: 29, label: 'SLOW MOTION', description: 'Toggle slow motion gameplay' }
    ];

    // ==========================================
    // - BACKEND SINGLE SOURCE OF TRUTH SCHEME -
    // Initialized from window.JellyEmuConfig.inputScheme and synchronized via /jellyemu/input/schemes
    // ==========================================
    var _activeBackendScheme = (window.JellyEmuConfig && window.JellyEmuConfig.inputScheme) || null;
    var SYSTEM_SCHEMES = {};
    if (_activeBackendScheme && _activeBackendScheme.id) {
        SYSTEM_SCHEMES[_activeBackendScheme.id] = _activeBackendScheme;
    }

    // Generic fallback for offline or uninitialized state
    SYSTEM_SCHEMES['default'] = {
        name: 'Standard Controller',
        buttons: [
            { id: 8, label: 'A' }, { id: 0, label: 'B' }, { id: 9, label: 'X' }, { id: 1, label: 'Y' },
            { id: 2, label: 'SELECT' }, { id: 3, label: 'START' },
            { id: 4, label: 'UP' }, { id: 5, label: 'DOWN' }, { id: 6, label: 'LEFT' }, { id: 7, label: 'RIGHT' },
            { id: 10, label: 'L1' }, { id: 11, label: 'R1' }
        ],
        analogAxes: []
    };

    var _jeCoreOrTagToScheme = {
        'melonds': 'nds', 'desmume': 'nds', 'desmume2015': 'nds', 'nds': 'nds', 'nintendo ds': 'nds', 'ds': 'nds',
        'mgba': 'gba', 'vba_next': 'gba', 'gba': 'gba', 'game boy advance': 'gba',
        'gambatte': 'gb', 'sameboy': 'gb', 'gb': 'gb', 'gbc': 'gb', 'game boy': 'gb', 'game boy color': 'gb',
        'nestopia': 'nes', 'fceumm': 'nes', 'nes': 'nes', 'famicom': 'nes',
        'snes9x': 'snes', 'bsnes': 'snes', 'snes9x2010': 'snes', 'snes9x2005': 'snes', 'snes': 'snes', 'super nintendo': 'snes',
        'mupen64plus_next': 'n64', 'parallel_n64': 'n64', 'n64': 'n64', 'nintendo 64': 'n64',
        'beetle_vb': 'vb', 'vb': 'vb', 'virtual boy': 'vb',
        'genesis_plus_gx': 'segaMD', 'genesis_plus_gx_wide': 'segaMD', 'picodrive': 'segaMD', 'segamd': 'segaMD', 'genesis': 'segaMD', 'sega genesis': 'segaMD', 'mega drive': 'segaMD',
        'smsplus': 'segaMS', 'segams': 'segaMS', 'master system': 'segaMS',
        'segagg': 'segaGG', 'game gear': 'segaGG',
        'yabause': 'segaSaturn', 'saturn': 'segaSaturn', 'sega saturn': 'segaSaturn',
        'pcsx_rearmed': 'psx', 'mednafen_psx_hw': 'psx', 'psx': 'psx', 'playstation': 'psx', 'ps1': 'psx',
        'ppsspp': 'psp', 'psp': 'psp', 'playstation portable': 'psp'
    };

    function _jeResolveSchemeKey(input) {
        if (!input) return '';
        var s = String(input).toLowerCase().trim();
        if (_jeCoreOrTagToScheme[s]) return _jeCoreOrTagToScheme[s];
        var clean = s.replace(/_/g, ' ');
        if (_jeCoreOrTagToScheme[clean]) return _jeCoreOrTagToScheme[clean];
        return s;
    }

    function getActiveControlScheme() {
        if (_activeBackendScheme && _activeBackendScheme.id) {
            return _activeBackendScheme.id;
        }
        var e = emu();
        if (e && typeof e.getControlScheme === 'function') {
            try {
                var cs = e.getControlScheme();
                if (cs && SYSTEM_SCHEMES[cs]) return cs;
                var mappedCs = _jeResolveSchemeKey(cs);
                if (mappedCs && SYSTEM_SCHEMES[mappedCs]) return mappedCs;
            } catch (err) {}
        }
        var core = (window.EJS_core || '').toLowerCase();
        var mappedCore = _jeResolveSchemeKey(core);
        if (mappedCore && SYSTEM_SCHEMES[mappedCore]) return mappedCore;

        var tag = (window.EJS_platformTag || '').toLowerCase().trim();
        var mappedTag = _jeResolveSchemeKey(tag);
        if (mappedTag && SYSTEM_SCHEMES[mappedTag]) return mappedTag;

        if (mappedCore) return mappedCore;
        if (mappedTag) return mappedTag;
        return 'default';
    }

    function getActiveSchemeDefinition() {
        var key = getActiveControlScheme();
        return SYSTEM_SCHEMES[key] || SYSTEM_SCHEMES['default'];
    }

    function getActiveInputButtons() {
        var scheme = getActiveSchemeDefinition();
        var buttons = (scheme && scheme.buttons) ? scheme.buttons.slice() : [];
        var hasHotkeys = buttons.some(function (b) { return b.id >= 24; });
        if (!hasHotkeys) {
            buttons = buttons.concat(HOTKEYS);
        }
        return buttons;
    }

    function getActiveInputMap() {
        var buttons = getActiveInputButtons();
        var map = {};
        for (var i = 0; i < buttons.length; i++) {
            map[buttons[i].id] = buttons[i].label;
        }
        return map;
    }

    function _jeIsN64() {
        return getActiveControlScheme() === 'n64';
    }

    // - Hotkey handlers -
    var _jeActiveSlot = activeSlot;

    function _jeHotkeyAction(idx) {
        switch (idx) {
            case 24: // Quick Save
                var g = gm(); if (!g) return;
                Promise.resolve(g.getState()).then(function (rawState) {
                    var state = _jeEnsureBinary(rawState); if (!state) return;
                    var saveHeaders = { 'Content-Type': 'application/octet-stream' };
                    if (token) saveHeaders['Authorization'] = 'MediaBrowser Token="' + token + '"';
                    fetch('/jellyemu/save/' + itemId + '/' + userId + '?slot=' + _jeActiveSlot, {
                        method: 'POST', headers: saveHeaders, body: state
                    }).then(function (r) {
                        if (!r.ok) throw new Error('Save rejected');
                        var canvas = document.querySelector('canvas.ejs_canvas') || document.querySelector('canvas');
                        if (canvas) {
                            try {
                                var ssHeaders = { 'Content-Type': 'application/json' };
                                if (token) ssHeaders['Authorization'] = 'MediaBrowser Token="' + token + '"';
                                fetch('/jellyemu/save-screenshot/' + itemId + '/' + userId + '/' + _jeActiveSlot, {
                                    method: 'POST', headers: ssHeaders,
                                    body: JSON.stringify({ dataUrl: canvas.toDataURL('image/png') })
                                }).catch(function () {});
                            } catch (e) {}
                        }
                    }).catch(function (err) { console.error('[JellyEmu] Quick save failed:', err); });
                });
                break;
            case 25: // Quick Load
                var loadHeaders = {};
                if (token) loadHeaders['Authorization'] = 'MediaBrowser Token="' + token + '"';
                fetch('/jellyemu/save/' + itemId + '/' + userId + '?slot=' + _jeActiveSlot, { headers: loadHeaders })
                    .then(function (r) { if (!r.ok) throw new Error('No save'); return r.arrayBuffer(); })
                    .then(function (buf) { var g = gm(); if (g) g.loadState(new Uint8Array(buf)); })
                    .catch(function (err) { console.warn('[JellyEmu] Quick load failed:', err); });
                break;
            case 26: // Change Slot
                _jeActiveSlot = (_jeActiveSlot % 5) + 1;
                console.log('[JellyEmu] Active save slot ->', _jeActiveSlot);
                break;
            case 27: // Fast Forward
                window._jeFFActive = !window._jeFFActive;
                var gff = gm(); if (gff) gff.toggleFastForward(window._jeFFActive ? 1 : 0);
                var ffBtn = document.getElementById('je-btn-ff');
                if (ffBtn) ffBtn.classList.toggle('je-active', window._jeFFActive);
                break;
            case 28: // Rewind
                var gr = gm(); if (gr && gr.toggleRewind) gr.toggleRewind(1);
                break;
            case 29: // Slow Motion
                window._jeSlowActive = !window._jeSlowActive;
                var gs = gm(); if (gs) gs.toggleSlowMotion(window._jeSlowActive ? 1 : 0);
                var slowBtn = document.getElementById('je-btn-slow');
                if (slowBtn) slowBtn.classList.toggle('je-active', window._jeSlowActive);
                break;
        }
    }

    // - Key code → display name -
    var keyCodeMap = {
        8:'Backspace',9:'Tab',13:'Enter',16:'Shift',17:'Ctrl',18:'Alt',
        19:'Pause',20:'Caps Lock',27:'Escape',32:'Space',33:'Page Up',
        34:'Page Down',35:'End',36:'Home',37:'← Left',38:'↑ Up',
        39:'→ Right',40:'↓ Down',45:'Insert',46:'Delete',
        48:'0',49:'1',50:'2',51:'3',52:'4',53:'5',54:'6',55:'7',56:'8',57:'9',
        65:'A',66:'B',67:'C',68:'D',69:'E',70:'F',71:'G',72:'H',73:'I',74:'J',
        75:'K',76:'L',77:'M',78:'N',79:'O',80:'P',81:'Q',82:'R',83:'S',84:'T',
        85:'U',86:'V',87:'W',88:'X',89:'Y',90:'Z',
        96:'Num 0',97:'Num 1',98:'Num 2',99:'Num 3',100:'Num 4',
        101:'Num 5',102:'Num 6',103:'Num 7',104:'Num 8',105:'Num 9',
        106:'Num *',107:'Num +',109:'Num -',110:'Num .',111:'Num /',
        112:'F1',113:'F2',114:'F3',115:'F4',116:'F5',117:'F6',
        118:'F7',119:'F8',120:'F9',121:'F10',122:'F11',123:'F12',
        144:'Num Lock',145:'Scroll Lock',
        186:';',187:'=',188:',',189:'-',190:'.',191:'/',
        192:'`',219:'[',220:'\\',221:']',222:"'"
    };

    var _jeGpLabels = {
        'BUTTON_1':'A / Cross','BUTTON_2':'B / Circle',
        'BUTTON_3':'X / Square','BUTTON_4':'Y / Triangle',
        'LEFT_TOP_SHOULDER':'LB / L1','RIGHT_TOP_SHOULDER':'RB / R1',
        'LEFT_BOTTOM_SHOULDER':'LT / L2','RIGHT_BOTTOM_SHOULDER':'RT / R2',
        'SELECT':'Select / Back','START':'Start',
        'LEFT_STICK':'L3','RIGHT_STICK':'R3',
        'DPAD_UP':'D-Up','DPAD_DOWN':'D-Down','DPAD_LEFT':'D-Left','DPAD_RIGHT':'D-Right',
        'LEFT_STICK_X:+1':'L-Stick →','LEFT_STICK_X:-1':'L-Stick ←',
        'LEFT_STICK_Y:+1':'L-Stick ↓','LEFT_STICK_Y:-1':'L-Stick ↑',
        'RIGHT_STICK_X:+1':'R-Stick →','RIGHT_STICK_X:-1':'R-Stick ←',
        'RIGHT_STICK_Y:+1':'R-Stick ↓','RIGHT_STICK_Y:-1':'R-Stick ↑'
    };
    function _jeGpName(s)    { return s ? (_jeGpLabels[s] || s) : '—'; }
    function _jeKeyName(code){ if (!code) return '—'; return keyCodeMap[code] || ('Key ' + code); }

    // - Button index → EJS label
    var _jeButtonIndexToLabel = [
        'BUTTON_1','BUTTON_2','BUTTON_3','BUTTON_4',
        'LEFT_TOP_SHOULDER','RIGHT_TOP_SHOULDER',
        'LEFT_BOTTOM_SHOULDER','RIGHT_BOTTOM_SHOULDER',
        'SELECT','START','LEFT_STICK','RIGHT_STICK',
        'DPAD_UP','DPAD_DOWN','DPAD_LEFT','DPAD_RIGHT'
    ];
    function _jeButtonLabel(bi) {
        return bi < _jeButtonIndexToLabel.length ? _jeButtonIndexToLabel[bi] : ('GAMEPAD_' + bi);
    }

    // - Axis index → EJS axis name
    var _jeAxisNames = ['LEFT_STICK_X','LEFT_STICK_Y','RIGHT_STICK_X','RIGHT_STICK_Y'];
    function _jeAxisLabel(ai, val) {
        var name = ai < _jeAxisNames.length ? _jeAxisNames[ai] : ('EXTRA_STICK_' + ai);
        return name + (val > 0 ? ':+1' : ':-1');
    }

    // - Base Default bindings -
    var _jeBaseDefaultBindings = {
        0:  { kb1:88,  kb2:0, gp1:'BUTTON_2',              gp2:'' },
        1:  { kb1:83,  kb2:0, gp1:'BUTTON_4',              gp2:'' },
        2:  { kb1:86,  kb2:0, gp1:'SELECT',                gp2:'' },
        3:  { kb1:13,  kb2:0, gp1:'START',                 gp2:'' },
        4:  { kb1:38,  kb2:0, gp1:'DPAD_UP',               gp2:'LEFT_STICK_Y:-1' },
        5:  { kb1:40,  kb2:0, gp1:'DPAD_DOWN',             gp2:'LEFT_STICK_Y:+1' },
        6:  { kb1:37,  kb2:0, gp1:'DPAD_LEFT',             gp2:'LEFT_STICK_X:-1' },
        7:  { kb1:39,  kb2:0, gp1:'DPAD_RIGHT',            gp2:'LEFT_STICK_X:+1' },
        8:  { kb1:90,  kb2:0, gp1:'BUTTON_1',              gp2:'' },
        9:  { kb1:65,  kb2:0, gp1:'BUTTON_3',              gp2:'' },
        10: { kb1:81,  kb2:0, gp1:'LEFT_TOP_SHOULDER',     gp2:'' },
        11: { kb1:69,  kb2:0, gp1:'RIGHT_TOP_SHOULDER',    gp2:'' },
        12: { kb1:9,   kb2:0, gp1:'LEFT_BOTTOM_SHOULDER',  gp2:'' },
        13: { kb1:82,  kb2:0, gp1:'RIGHT_BOTTOM_SHOULDER', gp2:'' },
        14: { kb1:77,  kb2:0, gp1:'LEFT_STICK',            gp2:'RIGHT_STICK' },
        15: { kb1:0,   kb2:0, gp1:'RIGHT_STICK',           gp2:'' },
        16: { kb1:72,  kb2:0, gp1:'LEFT_STICK_X:+1',       gp2:'' },
        17: { kb1:70,  kb2:0, gp1:'LEFT_STICK_X:-1',       gp2:'' },
        18: { kb1:71,  kb2:0, gp1:'LEFT_STICK_Y:+1',       gp2:'' },
        19: { kb1:84,  kb2:0, gp1:'LEFT_STICK_Y:-1',       gp2:'' },
        20: { kb1:76,  kb2:0, gp1:'RIGHT_STICK_X:+1',      gp2:'' },
        21: { kb1:74,  kb2:0, gp1:'RIGHT_STICK_X:-1',      gp2:'' },
        22: { kb1:75,  kb2:0, gp1:'RIGHT_STICK_Y:+1',      gp2:'' },
        23: { kb1:73,  kb2:0, gp1:'RIGHT_STICK_Y:-1',      gp2:'' },
        24: { kb1:49,  kb2:0, gp1:'', gp2:'' },
        25: { kb1:50,  kb2:0, gp1:'', gp2:'' },
        26: { kb1:51,  kb2:0, gp1:'', gp2:'' },
        27: { kb1:107, kb2:0, gp1:'', gp2:'' },
        28: { kb1:32,  kb2:0, gp1:'', gp2:'' },
        29: { kb1:109, kb2:0, gp1:'', gp2:'' }
    };
    var _jeDefaultBindings = _jeBaseDefaultBindings; // backward-compatibility alias

    function _jeGetDefaultBindings() {
        var schemeDef = getActiveSchemeDefinition();
        var result = {};
        if (schemeDef && schemeDef.defaultBindings && Object.keys(schemeDef.defaultBindings).length > 0) {
            result = JSON.parse(JSON.stringify(schemeDef.defaultBindings));
        }
        var buttons = getActiveInputButtons();
        for (var i = 0; i < buttons.length; i++) {
            var id = buttons[i].id;
            if (!result[id]) {
                if (_jeBaseDefaultBindings[id]) {
                    result[id] = JSON.parse(JSON.stringify(_jeBaseDefaultBindings[id]));
                } else {
                    result[id] = { kb1: 0, kb2: 0, gp1: '', gp2: '' };
                }
            }
        }
        return result;
    }

    // - Live binding map -
    var _jeBindings = {};

    function _jeEnsureBinding(idx) {
        if (!_jeBindings[idx] || typeof _jeBindings[idx] !== 'object') {
            var defaults = _jeGetDefaultBindings();
            _jeBindings[idx] = (defaults && defaults[idx])
                ? JSON.parse(JSON.stringify(defaults[idx]))
                : (_jeBaseDefaultBindings[idx] ? JSON.parse(JSON.stringify(_jeBaseDefaultBindings[idx])) : { kb1: 0, kb2: 0, gp1: '', gp2: '' });
        }
        if (_jeBindings[idx].kb1 === undefined) _jeBindings[idx].kb1 = 0;
        if (_jeBindings[idx].kb2 === undefined) _jeBindings[idx].kb2 = 0;
        if (_jeBindings[idx].gp1 === undefined) _jeBindings[idx].gp1 = '';
        if (_jeBindings[idx].gp2 === undefined) _jeBindings[idx].gp2 = '';
        return _jeBindings[idx];
    }

    function _jeLoadBindings(serverPrefs) {
        var defaults = _jeGetDefaultBindings();
        try {
            var raw = (serverPrefs && (serverPrefs.jeBindings || serverPrefs.controls))
                ? (serverPrefs.jeBindings || serverPrefs.controls)
                : (cfg.customBindings || null);
            var saved = raw ? (typeof raw === 'string' ? JSON.parse(raw) : raw) : null;
            _jeBindings = (saved && typeof saved === 'object') ? saved : JSON.parse(JSON.stringify(defaults));
        } catch (e) {
            _jeBindings = JSON.parse(JSON.stringify(defaults));
        }

        if (!_jeBindings || typeof _jeBindings !== 'object') {
            _jeBindings = JSON.parse(JSON.stringify(defaults));
        }

        // Ensure all active buttons for current scheme exist in _jeBindings
        var buttons = getActiveInputButtons();
        for (var i = 0; i < buttons.length; i++) {
            _jeEnsureBinding(buttons[i].id);
        }

        if (_jeIsN64()) {
            [4, 5, 6, 7].forEach(function (i) {
                if (_jeBindings[i] && _jeBindings[i].gp2 && _jeBindings[i].gp2.indexOf('LEFT_STICK') !== -1) {
                    _jeBindings[i].gp2 = '';
                }
            });
        }
    }
    _jeLoadBindings(null);

    if (userId) {
        var prefHeaders = {};
        if (token) prefHeaders['Authorization'] = 'MediaBrowser Token="' + token + '"';
        var cItemId = (window.JellyEmuConfig && window.JellyEmuConfig.itemId) ? encodeURIComponent(window.JellyEmuConfig.itemId) : '';
        var cPlatform = (window.JellyEmuConfig && window.JellyEmuConfig.platformTag) || window.EJS_platformTag || '';
        var consoleKey = getActiveControlScheme();
        var platformQuery = consoleKey || cPlatform;
        fetch('/jellyemu/prefs/' + userId + '/effective?itemId=' + cItemId + '&platform=' + encodeURIComponent(platformQuery), { headers: prefHeaders })
            .then(function (r) { if (r.ok) return r.json(); })
            .then(function (data) {
                if (data && (data.jeBindings || data.controls)) {
                    _jeLoadBindings(data);
                    if (document.getElementById('je-tab-kb')) {
                        buildKeyboardBinds();
                        buildGamepadBinds();
                    }
                }
            })
            .catch(function (err) { console.warn('[JellyEmu] Failed to load bindings:', err); });
    }

    // - Synchronize controller schemes from backend (single source of truth) -
    function _jeSyncSchemesFromBackend() {
        var schemeHeaders = {};
        if (token) schemeHeaders['Authorization'] = 'MediaBrowser Token="' + token + '"';
        var platformOrCore = (window.JellyEmuConfig && window.JellyEmuConfig.platformTag) || window.EJS_platformTag || window.EJS_core || '';
        var endpoint = platformOrCore ? ('/jellyemu/input/schemes/' + encodeURIComponent(platformOrCore)) : '/jellyemu/input/schemes';

        fetch(endpoint, { headers: schemeHeaders })
            .then(function (r) { if (r.ok) return r.json(); })
            .then(function (data) {
                if (!data) return;
                if (data.hotkeys && Array.isArray(data.hotkeys)) {
                    HOTKEYS = data.hotkeys;
                }
                if (data.scheme) {
                    _activeBackendScheme = data.scheme;
                    SYSTEM_SCHEMES[data.scheme.id] = data.scheme;
                } else if (data.schemes) {
                    Object.keys(data.schemes).forEach(function (k) {
                        SYSTEM_SCHEMES[k] = data.schemes[k];
                    });
                    var activeKey = getActiveControlScheme();
                    if (data.schemes[activeKey]) {
                        _activeBackendScheme = data.schemes[activeKey];
                    }
                }
                _jeLoadBindings(null);
                if (document.getElementById('je-tab-kb') && _popupOpen()) {
                    buildKeyboardBinds();
                    buildGamepadBinds(true);
                }
                console.log('[JellyEmu] Controller schemes synchronized from backend');
            })
            .catch(function (err) {
                console.warn('[JellyEmu] Backend controller schemes sync skipped:', err);
            });
    }
    _jeSyncSchemesFromBackend();

    var _jeSimulatedState = {};

    // - simulateInput bridge -
    function _jeSimulate(idx, pressed) {
        if (idx >= 24) { if (pressed) _jeHotkeyAction(idx); return; }
        var g = gm();
        if (!g) { console.warn('[JellyEmu Input] gm() is null'); return; }
        if (typeof g.simulateInput !== 'function') { console.warn('[JellyEmu Input] simulateInput not a function'); return; }

        var boolPressed = !!pressed;
        if (_jeSimulatedState[idx] === boolPressed) {
            return;
        }
        _jeSimulatedState[idx] = boolPressed;

        if (_isInputWindowOpen()) {
            var mapName = getActiveInputMap()[idx] || ('ID ' + idx);
            console.log('[JellyEmu Input] _jeSimulate | Index:', idx, '(' + mapName + ') | Pressed:', boolPressed);
        }

        var isAnalog = (idx >= 16 && idx <= 23);
        var simVal = boolPressed ? (isAnalog ? 32767 : 1) : 0;

        // If in netplay as guest, route input directly over low-latency WebRTC DataChannel
        if (typeof window._jeSendNetplayInput === 'function' && window._jeSendNetplayInput(idx, simVal)) {
            return;
        }

        try {
            g.simulateInput(0, idx, simVal);
        } catch (e) {
            console.warn('[JellyEmu Input] simulateInput error:', e);
        }
    }

    function _jeFindBindingsForGp(gpStr) {
        var results = [];
        if (gpStr === undefined || gpStr === null || gpStr === '') return results;
        var str = String(gpStr);

        for (var idx in _jeBindings) {
            var b = _jeBindings[idx];
            if (!b) continue;
            if (b.gp1 === str || b.gp2 === str) {
                results.push(parseInt(idx, 10));
            } else if (typeof gpStr === 'number' && (b.gp1 === _jeButtonLabel(gpStr) || b.gp2 === _jeButtonLabel(gpStr))) {
                results.push(parseInt(idx, 10));
            }
        }
        return results;
    }

    var _jeKbDown = {};
    var _popupOpen = function () { return !!window._jePopupOpen; };

    document.addEventListener('keydown', function (ev) {
        if (ev.target && (ev.target.tagName === 'INPUT' || ev.target.tagName === 'TEXTAREA' || ev.target.tagName === 'SELECT')) return;
        if (_popupOpen()) return;
        var kc = ev.keyCode;
        if (_jeKbDown[kc]) return;
        _jeKbDown[kc] = true;
        for (var idx in _jeBindings) {
            var b = _jeBindings[idx];
            if (!b) continue;
            if (b.kb1 === kc || b.kb2 === kc) {
                ev.preventDefault();
                _jeSimulate(parseInt(idx, 10), true);
            }
        }
    }, true);

    document.addEventListener('keyup', function (ev) {
        var kc = ev.keyCode;
        _jeKbDown[kc] = false;
        for (var idx in _jeBindings) {
            var b = _jeBindings[idx];
            if (!b) continue;
            if (b.kb1 === kc || b.kb2 === kc) _jeSimulate(parseInt(idx, 10), false);
        }
    }, true);

    // ============================================================
    // - DIRECT RAW GAMEPAD ENGINE -
    // Polls navigator.getGamepads() every animation frame.
    // Handles button mapping and emulation input directly with full logging.
    // ============================================================
    var _jeActiveGpListen = null; // { idx, field, bk, timeoutId, initialAxes }
    var _jeRawGpPrevButtons = {}; // padIndex -> { bi: bool }
    var _jeRawGpPrevAxes = {};    // padIndex -> { ai: val }
    var _jeGpActiveState = {};    // label -> bool (for simulation debounce)

    function _jeHandleAxisSimulation(label, isPressed) {
        if (isPressed) {
            if (!_jeGpActiveState[label]) {
                _jeGpActiveState[label] = true;
                var binds = _jeFindBindingsForGp(label);
                binds.forEach(function (idx) { _jeSimulate(idx, true); });
            }
        } else {
            if (_jeGpActiveState[label]) {
                _jeGpActiveState[label] = false;
                var binds = _jeFindBindingsForGp(label);
                binds.forEach(function (idx) { _jeSimulate(idx, false); });
            }
        }
    }

    function _jePollRawGamepads() {
        var pads = [];
        if (navigator.getGamepads) {
            try { pads = navigator.getGamepads() || []; } catch (e) {}
        } else if (navigator.webkitGetGamepads) {
            try { pads = navigator.webkitGetGamepads() || []; } catch (e) {}
        }

        for (var gi = 0; gi < pads.length; gi++) {
            var gp = pads[gi];
            if (!gp || !gp.connected) continue;

            if (!_jeRawGpPrevButtons[gp.index]) _jeRawGpPrevButtons[gp.index] = {};
            if (!_jeRawGpPrevAxes[gp.index]) _jeRawGpPrevAxes[gp.index] = {};

            // 1. Process Buttons (supports pressed bool, pressure value > 0.4, or numeric button)
            var buttonsCount = gp.buttons ? gp.buttons.length : 0;
            for (var bi = 0; bi < buttonsCount; bi++) {
                var btn = gp.buttons[bi];
                var val = 0;
                var pressed = false;
                if (typeof btn === 'object' && btn !== null) {
                    val = typeof btn.value === 'number' ? btn.value : (btn.pressed ? 1 : 0);
                    pressed = !!btn.pressed || val > 0.4;
                } else if (typeof btn === 'number') {
                    val = btn;
                    pressed = btn > 0.4;
                }

                var wasPressed = !!_jeRawGpPrevButtons[gp.index][bi];

                if (pressed !== wasPressed) {
                    _jeRawGpPrevButtons[gp.index][bi] = pressed;
                    var label = _jeButtonLabel(bi);
                    if (_isInputWindowOpen()) {
                        console.log('[JellyEmu Gamepad RAW] Pad #' + gp.index + ' (' + gp.id + ') Button ' + bi + ' [' + label + '] ' + (pressed ? 'PRESSED' : 'RELEASED') + ' (val: ' + val.toFixed(2) + ')');
                    }

                    // If currently listening for mapping in Input Settings modal:
                    if (_jeActiveGpListen && pressed) {
                        var listen = _jeActiveGpListen;
                        _jeActiveGpListen = null;
                        clearTimeout(listen.timeoutId);
                        listen.bk.classList.remove('je-listening');
                        var bind = _jeEnsureBinding(listen.idx);
                        bind[listen.field] = label;
                        listen.bk.textContent = _jeGpName(label);
                        _jeSyncBindingsToServer();
                        continue;
                    }

                    // If not mapping, dispatch to gameplay simulation
                    if (!_jeActiveGpListen) {
                        var matched = _jeFindBindingsForGp(label);
                        if (matched.length === 0) {
                            matched = _jeFindBindingsForGp(bi);
                        }
                        if (matched.length > 0) {
                            if (pressed) {
                                if (!_jeGpActiveState[label]) {
                                    _jeGpActiveState[label] = true;
                                    matched.forEach(function (idx) { _jeSimulate(idx, true); });
                                }
                            } else {
                                if (_jeGpActiveState[label]) {
                                    _jeGpActiveState[label] = false;
                                    matched.forEach(function (idx) { _jeSimulate(idx, false); });
                                }
                            }
                        }
                    }
                }
            }

            // 2. Process Axes (Analog Sticks & Triggers on axes)
            var axesCount = gp.axes ? gp.axes.length : 0;
            for (var ai = 0; ai < axesCount; ai++) {
                var aVal = gp.axes[ai];
                var prevVal = _jeRawGpPrevAxes[gp.index][ai] !== undefined ? _jeRawGpPrevAxes[gp.index][ai] : 0;
                var axisName = ai < _jeAxisNames.length ? _jeAxisNames[ai] : ('EXTRA_STICK_' + ai);

                var isMovedPos = aVal > 0.5;
                var isMovedNeg = aVal < -0.5;
                var wasMovedPos = prevVal > 0.5;
                var wasMovedNeg = prevVal < -0.5;

                _jeRawGpPrevAxes[gp.index][ai] = aVal;

                var posLabel = axisName + ':+1';
                var negLabel = axisName + ':-1';

                // Positive axis direction
                if (isMovedPos !== wasMovedPos) {
                    if (_isInputWindowOpen()) {
                        console.log('[JellyEmu Gamepad RAW] Pad #' + gp.index + ' Axis ' + ai + ' [' + posLabel + '] ' + (isMovedPos ? 'MOVED' : 'RELEASED') + ' (val: ' + aVal.toFixed(2) + ')');
                    }
                    if (_jeActiveGpListen && isMovedPos) {
                        var initA = (_jeActiveGpListen.initialAxes && _jeActiveGpListen.initialAxes[gp.index] && _jeActiveGpListen.initialAxes[gp.index][ai]) || 0;
                        if (Math.abs(aVal - initA) > 0.4) {
                            var listen = _jeActiveGpListen;
                            _jeActiveGpListen = null;
                            clearTimeout(listen.timeoutId);
                            listen.bk.classList.remove('je-listening');
                            var bind = _jeEnsureBinding(listen.idx);
                            bind[listen.field] = posLabel;
                            listen.bk.textContent = _jeGpName(posLabel);
                            _jeSyncBindingsToServer();
                            continue;
                        }
                    } else if (!_jeActiveGpListen) {
                        _jeHandleAxisSimulation(posLabel, isMovedPos);
                    }
                }

                // Negative axis direction
                if (isMovedNeg !== wasMovedNeg) {
                    if (_isInputWindowOpen()) {
                        console.log('[JellyEmu Gamepad RAW] Pad #' + gp.index + ' Axis ' + ai + ' [' + negLabel + '] ' + (isMovedNeg ? 'MOVED' : 'RELEASED') + ' (val: ' + aVal.toFixed(2) + ')');
                    }
                    if (_jeActiveGpListen && isMovedNeg) {
                        var initA = (_jeActiveGpListen.initialAxes && _jeActiveGpListen.initialAxes[gp.index] && _jeActiveGpListen.initialAxes[gp.index][ai]) || 0;
                        if (Math.abs(aVal - initA) > 0.4) {
                            var listen = _jeActiveGpListen;
                            _jeActiveGpListen = null;
                            clearTimeout(listen.timeoutId);
                            listen.bk.classList.remove('je-listening');
                            var bind = _jeEnsureBinding(listen.idx);
                            bind[listen.field] = negLabel;
                            listen.bk.textContent = _jeGpName(negLabel);
                            _jeSyncBindingsToServer();
                            continue;
                        }
                    } else if (!_jeActiveGpListen) {
                        _jeHandleAxisSimulation(negLabel, isMovedNeg);
                    }
                }
            }
        }

        requestAnimationFrame(_jePollRawGamepads);
    }
    requestAnimationFrame(_jePollRawGamepads);

    // Disable EJS internal controls so they do not conflict with JellyEmu raw controller simulation
    window.addEventListener('jellyemu:gamestart', function () {
        console.log('[JellyEmu] jellyemu:gamestart event received, clearing EJS controls for direct JellyEmu gamepad simulation');

        window.EJS_defaultControls = {
            0: {}, 1: {}, 2: {}, 3: {}
        };

        function _jeDisableEjsControls() {
            var e = window.EJS_emulator;
            if (!e) return;
            var ejsControls = e.controls && e.controls[0];
            if (ejsControls) {
                Object.keys(ejsControls).forEach(function (k) {
                    if (ejsControls[k] && ejsControls[k].value2 !== undefined) {
                        ejsControls[k].value2 = '';
                    }
                });
            }
        }
        _jeDisableEjsControls();
        setTimeout(_jeDisableEjsControls, 500);
        setTimeout(_jeDisableEjsControls, 1500);

        function _jeApplyVirtualControls(attemptsLeft) {
            var e = emu();
            if (e && e.started && e.toggleVirtualGamepad && e.toggleVirtualGamepadLeftHanded) {
                var jeCfg = window.JellyEmuConfig || {};
                var showVg = jeCfg.virtualGamepad === 'true';
                var leftyVg = jeCfg.virtualGamepadLefty === 'true';
                
                console.log('[JellyEmu] Applying virtual controls preference:', showVg, 'lefty:', leftyVg);
                e.toggleVirtualGamepad(showVg);
                e.toggleVirtualGamepadLeftHanded(leftyVg);
                
                setTimeout(function () {
                    if (e.toggleVirtualGamepad) e.toggleVirtualGamepad(showVg);
                    if (e.toggleVirtualGamepadLeftHanded) e.toggleVirtualGamepadLeftHanded(leftyVg);
                }, 300);
                return;
            }
            if (attemptsLeft > 0) {
                setTimeout(function () { _jeApplyVirtualControls(attemptsLeft - 1); }, 100);
            }
        }
        _jeApplyVirtualControls(100);
    });

    var _syncTimer = null;
    function _jeSyncBindingsToServer() {
        clearTimeout(_syncTimer);
        _syncTimer = setTimeout(function () {
            var headers = { 'Content-Type': 'application/json' };
            if (token) headers['Authorization'] = 'MediaBrowser Token="' + token + '"';
            var consoleKey = getActiveControlScheme();
            var cPlatform = (window.JellyEmuConfig && window.JellyEmuConfig.platformTag) || (window.EJS_platformTag || '');
            var targetConsole = consoleKey || cPlatform;
            var url = '/jellyemu/prefs/' + userId + (targetConsole ? ('?scope=system&targetId=' + encodeURIComponent(targetConsole)) : '');
            var payload = {
                scope: 'system',
                targetId: targetConsole,
                preferences: {
                    controls: JSON.stringify(_jeBindings),
                    jeBindings: JSON.stringify(_jeBindings)
                },
                controls: JSON.stringify(_jeBindings),
                jeBindings: JSON.stringify(_jeBindings)
            };
            fetch(url, {
                method: 'POST',
                headers: headers,
                body: JSON.stringify(payload)
            }).then(function (r) {
                if (r.ok) console.log('[JellyEmu] Custom controller bindings saved to SQLite for console ' + targetConsole);
            }).catch(function (err) { console.warn('[JellyEmu] Bindings sync failed:', err); });
        }, 800);
    }

    (function () {
        var s = document.createElement('style');
        s.textContent =
            '#je-tab-kb, #je-tab-gp, #je-tab-vg { flex-direction: column !important; width: 100% !important; }' +
            '#je-tab-kb.je-tab-active, #je-tab-gp.je-tab-active, #je-tab-vg.je-tab-active { display: flex !important; }' +
            '#je-gp-status { width: 100% !important; margin-bottom: 12px; }' +
            '#je-gp-binds { width: 100% !important; display: flex !important; flex-direction: column !important; }' +
            '#je-tab-vg .je-setting { width: 100% !important; display: flex !important; align-items: center !important; justify-content: space-between !important; }' +
            '#je-tab-kb .je-bind-headers,' +
            '#je-tab-kb .je-bind-row { grid-template-columns: 1fr 100px 100px !important; }' +
            '#je-tab-gp .je-bind-headers,' +
            '#je-tab-gp .je-bind-row { grid-template-columns: 1fr 120px 120px !important; }';
        document.head.appendChild(s);
    })();

    function _jeMakeBindKey(label, onClickFn) {
        var span = document.createElement('span');
        span.className = 'je-bind-key';
        span.textContent = label || '—';
        span.addEventListener('click', onClickFn);
        return span;
    }

    function buildKeyboardBinds() {
        var panel = document.getElementById('je-tab-kb');
        if (!panel) return;
        var schemeDef = getActiveSchemeDefinition();
        var buttons = getActiveInputButtons();

        panel.innerHTML =
            '<div class="je-platform-bind-header">' +
                '<span>Platform Layout: <strong>' + _jeEscapeHtml(schemeDef.name) + '</strong></span>' +
                '<span style="font-size:10.5px; opacity:0.8;">' + buttons.length + ' Inputs</span>' +
            '</div>' +
            '<div class="je-bind-headers">' +
                '<span>Action (ID)</span>' +
                '<span>KB 1</span>' +
                '<span>KB 2</span>' +
            '</div>';

        buttons.forEach(function (btn) {
            var idx = btn.id;
            var b   = _jeEnsureBinding(idx);
            var row = document.createElement('div');
            row.className = 'je-bind-row';

            var label = document.createElement('span');
            label.className = 'je-bind-label';

            var nameSpan = document.createElement('span');
            nameSpan.className = 'je-bind-name';
            nameSpan.textContent = btn.label;

            var idBadge = document.createElement('span');
            idBadge.className = 'je-bind-id-badge' + (idx >= 24 ? ' je-hotkey-badge' : '');
            idBadge.textContent = 'ID ' + idx;

            label.appendChild(nameSpan);
            label.appendChild(idBadge);
            row.appendChild(label);

            ['kb1', 'kb2'].forEach(function (field) {
                row.appendChild(_jeMakeBindKey(_jeKeyName(b[field]), function () {
                    _jeListenKeyboard(idx, field, row);
                }));
            });

            panel.appendChild(row);
        });
    }

    function _jeEscapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function parseGamepadDetails(rawGp) {
        if (!rawGp) return null;
        var rawId = rawGp.id || 'Standard Gamepad';
        var vendorId = '';
        var productId = '';

        var vidMatch = rawId.match(/(?:Vendor|VID)[_:\s]+([0-9a-fA-F]{4})/i) || rawId.match(/\b([0-9a-fA-F]{4})[:\-\/]([0-9a-fA-F]{4})\b/);
        var pidMatch = rawId.match(/(?:Product|PID)[_:\s]+([0-9a-fA-F]{4})/i);

        if (vidMatch) {
            vendorId = vidMatch[1];
            if (!pidMatch && vidMatch[2]) {
                productId = vidMatch[2];
            }
        }
        if (pidMatch && !productId) {
            productId = pidMatch[1];
        }

        var cleanName = rawId
            .replace(/\(STANDARD GAMEPAD.*?\)/gi, '')
            .replace(/\(Vendor:.*?\)/gi, '')
            .replace(/Vendor:.*$/gi, '')
            .replace(/VID_.*?PID_.*?/gi, '')
            .replace(/[\(\)]/g, '')
            .trim();

        var bCount = 0;
        if (typeof rawGp.buttons === 'number') {
            bCount = rawGp.buttons;
        } else if (rawGp.buttons && typeof rawGp.buttons.length === 'number') {
            bCount = rawGp.buttons.length;
        } else if (typeof rawGp.numButtons === 'number') {
            bCount = rawGp.numButtons;
        }

        var aCount = 0;
        if (typeof rawGp.axes === 'number') {
            aCount = rawGp.axes;
        } else if (rawGp.axes && typeof rawGp.axes.length === 'number') {
            aCount = rawGp.axes.length;
        } else if (typeof rawGp.numAxes === 'number') {
            aCount = rawGp.numAxes;
        }

        return {
            rawId: rawId,
            deviceName: cleanName,
            vendorId: vendorId ? ('0x' + vendorId.toUpperCase()) : 'N/A',
            productId: productId ? ('0x' + productId.toUpperCase()) : 'N/A',
            mapping: rawGp.mapping || 'standard',
            index: rawGp.index !== undefined ? rawGp.index : 0,
            buttonsCount: bCount,
            axesCount: aCount
        };
    }

    function updateGamepadStatus() {
        var gp = null;

        if (navigator.getGamepads) {
            try {
                var pads = navigator.getGamepads();
                if (pads) {
                    for (var i = 0; i < pads.length; i++) {
                        if (pads[i] && pads[i].connected !== false) {
                            gp = pads[i];
                            break;
                        }
                    }
                }
            } catch (err) {}
        }

        if (!gp) {
            var e  = window.EJS_emulator;
            var gh = e && e.gamepad;
            gp = gh && gh.gamepads && gh.gamepads[0];
        }

        var statusEl = document.getElementById('je-gp-status');
        var gpIconBlue = '<svg width="20" height="20" viewBox="0 0 24 24" fill="#00E5FF" style="vertical-align:middle;flex-shrink:0;filter:drop-shadow(0 0 4px rgba(0,229,255,0.4));"><path d="M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-10 7H9v2H8v-2H6v-1h2V10h1v2h2v1zm4.5 1c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm3-3c-.83 0-1.5-.67-1.5-1.5S17.67 9 18.5 9s1.5.67 1.5 1.5-.67 1.5-1.5 1.5z"/></svg>';
        var gpIconGrey = '<svg width="20" height="20" viewBox="0 0 24 24" fill="#CBD5E1" style="vertical-align:middle;flex-shrink:0;"><path d="M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-10 7H9v2H8v-2H6v-1h2V10h1v2h2v1zm4.5 1c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm3-3c-.83 0-1.5-.67-1.5-1.5S17.67 9 18.5 9s1.5.67 1.5 1.5-.67 1.5-1.5 1.5z"/></svg>';

        if (statusEl) {
            statusEl.style.opacity = '1';
            if (gp) {
                var info = parseGamepadDetails(gp);
                statusEl.innerHTML =
                    '<div style="background: linear-gradient(135deg, rgba(0, 164, 220, 0.22) 0%, rgba(0, 119, 182, 0.15) 100%); border: 1px solid rgba(0, 229, 255, 0.45); border-radius: 8px; padding: 12px 14px; margin-bottom: 14px; box-shadow: 0 4px 16px rgba(0, 164, 220, 0.15);">' +
                        '<div style="font-weight: 700; color: #ffffff; font-size: 13.5px; margin-bottom: 6px; display: flex; align-items: center; gap: 8px;">' +
                            gpIconBlue +
                            '<span>' + _jeEscapeHtml(info.deviceName) + '</span>' +
                            '<span style="font-size: 10px; background: #10B981; color: #ffffff; padding: 2px 8px; border-radius: 4px; font-weight: 700; letter-spacing: 0.04em; margin-left: auto; box-shadow: 0 0 8px rgba(16, 185, 129, 0.4);">CONNECTED</span>' +
                        '</div>' +
                        '<div style="font-size: 11.5px; color: #E2E8F0; word-break: break-all; margin-top: 4px;">' +
                            '<strong style="color: #94A3B8;">Device ID:</strong> ' + _jeEscapeHtml(info.rawId) +
                        '</div>' +
                        '<div style="display: flex; flex-wrap: wrap; gap: 12px; font-size: 11.5px; color: #E2E8F0; margin-top: 8px; padding-top: 8px; border-top: 1px solid rgba(255,255,255,0.12);">' +
                            '<span><strong style="color: #94A3B8;">Vendor (VID):</strong> <code style="background: rgba(0, 229, 255, 0.15); color: #00E5FF; padding: 1px 6px; border-radius: 4px; font-weight: 600; border: 1px solid rgba(0, 229, 255, 0.3);">' + info.vendorId + '</code></span>' +
                            '<span><strong style="color: #94A3B8;">Product (PID):</strong> <code style="background: rgba(0, 229, 255, 0.15); color: #00E5FF; padding: 1px 6px; border-radius: 4px; font-weight: 600; border: 1px solid rgba(0, 229, 255, 0.3);">' + info.productId + '</code></span>' +
                            '<span><strong style="color: #94A3B8;">Mapping:</strong> <span style="color: #FFFFFF; font-weight: 600;">' + _jeEscapeHtml(info.mapping) + '</span></span>' +
                            '<span><strong style="color: #94A3B8;">Index:</strong> <span style="color: #FFFFFF; font-weight: 600;">#' + info.index + '</span></span>' +
                            '<span><strong style="color: #94A3B8;">Inputs:</strong> <span style="color: #FFFFFF; font-weight: 600;">' + info.buttonsCount + ' buttons, ' + info.axesCount + ' axes</span></span>' +
                        '</div>' +
                    '</div>';
            } else {
                statusEl.innerHTML =
                    '<div style="background: rgba(30, 41, 59, 0.6); border: 1px solid rgba(255, 255, 255, 0.15); border-radius: 8px; padding: 14px; margin-bottom: 14px; color: #F1F5F9; text-align: center; font-size: 12.5px; font-weight: 500; display: flex; align-items: center; justify-content: center; gap: 10px;">' +
                        gpIconGrey +
                        '<span>No controller detected. Plug in a gamepad or press any button to connect.</span>' +
                    '</div>';
            }
        }
        return gp;
    }

    function buildGamepadBinds(forceRebuild) {
        updateGamepadStatus();

        var bindsPanel = document.getElementById('je-gp-binds');
        if (!bindsPanel) return;
        if (!forceRebuild && bindsPanel.children.length > 0) return;

        var schemeDef = getActiveSchemeDefinition();
        var buttons = getActiveInputButtons();

        bindsPanel.innerHTML =
            '<div class="je-platform-bind-header">' +
                '<span>Platform Layout: <strong>' + _jeEscapeHtml(schemeDef.name) + '</strong></span>' +
                '<span style="font-size:10.5px; opacity:0.8;">' + buttons.length + ' Inputs</span>' +
            '</div>' +
            '<div class="je-bind-headers">' +
                '<span>Action (ID)</span>' +
                '<span>GP 1</span>' +
                '<span>GP 2</span>' +
            '</div>';

        buttons.forEach(function (btn) {
            var idx = btn.id;
            var b   = _jeEnsureBinding(idx);
            var row = document.createElement('div');
            row.className = 'je-bind-row';

            var label = document.createElement('span');
            label.className = 'je-bind-label';

            var nameSpan = document.createElement('span');
            nameSpan.className = 'je-bind-name';
            nameSpan.textContent = btn.label;

            var idBadge = document.createElement('span');
            idBadge.className = 'je-bind-id-badge' + (idx >= 24 ? ' je-hotkey-badge' : '');
            idBadge.textContent = 'ID ' + idx;

            label.appendChild(nameSpan);
            label.appendChild(idBadge);
            row.appendChild(label);

            ['gp1', 'gp2'].forEach(function (field) {
                row.appendChild(_jeMakeBindKey(_jeGpName(b[field]), function () {
                    _jeListenGamepad(idx, field, row);
                }));
            });

            bindsPanel.appendChild(row);
        });
    }

    // - Keyboard listen -
    function _jeListenKeyboard(idx, field, row) {
        var col = field === 'kb1' ? 1 : 2;
        var bk  = row.children[col];
        if (!bk || bk.classList.contains('je-listening')) return;
        bk.classList.add('je-listening');
        bk.textContent = 'Press key…';
        function onKey(ev) {
            ev.preventDefault(); ev.stopPropagation();
            document.removeEventListener('keydown', onKey, true);
            bk.classList.remove('je-listening');
            var bind = _jeEnsureBinding(idx);
            if (ev.keyCode === 27) { bk.textContent = _jeKeyName(bind[field]); return; }
            bind[field] = ev.keyCode;
            bk.textContent = _jeKeyName(ev.keyCode);
            _jeSyncBindingsToServer();
        }
        document.addEventListener('keydown', onKey, true);
    }

    // - Gamepad listen (Uses direct raw Gamepad poller) -
    function _jeListenGamepad(idx, field, row) {
        var col = field === 'gp1' ? 1 : 2;
        var bk  = row.children[col];
        if (!bk || bk.classList.contains('je-listening')) return;

        // Cancel previous listener if any
        if (_jeActiveGpListen) {
            clearTimeout(_jeActiveGpListen.timeoutId);
            _jeActiveGpListen.bk.classList.remove('je-listening');
            var prevBind = _jeEnsureBinding(_jeActiveGpListen.idx);
            _jeActiveGpListen.bk.textContent = _jeGpName(prevBind[_jeActiveGpListen.field]);
            _jeActiveGpListen = null;
        }

        bk.classList.add('je-listening');
        bk.textContent = 'Move/press…';

        // Snapshot initial axes to avoid drifting stick false-triggers
        var initAxes = {};
        var pads = navigator.getGamepads ? navigator.getGamepads() : [];
        for (var gi = 0; gi < pads.length; gi++) {
            if (pads[gi] && pads[gi].axes) {
                initAxes[pads[gi].index] = pads[gi].axes.slice();
            }
        }

        var timeoutId = setTimeout(function () {
            if (_jeActiveGpListen && _jeActiveGpListen.bk === bk) {
                _jeActiveGpListen = null;
                bk.classList.remove('je-listening');
                var currBind = _jeEnsureBinding(idx);
                bk.textContent = _jeGpName(currBind[field]);
            }
        }, 10000);

        _jeActiveGpListen = {
            idx: idx,
            field: field,
            row: row,
            bk: bk,
            timeoutId: timeoutId,
            initialAxes: initAxes
        };
        console.log('[JellyEmu Gamepad] Listening for raw input on Action ID ' + idx + ' (' + field + ')...');
    }

    // - Virtual gamepad toggles -
    function syncVGTogglesLocal() {
        var jeCfg = window.JellyEmuConfig || {};
        var vgOn = document.getElementById('je-vg-toggle');
        if (vgOn) vgOn.checked = jeCfg.virtualGamepad === 'true';
        var vgLefty = document.getElementById('je-vg-lefty');
        if (vgLefty) vgLefty.checked = jeCfg.virtualGamepadLefty === 'true';
    }
    window._jeSyncVGToggles = syncVGTogglesLocal;

    function _jeSyncVGPrefs() {
        if (!userId) return;
        var vgOn = document.getElementById('je-vg-toggle');
        var vgLefty = document.getElementById('je-vg-lefty');
        var payload = {
            virtualGamepad: vgOn ? String(vgOn.checked) : 'false',
            virtualGamepadLefty: vgLefty ? String(vgLefty.checked) : 'false'
        };
        if (window.JellyEmuConfig) {
            window.JellyEmuConfig.virtualGamepad = payload.virtualGamepad;
            window.JellyEmuConfig.virtualGamepadLefty = payload.virtualGamepadLefty;
        }
        var headers = { 'Content-Type': 'application/json' };
        if (token) headers['Authorization'] = 'MediaBrowser Token="' + token + '"';
        fetch('/jellyemu/prefs/' + userId, {
            method: 'POST',
            headers: headers,
            body: JSON.stringify(payload)
        }).catch(function (err) { console.warn('[JellyEmu] VG prefs sync failed:', err); });
    }

    var vgToggle = document.getElementById('je-vg-toggle');
    if (vgToggle) {
        vgToggle.addEventListener('change', function () {
            var e = emu();
            if (e && e.toggleVirtualGamepad) {
                e.toggleVirtualGamepad(this.checked);
            }
            _jeSyncVGPrefs();
        });
    }

    var vgLefty = document.getElementById('je-vg-lefty');
    if (vgLefty) {
        vgLefty.addEventListener('change', function () {
            var e = emu();
            if (e && e.toggleVirtualGamepadLeftHanded) {
                e.toggleVirtualGamepadLeftHanded(this.checked);
            }
            _jeSyncVGPrefs();
        });
    }

    window.addEventListener('jeLoaded', function () {
        syncVGTogglesLocal();
    });

    // - Reset to defaults -
    var resetBtn = document.getElementById('je-input-reset');
    if (resetBtn) {
        resetBtn.addEventListener('click', function () {
            _jeBindings = JSON.parse(JSON.stringify(_jeGetDefaultBindings()));
            buildKeyboardBinds();
            buildGamepadBinds(true);
            _jeSyncBindingsToServer();

            var vgOn = document.getElementById('je-vg-toggle');
            if (vgOn) {
                vgOn.checked = false;
                var e = emu();
                if (e && e.toggleVirtualGamepad) e.toggleVirtualGamepad(false);
            }
            var vgL = document.getElementById('je-vg-lefty');
            if (vgL) {
                vgL.checked = false;
                var e = emu();
                if (e && e.toggleVirtualGamepadLeftHanded) e.toggleVirtualGamepadLeftHanded(false);
            }
            _jeSyncVGPrefs();
        });
    }

    // - Wire up the dock button -
    var mapBtn = document.getElementById('je-btn-inputmap');
    if (mapBtn) {
        mapBtn.addEventListener('click', function () {
            buildKeyboardBinds();
            buildGamepadBinds(true);
            syncVGTogglesLocal();
            openPopup('je-pop-inputmap');
        });
    }

    function _popupOpen() {
        return _isInputWindowOpen();
    }

    var _gpPollInterval = null;
    function startGpStatusPolling() {
        stopGpStatusPolling();
        _gpPollInterval = setInterval(function() {
            if (_popupOpen()) {
                updateGamepadStatus();
            } else {
                stopGpStatusPolling();
            }
        }, 500);
    }

    function stopGpStatusPolling() {
        if (_gpPollInterval) {
            clearInterval(_gpPollInterval);
            _gpPollInterval = null;
        }
    }

    // - Gamepad Hotplug events -
    window.addEventListener("gamepadconnected", function () {
        buildGamepadBinds(true);
    });
    window.addEventListener("gamepaddisconnected", function () {
        buildGamepadBinds(true);
    });

    // - Input Popup Event Handlers -
    window.addEventListener('jePopupOpened', function (e) {
        if (e.detail && e.detail.id === 'je-pop-inputmap') {
            startGpStatusPolling();
            var openEvent = (typeof CustomEvent === 'function') ?
                new CustomEvent('jeInputOpened') :
                document.createEvent('CustomEvent');
            if (typeof CustomEvent !== 'function') {
                openEvent.initCustomEvent('jeInputOpened', true, true, {});
            }
            window.dispatchEvent(openEvent);
            if (typeof window.onJeInputOpened === 'function') {
                window.onJeInputOpened();
            }
        }
    });

    window.addEventListener('jePopupClosed', function (e) {
        if (e.detail && e.detail.id === 'je-pop-inputmap') {
            var closeEvent = (typeof CustomEvent === 'function') ?
                new CustomEvent('jeInputClosed') :
                document.createEvent('CustomEvent');
            if (typeof CustomEvent !== 'function') {
                closeEvent.initCustomEvent('jeInputClosed', true, true, {});
            }
            window.dispatchEvent(closeEvent);
            if (typeof window.onJeInputClosed === 'function') {
                window.onJeInputClosed();
            }
        }
    });

    // - Expose functions for external callers if needed -
    window._jeBuildKeyboardBinds       = buildKeyboardBinds;
    window._jeBuildGamepadBinds        = buildGamepadBinds;
    window._jeGetActiveControlScheme   = getActiveControlScheme;
    window._jeGetActiveSchemeDefinition = getActiveSchemeDefinition;
    window._jeGetActiveInputButtons    = getActiveInputButtons;

})();