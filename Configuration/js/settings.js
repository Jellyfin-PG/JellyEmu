(function () {
    var pluginId = "9bab105e-9af0-4e25-a87d-876713b60962";

    function showNotification(msg, isError) {
        if (typeof window.jeShowNotification === 'function') {
            window.jeShowNotification(msg, isError);
            return;
        }

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
        toast.textContent = (isError ? '\u2716 ' : '\u2714 ') + (msg || '');
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

    window.jeToggle = function (id) {
        var hidden = document.getElementById(id);
        var btn = document.getElementById(id + 'Btn');
        if (!hidden || !btn) return;
        var nowOn = hidden.value !== 'true';
        hidden.value = nowOn ? 'true' : 'false';
        btn.textContent = nowOn ? 'ON' : 'OFF';
        btn.classList.toggle('je-on', nowOn);
        if (id === 'rommCollectionSyncEnabled') {
            var row = document.getElementById('rommCollectionSyncRow');
            if (row) row.style.display = nowOn ? '' : 'none';
        }
    };

    function jeSet(id, val) {
        var hidden = document.getElementById(id);
        var btn = document.getElementById(id + 'Btn');
        if (!hidden || !btn) return;
        hidden.value = val ? 'true' : 'false';
        btn.textContent = val ? 'ON' : 'OFF';
        btn.classList.toggle('je-on', !!val);
    }

    function populateFolderSelect(selectId, selectedPath, opts) {
        var page = document.querySelector('#JellyEmuConfigPage');
        if (!page) return;
        var selectEl = page.querySelector(selectId);
        if (!selectEl) return;

        selectEl.innerHTML = '<option value="">Loading libraries...</option>';

        ApiClient.getVirtualFolders().then(function (folders) {
            selectEl.innerHTML = '';

            var defaultOpt = document.createElement('option');
            defaultOpt.value = '';
            defaultOpt.textContent = opts.defaultLabel;
            selectEl.appendChild(defaultOpt);

            var count = 0;
            var matched = !selectedPath;
            (folders || []).filter(function (folder) {
                return !opts.collectionType || folder.CollectionType === opts.collectionType;
            }).forEach(function (folder) {
                (folder.Locations || []).forEach(function (locPath) {
                    count++;
                    var opt = document.createElement('option');
                    opt.value = opts.mapValue ? opts.mapValue(locPath) : locPath;
                    opt.textContent = folder.Name + ' (' + opt.value + ')';
                    if (selectedPath && opt.value === selectedPath) {
                        opt.selected = true;
                        matched = true;
                    }
                    selectEl.appendChild(opt);
                });
            });

            if (count === 0) {
                var noOpt = document.createElement('option');
                noOpt.value = '';
                noOpt.textContent = 'No media folders found';
                selectEl.appendChild(noOpt);
            }

            // Keep a previously configured path (e.g. set before a library was
            // removed or renamed) selectable so saving doesn't silently clear it.
            if (!matched) {
                var customOpt = document.createElement('option');
                customOpt.value = selectedPath;
                customOpt.textContent = 'Current: ' + selectedPath;
                customOpt.selected = true;
                selectEl.appendChild(customOpt);
            }
        }).catch(function (err) {
            console.error('[JellyEmu] Failed to load library folders:', err);
            selectEl.innerHTML = '<option value="">Failed to load library folders</option>';
        });
    }

    function loadLibraryFolders(selectedPath) {
        populateFolderSelect('#gamesLibraryPath', selectedPath, {
            defaultLabel: '-- Select a Library Folder --'
        });
    }

    function loadScreenshotFolders(selectedPath) {
        populateFolderSelect('#screenshotsFolder', selectedPath, {
            collectionType: 'homevideos',
            defaultLabel: '-- Download in browser --',
            mapValue: function (locPath) {
                var sep = locPath.indexOf('\\') >= 0 ? '\\' : '/';
                return locPath + sep + 'JellyEmu';
            }
        });
    }

    function loadBiosList(page) {
        var folderEl = page.querySelector('#biosFolderDisplay');
        var containerEl = page.querySelector('#biosListContainer');
        if (!containerEl) return;
        var authHeader = 'MediaBrowser Token="' + ApiClient.accessToken() + '"';

        fetch('/jellyemu/bios/list', {
            headers: { 'Authorization': authHeader }
        })
        .then(function(r) { return r.json(); })
        .then(function(d) {
            if (folderEl) folderEl.textContent = 'Folder: ' + (d.directory || d.Directory || 'Not found');
            var items = d.items || d.Items || [];
            if (items.length === 0) {
                containerEl.innerHTML = '<div style="color: #aaa; font-style: italic; padding: 10px 0;">No BIOS files detected. Place BIOS files (e.g. scph5501.bin, gba_bios.bin) in the folder above.</div>';
                return;
            }

            // Group by System
            var groups = {};
            items.forEach(function(it) {
                var sys = it.systemOrCore || it.SystemOrCore || 'Unknown';
                if (!groups[sys]) groups[sys] = [];
                groups[sys].push(it);
            });

            var sortedSysKeys = Object.keys(groups).sort(function(a, b) {
                if (a === 'Unknown') return 1;
                if (b === 'Unknown') return -1;
                return a.localeCompare(b);
            });

            var html = '<div style="display: flex; flex-direction: column; gap: 12px;">';

            sortedSysKeys.forEach(function(sysKey) {
                var groupItems = groups[sysKey];
                var sysLabel = sysKey === 'Unknown' ? 'Other / Unrecognized' : sysKey;
                var safeSysId = sysKey.replace(/[^a-zA-Z0-9_-]/g, '_');

                html += '<div style="background: rgba(255,255,255,0.03); border: 1px solid rgba(255,255,255,0.08); border-radius: 6px; padding: 10px 12px;">';
                html += '<div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; border-bottom: 1px solid rgba(255,255,255,0.05); padding-bottom: 4px;">';
                html += '<span style="font-weight: 600; color: #52B54B; font-size: 0.95em;">' + sysLabel + '</span>';
                html += '<span style="color: #888; font-size: 0.8em;">' + groupItems.length + (groupItems.length === 1 ? ' file' : ' files') + '</span>';
                html += '</div>';

                html += '<table style="width: 100%; border-collapse: collapse; text-align: left; font-size: 0.88em;">';

                groupItems.forEach(function(it) {
                    var rel = it.relativePath || it.RelativePath || it.fileName || it.FileName || '';
                    var fn = it.fileName || it.FileName || rel;
                    var rawSize = (typeof it.sizeBytes === 'number') ? it.sizeBytes : ((typeof it.SizeBytes === 'number') ? it.SizeBytes : 0);
                    var sz = (rawSize / 1024).toFixed(1) + ' KB';
                    var status = it.status || it.Status || 'Unrecognized';
                    var isActive = !!(it.isActive !== undefined ? it.isActive : it.IsActive);
                    var md5 = it.md5 || it.Md5 || '';
                    var expMd5 = it.expectedMd5 || it.ExpectedMd5 || '';

                    var statusBadge = '';
                    if (status === 'Verified') {
                        statusBadge = '<span style="display: inline-block; background: rgba(46, 204, 113, 0.15); color: #2ecc71; border: 1px solid rgba(46, 204, 113, 0.3); border-radius: 4px; padding: 2px 6px; font-size: 0.78em; font-weight: 600;" title="Verified Libretro Dump&#10;MD5: ' + md5 + '">✓ Verified</span>';
                    } else if (status === 'Mismatch') {
                        statusBadge = '<span style="display: inline-block; background: rgba(231, 76, 60, 0.15); color: #e74c3c; border: 1px solid rgba(231, 76, 60, 0.3); border-radius: 4px; padding: 2px 6px; font-size: 0.78em; font-weight: 600;" title="Checksum Mismatch (Bad Dump)&#10;MD5: ' + md5 + (expMd5 ? '&#10;Expected: ' + expMd5 : '') + '">⚠ Checksum Mismatch</span>';
                    } else {
                        statusBadge = '<span style="display: inline-block; background: rgba(255, 255, 255, 0.08); color: #aaa; border: 1px solid rgba(255, 255, 255, 0.15); border-radius: 4px; padding: 2px 6px; font-size: 0.78em;" title="MD5: ' + md5 + '">Unrecognized</span>';
                    }

                    var isRadioDisabled = (sysKey === 'Unknown');

                    html += '<tr style="border-bottom: 1px solid rgba(255,255,255,0.03);">' +
                        '<td style="padding: 6px 4px; width: 28px; text-align: center;">' +
                        (isRadioDisabled ? '' : '<input type="radio" class="je-bios-active-radio" name="bios_active_' + safeSysId + '" data-sys="' + sysKey.replace(/"/g, '&quot;') + '" data-rel="' + rel.replace(/"/g, '&quot;') + '" ' + (isActive ? 'checked' : '') + ' title="Set as Active BIOS for ' + sysKey + '" style="cursor: pointer; accent-color: #52B54B;" />') +
                        '</td>' +
                        '<td style="padding: 6px 8px; font-family: monospace; color: #fff;">' +
                        fn + (rel !== fn ? ' <small style="color: #777;">(' + rel + ')</small>' : '') +
                        '</td>' +
                        '<td style="padding: 6px 8px; text-align: right; white-space: nowrap;">' + statusBadge + '</td>' +
                        '<td style="padding: 6px 8px; text-align: right; color: #888; width: 70px;">' + sz + '</td>' +
                        '</tr>';
                });

                html += '</table>';
                html += '</div>';
            });

            html += '</div>';
            containerEl.innerHTML = html;

            var radios = containerEl.querySelectorAll('.je-bios-active-radio');
            radios.forEach(function(r) {
                r.addEventListener('change', function() {
                    if (!this.checked) return;
                    var sys = this.getAttribute('data-sys');
                    var rel = this.getAttribute('data-rel');

                    fetch('/jellyemu/bios/active', {
                        method: 'POST',
                        headers: {
                            'Authorization': authHeader,
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify({ system: sys, relativePath: rel })
                    })
                    .then(function(res) { return res.json(); })
                    .then(function() {
                        showStatus('Active BIOS for ' + sys + ' set to ' + rel, false);
                    })
                    .catch(function(err) {
                        console.error('[JellyEmu] Failed setting active BIOS:', err);
                        showStatus('Failed to set active BIOS.', true);
                    });
                });
            });
        })
        .catch(function(err) {
            containerEl.innerHTML = '<div style="color:#FF4444; padding: 6px 0;">Failed to fetch BIOS list: ' + err + '</div>';
        });
    }

    function loadConfig(page) {
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            var igdbId = page.querySelector('#igdbClientId');
            if (igdbId) igdbId.value = config.IgdbClientId || '';
            var igdbSec = page.querySelector('#igdbClientSecret');
            if (igdbSec) igdbSec.value = config.IgdbClientSecret || '';
            var rawg = page.querySelector('#rawgApiKey');
            if (rawg) rawg.value = config.RawgApiKey || '';
            var sgdb = page.querySelector('#steamGridDbApiKey');
            if (sgdb) sgdb.value = config.SteamGridDbApiKey || '';
            var tgdb = page.querySelector('#theGamesDbApiKey');
            if (tgdb) tgdb.value = config.TheGamesDbApiKey || '';
            var ssDevId = page.querySelector('#screenScraperDevId');
            if (ssDevId) ssDevId.value = config.ScreenScraperDevId || '';
            var ssDevPass = page.querySelector('#screenScraperDevPassword');
            if (ssDevPass) ssDevPass.value = config.ScreenScraperDevPassword || '';
            var ssSoft = page.querySelector('#screenScraperSoftName');
            if (ssSoft) ssSoft.value = config.ScreenScraperSoftName || 'JellyEmu';
            var ssUser = page.querySelector('#screenScraperUser');
            if (ssUser) ssUser.value = config.ScreenScraperUser || '';
            var ssPass = page.querySelector('#screenScraperPassword');
            if (ssPass) ssPass.value = config.ScreenScraperPassword || '';
            var ssRegion = page.querySelector('#screenScraperRegionPreference');
            if (ssRegion) ssRegion.value = (config.ScreenScraperRegionPreference || 'auto').toLowerCase();
            var ssLang = page.querySelector('#screenScraperLanguagePreference');
            if (ssLang) ssLang.value = (config.ScreenScraperLanguagePreference || 'en').toLowerCase();
            var rommUrl = page.querySelector('#rommInstanceUrl');
            if (rommUrl) rommUrl.value = config.RommInstanceUrl || '';
            var rommUser = page.querySelector('#rommUsername');
            if (rommUser) rommUser.value = config.RommUsername || '';
            var rommPass = page.querySelector('#rommPassword');
            if (rommPass) rommPass.value = config.RommPassword || '';

            jeSet('rommEnabled', config.RommEnabled === true);
            jeSet('rommSaveSyncEnabled', config.RommSaveSyncEnabled !== false);
            jeSet('rommPlaytimeReportEnabled', config.RommPlaytimeReportEnabled !== false);
            jeSet('rommCollectionSyncEnabled', config.RommCollectionSyncEnabled !== false);
            jeSet('rommScreenshotPushEnabled', config.RommScreenshotPushEnabled !== false);
            jeSet('useLoomInjector', config.UseLoomInjector === true);
            jeSet('vantageEnabled', config.VantageEnabled !== false);

            var collRow = page.querySelector('#rommCollectionSyncRow');
            if (collRow) collRow.style.display = config.RommCollectionSyncEnabled !== false ? '' : 'none';
            
            var npIce = page.querySelector('#netplayIceServers');
            if (npIce) npIce.value = config.NetplayIceServers !== undefined && config.NetplayIceServers !== null ? config.NetplayIceServers : 'stun:stun.l.google.com:19302\nstun:stun1.l.google.com:19302\nstun:stun2.l.google.com:19302';
            var ejsSel = page.querySelector('#ejsChannel');
            if (ejsSel) ejsSel.value = (config.EjsChannel || 'stable').toLowerCase();
            var feedUrl = page.querySelector('#marketplaceFeedUrl');
            if (feedUrl) feedUrl.value = config.MarketplaceFeedUrl || '';
            var bPath = page.querySelector('#biosPath');
            if (bPath) bPath.value = config.BiosPath || '';
            loadScreenshotFolders(config.ScreenshotsFolder || '');
            loadLibraryFolders(config.GamesLibraryPath || '');
            loadBiosList(page);
        }).catch(function (err) {
            console.error('[JellyEmu] Failed to load config:', err);
            showStatus('Failed to load settings.', true);
        });
    }

    function saveConfig(page) {
        if (window.Dashboard && typeof window.Dashboard.showLoadingMsg === 'function') {
            window.Dashboard.showLoadingMsg();
        }

        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            var igdbId = page.querySelector('#igdbClientId');
            if (igdbId) config.IgdbClientId = igdbId.value.trim();
            var igdbSec = page.querySelector('#igdbClientSecret');
            if (igdbSec) config.IgdbClientSecret = igdbSec.value.trim();
            var rawg = page.querySelector('#rawgApiKey');
            if (rawg) config.RawgApiKey = rawg.value.trim();
            var sgdb = page.querySelector('#steamGridDbApiKey');
            if (sgdb) config.SteamGridDbApiKey = sgdb.value.trim();
            var tgdb = page.querySelector('#theGamesDbApiKey');
            if (tgdb) config.TheGamesDbApiKey = tgdb.value.trim();
            var ssDevId = page.querySelector('#screenScraperDevId');
            if (ssDevId) config.ScreenScraperDevId = ssDevId.value.trim();
            var ssDevPass = page.querySelector('#screenScraperDevPassword');
            if (ssDevPass) config.ScreenScraperDevPassword = ssDevPass.value.trim();
            var ssSoft = page.querySelector('#screenScraperSoftName');
            if (ssSoft) config.ScreenScraperSoftName = ssSoft.value.trim() || 'JellyEmu';
            var ssUser = page.querySelector('#screenScraperUser');
            if (ssUser) config.ScreenScraperUser = ssUser.value.trim();
            var ssPass = page.querySelector('#screenScraperPassword');
            if (ssPass) config.ScreenScraperPassword = ssPass.value.trim();
            var ssRegion = page.querySelector('#screenScraperRegionPreference');
            if (ssRegion) config.ScreenScraperRegionPreference = ssRegion.value;
            var ssLang = page.querySelector('#screenScraperLanguagePreference');
            if (ssLang) config.ScreenScraperLanguagePreference = ssLang.value;
            
            var rEnabled = page.querySelector('#rommEnabled');
            if (rEnabled) config.RommEnabled = rEnabled.value === 'true';
            var rUrl = page.querySelector('#rommInstanceUrl');
            if (rUrl) config.RommInstanceUrl = rUrl.value.trim();
            var rUser = page.querySelector('#rommUsername');
            if (rUser) config.RommUsername = rUser.value.trim();
            var rPass = page.querySelector('#rommPassword');
            if (rPass) config.RommPassword = rPass.value.trim();
            
            var rSave = page.querySelector('#rommSaveSyncEnabled');
            if (rSave) config.RommSaveSyncEnabled = rSave.value === 'true';
            var rPlay = page.querySelector('#rommPlaytimeReportEnabled');
            if (rPlay) config.RommPlaytimeReportEnabled = rPlay.value === 'true';
            var rColl = page.querySelector('#rommCollectionSyncEnabled');
            if (rColl) config.RommCollectionSyncEnabled = rColl.value === 'true';
            var rShot = page.querySelector('#rommScreenshotPushEnabled');
            if (rShot) config.RommScreenshotPushEnabled = rShot.value === 'true';
            
            var loom = page.querySelector('#useLoomInjector');
            if (loom) config.UseLoomInjector = loom.value === 'true';
            var vantage = page.querySelector('#vantageEnabled');
            if (vantage) config.VantageEnabled = vantage.value === 'true';
            var ejsSel = page.querySelector('#ejsChannel');
            if (ejsSel) config.EjsChannel = ejsSel.value;
            var npIce = page.querySelector('#netplayIceServers');
            if (npIce) config.NetplayIceServers = npIce.value.trim();
            var gLib = page.querySelector('#gamesLibraryPath');
            if (gLib) config.GamesLibraryPath = gLib.value;
            var feedUrl = page.querySelector('#marketplaceFeedUrl');
            if (feedUrl) config.MarketplaceFeedUrl = feedUrl.value.trim();
            var bPath = page.querySelector('#biosPath');
            if (bPath) config.BiosPath = bPath.value.trim();
            var shotsFolder = page.querySelector('#screenshotsFolder');
            if (shotsFolder) config.ScreenshotsFolder = shotsFolder.value.trim();

            return ApiClient.updatePluginConfiguration(pluginId, config);
        }).then(function (result) {
            if (window.Dashboard && typeof window.Dashboard.processPluginConfigurationUpdateResult === 'function') {
                window.Dashboard.processPluginConfigurationUpdateResult(result);
            } else {
                if (window.Dashboard && typeof window.Dashboard.hideLoadingMsg === 'function') {
                    window.Dashboard.hideLoadingMsg();
                }
                showNotification('Settings saved successfully!', false);
            }
        }).catch(function (err) {
            console.error('[JellyEmu] Failed to save config:', err);
            if (window.Dashboard && typeof window.Dashboard.hideLoadingMsg === 'function') {
                window.Dashboard.hideLoadingMsg();
            }
            if (window.Dashboard && typeof window.Dashboard.alert === 'function') {
                window.Dashboard.alert('Failed to save settings: ' + (err.message || err));
            } else {
                showNotification('Failed to save settings.', true);
            }
        });
    }

    function bindSettingsListeners(page) {
        var btnSave = page.querySelector('#btnSaveConfig');
        if (btnSave) {
            btnSave.addEventListener('click', function() {
                saveConfig(page);
            });
        }

        // Accordion Groups Toggle
        var groupHeaders = page.querySelectorAll('.je-settings-group-header');
        groupHeaders.forEach(function(hdr) {
            hdr.addEventListener('click', function() {
                var group = hdr.closest('.je-settings-group');
                if (group) {
                    group.classList.toggle('collapsed');
                }
            });
        });

        // Expand All / Collapse All
        var btnExpandAll = page.querySelector('#btnExpandAllSettings');
        if (btnExpandAll) {
            btnExpandAll.addEventListener('click', function() {
                page.querySelectorAll('.je-settings-group').forEach(function(g) {
                    g.classList.remove('collapsed');
                });
            });
        }

        var btnCollapseAll = page.querySelector('#btnCollapseAllSettings');
        if (btnCollapseAll) {
            btnCollapseAll.addEventListener('click', function() {
                page.querySelectorAll('.je-settings-group').forEach(function(g) {
                    g.classList.add('collapsed');
                });
            });
        }

        // Category Filter Chips
        var chips = page.querySelectorAll('#jeSettingsChips .je-chip');
        var groups = page.querySelectorAll('.je-settings-group');
        chips.forEach(function(chip) {
            chip.addEventListener('click', function() {
                chips.forEach(function(c) { c.classList.remove('active'); });
                chip.classList.add('active');

                var cat = chip.getAttribute('data-category');
                var searchInput = page.querySelector('#jeSettingsSearch');
                if (searchInput) searchInput.value = '';

                groups.forEach(function(g) {
                    if (cat === 'all' || g.getAttribute('data-category') === cat) {
                        g.style.display = '';
                        if (cat !== 'all') {
                            g.classList.remove('collapsed');
                        }
                    } else {
                        g.style.display = 'none';
                    }
                });
            });
        });

        // Live Search Filter
        var searchInput = page.querySelector('#jeSettingsSearch');
        if (searchInput) {
            searchInput.addEventListener('input', function() {
                var q = this.value.trim().toLowerCase();
                if (!q) {
                    var activeChip = page.querySelector('#jeSettingsChips .je-chip.active');
                    var cat = activeChip ? activeChip.getAttribute('data-category') : 'all';
                    groups.forEach(function(g) {
                        if (cat === 'all' || g.getAttribute('data-category') === cat) {
                            g.style.display = '';
                        } else {
                            g.style.display = 'none';
                        }
                    });
                    return;
                }

                groups.forEach(function(g) {
                    var text = g.textContent.toLowerCase();
                    if (text.indexOf(q) !== -1) {
                        g.style.display = '';
                        g.classList.remove('collapsed');
                    } else {
                        g.style.display = 'none';
                    }
                });
            });
        }

        var btnRefreshBios = page.querySelector('#btnRefreshBios');
        if (btnRefreshBios) {
            btnRefreshBios.addEventListener('click', function() {
                loadBiosList(page);
            });
        }

        var btnRedownload = page.querySelector('#btnRedownloadEjs');
        if (btnRedownload) {
            btnRedownload.addEventListener('click', function() {
                var statusEl = page.querySelector('#ejsRedownloadStatus');
                if (!statusEl) return;
                statusEl.textContent = 'Triggering re-download…';
                statusEl.style.color = '#aaa';
                var authHeader = 'MediaBrowser Token="' + ApiClient.accessToken() + '"';
                fetch('/jellyemu/ejs/redownload', {
                    method: 'POST',
                    headers: { 'Authorization': authHeader }
                })
                .then(function(r) { return r.json(); })
                .then(function(d) {
                    statusEl.textContent = d.message || 'Started downloading!';
                    statusEl.style.color = '#52B54B';
                })
                .catch(function(err) {
                    statusEl.textContent = 'Failed: ' + err;
                    statusEl.style.color = '#FF4444';
                });
            });
        }

        var btnHealth = page.querySelector('#btnRommHealth');
        if (btnHealth) {
            btnHealth.addEventListener('click', function () {
                var statusEl = page.querySelector('#rommHealthStatus');
                var urlInput = page.querySelector('#rommInstanceUrl');
                if (!statusEl || !urlInput) return;
                var urlVal = urlInput.value.trim();
                if (!urlVal) {
                    statusEl.textContent = 'Enter a URL first.';
                    statusEl.style.color = '#f0c040';
                    return;
                }
                statusEl.textContent = 'Checking…';
                statusEl.style.color = '#aaa';
                fetch('/jellyemu/romm/health')
                    .then(function (r) { return r.json(); })
                    .then(function (d) {
                        if (d.reachable) {
                            statusEl.textContent = 'Reachable — ' + d.probe + ' → HTTP ' + d.status;
                            statusEl.style.color = '#52B54B';
                        } else {
                            statusEl.textContent = '' + (d.reason || 'Not reachable');
                            statusEl.style.color = '#FF4444';
                        }
                    })
                    .catch(function (err) {
                        statusEl.textContent = 'Request failed: ' + err;
                        statusEl.style.color = '#FF4444';
                    });
            });
        }

        var btnSync = page.querySelector('#btnRommSyncCollections');
        if (btnSync) {
            btnSync.addEventListener('click', function () {
                var status = page.querySelector('#rommCollectionSyncStatus');
                if (!status) return;
                status.textContent = 'Syncing…';
                status.style.color = '#aaa';
                var authHeader = 'MediaBrowser Token="' + ApiClient.accessToken() + '"';

                fetch('/jellyemu/romm/collections', {
                    headers: { 'Authorization': authHeader }
                })
                .then(function (r) { return r.json(); })
                .then(function (collections) {
                    if (!collections || !collections.length) {
                        status.textContent = 'No Romm collections found.';
                        return;
                    }
                    status.textContent = 'Collections synced!';
                    status.style.color = '#52B54B';
                })
                .catch(function (err) {
                    status.textContent = 'Sync failed: ' + err;
                    status.style.color = '#FF4444';
                });
            });
        }
    }

    window.jeInitSettingsTab = function (page) {
        loadConfig(page);
        bindSettingsListeners(page);
    };
})();
