using System.Text.RegularExpressions;

namespace JellyEmu.Services
{
    public class PatchRequestPayload
    {
        public string? Path { get; set; }
        public string? Contents { get; set; }
    }

    public static class JellyEmuUIInjector
    {
        private const string StartMarker = "<!-- JellyEmu-Mods-Start -->";
        private const string EndMarker = "<!-- JellyEmu-Mods-End -->";

        public static string InjectMods(PatchRequestPayload payload)
        {
            try
            {
                string htmlContent = payload.Contents ?? string.Empty;

                if (string.IsNullOrEmpty(htmlContent) || !htmlContent.Contains("</body>"))
                {
                    return htmlContent;
                }

                htmlContent = Regex.Replace(htmlContent, Regex.Escape(StartMarker) + @"[\s\S]*?" + Regex.Escape(EndMarker) + @"\n?", string.Empty);

                var injection = """
                <style data-jellyemu-mods="1">

                  #jellyemu-play-btn {
                      display: flex !important;
                      align-items: center !important;
                      justify-content: center !important;
                      width: 42px !important;
                      height: 42px !important;
                      border-radius: 50% !important;
                      background: rgba(255,255,255,0.15) !important;
                      color: #fff !important;
                      border: none !important;
                      box-shadow: none !important;
                      padding: 0 !important;
                      margin-right: .5em !important;
                      transition: transform 0.15s ease, background 0.15s ease, color 0.15s ease !important;
                      transform: scale(1);
                  }
                  #jellyemu-play-btn:hover {
                      transform: scale(1.18) !important;
                      background: rgba(255,255,255,0.25) !important;
                      color: #00a4dc !important;
                  }
                  #jellyemu-play-btn .detailButton-content {
                      display: flex !important;
                      align-items: center !important;
                      justify-content: center !important;
                  }
                  .jellyemu-card-play {
                      position: absolute !important;
                      top: 50% !important;
                      left: 50% !important;
                      display: flex !important;
                      align-items: center !important;
                      justify-content: center !important;
                      width: 52px !important;
                      height: 52px !important;
                      border-radius: 50% !important;
                      background: rgba(0,0,0,0.55) !important;
                      color: #fff !important;
                      border: none !important;
                      box-shadow: none !important;
                      padding: 0 !important;
                      background-image: none !important;
                      opacity: 0;
                      transform: translate(-50%, -50%) scale(0.85);
                      transition: transform 0.15s ease, opacity 0.15s ease, color 0.15s ease !important;
                  }
                  .jellyemu-card-play .material-icons {
                      font-size: 28px !important;
                  }
                  .card:hover .jellyemu-card-play,
                  .card:focus-within .jellyemu-card-play {
                      opacity: 1;
                      transform: translate(-50%, -50%) scale(1);
                  }
                  .card:hover .jellyemu-card-play:hover {
                      transform: translate(-50%, -50%) scale(1.15);
                      color: #00a4dc !important;
                  }
                  .jellyemu-game-page button[data-action="resume"],
                  .jellyemu-game-page button[data-action="play"],
                  .jellyemu-game-page .btnPlay {
                      display: none !important;
                  }
                </style>
                <script data-jellyemu-mods="1">
                (function() {
                    if (window.__jellyEmuLoaded) return;
                    window.__jellyEmuLoaded = true;
                    console.log('[JellyEmu] UI injection successful.');

                    let currentItemId = null;
                    let currentItemIsGame = false;
                    let lastGameCardId = null;

                    const romExtensions = new Set([
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
                        "bin"
                    ]);

                    const knownRegions = new Set([
                        "USA","Europe","Japan","World","Australia","Brazil","Canada","China",
                        "France","Germany","Italy","Korea","Netherlands","Russia","Spain","Sweden",
                        "Asia","Scandinavia","Unlicensed","Prototype","Demo","Sample"
                    ]);

                    // Platforms recognised for library management but not supported by EmulatorJS.
                    // Must stay in sync with PlatformResolver.LibraryOnlyAliases canonical values.
                    const ejsUnsupportedPlatforms = new Set([
                        "Dreamcast","PlayStation 2","PlayStation 3",
                        "Xbox","Xbox 360",
                        "GameCube","Wii","Wii U","Nintendo Switch","Nintendo 3DS",
                        "PlayStation Vita"
                    ]);

                    function isPlayable(tags) {
                        if (!tags || !tags.length) return false;
                        for (const tag of tags) {
                            if (tag === 'Unknown') return false;
                            if (ejsUnsupportedPlatforms.has(tag)) return false;
                        }
                        return true;
                    }

                    function isDiscTag(tag) {
                        return /^Dis[ck]\s+[1-9IVX]/i.test(tag);
                    }

                    function jeToast(msg, durationMs) {
                        durationMs = durationMs || 3500;
                        var t = document.createElement('div');
                        t.textContent = msg;
                        t.style.cssText = 'position:fixed;bottom:72px;left:50%;transform:translateX(-50%);' +
                            'background:rgba(0,0,0,0.82);color:#fff;padding:9px 18px;border-radius:6px;' +
                            'font-size:0.88em;z-index:200000;pointer-events:none;transition:opacity 0.4s;';
                        document.body.appendChild(t);
                        setTimeout(function() { t.style.opacity = '0'; setTimeout(function() { if (t.parentNode) t.parentNode.removeChild(t); }, 420); }, durationMs);
                    }

                    function launchEmulator(itemId) {
                        console.log('[JellyEmu] Launching emulator for item:', itemId);
                        var userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : '';
                        var playUrl = '/jellyemu/play/' + itemId + (userId ? '?userId=' + userId : '');

                        // Romm sync-on-launch: pull if Romm has a newer save
                        if (userId) {
                            fetch('/jellyemu/romm/sync-on-launch/' + itemId + '/' + userId, { method: 'POST' })
                                .then(function(r) { return r.ok ? r.json() : null; })
                                .then(function(d) { if (d && d.pulled) jeToast('\u2601 Loaded save from Romm (newer than local)'); })
                                .catch(function() {});
                        }

                        fetch('/jellyemu/core/' + itemId)
                            .then(function(r) { return r.ok ? r.json() : { needsThreads: false }; })
                            .catch(function() { return { needsThreads: false }; })
                            .then(function(info) {
                                if (info.needsThreads) {
                                    // Threaded cores (DOS, PSP) require SharedArrayBuffer
                                    // which needs cross-origin isolation — open in a new tab
                                    var gameTab = window.open(playUrl, '_blank');
                                    var jellyEmuChannel = new BroadcastChannel('jellyemu-exit');
                                    jellyEmuChannel.addEventListener('message', function(msg) {
                                        if (msg.data === 'close-jellyemu') {
                                            jellyEmuChannel.close();
                                            if (gameTab && !gameTab.closed) gameTab.close();
                                        }
                                    });
                                } else {
                                    // Non-threaded cores work fine in an iframe
                                    var iframe = document.createElement('iframe');
                                    iframe.id = 'jellyemu-iframe';
                                    iframe.style = 'width:100vw; height:100vh; border:none; position:fixed; top:0; left:0; z-index:99999; background:#000;';
                                    iframe.src = playUrl;
                                    document.body.appendChild(iframe);
                                    document.body.style.overflow = 'hidden';
                                }
                            });
                    }

                    function dismissActionSheet(sheetRoot) {
                        var dialog = sheetRoot.closest('.dialog') || sheetRoot.closest('[data-history]') || sheetRoot.parentElement;
                        if (dialog) dialog.remove();
                    }

                    window.addEventListener('message', function(e) {
                        if (e.data === 'close-jellyemu') {
                            var iframe = document.getElementById('jellyemu-iframe');
                            if (iframe) {
                                document.body.removeChild(iframe);
                                document.body.style.overflow = '';
                            }
                        }
                        var userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : '';
                        // Romm: push save to Romm after a save event from the emulator iframe
                        if (e.data && e.data.type === 'jellyemu-save-written') {
                            var itemId2 = e.data.itemId;
                            if (userId && itemId2) {
                                fetch('/jellyemu/romm/sync-after-save/' + itemId2 + '/' + userId, { method: 'POST' })
                                    .then(function(r) { return r.ok ? r.json() : null; })
                                    .then(function(d) { if (d && d.pushed) jeToast('\u2601 Save synced to Romm'); })
                                    .catch(function() {});
                            }
                        }
                        // Romm: report playtime when session ends
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
                        // Romm: push screenshot
                        if (e.data && e.data.type === 'jellyemu-screenshot') {
                            var itemId4 = e.data.itemId;
                            var dataUrl = e.data.dataUrl;
                            if (userId && itemId4 && dataUrl) {
                                fetch('/jellyemu/romm/screenshot/' + itemId4 + '/' + userId, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ dataUrl: dataUrl })
                                }).then(function(r) { return r.ok ? r.json() : null; })
                                  .then(function(d) { if (d && d.pushed) jeToast('\U0001f4f8 Screenshot saved to Romm'); })
                                  .catch(function() {});
                            }
                        }

                    });

                    document.body.addEventListener('click', function(e) {
                        const menuBtn = e.target.closest('button[data-action="menu"]');
                        if (!menuBtn) return;
                        const card = menuBtn.closest('.card[data-collectiontype="games"]') ||
                                     menuBtn.closest('.card[data-jellyemu-game="1"]');
                        if (card) lastGameCardId = card.getAttribute('data-id');
                    }, true);

                    function patchActionSheet(sheetRoot) {
                        if (!lastGameCardId) return;

                        const itemId = lastGameCardId;

                        const playBtn = sheetRoot.querySelector('button[data-id="resume"]');
                        if (playBtn && !playBtn.getAttribute('data-jellyemu-patched')) {
                            playBtn.setAttribute('data-jellyemu-patched', '1');

                            // Look up the card's stamped tags to decide if play is available
                            const sourceCard = document.querySelector('.card[data-id="' + itemId + '"]');
                            const tags = sourceCard && sourceCard.getAttribute('data-jellyemu-tags')
                                ? sourceCard.getAttribute('data-jellyemu-tags').split(',')
                                : null;

                            if (tags && !isPlayable(tags)) {
                                // Hide the play option entirely for unsupported/unknown platforms
                                playBtn.style.display = 'none';
                            } else {
                                const label = playBtn.querySelector('.actionSheetItemText');
                                if (label) label.textContent = 'Play Game';
                                playBtn.addEventListener('click', function(e) {
                                    e.preventDefault();
                                    e.stopImmediatePropagation();
                                    dismissActionSheet(sheetRoot);
                                    launchEmulator(itemId);
                                }, true);
                            }
                        }

                        const playFromHereBtn = sheetRoot.querySelector('button[data-id="playallfromhere"]');
                        if (playFromHereBtn) {
                            playFromHereBtn.style.display = 'none';
                        }
                    }

                    let cachedTags = [];

                    function injectMiscInfo(page) {
                        page = page || getVisibleDetailPage();
                        if (!page) return;
                        if (!cachedTags.length) return;
                        const miscBar = page.querySelector('.itemMiscInfo-primary');
                        if (!miscBar) return;
                        // Use a wrapper div as the single idempotency check.
                        // If it already exists we injected — bail immediately.
                        if (miscBar.querySelector('.jellyemu-info-wrap')) return;
                        const wrap = document.createElement('div');
                        wrap.className = 'jellyemu-info-wrap';
                        wrap.style.cssText = 'display:contents';
                        miscBar.appendChild(wrap);

                        perf.mark('inject-misc-start');

                        const systemTags = cachedTags.filter(t => t !== 'Game' && !knownRegions.has(t) && !isDiscTag(t));
                        const regionTags = cachedTags.filter(t => knownRegions.has(t));
                        const discTags   = cachedTags.filter(t => isDiscTag(t));
                        const allTags    = [...systemTags, ...regionTags, ...discTags];

                        allTags.forEach(tag => {
                            const div = document.createElement('div');
                            div.className = 'mediaInfoItem jellyemu-misc-item';
                            div.textContent = tag;
                            wrap.appendChild(div);
                        });

                        const userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : null;
                        const itemId = currentItemId;

                        // Fetch slot once — shared by the slot pill and the Romm sync badge
                        const slotPromise = (userId && itemId)
                            ? fetch('/jellyemu/slot/' + userId).then(r => r.ok ? r.json() : null).catch(() => null)
                            : Promise.resolve(null);

                        if (userId && itemId && !wrap.querySelector('.jellyemu-slot-pill')) {
                            slotPromise.then(data => {
                                if (!data) return;
                                const slot = data.slot || 1;
                                fetch('/jellyemu/save/' + itemId + '/' + userId, { method: 'HEAD' })
                                    .then(r => {
                                        const hasSave = r.ok;
                                        const pill = document.createElement('div');
                                        pill.className = 'mediaInfoItem jellyemu-slot-pill';
                                        pill.title = hasSave ? 'Save exists in slot ' + slot : 'No save in slot ' + slot;
                                        pill.style.cssText = 'display:inline-flex;align-items:center;gap:4px;cursor:default;';
                                        pill.innerHTML = '<span class="material-icons" style="font-size:13px;vertical-align:middle;">' +
                                            (hasSave ? 'save' : 'save_alt') + '</span>' +
                                            'Slot ' + slot +
                                            (hasSave ? ' <span class="material-icons" style="font-size:13px;vertical-align:middle;color:#00a4dc;">check_circle</span>' : '');
                                        wrap.appendChild(pill);
                                    })
                                    .catch(() => {});
                            });
                        }

                        if (userId && itemId && !wrap.querySelector('.jellyemu-playtime-pill')) {
                            fetch('/jellyemu/playtime/' + itemId + '/' + userId)
                                .then(r => r.ok ? r.json() : null)
                                .then(data => {
                                    if (!data || !data.seconds) return;
                                    const pill = document.createElement('div');
                                    pill.className = 'mediaInfoItem jellyemu-playtime-pill';
                                    pill.style.cssText = 'display:inline-flex;align-items:center;gap:4px;cursor:default;';
                                    pill.title = data.seconds + ' seconds played';
                                    const h = Math.floor(data.seconds / 3600);
                                    const min = Math.floor((data.seconds % 3600) / 60);
                                    const label = h > 0 ? h + 'h ' + min + 'm' : min > 0 ? min + 'm' : '<1m';
                                    pill.innerHTML = '<span class="material-icons" style="font-size:13px;vertical-align:middle;">schedule</span>' + label + ' played';
                                    wrap.appendChild(pill);
                                })
                                .catch(() => {});
                        }

                        // Romm sync-status badge — reuses the already-in-flight slotPromise
                        if (userId && itemId && !wrap.querySelector('.jellyemu-romm-sync-pill')) {
                            slotPromise
                                .then(slotData => {
                                    const activeSlot = slotData ? slotData.slot : 1;
                                    return fetch('/jellyemu/romm/sync-status/' + itemId + '/' + userId + '/' + activeSlot);
                                })
                                .then(r => r.ok ? r.json() : null)
                                .then(data => {
                                    if (!data || data.status === 'Disabled') return;
                                    const statusMap = {
                                        Pushed:     { icon: 'cloud_done',     color: '#52B54B', title: 'Saved to Romm' },
                                        InSync:     { icon: 'cloud_done',     color: '#52B54B', title: 'In sync with Romm' },
                                        RemoteWins: { icon: 'cloud_download', color: '#f0c040', title: 'Romm has a newer save' },
                                        LocalOnly:  { icon: 'cloud_upload',   color: '#aaa',    title: 'Not yet pushed to Romm' },
                                        RemoteOnly: { icon: 'cloud_download', color: '#aaa',    title: 'Remote save only' },
                                        Error:      { icon: 'cloud_off',      color: '#FF4444', title: 'Romm sync error' },
                                    };
                                    const s = statusMap[data.status] || statusMap['Error'];
                                    const pill = document.createElement('div');
                                    pill.className = 'mediaInfoItem jellyemu-romm-sync-pill';
                                    pill.style.cssText = 'display:inline-flex;align-items:center;gap:4px;cursor:default;';
                                    pill.title = s.title;
                                    pill.innerHTML = '<span class="material-icons" style="font-size:13px;vertical-align:middle;color:' + s.color + ';">' + s.icon + '</span>Romm';
                                    wrap.appendChild(pill);
                                })
                                .catch(() => {});
                        }

                        // Unsupported / Unknown platform pill
                        if (!wrap.querySelector('.jellyemu-platform-status-pill')) {
                            const unknownTag  = cachedTags.includes('Unknown');
                            const unsupported = !unknownTag && cachedTags.some(t => ejsUnsupportedPlatforms.has(t));
                            if (unknownTag || unsupported) {
                                const pill = document.createElement('div');
                                pill.className = 'mediaInfoItem jellyemu-platform-status-pill';
                                pill.style.cssText = 'display:inline-flex;align-items:center;gap:4px;cursor:default;' +
                                    'color:rgba(255,255,255,0.55);';
                                pill.title = unknownTag
                                    ? 'Platform could not be detected — add a folder name or [Console] token to the filename'
                                    : 'This platform is not supported by EmulatorJS — use an external emulator';
                                pill.innerHTML = '<span class="material-icons" style="font-size:13px;vertical-align:middle;">' +
                                    (unknownTag ? 'help' : 'info') + '</span>' +
                                    (unknownTag ? 'Unknown Platform' : 'External Only');
                                wrap.appendChild(pill);
                            }
                        }
                        perf.mark('inject-misc-end');
                        perf.measure('inject-misc', 'inject-misc-start', 'inject-misc-end');
                    }

                    function injectPlayButton(page) {
                        page = page || getVisibleDetailPage();
                        if (!page) return;
                        const detailButtonsContainer = page.querySelector('.mainDetailButtons');
                        if (!detailButtonsContainer) return;
                        if (detailButtonsContainer.querySelector('#jellyemu-play-btn')) return;

                        perf.mark('inject-play-start');

                        page.classList.add('jellyemu-game-page');

                        const btn = document.createElement('button');
                        btn.type      = 'button';
                        btn.id        = 'jellyemu-play-btn';
                        btn.className = 'jellyemu-play-btn-detail';
                        btn.title     = 'Play Game';
                        btn.innerHTML = '<div class="detailButton-content"><span class="material-icons detailButton-icon" aria-hidden="true">sports_esports</span></div>';
                        btn.addEventListener('click', function(e) {
                            e.preventDefault();
                            e.stopPropagation();
                            e.stopImmediatePropagation();
                            if (currentItemId) launchEmulator(currentItemId);
                        });

                        detailButtonsContainer.insertBefore(btn, detailButtonsContainer.firstChild);
                        perf.mark('inject-play-end');
                        perf.measure('inject-play', 'inject-play-start', 'inject-play-end');
                    }

                    function injectAll(page) {
                        if (!currentItemIsGame) return;
                        page = page || getVisibleDetailPage();
                        if (page) page.classList.add('jellyemu-game-page');
                        if (isPlayable(cachedTags)) injectPlayButton(page);
                        injectMiscInfo(page);
                    }

                    function processItemDetails(id, page) {
                        if (!window.ApiClient) return;
                        perf.mark('details-start:' + id);
                        currentItemIsGame = false;
                        cachedTags        = [];

                        // page is passed from viewshow — use it directly rather than
                        // querying getVisibleDetailPage() which may not find it yet
                        const visiblePage = page || getVisibleDetailPage();
                        const allDetailPages = document.querySelectorAll('.itemDetailPage');
                        allDetailPages.forEach(p => p.classList.remove('jellyemu-game-page'));
                        if (visiblePage) visiblePage.classList.add('jellyemu-game-page');

                        const cachedCard = document.querySelector('.card[data-id="' + id + '"][data-jellyemu-tags]');
                        if (cachedCard) {
                            const tags = cachedCard.getAttribute('data-jellyemu-tags').split(',');
                            if (tags.includes('Game')) {
                                perf.mark('details-fast-path:' + id);
                                currentItemIsGame = true;
                                cachedTags        = tags;
                                injectAll(visiblePage);
                            }
                        }

                        perf.mark('details-getItem-start:' + id);
                        window.ApiClient.getItem(window.ApiClient.getCurrentUserId(), id).then(item => {
                            perf.mark('details-getItem-end:' + id);
                            perf.measure('details-getItem:' + id, 'details-getItem-start:' + id, 'details-getItem-end:' + id);
                            if (item && item.Tags && item.Tags.includes('Game')) {
                                currentItemIsGame = true;
                                cachedTags        = item.Tags;
                                // Poll until .itemMiscInfo-primary exists, then inject once.
                                // This resolves all timing races between getItem and DOM render.
                                var _pollAttempts = 0;
                                var _pollId = setInterval(function() {
                                    // Stop if we've navigated to a different item
                                    if (currentItemId !== id) { clearInterval(_pollId); return; }
                                    var p = visiblePage || getVisibleDetailPage();
                                    if (!p) { if (++_pollAttempts > 20) clearInterval(_pollId); return; }
                                    if (isPlayable(cachedTags)) injectPlayButton(p);
                                    var bar = p.querySelector('.itemMiscInfo-primary');
                                    if (bar) { clearInterval(_pollId); injectMiscInfo(p); return; }
                                    if (++_pollAttempts > 20) clearInterval(_pollId);
                                }, 100);
                            } else {
                                currentItemIsGame = false;
                                cachedTags        = [];
                                if (visiblePage) visiblePage.classList.remove('jellyemu-game-page');
                            }
                            perf.mark('details-end:' + id);
                            perf.measure('details-total:' + id, 'details-start:' + id, 'details-end:' + id);
                        });
                    }

                    function getVisibleDetailPage() {
                        const pages = document.querySelectorAll('.itemDetailPage');
                        for (const p of pages) {
                            if (!p.classList.contains('hide')) return p;
                        }
                        return null;
                    }

                    const JELLYEMU_PREFS_HASH = '#/jellyemu-userprefs';
                    const JELLYEMU_SAVES_HASH = '#/jellyemu-saves';

                    // Jellyfin fires 'viewshow' on official pages and 'pageshow' on both
                    // official and unofficial (custom hash) pages — both bubble from the
                    // target page element. For custom hashes Jellyfin has no real page so
                    // it reuses whatever is currently visible (e.g. #myPreferencesMenuPage),
                    // so we use the hash — not e.target — to route custom pages.
                    function _onJellyfinPageShow(e) {
                        const hash = window.location.hash;
                        const page = e.target;
                        if (!page || !page.classList) return;

                        // Custom JellyEmu pages — hash takes priority over target element
                        // (Jellyfin fires pageshow on the last visible page for unknown hashes)
                        if (hash.startsWith(JELLYEMU_PREFS_HASH)) {
                            _detailObserverDisconnect();
                            // JellyEmu settings page removed
                            return;
                        }
                        if (hash.startsWith(JELLYEMU_SAVES_HASH)) {
                            _detailObserverDisconnect();
                            hijackJellyEmuSavesBrowser();
                            return;
                        }

                        // Official Jellyfin preferences menu
                        if (page.id === 'myPreferencesMenuPage') {
                            _detailObserverDisconnect();
                            injectPrefsMenuEntry(page);
                            return;
                        }

                        // Item detail page — page children are already present when
                        // viewshow fires, so process cards and attempt injection immediately.
                        // detailObserver stays connected as a fallback for any elements
                        // Jellyfin renders asynchronously after the event.
                        if (page.classList.contains('itemDetailPage')) {
                            _detailObserverConnect(page);
                            page.querySelectorAll('.card').forEach(scheduleCardProcess);
                            const match = hash.match(/id=([a-zA-Z0-9]+)/);
                            if (!match) return;
                            const id = match[1];
                            // Both viewshow and pageshow fire for the same transition —
                            // skip if we're already processing this item
                            if (currentItemId === id) return;
                            currentItemId     = id;
                            currentItemIsGame = false;
                            processItemDetails(id, page);
                            return;
                        }

                        // Any other page — disconnect detail observer, reset state,
                        // but process cards that are already present in this view
                        _detailObserverDisconnect();
                        currentItemId     = null;
                        currentItemIsGame = false;
                        page.querySelectorAll('.card').forEach(scheduleCardProcess);
                    }

                    document.addEventListener('viewshow', _onJellyfinPageShow);
                    document.addEventListener('pageshow',  _onJellyfinPageShow);

                    // Startup: handle direct-URL load where no viewshow/pageshow will fire
                    (function() {
                        const hash = window.location.hash;
                        if (hash.startsWith(JELLYEMU_PREFS_HASH))  { return; }
                        if (hash.startsWith(JELLYEMU_SAVES_HASH))  { hijackJellyEmuSavesBrowser(); return; }
                        const prefsMenu = document.getElementById('myPreferencesMenuPage');
                        if (prefsMenu && !prefsMenu.classList.contains('hide')) injectPrefsMenuEntry(prefsMenu);
                        // If landing directly on a detail page, find it and process it
                        const detailPage = getVisibleDetailPage();
                        if (detailPage) {
                            _detailObserverConnect(detailPage);
                            const match = hash.match(/id=([a-zA-Z0-9]+)/);
                            if (match) { currentItemId = match[1]; processItemDetails(match[1], detailPage); }
                        }
                    })();

                    const perf = {
                        mark:    (n)       => performance.mark('jellyemu:' + n),
                        measure: (n, a, b) => { try { performance.measure('jellyemu:' + n, 'jellyemu:' + a, 'jellyemu:' + b); } catch(_) {} },
                        // Wrap an async fn and measure start→settle with a named span
                        time:    (n, fn)   => {
                            const s = 'jellyemu:' + n + ':start';
                            const e = 'jellyemu:' + n + ':end';
                            performance.mark(s);
                            return Promise.resolve(fn()).finally(() => {
                                performance.mark(e);
                                try { performance.measure('jellyemu:' + n, s, e); } catch(_) {}
                            });
                        },
                    };

                    const BATCH_SIZE        = 50;
                    const BATCH_CONCURRENCY = 2;

                    // id → resolve callback, populated by queueGetItem
                    const _metaQueue    = [];   // [{ cardId, resolve }]
                    let _batchActive    = 0;
                    let _batchScheduled = false;

                    // Public API — same signature as old queueGetItem so call sites are unchanged
                    function queueGetItem(cardId, resolve) {
                        perf.mark('getItem-queued:' + cardId);
                        _metaQueue.push({ cardId, resolve });
                        if (!_batchScheduled) {
                            _batchScheduled = true;
                            // Small delay to let the current card flush collect more IDs
                            // before firing — coalesces rapid additions into fewer requests
                            setTimeout(_drainBatchQueue, 16);
                        }
                    }

                    function _drainBatchQueue() {
                        _batchScheduled = false;
                        while (_batchActive < BATCH_CONCURRENCY && _metaQueue.length > 0) {
                            // Slice up to BATCH_SIZE items off the front of the queue
                            const batch = _metaQueue.splice(0, BATCH_SIZE);
                            _batchActive++;

                            const ids      = batch.map(b => b.cardId);
                            const resolves = {};
                            batch.forEach(b => {
                                resolves[b.cardId] = b.resolve;
                                perf.mark('getItem-start:' + b.cardId);
                            });

                            perf.mark('batch-fetch-start:' + ids[0]);
                            fetch('/jellyemu/cardmeta?ids=' + ids.join(','))
                                .then(r => r.ok ? r.json() : {})
                                .catch(() => ({}))
                                .then(function(data) {
                                    perf.mark('batch-fetch-end:' + ids[0]);
                                    try { performance.measure('jellyemu:batch-fetch[' + ids.length + ']:' + ids[0], 'jellyemu:batch-fetch-start:' + ids[0], 'jellyemu:batch-fetch-end:' + ids[0]); } catch(_) {}

                                    // Dispatch each item's result to its waiting resolve callback
                                    batch.forEach(function(b) {
                                        const meta = data[b.cardId];
                                        perf.mark('getItem-end:' + b.cardId);
                                        try { performance.measure('jellyemu:getItem-api:' + b.cardId, 'jellyemu:getItem-start:' + b.cardId, 'jellyemu:getItem-end:' + b.cardId); } catch(_) {}
                                        // Normalise to the same shape applyGameCardTreatment expects
                                        b.resolve(meta ? {
                                            Tags:            meta.tags            || [],
                                            CommunityRating: meta.communityRating ?? null,
                                            ProviderIds:     meta.providerIds     || {},
                                        } : null);
                                    });
                                })
                                .finally(function() {
                                    _batchActive--;
                                    // If more items arrived while this batch was in flight, drain them
                                    if (_metaQueue.length > 0) _drainBatchQueue();
                                });
                        }

                        // If there are still items but we hit concurrency cap, schedule a retry
                        if (_metaQueue.length > 0 && _batchActive >= BATCH_CONCURRENCY && !_batchScheduled) {
                            _batchScheduled = true;
                            setTimeout(_drainBatchQueue, 16);
                        }
                    }

                    const _pendingCards = new Set();
                    let _cardFlushScheduled = false;

                    function scheduleCardProcess(card) {
                        _pendingCards.add(card);
                        if (!_cardFlushScheduled) {
                            _cardFlushScheduled = true;
                            perf.mark('card-flush-scheduled');
                            setTimeout(function() {
                                _cardFlushScheduled = false;
                                const batch = Array.from(_pendingCards);
                                _pendingCards.clear();
                                perf.mark('card-flush-start');
                                batch.forEach(processCard);
                                perf.mark('card-flush-end');
                                try { performance.measure('jellyemu:card-flush[' + batch.length + ']', 'jellyemu:card-flush-start', 'jellyemu:card-flush-end'); } catch(_) {}
                            }, 0);
                        }
                    }

                    function applyGameCardTreatment(card) {
                        card.setAttribute('data-collectiontype', 'games');
                        card.setAttribute('data-jellyemu-game', '1');

                        // Defer all DOM reads/writes to the next animation frame so the
                        // observer callback returns without blocking the current paint.
                        const cardId0 = card.getAttribute('data-id') || 'unknown';
                        perf.mark('card-rAF-scheduled:' + cardId0);
                        requestAnimationFrame(function() {
                            perf.mark('card-rAF-start:' + cardId0);
                            const iconSpan = card.querySelector('.cardImageIcon');
                            if (iconSpan) iconSpan.innerHTML = 'sports_esports';

                            card.querySelectorAll('button[data-action="resume"], button[data-action="play"]').forEach(function(b) {
                                b.style.display = 'none';
                            });

                            if (!card.querySelector('.jellyemu-card-badge-wrap')) {
                                const cardId = card.getAttribute('data-id');
                                if (cardId && window.ApiClient) {
                                    queueGetItem(cardId, function(item) {
                                        if (!item || !item.Tags) return;
                                        const imgCtr = card.querySelector('.cardImageContainer');
                                        if (!imgCtr) return;

                                        perf.mark('badge-render-start:' + cardId);

                                        card.setAttribute('data-jellyemu-tags', item.Tags.join(','));

                                        const badgeWrap = document.createElement('div');
                                        badgeWrap.className = 'jellyemu-card-badge-wrap';
                                        badgeWrap.style.cssText = 'position:absolute;bottom:4px;left:4px;display:flex;gap:3px;flex-wrap:wrap;z-index:2;pointer-events:none;';
                                        item.Tags.filter(t => t !== 'Game').forEach(function(tag) {
                                            const badge = document.createElement('span');
                                            const isRegion      = knownRegions.has(tag);
                                            const isDisc        = isDiscTag(tag);
                                            const isUnknown     = tag === 'Unknown';
                                            const isUnsupported = ejsUnsupportedPlatforms.has(tag);
                                            badge.style.cssText = 'font-size:9px;font-weight:700;letter-spacing:.03em;padding:1px 5px;border-radius:3px;opacity:.88;' +
                                                (isRegion
                                                    ? 'background:rgba(0,164,220,.85);color:#fff;'
                                                    : isDisc
                                                        ? 'background:rgba(220,140,0,.85);color:#fff;'
                                                        : 'background:rgba(0,0,0,.72);color:#e0e0e0;border:1px solid rgba(255,255,255,.18);');
                                            badge.textContent = tag;
                                            badgeWrap.appendChild(badge);
                                            if (isUnsupported || isUnknown) {
                                                const statusBadge = document.createElement('span');
                                                statusBadge.style.cssText = 'font-size:9px;font-weight:700;letter-spacing:.03em;padding:1px 5px;border-radius:3px;opacity:.88;' +
                                                    'background:rgba(200,120,0,.75);color:#fff;border:1px solid rgba(255,180,0,.3);';
                                                statusBadge.textContent = isUnknown ? 'Unknown' : 'Unsupported';
                                                badgeWrap.appendChild(statusBadge);
                                            }
                                        });
                                        if (badgeWrap.children.length > 0) imgCtr.appendChild(badgeWrap);

                                        const rating = item.CommunityRating;
                                        const pids = item.ProviderIds || {};
                                        if (typeof rating === 'number' && (pids['IGDB'] || pids['Romm'])) {
                                            const ratingBadge = document.createElement('div');
                                            ratingBadge.className = 'jellyemu-card-rating-badge';
                                            ratingBadge.title = (pids['IGDB'] ? 'IGDB' : 'RoMM') + ' rating: ' + rating.toFixed(1) + ' / 10';
                                            ratingBadge.style.cssText = 'position:absolute;top:4px;right:4px;z-index:2;pointer-events:none;' +
                                                'display:inline-flex;align-items:center;gap:2px;' +
                                                'background:rgba(0,0,0,.72);border:1px solid rgba(255,255,255,.18);' +
                                                'border-radius:3px;padding:1px 5px;font-size:9px;font-weight:700;color:#e0e0e0;opacity:.92;';
                                            ratingBadge.innerHTML =
                                                '<span class="material-icons starIcon star" aria-hidden="true" style="font-size:9px;line-height:1;"></span>' +
                                                rating.toFixed(1);
                                            imgCtr.appendChild(ratingBadge);
                                        }

                                        if (isPlayable(item.Tags)) {
                                            card.querySelectorAll('button[data-action="resume"], button[data-action="play"]').forEach(function(playBtn) {
                                                if (playBtn.parentNode && !playBtn.parentNode.querySelector('.jellyemu-card-play')) {
                                                    const sterileBtn = document.createElement('button');
                                                    sterileBtn.type = 'button';
                                                    sterileBtn.className = 'cardOverlayButton cardOverlayButton-hover jellyemu-card-play';
                                                    sterileBtn.title = 'Play Game';
                                                    sterileBtn.innerHTML = '<span class="material-icons" aria-hidden="true">sports_esports</span>';
                                                    sterileBtn.addEventListener('click', function(e) {
                                                        e.preventDefault();
                                                        e.stopPropagation();
                                                        e.stopImmediatePropagation();
                                                        launchEmulator(cardId);
                                                    });
                                                    playBtn.parentNode.insertBefore(sterileBtn, playBtn);
                                                }
                                            });
                                        }

                                        perf.mark('badge-render-end:' + cardId);
                                        try { performance.measure('jellyemu:badge-render:' + cardId, 'jellyemu:badge-render-start:' + cardId, 'jellyemu:badge-render-end:' + cardId); } catch(_) {}
                                    });
                                }
                            }

                            perf.mark('card-rAF-end:' + cardId0);
                            try { performance.measure('jellyemu:card-rAF:' + cardId0, 'jellyemu:card-rAF-start:' + cardId0, 'jellyemu:card-rAF-end:' + cardId0); } catch(_) {}
                        });
                    }

                    function processCard(card) {
                        const path = card.getAttribute('data-path');
                        let isGameCard = card.getAttribute('data-collectiontype') === 'games' ||
                                         card.getAttribute('data-jellyemu-game') === '1' ||
                                         (card.querySelector('.cardText') && (card.querySelector('.cardText').textContent.includes('Games') || card.querySelector('.cardText').textContent.includes('Emulators')));

                        if (path) {
                            const extMatch = path.match(/\.([a-zA-Z0-9]+)$/);
                            if (extMatch && romExtensions.has(extMatch[1].toLowerCase())) {
                                isGameCard = true;
                            }
                        }

                        if (isGameCard) {
                            applyGameCardTreatment(card);
                        } else if (
                            card.getAttribute('data-type') === 'Book' &&
                            !card.getAttribute('data-jellyemu-checked')
                        ) {
                            card.setAttribute('data-jellyemu-checked', '1');
                            const cardId = card.getAttribute('data-id');
                            if (cardId && window.ApiClient) {
                                queueGetItem(cardId, function(item) {
                                    if (item && item.Tags && item.Tags.includes('Game')) {
                                        applyGameCardTreatment(card);
                                    }
                                });
                            }
                        }
                    }

                    const cardObserver = new MutationObserver((mutations) => {
                        perf.mark('observer-batch-start');

                        mutations.forEach((mutation) => {
                            mutation.addedNodes.forEach((node) => {
                                if (node.nodeType !== 1) return;
                                if (node.getAttribute?.('data-jellyemu-mods')) return;

                                // Header button icon swap
                                if (node.tagName === 'BUTTON' && node.classList?.contains('headerButton')) {
                                    const titleStr = node.getAttribute('title') || '';
                                    if (titleStr.includes('Games')) {
                                        const iconSpan = node.querySelector('.material-icons');
                                        if (iconSpan) iconSpan.innerHTML = 'sports_esports';
                                    }
                                    return;
                                }

                                // Card detection — O(1) classList check first
                                if (node.classList?.contains('card')) {
                                    scheduleCardProcess(node);
                                } else if (node.classList?.contains('itemsContainer') ||
                                           node.classList?.contains('cardScroller') ||
                                           node.classList?.contains('section') ||
                                           node.tagName === 'SECTION') {
                                    // querySelectorAll only on known container types
                                    node.querySelectorAll('.card').forEach(scheduleCardProcess);
                                } else if (!node.classList?.contains('jellyemu-card-badge-wrap') &&
                                           !node.classList?.contains('jellyemu-card-rating-badge')) {
                                    // Walk up to find a parent card — cheaper than querying down
                                    const parentCard = node.closest?.('.card');
                                    if (parentCard) scheduleCardProcess(parentCard);
                                }
                            });
                        });

                        perf.mark('observer-batch-end');
                        perf.measure('observer-batch', 'observer-batch-start', 'observer-batch-end');
                    });

                    // Detail observer — scoped to Jellyfin's view container so subtree
                    // queries are bounded to a small part of the DOM
                    const detailObserver = new MutationObserver((mutations) => {
                        let checkDetails = false;
                        let cachedDetailPage = null;
                        function getDetailPage() {
                            if (cachedDetailPage === null) cachedDetailPage = getVisibleDetailPage() || undefined;
                            return cachedDetailPage || null;
                        }

                        mutations.forEach((mutation) => {
                            mutation.addedNodes.forEach((node) => {
                                if (node.nodeType !== 1) return;
                                const cls = node.classList;
                                if (!cls) return;

                                if (cls.contains('mainDetailButtons') || node.querySelector?.('.mainDetailButtons')) {
                                    checkDetails = true;
                                    const dp = getDetailPage();
                                    if (dp) dp.classList.add('jellyemu-game-page');
                                }
                                if (cls.contains('btnPlay') || node.getAttribute?.('data-action') === 'resume') {
                                    checkDetails = true;
                                    const dp = getDetailPage();
                                    if (dp) dp.classList.add('jellyemu-game-page');
                                }
                            });
                        });

                        if (checkDetails) injectAll();
                    });

                    const _viewContainer = document.querySelector('.view-manager') || document.body;

                    // Cards — always observe the view-manager; cards render on any page
                    cardObserver.observe(_viewContainer, { childList: true, subtree: true });

                    // Action sheets — Jellyfin appends a .dialog wrapper to body;
                    // .actionSheetContent is one level inside it, not the direct child.
                    const bodyObserver = new MutationObserver((mutations) => {
                        mutations.forEach((mutation) => {
                            mutation.addedNodes.forEach((node) => {
                                if (node.nodeType !== 1) return;
                                // The added node is .dialog; find .actionSheetContent within it
                                const sheetContent = node.classList?.contains('actionSheetContent')
                                    ? node
                                    : node.querySelector?.('.actionSheetContent');
                                if (sheetContent) patchActionSheet(sheetContent);
                            });
                        });
                    });
                    bodyObserver.observe(document.body, { childList: true });

                    // Detail observer — connected only while on a detail page via
                    // _detailObserverConnect / _detailObserverDisconnect so it doesn't
                    // burn cycles watching the entire view-manager on every page.
                    let _detailObserverTarget = null;
                    function _detailObserverConnect(page) {
                        if (_detailObserverTarget === page) return; // already watching this page
                        if (_detailObserverTarget) detailObserver.disconnect();
                        _detailObserverTarget = page;
                        detailObserver.observe(page, { childList: true, subtree: true });
                    }
                    function _detailObserverDisconnect() {
                        if (!_detailObserverTarget) return;
                        detailObserver.disconnect();
                        _detailObserverTarget = null;
                    }

                    document.querySelectorAll('.card').forEach(scheduleCardProcess);

                    function injectPrefsMenuEntry(page) {
                        if (page.querySelector('.jellyemu-prefs-entry')) return true; // already done

                        const userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : '';


                        const savesAnchor = document.createElement('a');
                        savesAnchor.className = 'emby-button jellyemu-prefs-entry listItem-border';
                        savesAnchor.href = JELLYEMU_SAVES_HASH + (userId ? '?userId=' + userId : '');
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
                            window.location.hash = JELLYEMU_SAVES_HASH + (userId ? '?userId=' + userId : '');
                        });

                        // In Jellyfin 10.11 the page has three verticalSections:
                        //   1. user settings (.verticalSection-extrabottompadding, no extra class)
                        //   2. .adminSection
                        //   3. .userSection (Sign Out)
                        // We want to append into the first one (user settings),
                        // before the Administration block.
                        const adminSection = page.querySelector('.adminSection');
                        const userSection  = page.querySelector('.userSection');
                        const targetSection =
                            page.querySelector('.verticalSection:not(.adminSection):not(.userSection)')
                            || page.querySelector('.verticalSection')
                            || page.querySelector('.readOnlyContent')
                            || page;
                        // DOM not ready yet — target section has no children, Jellyfin hasn't rendered
                        if (!targetSection.children.length) return false;
                        targetSection.appendChild(savesAnchor);
                        return true;
                    }



                    function hijackJellyEmuSavesBrowser() {
                        const activePage = document.querySelector('.page:not(.hide):not(#myPreferencesMenuPage)');
                        if (!activePage) return;

                        if (activePage.hasAttribute('data-jellyemu-saves-hijacked')) {
                            const headerTitle = document.querySelector('.skinHeader .pageTitle');
                            if (headerTitle && headerTitle.textContent !== 'Save State Browser') {
                                headerTitle.textContent = 'Save State Browser';
                            }
                            return;
                        }

                        activePage.setAttribute('data-jellyemu-saves-hijacked', '1');
                        activePage.className = 'page libraryPage noSecondaryNavPage mainAnimatedPage';
                        activePage.setAttribute('data-title', 'Save State Browser');
                        activePage.setAttribute('data-backbutton', 'true');

                        document.title = 'Save State Browser';
                        const headerTitle = document.querySelector('.skinHeader .pageTitle');
                        if (headerTitle) headerTitle.textContent = 'Save State Browser';

                        const userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : null;

                        activePage.innerHTML = `
                            <style>
                                .je-saves-grid {
                                    display: grid;
                                    grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
                                    gap: 18px;
                                    padding: 24px;
                                }
                                .je-save-card {
                                    background: rgba(255,255,255,0.05);
                                    border: 1px solid rgba(255,255,255,0.08);
                                    border-radius: 10px;
                                    overflow: hidden;
                                    display: flex;
                                    flex-direction: column;
                                    transition: transform 0.15s ease, border-color 0.15s ease;
                                    cursor: default;
                                }
                                .je-save-card:hover {
                                    transform: translateY(-3px);
                                    border-color: rgba(0,164,220,0.5);
                                }
                                .je-save-art {
                                    width: 100%;
                                    aspect-ratio: 16/9;
                                    object-fit: cover;
                                    background: rgba(0,0,0,0.4);
                                    display: block;
                                    flex-shrink: 0;
                                }
                                .je-save-art-poster {
                                    width: 100%;
                                    aspect-ratio: 2/3;
                                    object-fit: cover;
                                    background: rgba(0,0,0,0.4);
                                    display: block;
                                    flex-shrink: 0;
                                }
                                .je-save-art-placeholder {
                                    width: 100%;
                                    aspect-ratio: 16/9;
                                    background: rgba(0,0,0,0.35);
                                    display: flex;
                                    align-items: center;
                                    justify-content: center;
                                    flex-shrink: 0;
                                }
                                .je-save-art-placeholder .material-icons {
                                    font-size: 56px;
                                    color: rgba(255,255,255,0.15);
                                }
                                .je-save-body {
                                    padding: 12px 14px 14px;
                                    display: flex;
                                    flex-direction: column;
                                    gap: 6px;
                                    flex: 1;
                                }
                                .je-save-title {
                                    font-size: 0.88rem;
                                    font-weight: 600;
                                    color: #fff;
                                    white-space: nowrap;
                                    overflow: hidden;
                                    text-overflow: ellipsis;
                                    line-height: 1.3;
                                }
                                .je-save-badges {
                                    display: flex;
                                    flex-wrap: wrap;
                                    gap: 4px;
                                }
                                .je-save-badge {
                                    font-size: 10px;
                                    font-weight: 700;
                                    letter-spacing: .03em;
                                    padding: 2px 6px;
                                    border-radius: 4px;
                                    line-height: 1.4;
                                }
                                .je-save-badge-platform {
                                    background: rgba(255,255,255,0.1);
                                    color: #ccc;
                                    border: 1px solid rgba(255,255,255,0.15);
                                }
                                .je-save-badge-region {
                                    background: rgba(0,164,220,0.8);
                                    color: #fff;
                                }
                                .je-save-badge-disc {
                                    background: rgba(220,140,0,0.85);
                                    color: #fff;
                                }
                                .je-save-badge-slot {
                                    background: rgba(82,181,75,0.25);
                                    color: #7ed67a;
                                    border: 1px solid rgba(82,181,75,0.35);
                                }
                                .je-save-meta {
                                    font-size: 0.75rem;
                                    color: rgba(255,255,255,0.45);
                                    line-height: 1.4;
                                }
                                .je-save-actions {
                                    display: flex;
                                    gap: 8px;
                                    margin-top: auto;
                                    padding-top: 10px;
                                }
                                .je-save-btn {
                                    flex: 1;
                                    display: flex;
                                    align-items: center;
                                    justify-content: center;
                                    gap: 5px;
                                    padding: 7px 10px;
                                    border-radius: 6px;
                                    font-size: 0.78rem;
                                    font-weight: 600;
                                    cursor: pointer;
                                    border: none;
                                    transition: background 0.15s ease, opacity 0.15s ease;
                                    text-decoration: none;
                                }
                                .je-save-btn .material-icons { font-size: 15px; }
                                .je-save-btn-play {
                                    background: rgba(0,164,220,0.85);
                                    color: #fff;
                                }
                                .je-save-btn-play:hover { background: rgba(0,164,220,1); }
                                .je-save-btn-dl {
                                    background: rgba(255,255,255,0.08);
                                    color: rgba(255,255,255,0.75);
                                    border: 1px solid rgba(255,255,255,0.12);
                                }
                                .je-save-btn-dl:hover { background: rgba(255,255,255,0.15); }
                                .je-save-btn-romm-push {
                                    background: rgba(82,181,75,0.15);
                                    color: #52B54B;
                                    border: 1px solid rgba(82,181,75,0.3);
                                }
                                .je-save-btn-romm-push:hover { background: rgba(82,181,75,0.28); }
                                .je-save-btn-romm-pull {
                                    background: rgba(0,164,220,0.12);
                                    color: #00a4dc;
                                    border: 1px solid rgba(0,164,220,0.25);
                                }
                                .je-save-btn-romm-pull:hover { background: rgba(0,164,220,0.25); }
                                .je-saves-empty {
                                    text-align: center;
                                    color: rgba(255,255,255,0.35);
                                    padding: 80px 24px;
                                    font-size: 1rem;
                                }
                                .je-saves-empty .material-icons { font-size: 64px; display: block; margin-bottom: 16px; opacity: 0.3; }
                                .je-saves-header {
                                    display: flex;
                                    align-items: center;
                                    gap: 16px;
                                    padding: 20px 24px 4px;
                                }
                                .je-saves-filter {
                                    background: rgba(255,255,255,0.07);
                                    border: 1px solid rgba(255,255,255,0.12);
                                    border-radius: 6px;
                                    color: #fff;
                                    padding: 6px 12px;
                                    font-size: 0.82rem;
                                    cursor: pointer;
                                    outline: none;
                                    transition: border-color 0.15s;
                                }
                                .je-saves-filter option {
                                    background: #1a1a2e;
                                    color: #fff;
                                }
                                .je-saves-filter:focus { border-color: #00a4dc; }
                                .je-saves-count {
                                    font-size: 0.82rem;
                                    color: rgba(255,255,255,0.4);
                                    margin-left: auto;
                                }
                            </style>
                            <div class="je-saves-header">
                                <select id="je-filter-slot" class="je-saves-filter">
                                    <option value="">All slots</option>
                                    <option value="1">Slot 1</option>
                                    <option value="2">Slot 2</option>
                                    <option value="3">Slot 3</option>
                                    <option value="4">Slot 4</option>
                                    <option value="5">Slot 5</option>
                                </select>
                                <select id="je-filter-platform" class="je-saves-filter">
                                    <option value="">All platforms</option>
                                </select>
                                <span id="je-saves-count" class="je-saves-count"></span>
                            </div>
                            <div id="je-saves-grid" class="je-saves-grid">
                                <div class="je-saves-empty"><span class="material-icons">hourglass_empty</span>Loading save states…</div>
                            </div>`;

                        if (!userId) {
                            activePage.querySelector('#je-saves-grid').innerHTML =
                                '<div class="je-saves-empty"><span class="material-icons">person_off</span>Sign in to view your save states.</div>';
                            return;
                        }

                        function fmtDate(iso) {
                            try {
                                const d = new Date(iso);
                                return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) +
                                       ' ' + d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
                            } catch { return iso; }
                        }

                        function fmtSize(bytes) {
                            if (bytes < 1024) return bytes + ' B';
                            if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
                            return (bytes / 1048576).toFixed(1) + ' MB';
                        }

                        let allSaves = [];

                        function renderGrid(saves) {
                            const grid = activePage.querySelector('#je-saves-grid');
                            const count = activePage.querySelector('#je-saves-count');
                            if (count) count.textContent = saves.length + ' save' + (saves.length !== 1 ? 's' : '');
                            if (saves.length === 0) {
                                grid.innerHTML = '<div class="je-saves-empty"><span class="material-icons">save</span>No save states found.</div>';
                                return;
                            }
                            grid.innerHTML = '';
                            saves.forEach(s => {
                                const card = document.createElement('div');
                                card.className = 'je-save-card';

                                const artUrl = s.hasArt
                                    ? `/Items/${s.itemId}/Images/Primary?maxHeight=420&quality=90`
                                    : null;

                                const artWrap = document.createElement('div');
                                artWrap.style.cssText = 'position:relative;flex-shrink:0;';

                                // Placeholder shown until something better loads
                                const placeholder = document.createElement('div');
                                placeholder.className = 'je-save-art-placeholder';
                                placeholder.innerHTML = '<span class="material-icons">sports_esports</span>';
                                artWrap.appendChild(placeholder);

                                // Poster art sits behind the screenshot — shown if no screenshot
                                if (artUrl) {
                                    const posterImg = document.createElement('img');
                                    posterImg.className = 'je-save-art-poster';
                                    posterImg.alt = '';
                                    posterImg.loading = 'lazy';
                                    posterImg.style.display = 'none';
                                    posterImg.onload = function() {
                                        placeholder.style.display = 'none';
                                        this.style.display = 'block';
                                    };
                                    posterImg.src = artUrl;
                                    artWrap.appendChild(posterImg);
                                }

                                // Screenshot: fetch the endpoint, read the JSON, assign dataUrl directly
                                if (s.hasScreenshot) {
                                    const ssImg = document.createElement('img');
                                    ssImg.className = 'je-save-art';
                                    ssImg.alt = '';
                                    ssImg.style.display = 'none';
                                    artWrap.appendChild(ssImg);

                                    fetch(`/jellyemu/save-screenshot/${s.itemId}/${userId}/${s.slot}`)
                                        .then(function(r) { return r.ok ? r.json() : null; })
                                        .then(function(data) {
                                            if (!data || !data.dataUrl) return;
                                            ssImg.onload = function() {
                                                // Hide poster/placeholder once screenshot is ready
                                                artWrap.querySelectorAll('.je-save-art-poster, .je-save-art-placeholder')
                                                    .forEach(function(el) { el.style.display = 'none'; });
                                                ssImg.style.display = 'block';
                                            };
                                            ssImg.src = data.dataUrl;
                                        })
                                        .catch(function() {});
                                }

                                card.appendChild(artWrap);

                                const badges = [
                                    s.platform ? `<span class="je-save-badge je-save-badge-platform">${s.platform}</span>` : '',
                                    s.region   ? `<span class="je-save-badge je-save-badge-region">${s.region}</span>`   : '',
                                    s.disc     ? `<span class="je-save-badge je-save-badge-disc">${s.disc}</span>`       : '',
                                    `<span class="je-save-badge je-save-badge-slot">Slot ${s.slot}</span>`,
                                ].join('');

                                const body = document.createElement('div');
                                body.className = 'je-save-body';
                                body.innerHTML = `
                                    <div class="je-save-title" title="${s.gameName}">${s.gameName}</div>
                                    <div class="je-save-badges">${badges}</div>
                                    <div class="je-save-meta">${fmtDate(s.lastModified)} · ${fmtSize(s.sizeBytes)}</div>
                                    <div class="je-save-romm-status" data-item="${s.itemId}" data-slot="${s.slot}" style="font-size:0.75em;color:#aaa;margin:2px 0 4px;min-height:16px;"></div>
                                    <div class="je-save-actions">
                                        <button class="je-save-btn je-save-btn-play">
                                            <span class="material-icons">sports_esports</span>Play
                                        </button>
                                        <a class="je-save-btn je-save-btn-dl" href="${s.downloadUrl}" download="${s.gameName.replace(/[^a-zA-Z0-9 _-]/g,'_')}_slot${s.slot}.state">
                                            <span class="material-icons">download</span>
                                        </a>
                                        <button class="je-save-btn je-save-btn-romm-push" title="Push to Romm" style="display:none;">
                                            <span class="material-icons">cloud_upload</span>
                                        </button>
                                        <button class="je-save-btn je-save-btn-romm-pull" title="Pull from Romm" style="display:none;">
                                            <span class="material-icons">cloud_download</span>
                                        </button>
                                    </div>`;

                                body.querySelector('.je-save-btn-play').addEventListener('click', () => {
                                    launchEmulator(s.itemId);
                                });

                                // Romm sync status + push/pull buttons
                                (function(itemId, slot, bodyEl) {
                                    fetch('/jellyemu/romm/sync-status/' + itemId + '/' + userId + '/' + slot)
                                        .then(function(r) { return r.ok ? r.json() : null; })
                                        .then(function(d) {
                                            if (!d || d.status === 'Disabled') return;
                                            var statusEl = bodyEl.querySelector('.je-save-romm-status');
                                            var pushBtn  = bodyEl.querySelector('.je-save-btn-romm-push');
                                            var pullBtn  = bodyEl.querySelector('.je-save-btn-romm-pull');
                                            var iconMap = {
                                                Pushed:     '\u2601\ufe0f In sync with Romm',
                                                InSync:     '\u2601\ufe0f In sync with Romm',
                                                RemoteWins: '\u26a0\ufe0f Romm has a newer save',
                                                LocalOnly:  '\u2191 Not yet pushed to Romm',
                                                RemoteOnly: '\u2193 Remote save only',
                                                Error:      '\u274c Romm sync error',
                                            };
                                            if (statusEl) statusEl.textContent = iconMap[d.status] || d.status;
                                            if (pushBtn) { pushBtn.style.display = ''; }
                                            if (pullBtn) { pullBtn.style.display = ''; }
                                            pushBtn && pushBtn.addEventListener('click', function() {
                                                pushBtn.disabled = true;
                                                fetch('/jellyemu/romm/push/' + itemId + '/' + userId + '/' + slot, { method: 'POST' })
                                                    .then(function(r) { return r.json(); })
                                                    .then(function(d2) {
                                                        if (statusEl) statusEl.textContent = d2.pushed ? '\u2601\ufe0f Pushed to Romm' : '\u274c Push failed';
                                                        pushBtn.disabled = false;
                                                    }).catch(function() { pushBtn.disabled = false; });
                                            });
                                            pullBtn && pullBtn.addEventListener('click', function() {
                                                pullBtn.disabled = true;
                                                fetch('/jellyemu/romm/pull/' + itemId + '/' + userId + '/' + slot, { method: 'POST' })
                                                    .then(function(r) { return r.json(); })
                                                    .then(function(d2) {
                                                        if (statusEl) statusEl.textContent = d2.pulled ? '\u2193 Pulled from Romm' : '\u274c Pull failed';
                                                        pullBtn.disabled = false;
                                                    }).catch(function() { pullBtn.disabled = false; });
                                            });
                                        })
                                        .catch(function() {});
                                })(s.itemId, s.slot, body);

                                card.appendChild(body);
                                grid.appendChild(card);
                            });
                        }

                        function applyFilters() {
                            const slotVal     = activePage.querySelector('#je-filter-slot').value;
                            const platformVal = activePage.querySelector('#je-filter-platform').value;
                            const filtered    = allSaves.filter(s => {
                                if (slotVal     && String(s.slot)    !== slotVal)     return false;
                                if (platformVal && s.platform        !== platformVal) return false;
                                return true;
                            });
                            renderGrid(filtered);
                        }

                        fetch('/jellyemu/saves/' + userId)
                            .then(r => r.ok ? r.json() : [])
                            .then(saves => {
                                allSaves = saves;

                                const platforms = [...new Set(saves.map(s => s.platform).filter(Boolean))].sort();
                                const platformSelect = activePage.querySelector('#je-filter-platform');
                                platforms.forEach(p => {
                                    const opt = document.createElement('option');
                                    opt.value = p;
                                    opt.textContent = p;
                                    platformSelect.appendChild(opt);
                                });

                                activePage.querySelector('#je-filter-slot').addEventListener('change', applyFilters);
                                activePage.querySelector('#je-filter-platform').addEventListener('change', applyFilters);

                                renderGrid(allSaves);
                            })
                            .catch(() => {
                                activePage.querySelector('#je-saves-grid').innerHTML =
                                    '<div class="je-saves-empty"><span class="material-icons">error_outline</span>Failed to load save states.</div>';
                            });
                    }

                })();
                </script>
                """;

                string block = "\n" + StartMarker + "\n" + injection + EndMarker + "\n";
                htmlContent = Regex.Replace(htmlContent, @"(</body>)", block + "$1");

                return htmlContent;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JellyEmu] Fatal Error injecting mods: {ex.Message}");
                return payload?.Contents ?? string.Empty;
            }
        }
    }
}