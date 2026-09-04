(function() {
    window.JellyEmu = window.JellyEmu || {};
    const JE = window.JellyEmu;

    JE.getVisibleDetailPage = function() {
        const pages = document.querySelectorAll('.itemDetailPage');
        for (const p of pages) {
            if (!p.classList.contains('hide')) return p;
        }
        return null;
    };

    JE.injectMiscInfo = function(page) {
        page = page || JE.getVisibleDetailPage();
        if (!page) return;
        if (!JE.cachedTags.length) return;
        const miscBar = page.querySelector('.itemMiscInfo-primary');
        if (!miscBar) return;
        if (miscBar.querySelector('.jellyemu-info-wrap')) return;

        const wrap = document.createElement('div');
        wrap.className = 'jellyemu-info-wrap';
        wrap.style.cssText = 'display:contents';
        miscBar.appendChild(wrap);

        JE.perf.mark('inject-misc-start');

        const systemTags = JE.cachedTags.filter(t => t !== 'Game' && t !== 'JellyEmu' && t !== 'Unsupported' && !JE.knownRegions.has(t) && !JE.isDiscTag(t));
        const regionTags = JE.cachedTags.filter(t => JE.knownRegions.has(t));
        const discTags   = JE.cachedTags.filter(t => JE.isDiscTag(t));
        const allTags    = [...systemTags, ...regionTags, ...discTags];

        const tagFrag = document.createDocumentFragment();
        allTags.forEach(tag => {
            const div = document.createElement('div');
            div.className = 'mediaInfoItem jellyemu-misc-item';
            div.textContent = tag;
            tagFrag.appendChild(div);
        });
        wrap.appendChild(tagFrag);

        // Time to Beat
        const ttbRaw = JE.cachedProviderIds.IgdbTTB;
        if (ttbRaw && !wrap.querySelector('.jellyemu-ttb-pill')) {
            const data = ttbRaw.split(',');
            const map = {};
            data.forEach(d => map[d[0]] = d.substring(1));
            
            const pill = document.createElement('div');
            pill.className = 'mediaInfoItem jellyemu-ttb-pill';
            pill.style.cssText = 'display:inline-flex;align-items:center;gap:4px;cursor:default;';
            
            let title = 'Time to Beat: ';
            let labels = [];
            if (map.M) { labels.push(map.M + 'h'); title += 'Main: ' + map.M + 'h '; }
            if (map.H) { labels.push(map.H + 'h'); title += 'Main+Extras: ' + map.H + 'h '; }
            if (map.C) { labels.push(map.C + 'h'); title += 'Completionist: ' + map.C + 'h '; }
            
            pill.title = title.trim();
            pill.innerHTML = '<span class="material-icons" style="font-size:13px;vertical-align:middle;color:rgba(255,255,255,0.7);">hourglass_full</span>' + 
                labels.join(' \u2022 ');
            
            wrap.appendChild(pill);
        }

        const userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : null;
        const itemId = JE.currentItemId;

        const saveSlotsPromise = (userId && itemId)
            ? fetch('/jellyemu/save-slots/' + itemId + '/' + userId)
                .then(r => r.ok ? r.json() : [])
                .catch(() => [])
            : Promise.resolve([]);

        if (userId && itemId && !wrap.querySelector('.jellyemu-slot-pill')) {
            saveSlotsPromise.then(slots => {
                if (!Array.isArray(slots) || !slots.length) return;
                slots.forEach(s => {
                    if (wrap.querySelector('.jellyemu-slot-pill[data-slot="' + s.slot + '"]')) return;
                    const pill = document.createElement('div');
                    pill.className = 'mediaInfoItem jellyemu-slot-pill';
                    pill.setAttribute('data-slot', s.slot);
                    pill.style.cssText = 'display:inline-flex;align-items:center;gap:4px;cursor:pointer;';
                    let title = 'Save in Slot ' + s.slot;
                    if (s.lastModified) {
                        try {
                            const d = new Date(s.lastModified);
                            title += ' (' + d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) + ' ' +
                                d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' }) + ')';
                        } catch {}
                    }
                    title += ' \u2014 Click to play';
                    pill.title = title;
                    pill.innerHTML = '<span class="material-icons" style="font-size:13px;vertical-align:middle;color:#00a4dc;">save</span>' +
                        'Slot ' + s.slot;
                    pill.addEventListener('click', (e) => {
                        e.preventDefault();
                        e.stopPropagation();
                        JE.launchEmulator(itemId, s.slot);
                    });
                    wrap.appendChild(pill);
                });
            }).catch(() => {});
        }

        if (userId && itemId && !wrap.querySelector('.jellyemu-playtime-pill')) {
            const token = window.ApiClient ? window.ApiClient.accessToken() : '';
            fetch('/jellyemu/playtime/' + itemId + '/' + userId, {
                headers: {
                    'Authorization': 'MediaBrowser Token="' + token + '"',
                    'Accept': 'application/json'
                }
            })
                .then(r => r.ok ? r.json() : null)
                .then(data => {
                    if (!data || data.seconds === undefined) return;
                    const pill = document.createElement('div');
                    pill.className = 'mediaInfoItem jellyemu-playtime-pill';
                    pill.style.cssText = 'display:inline-flex;align-items:center;gap:4px;cursor:default;';
                    pill.title = data.seconds + ' seconds played';
                    const h = Math.floor(data.seconds / 3600);
                    const min = Math.floor((data.seconds % 3600) / 60);
                    const label = data.seconds === 0 ? '0m' : h > 0 ? h + 'h ' + min + 'm' : min > 0 ? min + 'm' : '<1m';
                    pill.innerHTML = '<span class="material-icons" style="font-size:13px;vertical-align:middle;">schedule</span>' + label + ' played';
                    wrap.appendChild(pill);
                })
                .catch(() => {});
        }

        if (userId && itemId && !wrap.querySelector('.jellyemu-romm-sync-pill')) {
            saveSlotsPromise
                .then(slots => {
                    const activeSlot = (slots && slots.length > 0) ? slots[0].slot : 1;
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

        if (userId && itemId && !wrap.querySelector('.jellyemu-ra-pill')) {
            const token = window.ApiClient ? window.ApiClient.accessToken() : '';
            fetch('/jellyemu/retroachievements/progress/' + itemId + '/' + userId, {
                headers: { 'Authorization': 'MediaBrowser Token="' + token + '"' }
            })
                .then(r => {
                    if (r.status === 401) return { error: 'unauthorized' };
                    return r.ok ? r.json() : null;
                })
                .then(data => {
                    if (!data) return;
                    const pill = document.createElement('div');
                    pill.className = 'mediaInfoItem jellyemu-ra-pill';
                    pill.style.cssText = 'display:inline-flex;align-items:center;gap:4px;cursor:pointer;';
                    
                    if (data.error === 'unauthorized') {
                        pill.title = 'Click to sign in to RetroAchievements';
                        pill.innerHTML = '<span class="material-icons" style="font-size:13px;vertical-align:middle;color:rgba(255,255,255,0.4);">emoji_events</span>Sign In';
                        pill.onclick = (e) => {
                            e.preventDefault();
                            e.stopPropagation();
                            window.location.hash = JE.JELLYEMU_SETTINGS_HASH;
                        };
                    } else {
                        pill.title = 'RetroAchievements: ' + data.numUnlocked + ' / ' + data.numTotal + ' unlocked';
                        pill.innerHTML = '<span class="material-icons" style="font-size:13px;vertical-align:middle;color:#f0c040;">emoji_events</span>' + 
                            data.numUnlocked + '/' + data.numTotal;
                        pill.onclick = () => window.open(data.raGameUrl, '_blank');
                        
                        if (data.numTotal > 0) {
                            const bar = document.createElement('div');
                            bar.style.cssText = 'width:32px;height:4px;background:rgba(255,255,255,0.1);border-radius:2px;overflow:hidden;margin-left:2px;';
                            bar.innerHTML = '<div style="width:' + data.progressPercent + '%;height:100%;background:#f0c040;"></div>';
                            pill.appendChild(bar);
                        }
                    }
                    wrap.appendChild(pill);
                })
                .catch(() => {});
        }

        if (!wrap.querySelector('.jellyemu-platform-status-pill')) {
            const unknownTag  = JE.cachedTags.includes('Unknown');
            const unsupported = !unknownTag && JE.cachedTags.some(t => JE.ejsUnsupportedPlatforms.has(t));
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

        JE.perf.mark('inject-misc-end');
        JE.perf.measure('inject-misc', 'inject-misc-start', 'inject-misc-end');
    };

    JE.injectPlayButton = function(page) {
        page = page || JE.getVisibleDetailPage();
        if (!page) return;
        const detailButtonsContainer = page.querySelector('.mainDetailButtons');
        if (!detailButtonsContainer) return;
        if (detailButtonsContainer.querySelector('#jellyemu-play-btn')) return;

        JE.perf.mark('inject-play-start');

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
            if (JE.currentItemId) JE.launchEmulator(JE.currentItemId);
        });

        detailButtonsContainer.insertBefore(btn, detailButtonsContainer.firstChild);

        if (JE.vantageEnabled && JE.currentItemId && !detailButtonsContainer.querySelector('#jellyemu-vantage-btn')) {
            const vBtn = document.createElement('button');
            vBtn.type      = 'button';
            vBtn.id        = 'jellyemu-vantage-btn';
            vBtn.className = 'jellyemu-play-btn-detail';
            vBtn.title     = 'Open in Vantage';
            vBtn.style.marginLeft = '.5em';
            vBtn.innerHTML = '<div class="detailButton-content"><span class="material-icons detailButton-icon" aria-hidden="true">open_in_new</span></div>';
            vBtn.addEventListener('click', function(e) {
                e.preventDefault();
                e.stopPropagation();
                window.location.href = 'vantage://launch?itemId=' + JE.currentItemId;
            });
            detailButtonsContainer.insertBefore(vBtn, btn.nextSibling);
        }

        JE.perf.mark('inject-play-end');
        JE.perf.measure('inject-play', 'inject-play-start', 'inject-play-end');
    };

    JE.injectAll = function(page) {
        if (!JE.currentItemIsGame) return;
        page = page || JE.getVisibleDetailPage();
        if (page) page.classList.add('jellyemu-game-page');
        if (JE.isPlayable(JE.cachedTags)) JE.injectPlayButton(page);
        JE.injectMiscInfo(page);
    };

    JE.processItemDetails = function(id, page) {
        if (!window.ApiClient) return;
        JE.perf.mark('details-start:' + id);
        JE.currentItemIsGame = false;
        JE.cachedTags        = [];
        JE.cachedProviderIds = {};

        const visiblePage = page || JE.getVisibleDetailPage();
        const allDetailPages = document.querySelectorAll('.itemDetailPage');
        allDetailPages.forEach(p => p.classList.remove('jellyemu-game-page'));

        const cachedCard = document.querySelector('.card[data-id="' + id + '"]');
        let isSpeculativeGame = false;
        if (cachedCard) {
            const tagsAttr = cachedCard.getAttribute('data-jellyemu-tags');
            const tags = tagsAttr ? tagsAttr.split(',') : [];
            if (tags.includes('JellyEmu') || cachedCard.getAttribute('data-jellyemu-game') === '1') {
                isSpeculativeGame = true;
            }
        }

        if (isSpeculativeGame && visiblePage) {
            visiblePage.classList.add('jellyemu-game-page');
        }

        const cachedCardWithTags = document.querySelector('.card[data-id="' + id + '"][data-jellyemu-tags]');
        if (cachedCardWithTags) {
            const tags = cachedCardWithTags.getAttribute('data-jellyemu-tags').split(',');
            if (tags.includes('JellyEmu')) {
                JE.perf.mark('details-fast-path:' + id);
                JE.currentItemIsGame = true;
                JE.cachedTags        = tags;
                JE.cachedProviderIds = {};
                JE.injectAll(visiblePage);
            }
        }

        JE.perf.mark('details-getItem-start:' + id);
        window.ApiClient.getItem(window.ApiClient.getCurrentUserId(), id).then(item => {
            JE.perf.mark('details-getItem-end:' + id);
            JE.perf.measure('details-getItem:' + id, 'details-getItem-start:' + id, 'details-getItem-end:' + id);
            if (item && item.Tags && item.Tags.includes('JellyEmu')) {
                JE.currentItemIsGame = true;
                JE.cachedTags        = item.Tags;
                JE.cachedProviderIds = item.ProviderIds || {};
                if (visiblePage) visiblePage.classList.add('jellyemu-game-page');
                var tryInject = function() {
                    if (JE.currentItemId !== id) return true;
                    var p = visiblePage || JE.getVisibleDetailPage();
                    if (!p) return false;
                    if (JE.isPlayable(JE.cachedTags)) JE.injectPlayButton(p);
                    var bar = p.querySelector('.itemMiscInfo-primary');
                    if (bar) { JE.injectMiscInfo(p); return true; }
                    return false;
                };

                if (!tryInject()) {
                    var detailTarget = visiblePage || JE.getVisibleDetailPage() || document.body;
                    var itemObserver = new MutationObserver(function(mutations, obs) {
                        if (tryInject()) {
                            obs.disconnect();
                        }
                    });
                    itemObserver.observe(detailTarget, { childList: true, subtree: true });
                    setTimeout(function() { itemObserver.disconnect(); }, 3000);
                }
            } else {
                JE.currentItemIsGame = false;
                JE.cachedTags        = [];
                JE.cachedProviderIds = {};
                if (visiblePage) visiblePage.classList.remove('jellyemu-game-page');
            }
            JE.perf.mark('details-end:' + id);
            JE.perf.measure('details-total:' + id, 'details-start:' + id, 'details-end:' + id);
        });
    };

    const detailObserver = new MutationObserver((mutations) => {
        let checkDetails = false;
        let cachedDetailPage = null;
        function getDetailPage() {
            if (cachedDetailPage === null) cachedDetailPage = JE.getVisibleDetailPage() || undefined;
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

        if (checkDetails) JE.injectAll();
    });

    let _detailObserverTarget = null;
    JE._detailObserverConnect = function(page) {
        if (_detailObserverTarget === page) return;
        if (_detailObserverTarget) detailObserver.disconnect();
        _detailObserverTarget = page;
        detailObserver.observe(page, { childList: true, subtree: true });
    };

    JE._detailObserverDisconnect = function() {
        if (!_detailObserverTarget) return;
        detailObserver.disconnect();
        _detailObserverTarget = null;
    };
})();
