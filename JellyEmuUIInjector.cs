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
                      color: #fff !important;
                      background-color: #00a4dc !important;
                      border-radius: 4px;
                      padding: 0.5em 1em;
                      margin-right: .5em;
                  }
                </style>
                <script data-jellyemu-mods="1">
                (function() {
                    if (window.__jellyEmuLoaded) return;
                    window.__jellyEmuLoaded = true;
                    console.log('[JellyEmu] UI injection successful.');

                    let currentItemId = null;
                    let currentItemIsGame = false;

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

                    window.addEventListener('message', function(e) {
                        if (e.data === 'close-jellyemu') {
                            const iframe = document.getElementById('jellyemu-iframe');
                            if (iframe) document.body.removeChild(iframe);
                            document.body.style.overflow = '';
                        }
                    });

                    function mutateDetailsButtons() {
                        if (!currentItemIsGame) return;

                        const detailButtonsContainer = document.querySelector('.mainDetailButtons');
                        if (!detailButtonsContainer) return;

                        if (detailButtonsContainer.querySelector('#jellyemu-play-btn')) return;

                        detailButtonsContainer.querySelectorAll('button[data-action="resume"], button[data-action="play"], .btnPlay')
                            .forEach(btn => btn.remove());

                        const sterileBtn = document.createElement('button');
                        sterileBtn.type = 'button';
                        sterileBtn.id = 'jellyemu-play-btn';
                        sterileBtn.className = 'button-flat detailButton';
                        sterileBtn.title = 'Play Game';
                        sterileBtn.innerHTML = '<div class="detailButton-content"><span class="material-icons detailButton-icon" aria-hidden="true">sports_esports</span></div>';

                        sterileBtn.addEventListener('click', function(e) {
                            e.preventDefault();
                            e.stopPropagation();
                            e.stopImmediatePropagation();
                            const match = window.location.hash.match(/id=([a-zA-Z0-9]+)/);
                            if (match) launchEmulator(match[1]);
                        });

                        detailButtonsContainer.insertBefore(sterileBtn, detailButtonsContainer.firstChild);
                    }

                    function processItemDetails(id) {
                        if (!window.ApiClient) return;
                        currentItemIsGame = false;
                        
                        window.ApiClient.getItem(window.ApiClient.getCurrentUserId(), id).then(item => {
                            if (item && item.Tags && item.Tags.includes('Game')) {
                                currentItemIsGame = true;
                                mutateDetailsButtons();
                            }
                        });
                    }

                    const observer = new MutationObserver((mutations) => {
                        let checkDetails = false;

                        mutations.forEach((mutation) => {
                            if (!mutation.addedNodes) return;
                            
                            mutation.addedNodes.forEach((node) => {
                                if (node.nodeType === 1) {
                                    
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
                                        let nestedCards = node.querySelectorAll('.card');
                                        nestedCards.forEach(c => cardsToProcess.push(c));
                                    }
                                    
                                    if (cardsToProcess.length === 0 && node.closest) {
                                        let parentCard = node.closest('.card');
                                        if (parentCard) cardsToProcess.push(parentCard);
                                    }

                                    cardsToProcess.forEach(card => {
                                        let path = card.getAttribute('data-path');
                                        let isGameCard = card.getAttribute('data-collectiontype') === 'games' || 
                                                           (card.querySelector('.cardText') && (card.querySelector('.cardText').innerText.includes('Games') || card.querySelector('.cardText').innerText.includes('Emulators')));
                                        
                                        if (path) {
                                            let extMatch = path.match(/\.([a-zA-Z0-9]+)$/);
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
                                                let sterileBtn = document.createElement('button');
                                                sterileBtn.type = 'button';
                                                sterileBtn.className = 'cardOverlayButton cardOverlayButton-hover cardOverlayFab-primary'; 
                                                sterileBtn.title = 'Play Game';
                                                sterileBtn.innerHTML = '<span class="material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover sports_esports" aria-hidden="true"></span>';
                                                
                                                sterileBtn.addEventListener('click', function(e) {
                                                    e.preventDefault();
                                                    e.stopPropagation();
                                                    e.stopImmediatePropagation();
                                                    launchEmulator(card.getAttribute('data-id'));
                                                });

                                                playBtn.replaceWith(sterileBtn);
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

                        const detailsPage = document.querySelector('.itemDetailPage');
                        if (detailsPage) {
                            const match = window.location.hash.match(/id=([a-zA-Z0-9]+)/);
                            if (match) {
                                const id = match[1];
                                if (currentItemId !== id) {
                                    currentItemId = id;
                                    processItemDetails(id);
                                }
                            }
                        } else {
                            currentItemId = null;
                            currentItemIsGame = false;
                        }
                    });

                    observer.observe(document.body, { childList: true, subtree: true });
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