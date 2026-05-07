/**
 * JellyEmu Save States + Import/Export Module
 *
 * Handles the Save States popup and Import/Export popup.
 *
 * Depends on:
 *   - window.JellyEmuConfig      { itemId, userId }
 *   - window._jeEnsureBinary     exposed by the main template IIFE
 *   - window._jeOpenPopup        exposed by the main template IIFE
 *   - window._jeClosePopup       exposed by the main template IIFE
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

    function ensureBinary(data) {
        return window._jeEnsureBinary ? window._jeEnsureBinary(data) : null;
    }

    function uploadScreenshot(slot, afterPromise) {
        if (window._jeUploadScreenshot) window._jeUploadScreenshot(slot, afterPromise);
    }

    // ── Save States ────────────────────────────────────────────────

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
                    var state = ensureBinary(rawState);
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
                        uploadScreenshot(s);
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

    document.getElementById('je-btn-saves').addEventListener('click', function () {
        buildSaveSlots();
        window._jeOpenPopup('je-pop-saves');
    });

    // ── Import / Export ────────────────────────────────────────────

    document.getElementById('je-btn-io').addEventListener('click', function () {
        window._jeOpenPopup('je-pop-io');
    });

    // Export Save State (.state)
    document.getElementById('je-io-exp-state').addEventListener('click', function () {
        var g = gm(); if (!g) return;
        Promise.resolve(g.getState()).then(function (rawState) {
            var stateBlob = ensureBinary(rawState);
            if (!stateBlob || stateBlob.size === 0) return alert('No state data available.');

            var url = URL.createObjectURL(stateBlob);
            var a = document.createElement('a');
            a.href = url;
            a.download = (window.EJS_gameName || 'game').replace(/[^a-z0-9]/gi, '_') + '.state';
            a.click();
            URL.revokeObjectURL(url);
        });
    });

    // Export SRAM (.sav)
    document.getElementById('je-io-exp-sram').addEventListener('click', function () {
        var g = gm(); if (!g) return;

        var rawSave = g.getSaveFile();
        if (!rawSave) return alert('No in-game SRAM data available. Make sure you saved in-game first!');

        var saveBlob = ensureBinary(rawSave);
        var url = URL.createObjectURL(saveBlob);
        var a = document.createElement('a');
        a.href = url;
        a.download = (window.EJS_gameName || 'game').replace(/[^a-z0-9]/gi, '_') + '.sav';
        a.click();
        URL.revokeObjectURL(url);
    });

    // Import drag/drop & file select
    var ioDrop = document.getElementById('je-io-dropzone');
    var ioFile = document.getElementById('je-io-file');

    ioDrop.addEventListener('click', function () { ioFile.click(); });

    ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(function (evt) {
        ioDrop.addEventListener(evt, function (e) { e.preventDefault(); e.stopPropagation(); }, false);
    });

    ['dragenter', 'dragover'].forEach(function (evt) {
        ioDrop.addEventListener(evt, function () {
            ioDrop.style.borderColor = 'rgba(100,200,255,.8)';
            ioDrop.style.background  = 'rgba(100,200,255,.1)';
        }, false);
    });

    ['dragleave', 'drop'].forEach(function (evt) {
        ioDrop.addEventListener(evt, function () {
            ioDrop.style.borderColor = 'rgba(255,255,255,.2)';
            ioDrop.style.background  = 'transparent';
        }, false);
    });

    ioDrop.addEventListener('drop', function (e) {
        if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
            _jeHandleImport(e.dataTransfer.files[0]);
        }
    }, false);

    ioFile.addEventListener('change', function (e) {
        if (e.target.files && e.target.files.length > 0) {
            _jeHandleImport(e.target.files[0]);
            e.target.value = '';
        }
    });

    function _jeHandleImport(file) {
        var g = gm(); if (!g) return;
        var isSram = file.name.toLowerCase().endsWith('.sav') || file.name.toLowerCase().endsWith('.srm');

        var reader = new FileReader();
        reader.onload = function (e) {
            try {
                var uint8 = new Uint8Array(e.target.result);

                if (isSram) {
                    var sramPath = g.getSaveFilePath();
                    if (!sramPath) {
                        alert('Could not determine the SRAM path for this emulator core.');
                        return;
                    }
                    try { g.FS.unlink(sramPath); } catch (err) {}
                    g.FS.writeFile(sramPath, uint8);
                    g.loadSaveFiles();
                    alert('SRAM imported successfully! The emulator will now restart to load the battery data.');
                    g.restart();
                    window._jeClosePopup('je-pop-io');
                } else {
                    g.loadState(uint8);
                    window._jeClosePopup('je-pop-io');
                }
            } catch (err) {
                console.error('[JellyEmu] Import error:', err);
                alert('Failed to import file. The data may be corrupt or incompatible with this emulator core.');
            }
        };
        reader.readAsArrayBuffer(file);
    }

})();