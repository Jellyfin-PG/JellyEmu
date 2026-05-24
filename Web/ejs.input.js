(function () {
    var cfg = window.JellyEmuConfig || {};
    var itemId      = cfg.itemId      || '';
    var userId      = cfg.userId      || '';
    var savePostUrl = cfg.savePostUrl || '';
    var activeSlot  = cfg.activeSlot  || 1;
    var token       = cfg.token       || '';

    // ── Helpers ──
    function emu() { return window.EJS_emulator; }
    function gm()  { var e = emu(); return e ? e.gameManager : null; }
    function _jeEnsureBinary(data) { return window._jeEnsureBinary ? window._jeEnsureBinary(data) : data; }
    function openPopup(id)  { window._jeOpenPopup  && window._jeOpenPopup(id);  }
    function closePopup(id) { window._jeClosePopup && window._jeClosePopup(id); }
    function syncVGToggles(){ window._jeSyncVGToggles && window._jeSyncVGToggles(); }

    // ── Input Map ──
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

    // ── Hotkey handlers ──
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

    // ── Key code → display name ──
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

    // ── Button index → EJS label
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

    // ── Axis index → EJS axis name
    var _jeAxisNames = ['LEFT_STICK_X','LEFT_STICK_Y','RIGHT_STICK_X','RIGHT_STICK_Y'];
    function _jeAxisLabel(ai, val) {
        var name = ai < _jeAxisNames.length ? _jeAxisNames[ai] : ('EXTRA_STICK_' + ai);
        return name + (val > 0 ? ':+1' : ':-1');
    }

    // ── Default bindings ──
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

    // ── Live binding map ──
    var _jeBindings = {};
    function _jeLoadBindings(serverPrefs) {
        try {
            var saved = serverPrefs && serverPrefs.jeBindings ? JSON.parse(serverPrefs.jeBindings) : null;
            _jeBindings = saved || JSON.parse(JSON.stringify(_jeDefaultBindings));
        } catch (e) {
            _jeBindings = JSON.parse(JSON.stringify(_jeDefaultBindings));
        }
    }
    _jeLoadBindings(null);

    if (userId) {
        var prefHeaders = {};
        if (token) prefHeaders['Authorization'] = 'MediaBrowser Token="' + token + '"';
        fetch('/jellyemu/prefs/' + userId, { headers: prefHeaders })
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

    // ── simulateInput bridge ──
    function _jeSimulate(idx, pressed) {
        if (idx >= 24) { if (pressed) _jeHotkeyAction(idx); return; }
        var g = gm();
        if (!g) { console.warn('[JellyEmu] _jeSimulate: gm() is null'); return; }
        if (typeof g.simulateInput !== 'function') { console.warn('[JellyEmu] _jeSimulate: simulateInput not a function'); return; }
        g.simulateInput(0, idx, pressed ? 1 : 0);
    }

    function _jeFindBindingsForGp(gpStr) {
        var results = [];
        if (!gpStr) return results;
        for (var idx in _jeBindings) {
            var b = _jeBindings[idx];
            if (b.gp1 === gpStr || b.gp2 === gpStr) results.push(parseInt(idx, 10));
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

    // ── Gamepad hijack ──
    var _prevOnGameStart = window.EJS_onGameStart;
    window.EJS_onGameStart = function () {
        console.log('[JellyEmu] EJS_onGameStart fired, hijacking gamepad listeners');

        window.EJS_defaultControls = {
            0: {}, 1: {}, 2: {}, 3: {}
        };

        function _jeHijack() {
            var e = window.EJS_emulator;
            if (!e || !e.gamepad) return false;

            var gh = e.gamepad;

            if (!gh.listeners['buttondown']) return false;

            var ejsButtonDown = gh.listeners['buttondown'] || null;
            var ejsButtonUp   = gh.listeners['buttonup']   || null;
            var ejsAxisChange = gh.listeners['axischanged'] || null;

            gh.on('buttondown', function (ev) {
                var matched = _jeFindBindingsForGp(ev.label);
                if (matched.length > 0) {
                    matched.forEach(function (idx) { _jeSimulate(idx, true); });
                } else {
                    if (ejsButtonDown) ejsButtonDown(ev);
                }
            });

            gh.on('buttonup', function (ev) {
                var matched = _jeFindBindingsForGp(ev.label);
                if (matched.length > 0) {
                    matched.forEach(function (idx) { _jeSimulate(idx, false); });
                } else {
                    if (ejsButtonUp) ejsButtonUp(ev);
                }
            });

            gh.on('axischanged', function (ev) {
                var axisName = ev.axis;
                var newVal   = ev.value;

                if (ev.label) {
                    var matched = _jeFindBindingsForGp(ev.label);
                    if (matched.length > 0) {
                        matched.forEach(function (idx) { _jeSimulate(idx, true); });
                        var oppositeDir = newVal > 0 ? ':-1' : ':+1';
                        _jeFindBindingsForGp(axisName + oppositeDir).forEach(function (idx) {
                            _jeSimulate(idx, false);
                        });
                        return;
                    }
                }

                if (!ev.label) {
                    [':+1', ':-1'].forEach(function (dir) {
                        _jeFindBindingsForGp(axisName + dir).forEach(function (idx) {
                            _jeSimulate(idx, false);
                        });
                    });
                    var anyBound = _jeFindBindingsForGp(axisName + ':+1').length > 0 ||
                                   _jeFindBindingsForGp(axisName + ':-1').length > 0;
                    if (!anyBound && ejsAxisChange) ejsAxisChange(ev);
                    return;
                }

                if (ejsAxisChange) ejsAxisChange(ev);
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
                if (_prevOnGameStart) _prevOnGameStart();
                return;
            }
            if (attemptsLeft <= 0) {
                console.warn('[JellyEmu] Gave up waiting for EJS gamepad listeners');
                if (_prevOnGameStart) _prevOnGameStart();
                return;
            }
            setTimeout(function () { _jeTryHijack(attemptsLeft - 1); }, 100);
        }

        _jeTryHijack(30);
    };

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
        panel.innerHTML =
            '<div class="je-bind-headers">' +
                '<span>Action</span>' +
                '<span>KB 1</span>' +
                '<span>KB 2</span>' +
            '</div>';

        Object.keys(inputMap).forEach(function (keyStr) {
            var idx = parseInt(keyStr, 10);
            var b   = _jeBindings[idx] || { kb1:0, kb2:0, gp1:'', gp2:'' };
            var row = document.createElement('div');
            row.className = 'je-bind-row';

            var label = document.createElement('span');
            label.className = 'je-bind-label';
            label.textContent = inputMap[idx];
            row.appendChild(label);

            ['kb1', 'kb2'].forEach(function (field) {
                row.appendChild(_jeMakeBindKey(_jeKeyName(b[field]), function () {
                    _jeListenKeyboard(idx, field, row);
                }));
            });

            panel.appendChild(row);
        });
    }

    function buildGamepadBinds() {
        var e  = window.EJS_emulator;
        var gh = e && e.gamepad;
        var gp = gh && gh.gamepads && gh.gamepads[0];

        document.getElementById('je-gp-status').textContent = gp
            ? ('Detected: ' + gp.id)
            : 'No gamepad detected';

        var bindsPanel = document.getElementById('je-gp-binds');
        bindsPanel.innerHTML =
            '<div class="je-bind-headers">' +
                '<span>Action</span>' +
                '<span>GP 1</span>' +
                '<span>GP 2</span>' +
            '</div>';

        Object.keys(inputMap).forEach(function (keyStr) {
            var idx = parseInt(keyStr, 10);
            var b   = _jeBindings[idx] || { kb1:0, kb2:0, gp1:'', gp2:'' };
            var row = document.createElement('div');
            row.className = 'je-bind-row';

            var label = document.createElement('span');
            label.className = 'je-bind-label';
            label.textContent = inputMap[idx];
            row.appendChild(label);

            ['gp1', 'gp2'].forEach(function (field) {
                row.appendChild(_jeMakeBindKey(_jeGpName(b[field]), function () {
                    _jeListenGamepad(idx, field, row);
                }));
            });

            bindsPanel.appendChild(row);
        });
    }

    // ── Keyboard listen ──
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

    // ── Gamepad listen
    function _jeListenGamepad(idx, field, row) {
        var col = field === 'gp1' ? 1 : 2;
        var bk  = row.children[col];
        if (!bk || bk.classList.contains('je-listening')) return;
        bk.classList.add('je-listening');
        bk.textContent = 'Move/press…';

        var baseAxes = [];
        var pads0 = navigator.getGamepads ? navigator.getGamepads() : [];
        for (var gi0 = 0; gi0 < pads0.length; gi0++) {
            var p0 = pads0[gi0]; if (!p0) continue;
            for (var ai0 = 0; ai0 < p0.axes.length; ai0++) baseAxes[ai0] = p0.axes[ai0];
            break;
        }

        var pollId = setInterval(function () {
            var pads = navigator.getGamepads ? navigator.getGamepads() : [];
            for (var gi = 0; gi < pads.length; gi++) {
                var pad = pads[gi]; if (!pad) continue;

                // Buttons
                for (var bi = 0; bi < pad.buttons.length; bi++) {
                    if (pad.buttons[bi].pressed) {
                        clearInterval(pollId);
                        var gpStr = _jeButtonLabel(bi);
                        bk.classList.remove('je-listening');
                        _jeBindings[idx][field] = gpStr;
                        bk.textContent = _jeGpName(gpStr);
                        _jeSyncBindingsToServer();
                        return;
                    }
                }

                // Axes
                for (var ai = 0; ai < pad.axes.length; ai++) {
                    var base = baseAxes[ai] !== undefined ? baseAxes[ai] : 0;
                    var val  = pad.axes[ai];
                    if (Math.abs(val) > 0.5 && Math.abs(val - base) > 0.5) {
                        clearInterval(pollId);
                        var gpStr = _jeAxisLabel(ai, val);
                        bk.classList.remove('je-listening');
                        _jeBindings[idx][field] = gpStr;
                        bk.textContent = _jeGpName(gpStr);
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

    // ── Virtual gamepad toggles ──
    function syncVGTogglesLocal() {
        var e = emu(); if (!e) return;
        var vgOn = document.getElementById('je-vg-toggle');
        if (vgOn && e.virtualGamepad) vgOn.checked = e.virtualGamepad.style.display !== 'none';
    }
    document.getElementById('je-vg-toggle').addEventListener('change', function () {
        var e = emu(); if (!e || !e.toggleVirtualGamepad) return;
        e.toggleVirtualGamepad(this.checked);
    });
    document.getElementById('je-vg-lefty').addEventListener('change', function () {
        var e = emu(); if (!e || !e.toggleVirtualGamepadLeftHanded) return;
        e.toggleVirtualGamepadLeftHanded(this.checked);
    });

    // ── Reset to defaults ──
    document.getElementById('je-input-reset').addEventListener('click', function () {
        _jeBindings = JSON.parse(JSON.stringify(_jeDefaultBindings));
        buildKeyboardBinds();
        buildGamepadBinds();
        _jeSyncBindingsToServer();
    });

    // ── Wire up the dock button ──
    document.getElementById('je-btn-inputmap').addEventListener('click', function () {
        buildKeyboardBinds();
        buildGamepadBinds();
        syncVGTogglesLocal();
        openPopup('je-pop-inputmap');
    });

    // ── Expose build functions for external callers if needed ──
    window._jeBuildKeyboardBinds = buildKeyboardBinds;
    window._jeBuildGamepadBinds  = buildGamepadBinds;

})();