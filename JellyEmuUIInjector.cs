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
                  [data-collectiontype="games"] .cardImageContainer,
                  [data-jellyemu-game="1"] .cardImageContainer {
                  }
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
                        // Romm: push save to Romm after a save event from the emulator iframe
                        if (e.data && e.data.type === 'jellyemu-save-written') {
                            var userId2 = window.ApiClient ? window.ApiClient.getCurrentUserId() : '';
                            var itemId2 = e.data.itemId;
                            if (userId2 && itemId2) {
                                fetch('/jellyemu/romm/sync-after-save/' + itemId2 + '/' + userId2, { method: 'POST' })
                                    .then(function(r) { return r.ok ? r.json() : null; })
                                    .then(function(d) { if (d && d.pushed) jeToast('\u2601 Save synced to Romm'); })
                                    .catch(function() {});
                            }
                        }
                        // Romm: report playtime when session ends
                        if (e.data && e.data.type === 'jellyemu-session-end') {
                            var userId3 = window.ApiClient ? window.ApiClient.getCurrentUserId() : '';
                            var itemId3 = e.data.itemId;
                            var seconds3 = e.data.seconds || 0;
                            if (userId3 && itemId3 && seconds3 > 0) {
                                fetch('/jellyemu/romm/report-playtime/' + itemId3 + '/' + userId3, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ seconds: seconds3 })
                                }).catch(function() {});
                            }
                        }
                        // Romm: push screenshot
                        if (e.data && e.data.type === 'jellyemu-screenshot') {
                            var userId4 = window.ApiClient ? window.ApiClient.getCurrentUserId() : '';
                            var itemId4 = e.data.itemId;
                            var dataUrl = e.data.dataUrl;
                            if (userId4 && itemId4 && dataUrl) {
                                fetch('/jellyemu/romm/screenshot/' + itemId4 + '/' + userId4, {
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
                            const label = playBtn.querySelector('.actionSheetItemText');
                            if (label) label.textContent = 'Play Game';
                            playBtn.addEventListener('click', function(e) {
                                e.preventDefault();
                                e.stopImmediatePropagation();
                                dismissActionSheet(sheetRoot);
                                launchEmulator(itemId);
                            }, true);
                        }

                        const playFromHereBtn = sheetRoot.querySelector('button[data-id="playallfromhere"]');
                        if (playFromHereBtn) {
                            playFromHereBtn.style.display = 'none';
                        }
                    }

                    let cachedTags = [];

                    function injectMiscInfo() {
                        const page = getVisibleDetailPage();
                        if (!page) return;
                        const miscBar = page.querySelector('.itemMiscInfo-primary');
                        if (!miscBar) return;

                        if (miscBar.querySelector('.jellyemu-misc-item')) return;

                        const systemTags = cachedTags.filter(t => t !== 'Game' && !knownRegions.has(t) && !isDiscTag(t));
                        const regionTags = cachedTags.filter(t => knownRegions.has(t));
                        const discTags   = cachedTags.filter(t => isDiscTag(t));
                        const allTags    = [...systemTags, ...regionTags, ...discTags];

                        allTags.forEach(tag => {
                            const div = document.createElement('div');
                            div.className = 'mediaInfoItem jellyemu-misc-item';
                            div.textContent = tag;
                            miscBar.appendChild(div);
                        });

                        const userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : null;
                        const m = window.location.hash.match(/id=([a-zA-Z0-9]+)/);
                        const itemId = m ? m[1] : null;
                        if (userId && itemId && !miscBar.querySelector('.jellyemu-slot-pill')) {
                            fetch('/jellyemu/slot/' + userId)
                                .then(r => r.ok ? r.json() : null)
                                .then(data => {
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
                                                (hasSave ? 'save' : 'save_outlined') + '</span>' +
                                                'Slot ' + slot +
                                                (hasSave ? ' <span class="material-icons" style="font-size:13px;vertical-align:middle;color:#00a4dc;">check_circle</span>' : '');
                                            miscBar.appendChild(pill);
                                        })
                                        .catch(() => {});
                                })
                                .catch(() => {});
                        }

                        if (userId && itemId && !miscBar.querySelector('.jellyemu-playtime-pill')) {
                            fetch('/jellyemu/playtime/' + itemId + '/' + userId)
                                .then(r => r.ok ? r.json() : null)
                                .then(data => {
                                    if (!data || !data.seconds) return; // hide pill if zero playtime
                                    const pill = document.createElement('div');
                                    pill.className = 'mediaInfoItem jellyemu-playtime-pill';
                                    pill.style.cssText = 'display:inline-flex;align-items:center;gap:4px;cursor:default;';
                                    pill.title = data.seconds + ' seconds played';
                                    const h = Math.floor(data.seconds / 3600);
                                    const min = Math.floor((data.seconds % 3600) / 60);
                                    const label = h > 0 ? h + 'h ' + min + 'm' : min > 0 ? min + 'm' : '<1m';
                                    pill.innerHTML = '<span class="material-icons" style="font-size:13px;vertical-align:middle;">schedule</span>' + label + ' played';
                                    miscBar.appendChild(pill);
                                })
                                .catch(() => {});
                        }

                        // Romm sync-status badge
                        if (userId && itemId && !miscBar.querySelector('.jellyemu-romm-sync-pill')) {
                            const slot = 1; // will refetch actual slot below
                            fetch('/jellyemu/slot/' + userId)
                                .then(r => r.ok ? r.json() : null)
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
                                    miscBar.appendChild(pill);
                                })
                                .catch(() => {});
                        }
                    }

                    function injectPlayButton() {
                        const page = getVisibleDetailPage();
                        if (!page) return;
                        const detailButtonsContainer = page.querySelector('.mainDetailButtons');
                        if (!detailButtonsContainer) return;

                        if (detailButtonsContainer.querySelector('#jellyemu-play-btn')) return;

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
                            const m = window.location.hash.match(/id=([a-zA-Z0-9]+)/);
                            if (m) launchEmulator(m[1]);
                        });

                        detailButtonsContainer.insertBefore(btn, detailButtonsContainer.firstChild);
                    }

                    function injectAll() {
                        if (!currentItemIsGame) return;
                        injectPlayButton();
                        injectMiscInfo();
                    }

                    function processItemDetails(id) {
                        if (!window.ApiClient) return;
                        currentItemIsGame = false;
                        cachedTags        = [];

                        document.querySelectorAll('.itemDetailPage').forEach(p => p.classList.remove('jellyemu-game-page'));

                        window.ApiClient.getItem(window.ApiClient.getCurrentUserId(), id).then(item => {
                            if (item && item.Tags && item.Tags.includes('Game')) {
                                currentItemIsGame = true;
                                cachedTags        = item.Tags;
                                injectAll();
                            }
                        });
                    }

                    function getVisibleDetailPage() {
                        const pages = document.querySelectorAll('.itemDetailPage');
                        for (const p of pages) {
                            if (!p.classList.contains('hide')) return p;
                        }
                        return null;
                    }

                    function tick() {
                        if (window.location.hash.startsWith(JELLYEMU_PREFS_HASH)) {
                            hijackJellyEmuPrefsPage();
                            return;
                        }
                        if (window.location.hash.startsWith(JELLYEMU_SAVES_HASH)) {
                            hijackJellyEmuSavesBrowser();
                            return;
                        }

                        const detailPage = getVisibleDetailPage();

                        if (!detailPage) {
                            currentItemId     = null;
                            currentItemIsGame = false;
                            return;
                        }

                        const match = window.location.hash.match(/id=([a-zA-Z0-9]+)/);
                        if (!match) return;
                        const id = match[1];

                        if (currentItemId !== id) {
                            currentItemId     = id;
                            currentItemIsGame = false;
                            processItemDetails(id);
                            return;
                        }

                        if (currentItemIsGame) {
                            injectAll();
                        }
                    }

                    setInterval(tick, 200);

                    function applyGameCardTreatment(card) {
                        card.setAttribute('data-collectiontype', 'games');
                        card.setAttribute('data-jellyemu-game', '1');
                        const iconSpan = card.querySelector('.cardImageIcon');
                        if (iconSpan) iconSpan.innerHTML = 'sports_esports';

                        if (!card.querySelector('.jellyemu-card-badge-wrap')) {
                            const cardId = card.getAttribute('data-id');
                            if (cardId && window.ApiClient) {
                                window.ApiClient.getItem(window.ApiClient.getCurrentUserId(), cardId).then(function(item) {
                                    if (!item || !item.Tags) return;
                                    const badgeWrap = document.createElement('div');
                                    badgeWrap.className = 'jellyemu-card-badge-wrap';
                                    badgeWrap.style.cssText = 'position:absolute;bottom:4px;left:4px;display:flex;gap:3px;flex-wrap:wrap;z-index:2;pointer-events:none;';
                                    item.Tags.filter(t => t !== 'Game').forEach(function(tag) {
                                        const badge = document.createElement('span');
                                        const isRegion = knownRegions.has(tag);
                                        const isDisc   = isDiscTag(tag);
                                        badge.style.cssText = 'font-size:9px;font-weight:700;letter-spacing:.03em;padding:1px 5px;border-radius:3px;opacity:.88;' +
                                            (isRegion
                                                ? 'background:rgba(0,164,220,.85);color:#fff;'
                                                : isDisc
                                                    ? 'background:rgba(220,140,0,.85);color:#fff;'
                                                    : 'background:rgba(0,0,0,.72);color:#e0e0e0;border:1px solid rgba(255,255,255,.18);');
                                        badge.textContent = tag;
                                        badgeWrap.appendChild(badge);
                                    });
                                    if (badgeWrap.children.length > 0) {
                                        const imgCtr = card.querySelector('.cardImageContainer');
                                        if (imgCtr) imgCtr.appendChild(badgeWrap);
                                    }
                                }).catch(function() {});
                            }
                        }

                        const playBtns = card.querySelectorAll('button[data-action="resume"], button[data-action="play"]');
                        playBtns.forEach(playBtn => {
                            playBtn.style.display = 'none';
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
                                    launchEmulator(card.getAttribute('data-id'));
                                });
                                playBtn.parentNode.insertBefore(sterileBtn, playBtn);
                            }
                        });
                    }

                    function processCard(card) {
                        const path = card.getAttribute('data-path');
                        let isGameCard = card.getAttribute('data-collectiontype') === 'games' ||
                                         card.getAttribute('data-jellyemu-game') === '1' ||
                                         (card.querySelector('.cardText') && (card.querySelector('.cardText').innerText.includes('Games') || card.querySelector('.cardText').innerText.includes('Emulators')));

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
                                window.ApiClient.getItem(window.ApiClient.getCurrentUserId(), cardId).then(function(item) {
                                    if (item && item.Tags && item.Tags.includes('Game')) {
                                        applyGameCardTreatment(card);
                                    }
                                }).catch(function() {});
                            }
                        }
                    }

                    const observer = new MutationObserver((mutations) => {
                        let checkDetails = false;

                        mutations.forEach((mutation) => {
                            if (!mutation.addedNodes) return;

                            mutation.addedNodes.forEach((node) => {
                                if (node.nodeType === 1) {

                                    const actionSheetContent = node.classList?.contains('actionSheetContent')
                                        ? node
                                        : node.querySelector?.('.actionSheetContent');
                                    if (actionSheetContent) {
                                        patchActionSheet(actionSheetContent);
                                    }

                                    const prefsMenuPage = node.id === 'myPreferencesMenuPage'
                                        ? node
                                        : node.querySelector?.('#myPreferencesMenuPage');
                                    if (prefsMenuPage) {
                                        injectPrefsMenuEntry(prefsMenuPage);
                                    }

                                    if ((node.classList && node.classList.contains('mainDetailButtons')) || (node.querySelector && node.querySelector('.mainDetailButtons'))) {
                                        checkDetails = true;
                                    }
                                    if (node.classList && (node.classList.contains('btnPlay') || node.getAttribute('data-action') === 'resume')) {
                                        checkDetails = true;
                                    }

                                    let cardsToProcess = [];
                                    if (node.classList && node.classList.contains('card')) {
                                        cardsToProcess.push(node);
                                    } else if (node.querySelectorAll) {
                                        node.querySelectorAll('.card').forEach(c => cardsToProcess.push(c));
                                    }

                                    if (cardsToProcess.length === 0 && node.closest) {
                                        const parentCard = node.closest('.card');
                                        if (parentCard) cardsToProcess.push(parentCard);
                                    }

                                    cardsToProcess.forEach(processCard);

                                    if (node.tagName === 'BUTTON' && node.classList.contains('headerButton')) {
                                        const titleStr = node.getAttribute('title') || '';
                                        if (titleStr.includes('Games')) {
                                            const iconSpan = node.querySelector('.material-icons');
                                            if (iconSpan) iconSpan.innerHTML = 'sports_esports';
                                        }
                                    }
                                }
                            });
                        });

                        if (checkDetails) {
                            mutateDetailsButtons();
                        }
                    });

                    observer.observe(document.body, { childList: true, subtree: true });

                    document.querySelectorAll('.card').forEach(processCard);

                    const JELLYEMU_PREFS_HASH  = '#/jellyemu-userprefs';
                    const JELLYEMU_SAVES_HASH  = '#/jellyemu-saves';

                    function injectPrefsMenuEntry(page) {
                        if (page.querySelector('.jellyemu-prefs-entry')) return;

                        const userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : '';
                        const href = JELLYEMU_PREFS_HASH + (userId ? '?userId=' + userId : '');

                        const anchor = document.createElement('a');
                        anchor.className = 'emby-button jellyemu-prefs-entry listItem-border';
                        anchor.href = href;
                        anchor.style.cssText = 'display:block; margin:0; padding:0;';
                        anchor.innerHTML = `
                            <div class="listItem">
                                <span class="material-icons listItemIcon listItemIcon-transparent sports_esports" aria-hidden="true"></span>
                                <div class="listItemBody">
                                    <div class="listItemBodyText">JellyEmu</div>
                                </div>
                            </div>`;
                        anchor.addEventListener('click', function(e) {
                            e.preventDefault();
                            window.location.hash = JELLYEMU_PREFS_HASH + (userId ? '?userId=' + userId : '');
                        });

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

                        const targetSection = page.querySelector('.verticalSection.verticalSection-extrabottompadding');
                        if (targetSection) {
                            targetSection.appendChild(anchor);
                            targetSection.appendChild(savesAnchor);
                        } else {
                            const readOnly = page.querySelector('.readOnlyContent');
                            if (readOnly) {
                                readOnly.appendChild(anchor);
                                readOnly.appendChild(savesAnchor);
                            }
                        }
                    }

                    function hijackJellyEmuPrefsPage() {
                        const activePage = document.querySelector('.page:not(.hide)');
                        if (!activePage) return;

                        if (activePage.hasAttribute('data-jellyemu-hijacked')) {
                            const headerTitle = document.querySelector('.skinHeader .pageTitle');
                            if (headerTitle && headerTitle.textContent !== 'JellyEmu Settings') {
                                headerTitle.textContent = 'JellyEmu Settings';
                            }
                            return;
                        }

                        activePage.setAttribute('data-jellyemu-hijacked', '1');

                        activePage.className = 'page libraryPage userPreferencesPage noSecondaryNavPage mainAnimatedPage';
                        activePage.setAttribute('data-title', 'JellyEmu Settings');
                        activePage.setAttribute('data-backbutton', 'true');
                        
                        document.title = 'JellyEmu Settings';
                        const headerTitle = document.querySelector('.skinHeader .pageTitle');
                        if (headerTitle) headerTitle.textContent = 'JellyEmu Settings';

                        const PREFS_KEY = 'jellyemu-userprefs';
                        function loadLocalPrefs() { try { return JSON.parse(localStorage.getItem(PREFS_KEY) || '{}'); } catch { return {}; } }
                        function saveLocalPrefs(prefs) { localStorage.setItem(PREFS_KEY, JSON.stringify(prefs)); }

                        activePage.innerHTML = `
                            <div class="settingsContainer padded-left padded-right padded-bottom-page">
                                <form style="margin:0 auto;">
                                    <div class="verticalSection">
                                        <h2 class="sectionTitle">Emulator</h2>
                                        <div class="selectContainer">
                                            <label class="selectLabel" for="jellyemu-pref-shader">Display Shader</label>
                                            <select id="jellyemu-pref-shader" is="emby-select" class="emby-select-withcolor emby-select">
                                                <option value="">None</option>
                                                <option value="2xScaleHQ.glslp">2xScaleHQ</option>
                                                <option value="4xScaleHQ.glslp">4xScaleHQ</option>
                                                <option value="sabr">Sabre</option>
                                                <option value="crt-aperture.glslp">CRT Aperture</option>
                                                <option value="crt-easymode.glslp">CRT Easy Mode</option>
                                                <option value="crt-geom.glslp">CRT Geometry</option>
                                                <option value="crt-mattias.glslp">CRT Mattias</option>
                                                <option value="crt-beam">CRT Beam</option>
                                                <option value="crt-caligari">CRT Caligari</option>
                                                <option value="crt-lottes">CRT Lottes</option>
                                                <option value="crt-zfast">CRT ZFast</option>
                                                <option value="crt-yeetron">CRT Yeetron</option>
                                                <option value="bicubic">Bicubic</option>
                                                <option value="mix-frames">Mix Frames</option>
                                            </select>
                                            <div class="selectArrowContainer"><div style="visibility:hidden;display:none;">0</div><span class="selectArrow material-icons keyboard_arrow_down" aria-hidden="true"></span></div>
                                        </div>
                                        <div class="selectContainer">
                                            <label class="selectLabel" for="jellyemu-pref-scale">Display Scale</label>
                                            <select id="jellyemu-pref-scale" is="emby-select" class="emby-select-withcolor emby-select">
                                                <option value="fit">Fit to screen</option>
                                                <option value="native">Native resolution</option>
                                                <option value="2x">2×</option>
                                                <option value="3x">3×</option>
                                                <option value="4x">4×</option>
                                            </select>
                                            <div class="selectArrowContainer"><div style="visibility:hidden;display:none;">0</div><span class="selectArrow material-icons keyboard_arrow_down" aria-hidden="true"></span></div>
                                        </div>
                                        <div class="selectContainer">
                                            <label class="selectLabel" for="jellyemu-pref-rotation">Video Rotation</label>
                                            <select id="jellyemu-pref-rotation" is="emby-select" class="emby-select-withcolor emby-select">
                                                <option value="0">No rotation</option>
                                                <option value="1">90°</option>
                                                <option value="2">180°</option>
                                                <option value="3">270°</option>
                                            </select>
                                            <div class="selectArrowContainer"><div style="visibility:hidden;display:none;">0</div><span class="selectArrow material-icons keyboard_arrow_down" aria-hidden="true"></span></div>
                                        </div>
                                        <div class="selectContainer">
                                            <label class="selectLabel" for="jellyemu-pref-mute">Mute on launch</label>
                                            <select id="jellyemu-pref-mute" is="emby-select" class="emby-select-withcolor emby-select">
                                                <option value="false">No</option>
                                                <option value="true">Yes</option>
                                            </select>
                                            <div class="selectArrowContainer"><div style="visibility:hidden;display:none;">0</div><span class="selectArrow material-icons keyboard_arrow_down" aria-hidden="true"></span></div>
                                        </div>
                                    </div>

                                    <div class="verticalSection">
                                        <h2 class="sectionTitle">Controls</h2>
                                        <div class="selectContainer">
                                            <label class="selectLabel" for="jellyemu-pref-controller">Preferred controller</label>
                                            <select id="jellyemu-pref-controller" is="emby-select" class="emby-select-withcolor emby-select">
                                                <option value="auto">Auto-detect</option>
                                                <option value="keyboard">Keyboard</option>
                                                <option value="gamepad0">Gamepad 1</option>
                                                <option value="gamepad1">Gamepad 2</option>
                                            </select>
                                            <div class="selectArrowContainer"><div style="visibility:hidden;display:none;">0</div><span class="selectArrow material-icons keyboard_arrow_down" aria-hidden="true"></span></div>
                                        </div>
                                        <div class="selectContainer">
                                            <label class="selectLabel" for="jellyemu-pref-haptics">Haptic feedback (mobile)</label>
                                            <select id="jellyemu-pref-haptics" is="emby-select" class="emby-select-withcolor emby-select">
                                                <option value="true">On</option>
                                                <option value="false">Off</option>
                                            </select>
                                            <div class="selectArrowContainer"><div style="visibility:hidden;display:none;">0</div><span class="selectArrow material-icons keyboard_arrow_down" aria-hidden="true"></span></div>
                                        </div>

                                        <div style="margin-top:1.5em;">
                                            <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:0.75em;">
                                                <h3 style="margin:0;font-size:0.95em;color:#ccc;">Key Bindings</h3>
                                                <button type="button" id="jellyemu-controls-reset" style="font-size:0.78em;padding:4px 12px;background:rgba(255,255,255,0.07);border:1px solid rgba(255,255,255,0.15);border-radius:4px;color:#aaa;cursor:pointer;">
                                                    Reset to defaults
                                                </button>
                                            </div>
                                            <p style="font-size:0.8em;color:#777;margin:0 0 1em;">Click a button then press a key to remap it. Changes apply next time you launch a game.</p>
                                            <div id="jellyemu-bindings-grid" style="display:grid;grid-template-columns:1fr 1fr;gap:6px 16px;"></div>
                                        </div>
                                    </div>

                                    <div class="verticalSection">
                                        <h2 class="sectionTitle">Save States</h2>
                                        <div class="selectContainer">
                                            <label class="selectLabel" for="jellyemu-pref-slot">Active save slot</label>
                                            <select id="jellyemu-pref-slot" is="emby-select" class="emby-select-withcolor emby-select">
                                                <option value="1">Slot 1</option>
                                                <option value="2">Slot 2</option>
                                                <option value="3">Slot 3</option>
                                                <option value="4">Slot 4</option>
                                                <option value="5">Slot 5</option>
                                            </select>
                                            <div class="selectArrowContainer"><div style="visibility:hidden;display:none;">0</div><span class="selectArrow material-icons keyboard_arrow_down" aria-hidden="true"></span></div>
                                        </div>
                                        <div class="selectContainer">
                                            <label class="selectLabel" for="jellyemu-pref-autosave">Auto-save on exit</label>
                                            <select id="jellyemu-pref-autosave" is="emby-select" class="emby-select-withcolor emby-select">
                                                <option value="true">On</option>
                                                <option value="false">Off</option>
                                            </select>
                                            <div class="selectArrowContainer"><div style="visibility:hidden;display:none;">0</div><span class="selectArrow material-icons keyboard_arrow_down" aria-hidden="true"></span></div>
                                        </div>
                                    </div>

                                    <button id="jellyemu-prefs-save" is="emby-button" type="submit" class="raised button-submit block emby-button">
                                        <span>Save Settings</span>
                                    </button>
                                    <p id="jellyemu-prefs-status" style="margin-top:1em; text-align:center; display:none;"></p>
                                </form>
                            </div>`;

                        const sel = (id) => activePage.querySelector('#' + id);

                        // Load instantly from localStorage cache to prevent form flicker
                        const localPrefs = loadLocalPrefs();
                        sel('jellyemu-pref-shader').value      = localPrefs.shader      || '';
                        sel('jellyemu-pref-scale').value       = localPrefs.scale       || 'fit';
                        sel('jellyemu-pref-mute').value        = localPrefs.mute        || 'false';
                        sel('jellyemu-pref-controller').value  = localPrefs.controller  || 'auto';
                        sel('jellyemu-pref-haptics').value     = localPrefs.haptics     || 'true';
                        sel('jellyemu-pref-autosave').value    = localPrefs.autosave    || 'true';
                        sel('jellyemu-pref-rotation').value    = String(localPrefs.videoRotation ?? 0);

                        const userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : null;
                        
                        if (userId) {
                            // Fetch slot exclusively
                            fetch('/jellyemu/slot/' + userId)
                                .then(r => r.ok ? r.json() : null)
                                .then(data => {
                                    if (data && data.slot) {
                                        sel('jellyemu-pref-slot').value = String(data.slot);
                                    }
                                })
                                .catch(() => {});

                            // Fetch full preferences (which now includes shader & rotation)
                            fetch('/jellyemu/prefs/' + userId)
                                .then(r => r.ok ? r.json() : null)
                                .then(data => {
                                    if (data) {
                                        if (data.shader !== undefined) sel('jellyemu-pref-shader').value = data.shader;
                                        if (data.scale !== undefined) sel('jellyemu-pref-scale').value = data.scale;
                                        if (data.mute !== undefined) sel('jellyemu-pref-mute').value = data.mute;
                                        if (data.controller !== undefined) sel('jellyemu-pref-controller').value = data.controller;
                                        if (data.haptics !== undefined) sel('jellyemu-pref-haptics').value = data.haptics;
                                        if (data.autosave !== undefined) sel('jellyemu-pref-autosave').value = data.autosave;
                                        if (data.videoRotation !== undefined) sel('jellyemu-pref-rotation').value = String(data.videoRotation);
                                        if (data.controls) {
                                            loadBindingsFromJson(data.controls);
                                            renderBindingsGrid();
                                        }
                                        
                                        // Keep localStorage cache in sync with the server response
                                        saveLocalPrefs(data);
                                    }
                                })
                                .catch(() => {});
                        }

                        activePage.querySelector('form').addEventListener('submit', function(e) {
                            e.preventDefault();
                        });

                        // ── Key binding editor ────────────────────────────────────────────
                        // All 30 EJS button indices with human names and EJS defaults
                        const EJS_BINDINGS = [
                            { idx: 0,  name: 'B',               def: 'x'          },
                            { idx: 1,  name: 'Y',               def: 's'          },
                            { idx: 2,  name: 'Select',          def: 'v'          },
                            { idx: 3,  name: 'Start',           def: 'Enter'      },
                            { idx: 4,  name: 'D-Pad Up',        def: 'ArrowUp'    },
                            { idx: 5,  name: 'D-Pad Down',      def: 'ArrowDown'  },
                            { idx: 6,  name: 'D-Pad Left',      def: 'ArrowLeft'  },
                            { idx: 7,  name: 'D-Pad Right',     def: 'ArrowRight' },
                            { idx: 8,  name: 'A',               def: 'z'          },
                            { idx: 9,  name: 'X',               def: 'a'          },
                            { idx: 10, name: 'L',               def: 'q'          },
                            { idx: 11, name: 'R',               def: 'e'          },
                            { idx: 12, name: 'L2',              def: 'Tab'        },
                            { idx: 13, name: 'R2',              def: 'r'          },
                            { idx: 14, name: 'L3',              def: ''           },
                            { idx: 15, name: 'R3',              def: ''           },
                            { idx: 16, name: 'L Stick Right',   def: 'h'          },
                            { idx: 17, name: 'L Stick Left',    def: 'f'          },
                            { idx: 18, name: 'L Stick Down',    def: 'g'          },
                            { idx: 19, name: 'L Stick Up',      def: 't'          },
                            { idx: 20, name: 'R Stick Right',   def: 'l'          },
                            { idx: 21, name: 'R Stick Left',    def: 'j'          },
                            { idx: 22, name: 'R Stick Down',    def: 'k'          },
                            { idx: 23, name: 'R Stick Up',      def: 'i'          },
                            { idx: 24, name: 'Quick Save',      def: '1'          },
                            { idx: 25, name: 'Quick Load',      def: '2'          },
                            { idx: 26, name: 'Change Slot',     def: '3'          },
                            { idx: 27, name: 'Fast Forward',    def: '+'          },
                            { idx: 28, name: 'Rewind',          def: ' '          },
                            { idx: 29, name: 'Slow Motion',     def: '-'          },
                        ];

                        // currentBindings: index → key string (event.key)
                        var currentBindings = {};
                        EJS_BINDINGS.forEach(function(b) { currentBindings[b.idx] = b.def; });

                        function loadBindingsFromJson(json) {
                            if (!json) return;
                            try {
                                var saved = JSON.parse(json);
                                Object.keys(saved).forEach(function(k) {
                                    var entry = saved[k];
                                    if (entry && entry.value !== undefined) {
                                        currentBindings[parseInt(k, 10)] = entry.value;
                                    }
                                });
                            } catch(e) {}
                        }

                        function bindingsToJson() {
                            var out = {};
                            EJS_BINDINGS.forEach(function(b) {
                                out[b.idx] = { value: currentBindings[b.idx] || '' };
                            });
                            return JSON.stringify(out);
                        }

                        function renderBindingLabel(key) {
                            if (!key || key === '') return '—';
                            var display = {
                                ' ': 'Space', 'ArrowUp': '↑', 'ArrowDown': '↓',
                                'ArrowLeft': '←', 'ArrowRight': '→',
                                'Enter': 'Enter', 'Tab': 'Tab', 'Escape': 'Esc',
                                'Backspace': 'Bksp', 'Delete': 'Del',
                                '+': '+', '-': '-',
                            };
                            return display[key] || key.toUpperCase();
                        }

                        var listeningIdx = null;

                        function renderBindingsGrid() {
                            var grid = activePage.querySelector('#jellyemu-bindings-grid');
                            if (!grid) return;
                            grid.innerHTML = '';
                            EJS_BINDINGS.forEach(function(b) {
                                var row = document.createElement('div');
                                row.style.cssText = 'display:flex;align-items:center;justify-content:space-between;padding:4px 0;border-bottom:1px solid rgba(255,255,255,0.05);';

                                var label = document.createElement('span');
                                label.textContent = b.name;
                                label.style.cssText = 'font-size:0.85em;color:#ccc;flex:1;';

                                var btn = document.createElement('button');
                                btn.type = 'button';
                                btn.dataset.bindingIdx = b.idx;
                                btn.textContent = renderBindingLabel(currentBindings[b.idx]);
                                btn.style.cssText = 'min-width:72px;padding:4px 10px;background:rgba(255,255,255,0.07);' +
                                    'border:1px solid rgba(255,255,255,0.18);border-radius:4px;' +
                                    'color:#e0e0e0;font-size:0.82em;cursor:pointer;text-align:center;';

                                btn.addEventListener('click', function() {
                                    // Deselect any previously listening button
                                    activePage.querySelectorAll('[data-binding-idx]').forEach(function(b2) {
                                        b2.style.background = 'rgba(255,255,255,0.07)';
                                        b2.style.borderColor = 'rgba(255,255,255,0.18)';
                                        b2.style.color = '#e0e0e0';
                                        if (parseInt(b2.dataset.bindingIdx, 10) === listeningIdx) {
                                            b2.textContent = renderBindingLabel(currentBindings[listeningIdx]);
                                        }
                                    });
                                    listeningIdx = b.idx;
                                    btn.textContent = 'Press a key…';
                                    btn.style.background = 'rgba(0,164,220,0.2)';
                                    btn.style.borderColor = '#00a4dc';
                                    btn.style.color = '#00a4dc';
                                });

                                row.appendChild(label);
                                row.appendChild(btn);
                                grid.appendChild(row);
                            });
                        }

                        // Global keydown listener for capturing bindings
                        document.addEventListener('keydown', function(e) {
                            if (listeningIdx === null) return;
                            // Don't capture modifier-only presses
                            if (['Control','Shift','Alt','Meta'].includes(e.key)) return;
                            e.preventDefault();
                            e.stopPropagation();
                            currentBindings[listeningIdx] = e.key;
                            var btn = activePage.querySelector('[data-binding-idx="' + listeningIdx + '"]');
                            if (btn) {
                                btn.textContent = renderBindingLabel(e.key);
                                btn.style.background = 'rgba(82,181,75,0.15)';
                                btn.style.borderColor = 'rgba(82,181,75,0.5)';
                                btn.style.color = '#52B54B';
                            }
                            listeningIdx = null;
                        }, true);

                        activePage.querySelector('#jellyemu-controls-reset').addEventListener('click', function() {
                            EJS_BINDINGS.forEach(function(b) { currentBindings[b.idx] = b.def; });
                            renderBindingsGrid();
                        });

                        // Render grid immediately with defaults, then overwrite with saved bindings
                        renderBindingsGrid();

                        // ── Save button ───────────────────────────────────────────────────────
                        sel('jellyemu-prefs-save').addEventListener('click', function() {
                            // Cancel any active listen
                            listeningIdx = null;

                            const prefsPayload = {
                                shader:        sel('jellyemu-pref-shader').value,
                                scale:         sel('jellyemu-pref-scale').value,
                                mute:          sel('jellyemu-pref-mute').value,
                                controller:    sel('jellyemu-pref-controller').value,
                                haptics:       sel('jellyemu-pref-haptics').value,
                                autosave:      sel('jellyemu-pref-autosave').value,
                                videoRotation: parseInt(sel('jellyemu-pref-rotation').value, 10) || 0,
                                controls:      bindingsToJson(),
                            };

                            // Always save locally so the emulator iframe has instant fallback access
                            saveLocalPrefs(prefsPayload);

                            const slotVal     = parseInt(sel('jellyemu-pref-slot').value, 10) || 1;
                            const slotUserId  = window.ApiClient ? window.ApiClient.getCurrentUserId() : null;

                            if (slotUserId) {
                                const saveSlotReq = fetch('/jellyemu/slot/' + slotUserId + '?slot=' + slotVal, { method: 'POST' });
                                const savePrefsReq = fetch('/jellyemu/prefs/' + slotUserId, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify(prefsPayload)
                                });

                                Promise.all([saveSlotReq, savePrefsReq]).then(() => {
                                    const status = sel('jellyemu-prefs-status');
                                    status.textContent = 'Settings saved.';
                                    status.style.color = '#52B54B';
                                    status.style.display = 'block';
                                    setTimeout(() => status.style.display = 'none', 3000);
                                }).catch(() => {
                                    const status = sel('jellyemu-prefs-status');
                                    status.textContent = 'Failed to save settings to server.';
                                    status.style.color = '#FF4444';
                                    status.style.display = 'block';
                                    setTimeout(() => status.style.display = 'none', 3000);
                                });
                            } else {
                                const status = sel('jellyemu-prefs-status');
                                status.textContent = 'Settings saved locally.';
                                status.style.color = '#52B54B';
                                status.style.display = 'block';
                                setTimeout(() => status.style.display = 'none', 3000);
                            }
                        });
                    }

                    function hijackJellyEmuSavesBrowser() {
                        const activePage = document.querySelector('.page:not(.hide)');
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
                                    aspect-ratio: 2/3;
                                    object-fit: cover;
                                    background: rgba(0,0,0,0.4);
                                    display: block;
                                    flex-shrink: 0;
                                }
                                .je-save-art-placeholder {
                                    width: 100%;
                                    aspect-ratio: 2/3;
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

                                card.innerHTML = artUrl
                                    ? `<img class="je-save-art" src="${artUrl}" alt="" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">
                                       <div class="je-save-art-placeholder" style="display:none"><span class="material-icons">sports_esports</span></div>`
                                    : `<div class="je-save-art-placeholder"><span class="material-icons">sports_esports</span></div>`;

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

                    function checkPrefsRoute() {
                        const hash = window.location.hash;

                        if (hash.startsWith(JELLYEMU_SAVES_HASH)) {
                            hijackJellyEmuSavesBrowser();
                        } else if (hash.startsWith(JELLYEMU_PREFS_HASH)) {
                            hijackJellyEmuPrefsPage();
                        }
                    }

                    window.addEventListener('hashchange', checkPrefsRoute);
                    checkPrefsRoute();

                    tick();
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