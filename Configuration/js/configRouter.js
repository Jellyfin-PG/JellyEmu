(function () {
    var pluginId = "9bab105e-9af0-4e25-a87d-876713b60962";
    var jeLoadedTabs = {};
    var activeSystemFilter = '';
    var activeLetterFilter = '';

    var tabTemplateMap = {
        'statistics': 'StatisticsTab',
        'settings': 'SettingsTab',
        'marketplace': 'MarketplaceTab',
        'api': 'ApiTab',
        'about': 'AboutTab',
        'community': 'CommunityTab'
    };

    var knownRegions = new Set([
        "USA","Europe","Japan","World","Australia","Brazil","Canada","China",
        "France","Germany","Italy","Korea","Netherlands","Russia","Spain","Sweden",
        "Asia","Scandinavia","Unlicensed","Prototype","Demo","Sample"
    ]);

    function updateUrlTab(tabId) {
        var hash = window.location.hash || '';
        var qIndex = hash.indexOf('?');
        var basePath = qIndex !== -1 ? hash.substring(0, qIndex) : hash;
        var params = new URLSearchParams(qIndex !== -1 ? hash.substring(qIndex) : '');
        if (params.get('tab') !== tabId) {
            params.set('tab', tabId);
            history.replaceState(null, '', basePath + '?' + params.toString());
        }
    }

    window.jeSwitchTab = function (tabId) {
        var page = document.querySelector('#JellyEmuConfigPage');
        if (!page) return;

        var navButtons = page.querySelectorAll('.je-tab-btn');
        navButtons.forEach(function (btn) {
            btn.classList.toggle('active', btn.getAttribute('data-tab') === tabId);
        });

        var container = page.querySelector('#je-tab-container');
        if (!container) return;

        var templateName = tabTemplateMap[tabId] || 'StatisticsTab';

        function renderTab(html) {
            container.innerHTML = html;
            updateUrlTab(tabId);
            initTabModule(tabId, page);
        }

        if (jeLoadedTabs[tabId]) {
            renderTab(jeLoadedTabs[tabId]);
        } else {
            var authHeader = 'MediaBrowser Token="' + ApiClient.accessToken() + '"';
            fetch('/jellyemu/config/partial/' + templateName, {
                headers: { 'Authorization': authHeader }
            })
            .then(function(r) { return r.text(); })
            .then(function(html) {
                jeLoadedTabs[tabId] = html;
                renderTab(html);
            })
            .catch(function(err) {
                container.innerHTML = '<div style="color: #FF4444; padding: 2em;">Failed to load tab view: ' + err + '</div>';
            });
        }
    };

    function initTabModule(tabId, page) {
        if (tabId === 'statistics') {
            loadRomCount(page);
        } else if (tabId === 'settings') {
            if (window.jeInitSettingsTab) {
                window.jeInitSettingsTab(page);
            }
        } else if (tabId === 'marketplace') {
            loadProviders();
            bindMarketplaceListeners(page);
        } else if (tabId === 'api') {
            loadRetroArchUrl(page);
        } else if (tabId === 'community') {
            if (window.jeInitCommunityTab) {
                window.jeInitCommunityTab(page);
            }
        }
    }

    function getConsoleColor(consoleName) {
        var c = consoleName.toLowerCase();
        if (c.indexOf('nes') !== -1 || c.indexOf('nintendo entertainment system') !== -1) return '#e60012';
        if (c.indexOf('snes') !== -1 || c.indexOf('super nintendo') !== -1) return '#8c52ff';
        if (c.indexOf('n64') !== -1 || c.indexOf('nintendo 64') !== -1) return '#007cff';
        if (c.indexOf('game boy') !== -1 || c.indexOf('gb') !== -1) return '#52B54B';
        if (c.indexOf('windows') !== -1 || c.indexOf('gog') !== -1) return '#00a4dc';
        if (c.indexOf('playstation') !== -1 || c.indexOf('ps') !== -1) return '#2e6db4';
        if (c.indexOf('pico') !== -1) return '#ff7e00';
        if (c.indexOf('dreamcast') !== -1) return '#ff5a00';
        if (c.indexOf('xbox') !== -1) return '#107c10';
        if (c.indexOf('wii') !== -1) return '#00d2ff';
        if (c.indexOf('sega') !== -1 || c.indexOf('genesis') !== -1 || c.indexOf('megadrive') !== -1) return '#005ecc';
        if (c.indexOf('atari') !== -1) return '#e21b22';
        
        var hash = 0;
        for (var i = 0; i < consoleName.length; i++) {
            hash = consoleName.charCodeAt(i) + ((hash << 5) - hash);
        }
        var h = Math.abs(hash % 360);
        return 'hsl(' + h + ', 65%, 55%)';
    }

    function showNotification(msg, isError) {
        if (window.Dashboard) {
            if (typeof window.Dashboard.showNotification === 'function') {
                window.Dashboard.showNotification({
                    message: msg,
                    level: isError ? 'error' : 'info'
                });
                return;
            }
            if (typeof window.Dashboard.alert === 'function' && isError) {
                window.Dashboard.alert(msg);
                return;
            }
        }

        var toast = document.getElementById('je-toast-notification');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'je-toast-notification';
            toast.style.cssText = 'position:fixed;bottom:28px;right:28px;z-index:999999;padding:12px 22px;' +
                'border-radius:8px;font-size:0.95em;font-weight:600;color:#fff;box-shadow:0 8px 24px rgba(0,0,0,0.6);' +
                'backdrop-filter:blur(8px);transition:opacity 0.25s ease, transform 0.25s ease;pointer-events:none;' +
                'display:flex;align-items:center;gap:10px;';
            document.body.appendChild(toast);
        }
        toast.style.background = isError ? 'rgba(211, 47, 47, 0.95)' : 'rgba(46, 125, 50, 0.95)';
        toast.style.border = isError ? '1px solid #ef5350' : '1px solid #66bb6a';
        toast.innerHTML = (isError ? '&#10006; ' : '&#10004; ') + msg;
        toast.style.opacity = '1';
        toast.style.transform = 'translateY(0)';

        clearTimeout(toast._timeout);
        toast._timeout = setTimeout(function () {
            toast.style.opacity = '0';
            toast.style.transform = 'translateY(12px)';
        }, 3500);
    }

    function showStatus(msg, isError) {
        showNotification(msg, isError);
    }
    window.jeShowNotification = showNotification;

    function loadRetroArchUrl(page) {
        var base = window.location.origin;
        var set = function (id, val) { var el = page.querySelector('#' + id); if (el) el.textContent = val; };
        set('raPlaylistUrl', base + '/jellyemu/retroarch/playlist');
        set('raPlaylistSystemUrl', base + '/jellyemu/retroarch/playlist/{system}');
        set('raLaunchUrl', base + '/jellyemu/retroarch/launch/{itemId}');
        set('raInfoUrl', base + '/jellyemu/retroarch/info');
        set('raRomUrl', base + '/jellyemu/rom/{itemId}');
        set('raAssetsBaseUrl', base + '/jellyemu/retroarch/');
        set('raCoresIndexDirsUrl', base + '/jellyemu/retroarch/cores/.index-dirs');
        set('raCoresIndexUrl', base + '/jellyemu/retroarch/cores/{system}/.index');
        set('raCoresFileUrl', base + '/jellyemu/retroarch/cores/{system}/{filename}');
        set('raSystemIndexUrl', base + '/jellyemu/retroarch/system/{path}');
        set('raFrontendUrl', base + '/jellyemu/retroarch/frontend/{file}');
    }

    function loadRomCount(page) {
        var display = page.querySelector('#romCountDisplay');
        if (display) {
            display.innerText = '...';
            display.style.color = '#00a4dc';
        }
        
        var statsContainer = page.querySelector('#je-stats-console-grid');
        if (statsContainer) statsContainer.innerHTML = '<p style="color: #aaa; text-align: center; width: 100%;">Analyzing your library...</p>';
        
        ApiClient.getItems(ApiClient.getCurrentUserId(), {
            Recursive: true,
            IncludeItemTypes: 'Book',
            Fields: 'Tags'
        }).then(function (result) {
            var items = (result.Items || []).filter(function (i) {
                return i.Tags && i.Tags.includes('JellyEmu');
            });
            
            var count = items.length;
            if (display) {
                display.innerText = count;
                display.style.color = count > 0 ? '#52B54B' : '#00a4dc';
            }
            
            var consoleCounts = {};
            var unsupportedCount = 0;
            
            var ejsUnsupportedPlatforms = new Set([
                "Dreamcast","PlayStation 2","PlayStation 3",
                "Xbox","Xbox 360",
                "GameCube","Wii","Wii U","Nintendo Switch","Nintendo 3DS",
                "PlayStation Vita","Windows","Unsupported"
            ]);
            
            items.forEach(function (item) {
                var consoleTag = null;
                var isUnsupported = false;
                if (item.Tags) {
                    if (item.Tags.includes('Unsupported')) {
                        isUnsupported = true;
                    }
                    item.Tags.forEach(function (tag) {
                        if (tag === 'JellyEmu' || tag === 'Game' || tag === 'Unsupported' || tag === 'MultiDisc') return;
                        if (knownRegions.has(tag)) return;
                        if (tag.indexOf('Disc ') === 0 || tag.indexOf('Side ') === 0 || tag.indexOf('Tape ') === 0) return;
                        
                        if (ejsUnsupportedPlatforms.has(tag)) {
                            isUnsupported = true;
                        }
                        consoleTag = tag;
                    });
                }
                if (isUnsupported) {
                    unsupportedCount++;
                }
                if (!consoleTag) consoleTag = 'Unknown';
                consoleCounts[consoleTag] = (consoleCounts[consoleTag] || 0) + 1;
            });
            
            if (statsContainer) {
                statsContainer.innerHTML = '';
                var consoles = Object.keys(consoleCounts).sort(function (a, b) {
                    return consoleCounts[b] - consoleCounts[a];
                });
                
                if (consoles.length === 0) {
                    statsContainer.innerHTML = '<p style="color: #aaa; text-align: center; width: 100%;">No games found in your library.</p>';
                } else {
                    consoles.forEach(function (consoleName) {
                        var cCount = consoleCounts[consoleName];
                        var percent = count > 0 ? Math.round((cCount / count) * 100) : 0;
                        var color = getConsoleColor(consoleName);
                        
                        var card = document.createElement('div');
                        card.style.cssText = 'background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.05); border-radius: 8px; padding: 1.25em; display: flex; flex-direction: column; gap: 8px; position: relative; overflow: hidden;';
                        
                        var glow = document.createElement('div');
                        glow.style.cssText = 'position: absolute; left: 0; top: 0; bottom: 0; width: 4px; background: ' + color + ';';
                        card.appendChild(glow);
                        
                        var headerRow = document.createElement('div');
                        headerRow.style.cssText = 'display: flex; justify-content: space-between; align-items: center; margin-left: 6px;';
                        
                        var nameSpan = document.createElement('span');
                        nameSpan.style.cssText = 'font-weight: 600; font-size: 1.05em; color: #fff;';
                        nameSpan.textContent = consoleName;
                        headerRow.appendChild(nameSpan);
                        
                        var countSpan = document.createElement('span');
                        countSpan.style.cssText = 'font-size: 0.9em; color: #aaa;';
                        countSpan.innerHTML = '<strong style="color: ' + color + ';">' + cCount + '</strong> ' + (cCount === 1 ? 'game' : 'games');
                        headerRow.appendChild(countSpan);
                        
                        card.appendChild(headerRow);
                        
                        var progressWrap = document.createElement('div');
                        progressWrap.style.cssText = 'height: 6px; background: rgba(255,255,255,0.06); border-radius: 3px; overflow: hidden; margin-left: 6px;';
                        
                        var progressFill = document.createElement('div');
                        progressFill.style.cssText = 'height: 100%; width: ' + percent + '%; background: ' + color + '; border-radius: 3px;';
                        progressWrap.appendChild(progressFill);
                        
                        card.appendChild(progressWrap);
                        statsContainer.appendChild(card);
                    });
                }
            }
            
            var unsupportDisplay = page.querySelector('#unsupportedCountDisplay');
            if (unsupportDisplay) {
                unsupportDisplay.innerText = unsupportedCount;
                unsupportDisplay.style.color = unsupportedCount > 0 ? '#f0c040' : '#aaa';
            }
        }).catch(function (err) {
            console.error('[JellyEmu] Failed to load stats:', err);
            if (display) {
                display.innerText = 'Error';
                display.style.color = '#FF4444';
            }
        });
    }


    function loadProviders() {
        var authHeader = 'MediaBrowser Token="' + ApiClient.accessToken() + '"';
        fetch('/jellyemu/marketplace/feed-providers', {
            headers: { 'Authorization': authHeader }
        })
        .then(function(r) { return r.json(); })
        .then(function(providers) {
            var container = document.querySelector('#JellyEmuConfigPage #activeProvidersList');
            if (!container) return;
            container.innerHTML = '';

            if (!providers || providers.length === 0) {
                container.innerHTML = '<div style="color: #aaa; font-size: 0.9em; font-style: italic;">No providers available from feed URL.</div>';
                return;
            }

            providers.forEach(function(p) {
                var isEnabled = p.isEnabled || p.enabled || false;
                var providerUrl = p.url || p.baseUrl || ('https://' + (p.domain || ''));

                var row = document.createElement('div');
                row.className = 'je-provider-row';

                var infoDiv = document.createElement('div');
                infoDiv.style.cssText = 'display:flex; flex-direction:column; gap:2px;';

                var nameSpan = document.createElement('span');
                nameSpan.style.cssText = 'font-weight:600; color:#fff; font-size:0.95em;';
                nameSpan.textContent = p.name || 'Unknown Provider';

                var urlSpan = document.createElement('span');
                urlSpan.className = 'je-provider-url';
                urlSpan.textContent = providerUrl;

                infoDiv.appendChild(nameSpan);
                infoDiv.appendChild(urlSpan);

                var toggleBtn = document.createElement('button');
                toggleBtn.type = 'button';
                toggleBtn.className = 'je-toggle-btn ' + (isEnabled ? 'je-on' : '');
                toggleBtn.textContent = isEnabled ? 'ENABLED' : 'DISABLED';

                toggleBtn.addEventListener('click', function() {
                    var willEnable = !isEnabled;
                    var endpoint = willEnable ? '/jellyemu/marketplace/providers' : ('/jellyemu/marketplace/providers?url=' + encodeURIComponent(providerUrl));
                    var options = {
                        method: willEnable ? 'POST' : 'DELETE',
                        headers: {
                            'Authorization': authHeader,
                            'Content-Type': 'application/json'
                        }
                    };
                    if (willEnable) {
                        options.body = JSON.stringify({ url: providerUrl });
                    }

                    fetch(endpoint, options)
                    .then(function(r) {
                        if (r.ok) {
                            isEnabled = willEnable;
                            toggleBtn.textContent = isEnabled ? 'ENABLED' : 'DISABLED';
                            toggleBtn.className = 'je-toggle-btn ' + (isEnabled ? 'je-on' : '');
                        } else {
                            showStatus('Failed to update provider status.', true);
                        }
                    })
                    .catch(function(err) {
                        console.error('[JellyEmu] Error updating provider:', err);
                        showStatus('Error updating provider status.', true);
                    });
                });

                row.appendChild(infoDiv);
                row.appendChild(toggleBtn);
                container.appendChild(row);
            });
        })
        .catch(function(err) {
            console.error('[JellyEmu] Error loading providers:', err);
        });
    }

    function searchMarketplace() {
        var queryInput = document.querySelector('#JellyEmuConfigPage #marketplaceQuery');
        var query = queryInput ? queryInput.value.trim() : '';

        var resultsEl = document.querySelector('#JellyEmuConfigPage #marketplaceResults');
        var statusEl = document.querySelector('#JellyEmuConfigPage #marketplaceStatus');
        if (!resultsEl || !statusEl) return;

        if (!query && !activeSystemFilter && !activeLetterFilter) {
            resultsEl.innerHTML = '';
            statusEl.style.display = 'block';
            statusEl.innerHTML = '<span style="color: #aaa;">Enter a game title or select a console/letter filter to browse ROMs.</span>';
            return;
        }

        statusEl.style.display = 'block';
        statusEl.innerHTML = '<div style="display: flex; align-items: center; justify-content: center; gap: 10px;">' +
            '<span class="material-icons je-spinner" style="font-size: 24px; color: #00a4dc;">sync</span>' +
            '<span>Searching ROM sources...</span></div>';
        resultsEl.innerHTML = '';

        var authHeader = 'MediaBrowser Token="' + ApiClient.accessToken() + '"';
        var params = new URLSearchParams();
        if (query) {
            params.append('query', query);
            params.append('q', query);
        }
        if (activeSystemFilter) params.append('system', activeSystemFilter);
        if (activeLetterFilter) params.append('letter', activeLetterFilter);

        var endpoint = query ? '/jellyemu/marketplace/search?' : '/jellyemu/marketplace/browse?';

        fetch(endpoint + params.toString(), {
            headers: { 'Authorization': authHeader }
        })
        .then(function (r) { return r.json(); })
        .then(function (results) {
            if (!results || results.length === 0) {
                statusEl.style.display = 'block';
                statusEl.innerHTML = '<span style="color: #aaa;">No ROM results found. Try a different search or filter.</span>';
                return;
            }

            statusEl.style.display = 'none';
            results.forEach(function (rom) {
                var rTitle = rom.Title || rom.title || '';
                var rSystem = rom.System || rom.system || '';
                var rProvider = rom.ProviderName || rom.providerName || '';
                var rDetailUrl = rom.DetailUrl || rom.detailUrl || '';
                var rThumbUrl = rom.ThumbnailUrl || rom.thumbnailUrl || '';
                var rRegion = rom.Region || rom.region || '';
                var rVersion = rom.Version || rom.version || '';
                var rHasManual = (rom.HasManual !== undefined) ? rom.HasManual : (rom.hasManual !== undefined ? rom.hasManual : false);

                var card = document.createElement('div');
                card.className = 'je-rom-card';

                var thumbBox = document.createElement('div');
                thumbBox.className = 'je-rom-thumb-container';
                if (rThumbUrl) {
                    var img = document.createElement('img');
                    img.className = 'je-rom-thumb';
                    img.src = rThumbUrl;
                    img.alt = rTitle;
                    thumbBox.appendChild(img);
                } else {
                    thumbBox.innerHTML = '<span class="material-icons" style="font-size: 32px; color: #555;">sports_esports</span>';
                }

                var infoBox = document.createElement('div');
                infoBox.className = 'je-rom-info';

                var title = document.createElement('h4');
                title.className = 'je-rom-title';
                title.textContent = rTitle;
                title.title = rTitle;

                var badgeRow = document.createElement('div');
                badgeRow.className = 'je-rom-badge-row';

                if (rSystem) {
                    var sysBadge = document.createElement('span');
                    sysBadge.className = 'je-rom-badge system';
                    sysBadge.textContent = rSystem;
                    badgeRow.appendChild(sysBadge);
                }

                if (rProvider) {
                    var provBadge = document.createElement('span');
                    provBadge.className = 'je-rom-badge provider';
                    provBadge.textContent = rProvider;
                    badgeRow.appendChild(provBadge);
                }

                if (rRegion) {
                    var regBadge = document.createElement('span');
                    regBadge.className = 'je-rom-badge region';
                    regBadge.textContent = rRegion;
                    badgeRow.appendChild(regBadge);
                }

                if (rVersion) {
                    var verBadge = document.createElement('span');
                    verBadge.className = 'je-rom-badge version';
                    verBadge.textContent = 'v' + rVersion;
                    badgeRow.appendChild(verBadge);
                }

                if (rHasManual) {
                    var manBadge = document.createElement('span');
                    manBadge.className = 'je-rom-badge manual';
                    manBadge.textContent = 'MANUAL';
                    badgeRow.appendChild(manBadge);
                }

                var dlBtn = document.createElement('button');
                dlBtn.type = 'button';
                dlBtn.className = 'je-rom-dl-btn';
                dlBtn.innerHTML = '<span class="material-icons" style="font-size: 16px;">download</span><span>Download</span>';

                dlBtn.addEventListener('click', function () {
                    dlBtn.className = 'je-rom-dl-btn downloading';
                    dlBtn.innerHTML = '<span class="material-icons je-spinner" style="font-size: 16px;">sync</span><span>Downloading...</span>';

                    fetch('/jellyemu/marketplace/download', {
                        method: 'POST',
                        headers: {
                            'Authorization': authHeader,
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify({
                            detailUrl: rDetailUrl,
                            system: rSystem
                        })
                    })
                    .then(function (r) { return r.json(); })
                    .then(function (data) {
                        if (data.success) {
                            dlBtn.className = 'je-rom-dl-btn downloaded';
                            dlBtn.innerHTML = '<span class="material-icons" style="font-size: 16px;">check_circle</span><span>Downloaded</span>';
                        } else {
                            dlBtn.className = 'je-rom-dl-btn';
                            dlBtn.innerHTML = '<span class="material-icons" style="font-size: 16px;">error</span><span>Failed</span>';
                        }
                    })
                    .catch(function () {
                        dlBtn.className = 'je-rom-dl-btn';
                        dlBtn.innerHTML = '<span class="material-icons" style="font-size: 16px;">error</span><span>Failed</span>';
                    });
                });

                infoBox.appendChild(title);
                infoBox.appendChild(badgeRow);
                infoBox.appendChild(dlBtn);

                card.appendChild(thumbBox);
                card.appendChild(infoBox);

                resultsEl.appendChild(card);
            });
        })
        .catch(function (err) {
            console.error('[JellyEmu] Search error:', err);
            statusEl.style.display = 'block';
            statusEl.innerHTML = '<span style="color: #FF4444;">Search request failed. Please check server logs.</span>';
        });
    }

    window.jeSetSystemFilter = function (sys) {
        activeSystemFilter = sys;
        var cards = document.querySelectorAll('#JellyEmuConfigPage .je-console-card');
        cards.forEach(function (c) {
            c.classList.toggle('active', c.getAttribute('data-system') === sys);
        });
        searchMarketplace();
    };

    window.jeSetLetterFilter = function (letVal) {
        activeLetterFilter = letVal;
        var btns = document.querySelectorAll('#JellyEmuConfigPage .je-letter-btn');
        btns.forEach(function (b) {
            b.classList.toggle('active', b.getAttribute('data-letter') === letVal);
        });
        searchMarketplace();
    };

    function bindMarketplaceListeners(page) {
        var btnSearch = page.querySelector('#btnSearchMarketplace');
        if (btnSearch) {
            btnSearch.addEventListener('click', function() {
                searchMarketplace();
            });
        }

        var queryInput = page.querySelector('#marketplaceQuery');
        if (queryInput) {
            queryInput.addEventListener('keypress', function(e) {
                if (e.key === 'Enter') {
                    searchMarketplace();
                }
            });
        }
    }

    document.addEventListener('pageshow', function () {
        var page = document.querySelector('#JellyEmuConfigPage');
        if (!page) return;

        var hash = window.location.hash || '';
        var initialTab = 'statistics';
        var qIndex = hash.indexOf('?');
        if (qIndex !== -1) {
            var params = new URLSearchParams(hash.substring(qIndex));
            if (params.get('tab')) {
                initialTab = params.get('tab');
            }
        }

        jeSwitchTab(initialTab);
    }, true);
})();
