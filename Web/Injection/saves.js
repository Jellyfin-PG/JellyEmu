(function() {
    window.JellyEmu = window.JellyEmu || {};
    const JE = window.JellyEmu;

    JE.hijackJellyEmuSavesBrowser = function() {
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
        const token = window.ApiClient ? window.ApiClient.accessToken() : '';

        activePage.innerHTML = `
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

                const placeholder = document.createElement('div');
                placeholder.className = 'je-save-art-placeholder';
                placeholder.innerHTML = '<span class="material-icons">sports_esports</span>';
                artWrap.appendChild(placeholder);

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
                        <button class="je-save-btn je-save-btn-delete">
                            <span class="material-icons">delete</span>
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
                    JE.launchEmulator(s.itemId, s.slot);
                });

                body.querySelector('.je-save-btn-delete').addEventListener('click', async () => {
                    if (confirm(`Are you sure you want to delete save slot ${s.slot}?`)) {
                        JE.deleteSave(s.itemId, s.slot);
                        await JE.delay(100);
                        reloadGrid();
                    }
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

        function reloadGrid() {
            fetch('/jellyemu/saves/' + userId, {
                headers: { 'Authorization': 'MediaBrowser Token="' + token + '"' }
            })
            .then(r => r.ok ? r.json() : [])
            .then(saves => {
                allSaves = saves;

                const platforms = [...new Set(saves.map(s => s.platform).filter(Boolean))].sort();
                const platformSelect = activePage.querySelector('#je-filter-platform');
                if (platformSelect) {
                    platformSelect.innerHTML = '<option value="">All platforms</option>';
                    platforms.forEach(p => {
                        const opt = document.createElement('option');
                        opt.value = p;
                        opt.textContent = p;
                        platformSelect.appendChild(opt);
                    });
                }

                const slotSelect = activePage.querySelector('#je-filter-slot');
                if (slotSelect) slotSelect.addEventListener('change', applyFilters);
                if (platformSelect) platformSelect.addEventListener('change', applyFilters);

                renderGrid(allSaves);
            })
            .catch(() => {
                activePage.querySelector('#je-saves-grid').innerHTML =
                    '<div class="je-saves-empty"><span class="material-icons">error_outline</span>Failed to load save states.</div>';
            });
        }

        reloadGrid();
    };
})();
