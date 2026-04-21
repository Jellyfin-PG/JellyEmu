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
                  [data-collectiontype="games"] .cardImageContainer {
                      padding-bottom: 150% !important;
                      background-size: cover;
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
                        "pbp","cue","iso","chd",
                        "a26","a78","lnx","jag","j64",
                        "ws","wsc","pce",
                        "col","cv","ngp","ngc"
                    ]);

                    const knownRegions = new Set([
                        "USA","Europe","Japan","World","Australia","Brazil","Canada","China",
                        "France","Germany","Italy","Korea","Netherlands","Russia","Spain","Sweden",
                        "Asia","Scandinavia","Unlicensed","Prototype","Demo","Sample"
                    ]);

                    function launchEmulator(itemId) {
                        console.log('[JellyEmu] Launching emulator for item:', itemId);
                        const iframe = document.createElement('iframe');
                        iframe.id = 'jellyemu-iframe';
                        iframe.style = 'width:100vw; height:100vh; border:none; position:fixed; top:0; left:0; z-index:99999; background:#000;';
                        const userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : '';
                        iframe.src = '/jellyemu/play/' + itemId + (userId ? '?userId=' + userId : '');
                        document.body.appendChild(iframe);
                        document.body.style.overflow = 'hidden';
                    }

                    function dismissActionSheet(sheetRoot) {
                        const dialog = sheetRoot.closest('.dialog') || sheetRoot.closest('[data-history]') || sheetRoot.parentElement;
                        if (dialog) dialog.remove();
                    }

                    window.addEventListener('message', function(e) {
                        if (e.data === 'close-jellyemu') {
                            const iframe = document.getElementById('jellyemu-iframe');
                            if (iframe) document.body.removeChild(iframe);
                            document.body.style.overflow = '';
                        }
                    });

                    document.body.addEventListener('click', function(e) {
                        const menuBtn = e.target.closest('button[data-action="menu"]');
                        if (!menuBtn) return;
                        const card = menuBtn.closest('.card[data-collectiontype="games"]');
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

                        const systemTags = cachedTags.filter(t => t !== 'Game' && !knownRegions.has(t));
                        const regionTags = cachedTags.filter(t => knownRegions.has(t));
                        const allTags    = [...systemTags, ...regionTags];

                        allTags.forEach(tag => {
                            const div = document.createElement('div');
                            div.className = 'mediaInfoItem jellyemu-misc-item';
                            div.textContent = tag;
                            miscBar.appendChild(div);
                        });
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

                                    const selectEl = node.id === 'selectCollectionType' ? node : (node.querySelector ? node.querySelector('#selectCollectionType') : null);
                                    if (selectEl && !selectEl.querySelector('option[data-jellyemu="true"]')) {
                                        const opt = document.createElement('option');
                                        opt.value = 'books';
                                        opt.innerText = 'Games';
                                        opt.setAttribute('data-jellyemu', 'true');
                                        selectEl.appendChild(opt);
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

                                    cardsToProcess.forEach(card => {
                                        const path = card.getAttribute('data-path');
                                        let isGameCard = card.getAttribute('data-collectiontype') === 'games' ||
                                                           (card.querySelector('.cardText') && (card.querySelector('.cardText').innerText.includes('Games') || card.querySelector('.cardText').innerText.includes('Emulators')));

                                        if (path) {
                                            const extMatch = path.match(/\.([a-zA-Z0-9]+)$/);
                                            if (extMatch && romExtensions.has(extMatch[1].toLowerCase())) {
                                                isGameCard = true;
                                            }
                                        }

                                        if (isGameCard) {
                                            card.setAttribute('data-collectiontype', 'games');
                                            const iconSpan = card.querySelector('.cardImageIcon');
                                            if (iconSpan) iconSpan.innerHTML = 'sports_esports';

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
                                    });

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

                    const JELLYEMU_PREFS_HASH = '#/jellyemu-userprefs';

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

                        const targetSection = page.querySelector('.verticalSection.verticalSection-extrabottompadding');
                        if (targetSection) {
                            targetSection.appendChild(anchor);
                        } else {
                            const readOnly = page.querySelector('.readOnlyContent');
                            if (readOnly) readOnly.appendChild(anchor);
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
                        function loadPrefs() { try { return JSON.parse(localStorage.getItem(PREFS_KEY) || '{}'); } catch { return {}; } }
                        function savePrefs(prefs) { localStorage.setItem(PREFS_KEY, JSON.stringify(prefs)); }
                        const prefs = loadPrefs();

                        activePage.innerHTML = `
                            <div class="settingsContainer padded-left padded-right padded-bottom-page">
                                <form style="margin:0 auto;">
                                    <div class="verticalSection">
                                        <h2 class="sectionTitle">Emulator</h2>
                                        <div class="selectContainer">
                                            <label class="selectLabel" for="jellyemu-pref-shader">Display Shader</label>
                                            <select id="jellyemu-pref-shader" is="emby-select" class="emby-select-withcolor emby-select">
                                                <option value="">None</option>
                                                <option value="crt-easymode">CRT Easy Mode</option>
                                                <option value="crt-royale">CRT Royale</option>
                                                <option value="lcd-grid">LCD Grid</option>
                                                <option value="scanlines">Scanlines</option>
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

                        sel('jellyemu-pref-shader').value      = prefs.shader      || '';
                        sel('jellyemu-pref-scale').value       = prefs.scale       || 'fit';
                        sel('jellyemu-pref-mute').value        = prefs.mute        || 'false';
                        sel('jellyemu-pref-controller').value  = prefs.controller  || 'auto';
                        sel('jellyemu-pref-haptics').value     = prefs.haptics     || 'true';
                        sel('jellyemu-pref-autosave').value    = prefs.autosave    || 'true';

                        const userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : null;
                        if (userId) {
                            fetch('/jellyemu/slot/' + userId)
                                .then(r => r.ok ? r.json() : null)
                                .then(data => { if (data) sel('jellyemu-pref-slot').value = String(data.slot); })
                                .catch(() => {});
                        }

                        activePage.querySelector('form').addEventListener('submit', function(e) {
                            e.preventDefault();
                        });

                        sel('jellyemu-prefs-save').addEventListener('click', function() {
                            savePrefs({
                                shader:     sel('jellyemu-pref-shader').value,
                                scale:      sel('jellyemu-pref-scale').value,
                                mute:       sel('jellyemu-pref-mute').value,
                                controller: sel('jellyemu-pref-controller').value,
                                haptics:    sel('jellyemu-pref-haptics').value,
                                autosave:   sel('jellyemu-pref-autosave').value,
                            });

                            const slotVal = parseInt(sel('jellyemu-pref-slot').value, 10) || 1;
                            const slotUserId = window.ApiClient ? window.ApiClient.getCurrentUserId() : null;
                            const slotSave = slotUserId
                                ? fetch('/jellyemu/slot/' + slotUserId + '?slot=' + slotVal, { method: 'POST' })
                                : Promise.resolve();

                            slotSave.then(() => {
                                const status = sel('jellyemu-prefs-status');
                                status.textContent = 'Settings saved.';
                                status.style.color = '#52B54B';
                                status.style.display = 'block';
                                setTimeout(() => status.style.display = 'none', 3000);
                            }).catch(() => {
                                const status = sel('jellyemu-prefs-status');
                                status.textContent = 'Failed to save slot setting.';
                                status.style.color = '#FF4444';
                                status.style.display = 'block';
                                setTimeout(() => status.style.display = 'none', 3000);
                            });
                        });
                    }

                    function checkPrefsRoute() {
                        const hash = window.location.hash;
                        const prefsPage = document.getElementById('jellyemu-prefs-page');

                        if (hash.startsWith(JELLYEMU_PREFS_HASH)) {
                            renderJellyEmuPrefsPage();
                        } else if (prefsPage) {
                            prefsPage.style.display = 'none';
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
