/**
 * JellyEmu Save States Module
 *
 * Handles the Save States popup: building slots, saving, loading, screenshots.
 *
 * Depends on:
 *   - window.JellyEmuConfig  { itemId, userId }
 *   - window._jeEnsureBinary   exposed by the main template IIFE
 *   - window._jeOpenPopup      exposed by the main template IIFE
 *   - window._jeClosePopup     exposed by the main template IIFE
 *   - window.EJS_emulator / gameManager
 */
(function () {
    'use strict';

    var cfg    = window.JellyEmuConfig || {};
    var itemId = cfg.itemId || '';
    var userId = cfg.userId || '';

    function gm() {
        var e = window.EJS_emulator;
        return e ? e.gameManager : null;
    }

    function buildSaveSlots() {
        var body = document.getElementById('je-saves-body');
        body.innerHTML = '';

        for (var i = 1; i <= 5; i++) {
            var slot = document.createElement('div');
            slot.className = 'je-slot';
            slot.innerHTML =
                '<div class="je-slot-num">' + i + '</div>' +
                '<div class="je-slot-info"><div>Slot ' + i + '</div>' +
                '<small id="je-slot-status-' + i + '">Checking…</small></div>' +
                '<div class="je-slot-actions">' +
                '<button class="je-btn" data-save="' + i + '">Save</button>' +
                '<button class="je-btn je-btn-primary" data-load="' + i + '">Load</button>' +
                '</div>';
            body.appendChild(slot);

            // Check whether this slot already has data
            (function (s) {
                fetch('/jellyemu/save/' + itemId + '/' + userId + '?slot=' + s, { method: 'HEAD' })
                    .then(function (r) {
                        var el = document.getElementById('je-slot-status-' + s);
                        if (el) el.textContent = r.ok ? 'Has save data' : 'Empty';
                    })
                    .catch(function () {
                        var el = document.getElementById('je-slot-status-' + s);
                        if (el) el.textContent = 'Empty';
                    });
            })(i);
        }

        // Save buttons
        body.querySelectorAll('[data-save]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var s = parseInt(btn.getAttribute('data-save'));
                var g = gm(); if (!g) return;

                Promise.resolve(g.getState()).then(function (rawState) {
                    var state = window._jeEnsureBinary(rawState);
                    if (!state) return;
                    console.log('[JellyEmu] Pipeline STAGE 1 (Client Gen): Payload size ->', state.size || state.byteLength, 'bytes');

                    fetch('/jellyemu/save/' + itemId + '/' + userId + '?slot=' + s, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/octet-stream' },
                        body: state
                    }).then(function (r) {
                        if (!r.ok) throw new Error('Save rejected by server');
                        var el = document.getElementById('je-slot-status-' + s);
                        if (el) el.textContent = 'Saved!';

                        // Capture and upload screenshot for this slot
                        var canvas = document.querySelector('canvas.ejs_canvas') || document.querySelector('canvas');
                        if (canvas) {
                            try {
                                var dataUrl = canvas.toDataURL('image/png');
                                fetch('/jellyemu/save-screenshot/' + itemId + '/' + userId + '/' + s, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ dataUrl: dataUrl })
                                }).catch(function (err) { console.warn('[JellyEmu] Screenshot upload failed:', err); });
                            } catch (ex) { console.warn('[JellyEmu] Screenshot capture failed:', ex); }
                        }
                    }).catch(function (err) {
                        console.error('[JellyEmu] Save failed:', err);
                        var el = document.getElementById('je-slot-status-' + s);
                        if (el) el.textContent = 'Save Failed';
                    });
                });
            });
        });

        // Load buttons
        body.querySelectorAll('[data-load]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var s = parseInt(btn.getAttribute('data-load'));

                fetch('/jellyemu/save/' + itemId + '/' + userId + '?slot=' + s)
                    .then(function (r) {
                        if (!r.ok) throw new Error('No save');
                        return r.arrayBuffer();
                    })
                    .then(function (buf) {
                        var g = gm(); if (!g) return;
                        console.log('[JellyEmu] Pipeline STAGE 4 (Client Receive): Downloaded bytes ->', buf.byteLength);
                        window._jeClosePopup('je-pop-saves');
                        setTimeout(function () {
                            g.loadState(new Uint8Array(buf));
                        }, 100);
                    })
                    .catch(function () {
                        var el = document.getElementById('je-slot-status-' + s);
                        if (el) el.textContent = 'No save to load';
                    });
            });
        });
    }

    // Wire up the dock button once the DOM is ready
    document.getElementById('je-btn-saves').addEventListener('click', function () {
        buildSaveSlots();
        window._jeOpenPopup('je-pop-saves');
    });

})();