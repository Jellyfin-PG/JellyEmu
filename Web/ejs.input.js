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

    // - Input Map -
    var inputMap = {
        0:  'B',               1:  'Y',              2:  'SELECT',         3:  'START',
        4:  'UP',              5:  'DOWN',            6:  'LEFT',           7:  'RIGHT',
        8:  'A',               9:  'X',               10: 'L',              11: 'R',
        12: 'L2',             13: 'R2',              14: 'L3',             15: 'R3',
        16: 'L STICK RIGHT',  17: 'L STICK LEFT',   18: 'L STICK DOWN',  19: 'L STICK UP',
        20: 'R STICK RIGHT',  21: 'R STICK LEFT',   22: 'R STICK DOWN',  23: 'R STICK UP',
        24: 'QUICK SAVE',     25: 'QUICK LOAD',      26: 'CHANGE SLOT',   27: 'FAST FORWARD',
        28: 'REWIND',          29: 'SLOW MOTION'
    };

    var n64InputMap = {
        0:  'N64 B BUTTON',   1:  'C-BUTTON LEFT',   2:  'Z-TRIGGER (Alt)',3:  'START',
        4:  'D-PAD UP',       5:  'D-PAD DOWN',      6:  'D-PAD LEFT',     7:  'D-PAD RIGHT',
        8:  'N64 A BUTTON',   9:  'C-BUTTON DOWN',   10: 'L SHOULDER',     11: 'R SHOULDER',
        12: 'Z-TRIGGER',      13: 'C-BUTTON RIGHT',  14: 'C-BUTTON UP',    15: 'R3',
        16: 'STICK RIGHT',    17: 'STICK LEFT',      18: 'STICK DOWN',     19: 'STICK UP',
        20: 'C-BUTTON RIGHT', 21: 'C-BUTTON LEFT',   22: 'C-BUTTON DOWN',  23: 'C-BUTTON UP',
        24: 'QUICK SAVE',     25: 'QUICK LOAD',      26: 'CHANGE SLOT',   27: 'FAST FORWARD',
        28: 'REWIND',          29: 'SLOW MOTION'
    };

    function _jeIsN64() {
        var core = (window.EJS_core || '').toLowerCase();
        var platform = (window.EJS_platformTag || '').toUpperCase();
        return platform === 'N64' || core === 'mupen64plus_next' || core === 'parallel_n64' || core === 'n64';
    }

    function getActiveInputMap() {
        return _jeIsN64() ? n64InputMap : inputMap;
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

    // - Default bindings -
    var _jeDefaultBindings = {
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
        14: { kb1:0,   kb2:0, gp1:'LEFT_STICK',            gp2:'' },
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

    var _jeN64DefaultBindings = {
        0:  { kb1:67,  kb2:0, gp1:'BUTTON_2',              gp2:'BUTTON_3' },
        1:  { kb1:0,   kb2:0, gp1:'RIGHT_STICK_X:-1',      gp2:'' },
        2:  { kb1:0,   kb2:0, gp1:'',                      gp2:'' },
        3:  { kb1:13,  kb2:0, gp1:'START',                 gp2:'' },
        4:  { kb1:38,  kb2:0, gp1:'DPAD_UP',               gp2:'' },
        5:  { kb1:40,  kb2:0, gp1:'DPAD_DOWN',             gp2:'' },
        6:  { kb1:37,  kb2:0, gp1:'DPAD_LEFT',             gp2:'' },
        7:  { kb1:39,  kb2:0, gp1:'DPAD_RIGHT',            gp2:'' },
        8:  { kb1:88,  kb2:0, gp1:'BUTTON_1',              gp2:'' },
        9:  { kb1:0,   kb2:0, gp1:'RIGHT_STICK_Y:+1',      gp2:'' },
        10: { kb1:81,  kb2:0, gp1:'LEFT_TOP_SHOULDER',     gp2:'' },
        11: { kb1:69,  kb2:0, gp1:'RIGHT_TOP_SHOULDER',    gp2:'' },
        12: { kb1:90,  kb2:0, gp1:'LEFT_BOTTOM_SHOULDER',  gp2:'RIGHT_BOTTOM_SHOULDER' },
        13: { kb1:0,   kb2:0, gp1:'RIGHT_STICK_X:+1',      gp2:'' },
        14: { kb1:0,   kb2:0, gp1:'RIGHT_STICK_Y:-1',      gp2:'BUTTON_4' },
        15: { kb1:0,   kb2:0, gp1:'LEFT_STICK',            gp2:'' },
        16: { kb1:76,  kb2:0, gp1:'LEFT_STICK_X:+1',       gp2:'' },
        17: { kb1:74,  kb2:0, gp1:'LEFT_STICK_X:-1',       gp2:'' },
        18: { kb1:75,  kb2:0, gp1:'LEFT_STICK_Y:+1',       gp2:'' },
        19: { kb1:73,  kb2:0, gp1:'LEFT_STICK_Y:-1',       gp2:'' },
        20: { kb1:0,   kb2:0, gp1:'',                      gp2:'' },
        21: { kb1:83,  kb2:0, gp1:'',                      gp2:'' },
        22: { kb1:65,  kb2:0, gp1:'',                      gp2:'' },
        23: { kb1:87,  kb2:0, gp1:'',                      gp2:'' },
        24: { kb1:49,  kb2:0, gp1:'', gp2:'' },
        25: { kb1:50,  kb2:0, gp1:'', gp2:'' },
        26: { kb1:51,  kb2:0, gp1:'', gp2:'' },
        27: { kb1:107, kb2:0, gp1:'', gp2:'' },
        28: { kb1:32,  kb2:0, gp1:'', gp2:'' },
        29: { kb1:109, kb2:0, gp1:'', gp2:'' }
    };

    // - Live binding map -
    var _jeBindings = {};
    function _jeLoadBindings(serverPrefs) {
        var defaults = _jeIsN64() ? _jeN64DefaultBindings : _jeDefaultBindings;
        try {
            var saved = serverPrefs && serverPrefs.jeBindings ? JSON.parse(serverPrefs.jeBindings) : null;
            _jeBindings = saved || JSON.parse(JSON.stringify(defaults));
        } catch (e) {
            _jeBindings = JSON.parse(JSON.stringify(defaults));
        }

        if (_jeIsN64()) {
            if (!_jeBindings[16] || !_jeBindings[16].gp1) _jeBindings[16] = JSON.parse(JSON.stringify(_jeN64DefaultBindings[16]));
            if (!_jeBindings[17] || !_jeBindings[17].gp1) _jeBindings[17] = JSON.parse(JSON.stringify(_jeN64DefaultBindings[17]));
            if (!_jeBindings[18] || !_jeBindings[18].gp1) _jeBindings[18] = JSON.parse(JSON.stringify(_jeN64DefaultBindings[18]));
            if (!_jeBindings[19] || !_jeBindings[19].gp1) _jeBindings[19] = JSON.parse(JSON.stringify(_jeN64DefaultBindings[19]));

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
        var cPlatform = (window.JellyEmuConfig && window.JellyEmuConfig.platformTag) ? encodeURIComponent(window.JellyEmuConfig.platformTag) : '';
        fetch('/jellyemu/prefs/' + userId + '/effective?itemId=' + cItemId + '&platform=' + cPlatform, { headers: prefHeaders })
            .then(function (r) { if (r.ok) return r.json(); })
            .then(function (data) {
                if (data && data.jeBindings) {
                    _jeLoadBindings(data);
                    if (document.getElementById('je-tab-kb')) {
                        buildKeyboardBinds();
                        buildGamepadBinds();
                    }
                }
            })
            .catch(function (err) { console.warn('[JellyEmu] Failed to load bindings:', err); });
    }

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

        var mapName = getActiveInputMap()[idx] || idx;
        console.log('[JellyEmu Input] _jeSimulate | Index:', idx, '(' + mapName + ') | Pressed:', boolPressed);

        if (_jeIsN64() && idx >= 16 && idx <= 19) {
            try { g.simulateInput(0, idx, boolPressed ? 32767 : 0); } catch (e) {}
            return;
        }

        g.simulateInput(0, idx, boolPressed ? 1 : 0);
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
            if (b.kb1 === kc || b.kb2 === kc) _jeSimulate(parseInt(idx, 10), false);
        }
    }, true);

    // - Gamepad hijack -
    window.addEventListener('jellyemu:gamestart', function () {
        console.log('[JellyEmu] jellyemu:gamestart event received, hijacking gamepad listeners');

        window.EJS_defaultControls = {
            0: {}, 1: {}, 2: {}, 3: {}
        };

        var ejsButtonDown = null;
        var ejsButtonUp   = null;
        var ejsAxisChange = null;
        var _jeGpActiveState = {};

        function _jeHijack() {
            var e = window.EJS_emulator;
            if (!e || !e.gamepad) return false;

            var gh = e.gamepad;

            var originalOn = gh.on;
            if (originalOn && !originalOn._jeHijacked) {
                gh.on = function (name, cb) {
                    var lowerName = name.toLowerCase();
                    if (lowerName === 'buttondown') {
                        ejsButtonDown = cb;
                    } else if (lowerName === 'buttonup') {
                        ejsButtonUp = cb;
                    } else if (lowerName === 'axischanged') {
                        ejsAxisChange = cb;
                    } else {
                        originalOn.call(gh, name, cb);
                    }
                };
                gh.on._jeHijacked = true;
            }

            if (gh.listeners) {
                if (gh.listeners['buttondown'] && gh.listeners['buttondown'] !== ejsButtonDown) {
                    ejsButtonDown = gh.listeners['buttondown'];
                }
                if (gh.listeners['buttonup'] && gh.listeners['buttonup'] !== ejsButtonUp) {
                    ejsButtonUp = gh.listeners['buttonup'];
                }
                if (gh.listeners['axischanged'] && gh.listeners['axischanged'] !== ejsAxisChange) {
                    ejsAxisChange = gh.listeners['axischanged'];
                }
            }

            var registerOn = originalOn || gh.on;

            registerOn.call(gh, 'buttondown', function (ev) {
                var label = ev.label || (ev.button !== undefined ? _jeButtonLabel(ev.button) : '');
                console.log('[JellyEmu Gamepad] buttondown | Label:', label, '| Ev:', ev);
                var matched = _jeFindBindingsForGp(label);
                if (matched.length === 0 && ev.button !== undefined) {
                    matched = _jeFindBindingsForGp(_jeButtonLabel(ev.button));
                }
                if (matched.length > 0) {
                    if (!_jeGpActiveState[label]) {
                        _jeGpActiveState[label] = true;
                        matched.forEach(function (idx) { _jeSimulate(idx, true); });
                    }
                } else {
                    if (ejsButtonDown) ejsButtonDown(ev);
                }
            });

            registerOn.call(gh, 'buttonup', function (ev) {
                var label = ev.label || (ev.button !== undefined ? _jeButtonLabel(ev.button) : '');
                console.log('[JellyEmu Gamepad] buttonup | Label:', label);
                var matched = _jeFindBindingsForGp(label);
                if (matched.length === 0 && ev.button !== undefined) {
                    matched = _jeFindBindingsForGp(_jeButtonLabel(ev.button));
                }
                if (matched.length > 0) {
                    if (_jeGpActiveState[label]) {
                        _jeGpActiveState[label] = false;
                        matched.forEach(function (idx) { _jeSimulate(idx, false); });
                    }
                } else {
                    if (ejsButtonUp) ejsButtonUp(ev);
                }
            });

            registerOn.call(gh, 'axischanged', function (ev) {
                var axisName = ev.axis;
                var newVal   = ev.value;

                if (Math.abs(newVal) > 0.25) {
                    var isPos = newVal > 0;
                    var posLabel = axisName + ':+1';
                    var negLabel = axisName + ':-1';
                    var activeLabel = isPos ? posLabel : negLabel;
                    var inactiveLabel = isPos ? negLabel : posLabel;

                    if (!_jeGpActiveState[activeLabel]) {
                        _jeGpActiveState[activeLabel] = true;
                        var activeBinds = _jeFindBindingsForGp(activeLabel);
                        activeBinds.forEach(function (idx) {
                            _jeSimulate(idx, true);
                        });
                    }

                    if (_jeGpActiveState[inactiveLabel]) {
                        _jeGpActiveState[inactiveLabel] = false;
                        _jeFindBindingsForGp(inactiveLabel).forEach(function (idx) {
                            _jeSimulate(idx, false);
                        });
                    }
                } else {
                    [':+1', ':-1'].forEach(function (dir) {
                        var checkLabel = axisName + dir;
                        if (_jeGpActiveState[checkLabel]) {
                            _jeGpActiveState[checkLabel] = false;
                            _jeFindBindingsForGp(checkLabel).forEach(function (idx) {
                                _jeSimulate(idx, false);
                            });
                        }
                    });
                }

                if (ejsAxisChange) {
                    try { ejsAxisChange(ev); } catch (e) {}
                }
            });

            var ejsControls = e.controls && e.controls[0];
            if (ejsControls) {
                Object.keys(ejsControls).forEach(function (k) {
                    if (ejsControls[k] && ejsControls[k].value2 !== undefined) {
                        ejsControls[k].value2 = '';
                    }
                });
                console.log('[JellyEmu] EJS controls.value2 cleared');
            }

            console.log('[JellyEmu] Gamepad listeners hijacked successfully');
            return true;
        }

        function _jeTryHijack(attemptsLeft) {
            if (_jeHijack()) {
                return;
            }
            if (attemptsLeft <= 0) {
                console.warn('[JellyEmu] Gave up waiting for EJS gamepad object');
                return;
            }
            setTimeout(function () { _jeTryHijack(attemptsLeft - 1); }, 100);
        }

        _jeTryHijack(50);

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
            fetch('/jellyemu/prefs/' + userId, {
                method: 'POST',
                headers: headers,
                body: JSON.stringify({ jeBindings: JSON.stringify(_jeBindings) })
            }).catch(function (err) { console.warn('[JellyEmu] Bindings sync failed:', err); });
        }, 800);
    }

    (function () {
        var s = document.createElement('style');
        s.textContent =
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
        var activeMap = getActiveInputMap();
        panel.innerHTML =
            '<div class="je-bind-headers">' +
                '<span>Action</span>' +
                '<span>KB 1</span>' +
                '<span>KB 2</span>' +
            '</div>';

        Object.keys(activeMap).forEach(function (keyStr) {
            var idx = parseInt(keyStr, 10);
            var b   = _jeBindings[idx] || { kb1:0, kb2:0, gp1:'', gp2:'' };
            var row = document.createElement('div');
            row.className = 'je-bind-row';

            var label = document.createElement('span');
            label.className = 'je-bind-label';
            label.textContent = activeMap[idx];
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

        var activeMap = getActiveInputMap();
        bindsPanel.innerHTML =
            '<div class="je-bind-headers">' +
                '<span>Action</span>' +
                '<span>GP 1</span>' +
                '<span>GP 2</span>' +
            '</div>';

        Object.keys(activeMap).forEach(function (keyStr) {
            var idx = parseInt(keyStr, 10);
            var b   = _jeBindings[idx] || { kb1:0, kb2:0, gp1:'', gp2:'' };
            var row = document.createElement('div');
            row.className = 'je-bind-row';

            var label = document.createElement('span');
            label.className = 'je-bind-label';
            label.textContent = activeMap[idx];
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
            if (ev.keyCode === 27) { bk.textContent = _jeKeyName(_jeBindings[idx][field]); return; }
            _jeBindings[idx][field] = ev.keyCode;
            bk.textContent = _jeKeyName(ev.keyCode);
            _jeSyncBindingsToServer();
        }
        document.addEventListener('keydown', onKey, true);
    }

    // - Gamepad listen
    function _jeListenGamepad(idx, field, row) {
        var col = field === 'gp1' ? 1 : 2;
        var bk  = row.children[col];
        if (!bk || bk.classList.contains('je-listening')) return;
        bk.classList.add('je-listening');
        bk.textContent = 'Move/press…';

        var baseAxes = {};
        var pads0 = navigator.getGamepads ? navigator.getGamepads() : [];
        for (var gi0 = 0; gi0 < pads0.length; gi0++) {
            var p0 = pads0[gi0]; if (!p0) continue;
            baseAxes[p0.index] = [];
            for (var ai0 = 0; ai0 < p0.axes.length; ai0++) {
                baseAxes[p0.index][ai0] = p0.axes[ai0];
            }
        }

        var pollId = setInterval(function () {
            var pads = navigator.getGamepads ? navigator.getGamepads() : [];
            for (var gi = 0; gi < pads.length; gi++) {
                var p = pads[gi]; if (!p) continue;

                for (var bi = 0; bi < p.buttons.length; bi++) {
                    var btn = p.buttons[bi];
                    var pressed = (typeof btn === 'object') ? btn.pressed : (btn === 1.0);
                    if (pressed) {
                        clearInterval(pollId);
                        var bindStr = _jeButtonLabel(bi);
                        _jeBindings[idx][field] = bindStr;
                        bk.classList.remove('je-listening');
                        bk.textContent = _jeGpName(bindStr);
                        _jeSyncBindingsToServer();
                        return;
                    }
                }

                var origAxes = baseAxes[p.index] || [];
                for (var ai = 0; ai < p.axes.length; ai++) {
                    var val = p.axes[ai];
                    var initVal = origAxes[ai] !== undefined ? origAxes[ai] : 0;
                    if (Math.abs(val - initVal) > 0.45 && Math.abs(val) > 0.5) {
                        clearInterval(pollId);
                        var axisStr = _jeAxisLabel(ai, val);
                        _jeBindings[idx][field] = axisStr;
                        bk.classList.remove('je-listening');
                        bk.textContent = _jeGpName(axisStr);
                        _jeSyncBindingsToServer();
                        return;
                    }
                }
            }
        }, 50);

        setTimeout(function () {
            if (bk.classList.contains('je-listening')) {
                clearInterval(pollId);
                bk.classList.remove('je-listening');
                bk.textContent = _jeGpName(_jeBindings[idx][field]);
            }
        }, 10000);
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

    document.getElementById('je-vg-toggle').addEventListener('change', function () {
        var e = emu();
        if (e && e.toggleVirtualGamepad) {
            e.toggleVirtualGamepad(this.checked);
        }
        _jeSyncVGPrefs();
    });
    document.getElementById('je-vg-lefty').addEventListener('change', function () {
        var e = emu();
        if (e && e.toggleVirtualGamepadLeftHanded) {
            e.toggleVirtualGamepadLeftHanded(this.checked);
        }
        _jeSyncVGPrefs();
    });

    window.addEventListener('jeLoaded', function () {
        syncVGTogglesLocal();
    });

    // - Reset to defaults -
    document.getElementById('je-input-reset').addEventListener('click', function () {
        _jeBindings = JSON.parse(JSON.stringify(_jeDefaultBindings));
        buildKeyboardBinds();
        buildGamepadBinds(true);
        _jeSyncBindingsToServer();

        var vgOn = document.getElementById('je-vg-toggle');
        if (vgOn) {
            vgOn.checked = false;
            var e = emu();
            if (e && e.toggleVirtualGamepad) e.toggleVirtualGamepad(false);
        }
        var vgLefty = document.getElementById('je-vg-lefty');
        if (vgLefty) {
            vgLefty.checked = false;
            var e = emu();
            if (e && e.toggleVirtualGamepadLeftHanded) e.toggleVirtualGamepadLeftHanded(false);
        }
        _jeSyncVGPrefs();
    });

    // - Wire up the dock button -
    document.getElementById('je-btn-inputmap').addEventListener('click', function () {
        buildKeyboardBinds();
        buildGamepadBinds(true);
        syncVGTogglesLocal();
        openPopup('je-pop-inputmap');
    });

    function _popupOpen() {
        var pop = document.getElementById('je-pop-inputmap');
        return pop && (pop.classList.contains('je-popup-active') || pop.style.display !== 'none');
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

    // - Expose build functions for external callers if needed -
    window._jeBuildKeyboardBinds = buildKeyboardBinds;
    window._jeBuildGamepadBinds  = buildGamepadBinds;

})();