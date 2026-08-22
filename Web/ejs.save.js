/**
 * JellyEmu Save States + SRAM Cloud Backup Manager
 *
 * Handles the unified Saves & States popup (Cloud & Local) with responsive tabs.
 *
 * Depends on:
 *   - window.JellyEmuConfig      { itemId, userId, token }
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
    var token  = cfg.token || '';
    var isM3u  = !!cfg.isM3u;

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

    // Helper for authenticated requests
    function jeFetch(url, options) {
        options = options || {};
        if (token) {
            options.headers = options.headers || {};
            options.headers['Authorization'] = 'MediaBrowser Token="' + token + '"';
        }
        return fetch(url, options);
    }

    // Tab Management

    var tabBtnStates = document.getElementById('je-tab-btn-states');
    var tabBtnSram   = document.getElementById('je-tab-btn-sram');
    var panelStates  = document.getElementById('je-panel-states');
    var panelSram    = document.getElementById('je-panel-sram');

    function setActiveTab(tab) {
        if (tab === 'states') {
            tabBtnStates.classList.add('je-tab-active');
            tabBtnSram.classList.remove('je-tab-active');
            panelStates.style.display = 'flex';
            panelSram.style.display = 'none';
        } else {
            tabBtnSram.classList.add('je-tab-active');
            tabBtnStates.classList.remove('je-tab-active');
            panelSram.style.display = 'flex';
            panelStates.style.display = 'none';
        }
    }

    if (tabBtnStates && tabBtnSram) {
        tabBtnStates.addEventListener('click', function () { setActiveTab('states'); });
        tabBtnSram.addEventListener('click', function () { setActiveTab('sram'); });
    }

    // Screenshots

    function loadSlotScreenshot(s, thumbEl) {
        jeFetch('/jellyemu/save-screenshot/' + itemId + '/' + userId + '/' + s)
            .then(function (r) {
                if (r.ok) return r.json();
                throw new Error();
            })
            .then(function (data) {
                if (data && data.dataUrl) {
                    thumbEl.innerHTML = '<img src="' + data.dataUrl + '" style="width:100%;height:100%;object-fit:cover">';
                    thumbEl.style.opacity = '1';
                } else {
                    showPlaceholder();
                }
            })
            .catch(function () {
                showPlaceholder();
            });

        function showPlaceholder() {
            thumbEl.innerHTML = '<svg viewBox="0 0 24 24" style="width:20px;height:20px;fill:rgba(255,255,255,.3)"><path d="M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z"/></svg>';
            thumbEl.style.opacity = '0.5';
        }
    }

    function loadSramPlaceholder(thumbEl) {
        thumbEl.innerHTML = '<svg viewBox="0 0 24 24" style="width:20px;height:20px;fill:rgba(255,255,255,.3)"><path d="M17 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V7l-4-4zm-5 16c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm3-10H5V5h10v4z"/></svg>';
        thumbEl.style.opacity = '0.5';
    }

    // Save States (Cloud)

    function buildSaveSlots() {
        var body = document.getElementById('je-saves-body');
        if (!body) return;
        body.innerHTML = '';

        for (var i = 1; i <= 5; i++) {
            var slot = document.createElement('div');
            slot.className = 'je-slot';
            slot.innerHTML =
                '<div class="je-slot-num">' + i + '</div>' +
                '<div class="je-slot-thumb" id="je-state-thumb-' + i + '"></div>' +
                '<div class="je-slot-info"><div>Slot ' + i + '</div>' +
                '<small id="je-slot-status-' + i + '">Checking…</small></div>' +
                '<div class="je-slot-actions">' +
                '<button class="je-btn" data-save="' + i + '">Save</button>' +
                '<button class="je-btn je-btn-primary" data-load="' + i + '">Load</button>' +
                '</div>';
            body.appendChild(slot);

            var thumbEl = document.getElementById('je-state-thumb-' + i);
            loadSlotScreenshot(i, thumbEl);

            (function (s) {
                jeFetch('/jellyemu/save/' + itemId + '/' + userId + '?slot=' + s, { method: 'HEAD' })
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

                    var headers = { 'Content-Type': 'application/octet-stream' };
                    jeFetch('/jellyemu/save/' + itemId + '/' + userId + '?slot=' + s, {
                        method: 'POST',
                        headers: headers,
                        body: state
                    }).then(function (r) {
                        if (!r.ok) throw new Error('Save rejected');
                        var el = document.getElementById('je-slot-status-' + s);
                        if (el) el.textContent = 'Saved!';
                        var thumbEl = document.getElementById('je-state-thumb-' + s);
                        uploadScreenshot(s, new Promise(function(resolve) {
                            setTimeout(function() {
                                if (thumbEl) loadSlotScreenshot(s, thumbEl);
                                resolve();
                            }, 500);
                        }));
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

                jeFetch('/jellyemu/save/' + itemId + '/' + userId + '?slot=' + s)
                    .then(function (r) {
                        if (!r.ok) throw new Error('No save');
                        return r.arrayBuffer();
                    })
                    .then(function (buf) {
                        var g = gm(); if (!g) return;
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

    // SRAM (Cloud Backups)

    function buildSramSlots() {
        var body = document.getElementById('je-sram-body');
        if (!body) return;
        body.innerHTML = '';

        for (var i = 1; i <= 5; i++) {
            var slot = document.createElement('div');
            slot.className = 'je-slot';
            slot.innerHTML =
                '<div class="je-slot-num">' + i + '</div>' +
                '<div class="je-slot-thumb" id="je-sram-thumb-' + i + '"></div>' +
                '<div class="je-slot-info"><div>Slot ' + i + '</div>' +
                '<small id="je-sram-status-' + i + '">Checking…</small></div>' +
                '<div class="je-slot-actions">' +
                '<button class="je-btn" data-save-sram="' + i + '">Backup</button>' +
                '<button class="je-btn je-btn-primary" data-load-sram="' + i + '">Restore</button>' +
                '</div>';
            body.appendChild(slot);

            var thumbEl = document.getElementById('je-sram-thumb-' + i);
            loadSramPlaceholder(thumbEl);

            (function (s) {
                jeFetch('/jellyemu/sram/' + itemId + '/' + userId + '?slot=' + s, { method: 'HEAD' })
                    .then(function (r) {
                        var el = document.getElementById('je-sram-status-' + s);
                        if (el) el.textContent = r.ok ? 'Has backup' : 'Empty';
                    })
                    .catch(function () {
                        var el = document.getElementById('je-sram-status-' + s);
                        if (el) el.textContent = 'Empty';
                    });
            })(i);
        }

        // Backup buttons
        body.querySelectorAll('[data-save-sram]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var s = parseInt(btn.getAttribute('data-save-sram'));
                var g = gm(); if (!g) return;

                var rawSave = g.getSaveFile();
                if (!rawSave) return alert('No in-game SRAM data available. Make sure you saved in-game first!');

                var saveBlob = ensureBinary(rawSave);
                var headers = { 'Content-Type': 'application/octet-stream' };

                jeFetch('/jellyemu/sram/' + itemId + '/' + userId + '?slot=' + s, {
                    method: 'POST',
                    headers: headers,
                    body: saveBlob
                }).then(function (r) {
                    if (!r.ok) throw new Error('SRAM backup rejected');
                    var el = document.getElementById('je-sram-status-' + s);
                    if (el) el.textContent = 'Backed up!';
                }).catch(function (err) {
                    console.error('[JellyEmu] SRAM backup failed:', err);
                    var el = document.getElementById('je-sram-status-' + s);
                    if (el) el.textContent = 'Backup Failed';
                });
            });
        });

        // Restore buttons
        body.querySelectorAll('[data-load-sram]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var s = parseInt(btn.getAttribute('data-load-sram'));
                var g = gm(); if (!g) return;

                if (!confirm('Restoring this SRAM backup will restart the game and overwrite any unsaved progress. Continue?')) return;

                jeFetch('/jellyemu/sram/' + itemId + '/' + userId + '?slot=' + s)
                    .then(function (r) {
                        if (!r.ok) throw new Error('No SRAM backup');
                        return r.arrayBuffer();
                    })
                    .then(function (buf) {
                        var sramPath = g.getSaveFilePath();
                        if (!sramPath) return alert('SRAM not supported by this core.');
                        var uint8 = new Uint8Array(buf);

                        try { g.FS.unlink(sramPath); } catch (err) {}
                        g.FS.writeFile(sramPath, uint8);
                        g.loadSaveFiles();
                        
                        window._jeClosePopup('je-pop-saves');
                        alert('SRAM backup restored! Restarting...');
                        g.restart();
                    })
                    .catch(function () {
                        var el = document.getElementById('je-sram-status-' + s);
                        if (el) el.textContent = 'No backup to restore';
                    });
            });
        });
    }

    // Dock Save States button triggers our unified modal
    document.getElementById('je-btn-saves').addEventListener('click', function () {
        setActiveTab('states');
        buildSaveSlots();
        buildSramSlots();
        window._jeOpenPopup('je-pop-saves');
    });

    // Local Import / Export

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

    // Wire up State drag/drop and file click
    setupDropzone('je-state-dropzone', 'je-state-file', false);
    // Wire up SRAM drag/drop and file click
    setupDropzone('je-sram-dropzone', 'je-sram-file', true);

    function setupDropzone(dropzoneId, fileInputId, isSram) {
        var dropzone = document.getElementById(dropzoneId);
        var fileInput = document.getElementById(fileInputId);
        if (!dropzone || !fileInput) return;

        dropzone.addEventListener('click', function () { fileInput.click(); });

        ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(function (evt) {
            dropzone.addEventListener(evt, function (e) { e.preventDefault(); e.stopPropagation(); }, false);
        });

        ['dragenter', 'dragover'].forEach(function (evt) {
            dropzone.addEventListener(evt, function () {
                dropzone.style.borderColor = 'rgba(100,200,255,.8)';
                dropzone.style.background  = 'rgba(100,200,255,.1)';
            }, false);
        });

        ['dragleave', 'drop'].forEach(function (evt) {
            dropzone.addEventListener(evt, function () {
                dropzone.style.borderColor = 'rgba(255,255,255,.2)';
                dropzone.style.background  = 'transparent';
            }, false);
        });

        dropzone.addEventListener('drop', function (e) {
            if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
                handleImport(e.dataTransfer.files[0], isSram);
            }
        }, false);

        fileInput.addEventListener('change', function (e) {
            if (e.target.files && e.target.files.length > 0) {
                handleImport(e.target.files[0], isSram);
                e.target.value = '';
            }
        });
    }

    function handleImport(file, isSram) {
        var g = gm(); if (!g) return;

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
                    window._jeClosePopup('je-pop-saves');
                    alert('SRAM imported successfully! Restarting game...');
                    g.restart();
                } else {
                    g.loadState(uint8);
                    window._jeClosePopup('je-pop-saves');
                }
            } catch (err) {
                console.error('[JellyEmu] Import error:', err);
                alert('Failed to import file. The data may be corrupt or incompatible with this emulator core.');
            }
        };
        reader.readAsArrayBuffer(file);
    }

    // --- Multi-Disc / Playlist (J3U/M3U) Swapping & Restoration ---
    if (isM3u) {
        // Subscribe to jellyemu:gamestart to check and restore Slot 99 SRAM
        window.addEventListener('jellyemu:gamestart', function () {
            setTimeout(function () {
                var loadHeaders = {};
                if (token) {
                    loadHeaders['Authorization'] = 'MediaBrowser Token="' + token + '"';
                }
                fetch('/jellyemu/sram/' + itemId + '/' + userId + '?slot=99', { headers: loadHeaders })
                    .then(function (r) {
                        if (r.ok) return r.arrayBuffer();
                        throw new Error('No Slot 99 backup');
                    })
                    .then(function (buf) {
                        var g = gm();
                        if (!g) return;
                        var sramPath = g.getSaveFilePath ? g.getSaveFilePath() : '';
                        if (!sramPath) return;
                        try { g.FS.unlink(sramPath); } catch (_) {}
                        g.FS.writeFile(sramPath, new Uint8Array(buf));
                        g.loadSaveFiles();
                        g.restart();
                        console.log('[JellyEmu] Restored Slot 99 SRAM for next disc.');

                        // Delete the slot 99 save from server
                        fetch('/jellyemu/sram/' + itemId + '/' + userId + '?slot=99', {
                            method: 'DELETE',
                            headers: loadHeaders
                        }).catch(function () {});
                    })
            }, 500);
        });

        // Wire up disc swap UI triggers
        var btnNext = document.getElementById('je-btn-nextdisc');
        var btnSel = document.getElementById('je-btn-selectdisc');
        
        if (btnNext) {
            btnNext.addEventListener('click', function () {
                triggerDiscSwap('next');
            });
        }
        
        if (btnSel) {
            btnSel.addEventListener('click', function () {
                var listEl = document.getElementById('je-disc-list');
                listEl.innerHTML = '<div style="opacity:.4;font-size:13px;text-align:center;padding:12px 0;">Loading discs…</div>';
                
                jeFetch('/jellyemu/playlist/' + itemId + '/discs/' + userId)
                    .then(function (r) { return r.json(); })
                    .then(function (data) {
                        listEl.innerHTML = '';
                        if (!data.discs || data.discs.length === 0) {
                            listEl.innerHTML = '<div style="opacity:.4;font-size:13px;text-align:center;padding:12px 0;">No discs found.</div>';
                            return;
                        }
                        data.discs.forEach(function (disc) {
                            var item = document.createElement('div');
                            item.className = 'je-disc-item' + (disc.index === data.activeDiscIndex ? ' je-active' : '');
                            item.textContent = disc.name + ' (' + disc.filename + ')';
                            
                            item.addEventListener('click', function () {
                                if (disc.index === data.activeDiscIndex) {
                                    window._jeClosePopup('je-pop-selectdisc');
                                    return;
                                }
                                triggerDiscSwap(disc.index);
                            });
                            listEl.appendChild(item);
                        });
                    })
                    .catch(function () {
                        listEl.innerHTML = '<div style="opacity:.4;font-size:13px;text-align:center;padding:12px 0;color:#f44">Failed to load discs.</div>';
                    });
                window._jeOpenPopup('je-pop-selectdisc');
            });
        }
    }

    function triggerDiscSwap(targetDisc) {
        var btnNext = document.getElementById('je-btn-nextdisc');
        var btnSel = document.getElementById('je-btn-selectdisc');
        if (btnNext) btnNext.disabled = true;
        if (btnSel) btnSel.disabled = true;
        
        var statusEl = document.getElementById('je-loader-status');
        if (statusEl) statusEl.textContent = 'Saving progress & swapping disc...';
        
        var loader = document.getElementById('je-loader');
        if (loader) {
            loader.style.display = 'flex';
            loader.classList.remove('je-dismiss');
        }
        
        // 1. Get SRAM
        var g = gm();
        var rawSave = g ? g.getSaveFile() : null;
        var savePromise = Promise.resolve();
        
        if (rawSave) {
            var saveBlob = ensureBinary(rawSave);
            if (saveBlob) {
                var headers = { 'Content-Type': 'application/octet-stream' };
                // 2. Upload to slot 99
                savePromise = jeFetch('/jellyemu/sram/' + itemId + '/' + userId + '?slot=99', {
                    method: 'POST',
                    headers: headers,
                    body: saveBlob
                });
            }
        }
        
        // 3. Swap index and reload
        savePromise.finally(function () {
            jeFetch('/jellyemu/playlist/' + itemId + '/swap/' + userId + '?disc=' + targetDisc, {
                method: 'POST'
            })
            .finally(function () {
                window.location.reload();
            });
        });
    }

})();