(function() {
    window.JellyEmu = window.JellyEmu || {};
    const JE = window.JellyEmu;

    if (window.__jellyEmuLoaded) return;
    window.__jellyEmuLoaded = true;
    console.log('[JellyEmu] UI injection initialized.');

    const config = window.__JELLYEMU_CONFIG__ || {};
    JE.vantageEnabled = typeof config.vantageEnabled === 'boolean' ? config.vantageEnabled : true;

    JE.currentItemId = null;
    JE.currentItemIsGame = false;
    JE.lastGameCardId = null;
    JE.cachedTags = [];
    JE.cachedProviderIds = {};

    JE.romExtensions = new Set([
        "nes","fds","unf","unif",
        "smc","sfc","swc","fig",
        "z64","n64","v64",
        "gb","gbc","gba","nds","vb",
        "sms","gg",
        "md","smd","gen","68k","32x",
        "pbp","cue","iso","chd","gdi","cdi","mdf",
        "cso",
        "a26","a78","lnx","jag","j64",
        "ws","wsc","pce",
        "col","cv","ngp","ngc",
        "zip",
        "d64","t64","crt","tap","prg",
        "adf","dms","ipf","adz",
        "dsk",
        "bin",
        "3ds","cci","cia"
    ]);

    JE.knownRegions = new Set([
        "USA","Europe","Japan","World","Australia","Brazil","Canada","China",
        "France","Germany","Italy","Korea","Netherlands","Russia","Spain","Sweden",
        "Asia","Scandinavia","Unlicensed","Prototype","Demo","Sample"
    ]);

    // Platforms recognised for library management but not supported by EmulatorJS.
    JE.ejsUnsupportedPlatforms = new Set([
        "Dreamcast","PlayStation 2","PlayStation 3",
        "Xbox","Xbox 360",
        "GameCube","Wii","Wii U","Nintendo Switch",
        "PlayStation Vita","Windows","Unsupported"
    ]);

    JE.perf = {
        mark: (n) => performance.mark('jellyemu:' + n),
        measure: (n, a, b) => { try { performance.measure('jellyemu:' + n, 'jellyemu:' + a, 'jellyemu:' + b); } catch(_) {} },
        time: (n, fn) => {
            const s = 'jellyemu:' + n + ':start';
            const e = 'jellyemu:' + n + ':end';
            performance.mark(s);
            return Promise.resolve(fn()).finally(() => {
                performance.mark(e);
                try { performance.measure('jellyemu:' + n, s, e); } catch(_) {}
            });
        },
    };

    JE.delay = ms => new Promise(resolve => setTimeout(resolve, ms));

    JE.isPlayable = function(tags) {
        if (!tags || !tags.length) return false;
        if (!tags.includes('JellyEmu')) return false;
        for (const tag of tags) {
            if (tag === 'Unknown') return false;
            if (JE.ejsUnsupportedPlatforms.has(tag)) return false;
        }
        return true;
    };

    JE.isDiscTag = function(tag) {
        return /^Dis[ck]\s+[1-9IVX]/i.test(tag);
    };

    JE.jeToast = function(msg, durationMs) {
        durationMs = durationMs || 3500;
        var t = document.createElement('div');
        t.textContent = msg;
        t.style.cssText = 'position:fixed;bottom:72px;left:50%;transform:translateX(-50%);' +
            'background:rgba(0,0,0,0.82);color:#fff;padding:9px 18px;border-radius:6px;' +
            'font-size:0.88em;z-index:200000;pointer-events:none;transition:opacity 0.4s;';
        document.body.appendChild(t);
        setTimeout(function() { t.style.opacity = '0'; setTimeout(function() { if (t.parentNode) t.parentNode.removeChild(t); }, 420); }, durationMs);
    };

    JE.launchEmulator = function(itemId, slot) {
        console.log('[JellyEmu] Launching emulator for item:', itemId);
        var userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : '';
        var playUrl = '/jellyemu/play/' + itemId + (userId ? '?userId=' + userId : '');
        if (slot) {
            playUrl += (playUrl.indexOf('?') !== -1 ? '&' : '?') + 'slot=' + slot;
        }

        // Romm sync-on-launch: pull if Romm has a newer save
        if (userId) {
            fetch('/jellyemu/romm/sync-on-launch/' + itemId + '/' + userId, { method: 'POST' })
                .then(function(r) { return r.ok ? r.json() : null; })
                .then(function(d) { if (d && d.pulled) JE.jeToast('\u2601 Loaded save from Romm (newer than local)'); })
                .catch(function() {});
        }

        fetch('/jellyemu/core/' + itemId + (userId ? '?userId=' + userId : ''))
            .then(function(r) { return r.ok ? r.json() : { needsThreads: false }; })
            .catch(function() { return { needsThreads: false }; })
            .then(function(info) {
                if (info.needsThreads) {
                    var gameTab = window.open(playUrl, '_blank');
                    var jellyEmuChannel = new BroadcastChannel('jellyemu-exit');
                    jellyEmuChannel.addEventListener('message', function(msg) {
                        if (msg.data === 'close-jellyemu') {
                            jellyEmuChannel.close();
                            if (gameTab && !gameTab.closed) gameTab.close();
                        }
                    });
                } else {
                    var iframe = document.createElement('iframe');
                    iframe.id = 'jellyemu-iframe';
                    iframe.allow = 'autoplay; fullscreen; gamepad *; xr-spatial-tracking';
                    iframe.tabIndex = 0;
                    iframe.style = 'width:100vw; height:100vh; border:none; position:fixed; top:0; left:0; z-index:99999; background:#000;';
                    iframe.src = playUrl;
                    document.body.appendChild(iframe);
                    document.body.style.overflow = 'hidden';
                    setTimeout(function() {
                        try { iframe.focus(); } catch (e) {}
                    }, 100);
                }
            });
    };

    JE.deleteSave = async function(itemId, slot) {
        try {
            const userId = ApiClient.getCurrentUserId();
            const token = ApiClient.accessToken();
            const url = `/jellyemu/save/${itemId}/${userId}?slot=${slot}`;

            const response = await fetch(url, {
                method: 'DELETE',
                headers: {
                    'Authorization': `MediaBrowser Token="${token}"`, 
                    'Accept': 'application/json'
                }
            });

            if (response.status === 204) {
                console.log(`[JellyEmu] Successfully deleted save slot ${slot} for item ${itemId}.`);
                return true;
            } else if (response.status === 404) {
                console.warn(`[JellyEmu] Save slot ${slot} not found.`);
                return false;
            } else if (response.status === 403) {
                console.error(`[JellyEmu] Forbidden: You do not have permission to delete this save.`);
                return false;
            } else {
                console.error(`[JellyEmu] Unexpected error. Server returned status: ${response.status}`);
                return false;
            }
        } catch (error) {
            console.error(`[JellyEmu] Network error while trying to delete save:`, error);
            return false;
        }
    };

    JE.dismissActionSheet = function(sheetRoot) {
        const container = document.querySelector('.dialogContainer');
        const backdrop = document.querySelector('.dialogBackdrop');
        if (container) container.remove();
        if (backdrop) backdrop.remove();

        var dialog = sheetRoot.closest('.dialog') || sheetRoot.closest('[data-history]') || sheetRoot.parentElement;
        if (dialog && dialog.parentNode) dialog.remove();
    };

    window.addEventListener('message', function(e) {
        if (e.data === 'close-jellyemu') {
            var iframe = document.getElementById('jellyemu-iframe');
            if (iframe) {
                document.body.removeChild(iframe);
                document.body.style.overflow = '';
            }
        }
        var userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : '';
        if (e.data && e.data.type === 'jellyemu-save-written') {
            var itemId2 = e.data.itemId;
            if (userId && itemId2) {
                fetch('/jellyemu/romm/sync-after-save/' + itemId2 + '/' + userId, { method: 'POST' })
                    .then(function(r) { return r.ok ? r.json() : null; })
                    .then(function(d) { if (d && d.pushed) JE.jeToast('\u2601 Save synced to Romm'); })
                    .catch(function() {});
            }
        }
        if (e.data && e.data.type === 'jellyemu-session-end') {
            var itemId3 = e.data.itemId;
            var seconds3 = e.data.seconds || 0;
            if (userId && itemId3 && seconds3 > 0) {
                fetch('/jellyemu/romm/report-playtime/' + itemId3 + '/' + userId, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ seconds: seconds3 })
                }).catch(function() {});
            }
        }
        if (e.data && e.data.type === 'jellyemu-screenshot') {
            var itemId4 = e.data.itemId;
            var dataUrl = e.data.dataUrl;
            if (userId && itemId4 && dataUrl) {
                fetch('/jellyemu/romm/screenshot/' + itemId4 + '/' + userId, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ dataUrl: dataUrl })
                }).then(function(r) { return r.ok ? r.json() : null; })
                  .then(function(d) { if (d && d.pushed) JE.jeToast('\U0001f4f8 Screenshot saved to Romm'); })
                  .catch(function() {});
            }
        }
    });

    document.body.addEventListener('click', function(e) {
        const menuBtn = e.target.closest('button[data-action="menu"]');
        if (!menuBtn) return;
        const card = menuBtn.closest('.card[data-jellyemu-game="1"]');
        if (card) JE.lastGameCardId = card.getAttribute('data-id');
    }, true);

    JE.patchActionSheet = function(sheetRoot) {
        if (!JE.lastGameCardId) return;
        const itemId = JE.lastGameCardId;

        const playBtn = sheetRoot.querySelector('button[data-id="resume"]');
        if (playBtn && !playBtn.getAttribute('data-jellyemu-patched')) {
            playBtn.setAttribute('data-jellyemu-patched', '1');

            const sourceCard = document.querySelector('.card[data-id="' + itemId + '"]');
            const tags = sourceCard && sourceCard.getAttribute('data-jellyemu-tags')
                ? sourceCard.getAttribute('data-jellyemu-tags').split(',')
                : null;

            if (tags && !JE.isPlayable(tags)) {
                playBtn.style.display = 'none';
            } else {
                const label = playBtn.querySelector('.actionSheetItemText');
                if (label) label.textContent = 'Play Game';
                playBtn.addEventListener('click', function(e) {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    JE.dismissActionSheet(sheetRoot);
                    JE.launchEmulator(itemId);
                }, true);
            }
        }

        const playFromHereBtn = sheetRoot.querySelector('button[data-id="playallfromhere"]');
        if (playFromHereBtn) {
            playFromHereBtn.style.display = 'none';
        }

        if (JE.vantageEnabled && !sheetRoot.querySelector('button[data-jellyemu-vantage]')) {
            const sourceCard = document.querySelector('.card[data-id="' + itemId + '"]');
            const tags = sourceCard && sourceCard.getAttribute('data-jellyemu-tags')
                ? sourceCard.getAttribute('data-jellyemu-tags').split(',')
                : null;

            if (tags && tags.includes('JellyEmu')) {
                const vantageBtn = document.createElement('button');
                vantageBtn.type = 'button';
                vantageBtn.setAttribute('data-jellyemu-vantage', '1');
                
                const playBtn = sheetRoot.querySelector('button[data-id="resume"]');
                vantageBtn.className = playBtn ? playBtn.className : 'actionSheetMenuItem emby-button';
                vantageBtn.innerHTML = playBtn ? playBtn.innerHTML : '<i class="md-icon actionSheetItemIcon">open_in_new</i><div class="actionSheetItemText">Open in Vantage</div>';
                
                const icon = vantageBtn.querySelector('.actionSheetItemIcon');
                if (icon) {
                    icon.textContent = 'open_in_new';
                    icon.style.color = '';
                }
                
                const text = vantageBtn.querySelector('.actionSheetItemText');
                if (text) {
                    text.textContent = 'Open in Vantage';
                }
                vantageBtn.addEventListener('click', function(e) {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    JE.dismissActionSheet(sheetRoot);
                    window.location.href = 'vantage://launch?itemId=' + itemId;
                }, true);
                
                const scroller = sheetRoot.querySelector('.actionSheetScroller');
                if (playBtn && playBtn.nextSibling) {
                    playBtn.parentNode.insertBefore(vantageBtn, playBtn.nextSibling);
                } else if (scroller) {
                    scroller.insertBefore(vantageBtn, scroller.firstChild);
                } else {
                    sheetRoot.appendChild(vantageBtn);
                }
            }
        }
    };

    JE.JELLYEMU_PREFS_HASH    = '#/jellyemu-userprefs';
    JE.JELLYEMU_SETTINGS_HASH = '#/jellyemu-settings';
    JE.JELLYEMU_SAVES_HASH    = '#/jellyemu-saves';

    JE.injectPrefsMenuEntry = function(page) {
        if (page.querySelector('.jellyemu-prefs-entry')) return true;

        const userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : '';

        const savesAnchor = document.createElement('a');
        savesAnchor.className = 'emby-button jellyemu-prefs-entry listItem-border';
        savesAnchor.href = JE.JELLYEMU_SAVES_HASH + (userId ? '?userId=' + userId : '');
        savesAnchor.style.cssText = 'display:block; margin:0; padding:0;';
        savesAnchor.innerHTML = `
            <div class="listItem">
                <span class="material-icons listItemIcon listItemIcon-transparent save" aria-hidden="true"></span>
                <div class="listItemBody">
                    <div class="listItemBodyText">Save State Browser</div>
                </div>
            </div>`;
        savesAnchor.addEventListener('click', function(e) {
            e.preventDefault();
            window.location.hash = JE.JELLYEMU_SAVES_HASH + (userId ? '?userId=' + userId : '');
        });

        const settingsAnchor = document.createElement('a');
        settingsAnchor.className = 'emby-button jellyemu-prefs-entry listItem-border';
        settingsAnchor.href = JE.JELLYEMU_SETTINGS_HASH;
        settingsAnchor.style.cssText = 'display:block; margin:0; padding:0;';
        settingsAnchor.innerHTML = `
            <div class="listItem">
                <span class="listItemIcon listItemIcon-transparent" style="display:inline-flex;align-items:center;justify-content:center;color:inherit;vertical-align:middle;">
                    <svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" style="width:20px;height:20px;">
                        <path d="M12 2L3 18a2 2 0 0 0 1.7 3h14.6a2 2 0 0 0 1.7-3L12 2z" />
                        <path d="M7.5 15.5h3M9 14v3" stroke-width="1.2" />
                        <circle cx="15.5" cy="14.5" r="0.8" fill="currentColor" stroke="none" />
                        <circle cx="17" cy="16.5" r="0.8" fill="currentColor" stroke="none" />
                    </svg>
                </span>
                <div class="listItemBody">
                    <div class="listItemBodyText">JellyEmu Settings</div>
                </div>
            </div>`;
        settingsAnchor.addEventListener('click', function(e) {
            e.preventDefault();
            window.location.hash = JE.JELLYEMU_SETTINGS_HASH;
        });

        const targetSection =
            page.querySelector('.verticalSection:not(.adminSection):not(.userSection)')
            || page.querySelector('.verticalSection')
            || page.querySelector('.readOnlyContent')
            || page;

        if (!targetSection.children.length) return false;
        targetSection.appendChild(settingsAnchor);
        targetSection.appendChild(savesAnchor);
        return true;
    };

    JE._onJellyfinPageShow = function(e) {
        const hash = window.location.hash;
        const page = e.target;
        if (!page || !page.classList) return;

        if (hash.startsWith(JE.JELLYEMU_PREFS_HASH)) {
            if (JE._detailObserverDisconnect) JE._detailObserverDisconnect();
            return;
        }
        if (hash.startsWith(JE.JELLYEMU_SAVES_HASH)) {
            if (JE._detailObserverDisconnect) JE._detailObserverDisconnect();
            if (JE.hijackJellyEmuSavesBrowser) JE.hijackJellyEmuSavesBrowser();
            return;
        }
        if (hash.startsWith(JE.JELLYEMU_SETTINGS_HASH)) {
            if (JE._detailObserverDisconnect) JE._detailObserverDisconnect();
            if (JE.hijackJellyEmuSettings) JE.hijackJellyEmuSettings();
            return;
        }

        if (page.id === 'myPreferencesMenuPage') {
            if (JE._detailObserverDisconnect) JE._detailObserverDisconnect();
            JE.injectPrefsMenuEntry(page);
            return;
        }

        if (page.classList.contains('itemDetailPage')) {
            if (JE._detailObserverConnect) JE._detailObserverConnect(page);
            if (JE.scheduleCardProcess) page.querySelectorAll('.card').forEach(JE.scheduleCardProcess);
            const match = hash.match(/id=([a-zA-Z0-9]+)/);
            if (!match) return;
            const id = match[1];
            if (JE.currentItemId === id) return;
            JE.currentItemId     = id;
            JE.currentItemIsGame = false;
            JE.cachedProviderIds = {};
            if (JE.processItemDetails) JE.processItemDetails(id, page);
            return;
        }

        if (JE._detailObserverDisconnect) JE._detailObserverDisconnect();
        JE.currentItemId     = null;
        JE.currentItemIsGame = false;
        JE.cachedProviderIds = {};
        if (JE.scheduleCardProcess) page.querySelectorAll('.card').forEach(JE.scheduleCardProcess);
    };

    document.addEventListener('viewshow', JE._onJellyfinPageShow);
    document.addEventListener('pageshow',  JE._onJellyfinPageShow);

    // Direct URL entry handling
    setTimeout(function() {
        const hash = window.location.hash;
        if (hash.startsWith(JE.JELLYEMU_PREFS_HASH))  { return; }
        if (hash.startsWith(JE.JELLYEMU_SAVES_HASH))  { if (JE.hijackJellyEmuSavesBrowser) JE.hijackJellyEmuSavesBrowser(); return; }
        if (hash.startsWith(JE.JELLYEMU_SETTINGS_HASH)) { if (JE.hijackJellyEmuSettings) JE.hijackJellyEmuSettings(); return; }
        const prefsMenu = document.getElementById('myPreferencesMenuPage');
        if (prefsMenu && !prefsMenu.classList.contains('hide')) JE.injectPrefsMenuEntry(prefsMenu);
        const detailPage = JE.getVisibleDetailPage ? JE.getVisibleDetailPage() : null;
        if (detailPage) {
            if (JE._detailObserverConnect) JE._detailObserverConnect(detailPage);
            const match = hash.match(/id=([a-zA-Z0-9]+)/);
            if (match) { 
                JE.currentItemId = match[1]; 
                if (JE.processItemDetails) JE.processItemDetails(match[1], detailPage); 
            }
        }
    }, 0);
})();
