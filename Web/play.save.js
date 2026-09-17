// JellyEmu Play! Save & Memory Card Manager
// Syncs PS2 Virtual Folder Memory Cards (/work/mc0/*, /vfs/mc0/*) with JellyEmu server save endpoints.
(function(window) {
    'use strict';

    function uint8ToBase64(uint8) {
        let binary = '';
        const len = uint8.byteLength;
        const chunkSize = 0x8000;
        for (let i = 0; i < len; i += chunkSize) {
            const chunk = uint8.subarray(i, Math.min(i + chunkSize, len));
            binary += String.fromCharCode.apply(null, chunk);
        }
        return btoa(binary);
    }

    function base64ToUint8(base64) {
        const binary = atob(base64);
        const len = binary.length;
        const bytes = new Uint8Array(len);
        for (let i = 0; i < len; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    }

    class PlaySaveManager {
        constructor(config) {
            this.itemId = config.itemId || '';
            this.userId = config.userId || '';
            this.activeSlot = config.activeSlot || 1;
            this.saveGetUrl = config.saveGetUrl || '';
            this.savePostUrl = config.savePostUrl || '';
            this.playModule = null;
            this.token = config.token || '';
            if (!this.token) {
                try {
                    const creds = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
                    const server = (creds.Servers || []).find(s => s.UserId === this.userId) || (creds.Servers || [])[0];
                    this.token = (server && server.AccessToken) || '';
                } catch (e) {}
            }
            this.mcPaths = [
                '/work/mc0',
                '/work/mc1',
                '/work/vfs/mc0',
                '/work/vfs/mc1',
                '/work/Play!/mc0',
                '/work/Play!/mc1',
                '/home/web_user/.config/Play!/mc0',
                '/home/web_user/.config/Play!/mc1',
                '/home/web_user/.local/share/Play!/mc0',
                '/home/web_user/.local/share/Play!/mc1',
                '/home/web_user/Play!/mc0',
                '/home/web_user/mc0',
                '/vfs/mc0',
                '/vfs/mc1',
                '/mc0',
                '/mc1'
            ];
            this.hasSaves = !!this.userId;
        }

        setPlayModule(playModule) {
            this.playModule = playModule;
            this.ensureDirectories();
        }

        ensureDirectories() {
            if (!this.playModule || !this.playModule.FS) return;
            const fs = this.playModule.FS;
            const dirs = [
                '/work',
                '/work/mc0',
                '/work/mc1',
                '/work/vfs',
                '/work/vfs/mc0',
                '/work/vfs/mc1',
                '/work/Play!',
                '/work/Play!/mc0',
                '/work/Play!/mc1',
                '/home',
                '/home/web_user',
                '/home/web_user/.config',
                '/home/web_user/.config/Play!',
                '/home/web_user/.config/Play!/mc0',
                '/home/web_user/.config/Play!/mc1',
                '/home/web_user/.local',
                '/home/web_user/.local/share',
                '/home/web_user/.local/share/Play!',
                '/home/web_user/.local/share/Play!/mc0',
                '/home/web_user/.local/share/Play!/mc1',
                '/home/web_user/Play!',
                '/home/web_user/Play!/mc0',
                '/home/web_user/mc0',
                '/vfs',
                '/vfs/mc0',
                '/vfs/mc1',
                '/mc0',
                '/mc1'
            ];
            for (const d of dirs) {
                try {
                    const parts = d.split('/').filter(Boolean);
                    let curr = '';
                    for (const p of parts) {
                        curr += '/' + p;
                        try { fs.mkdir(curr); } catch (e) {}
                    }
                } catch (e) {}
            }
        }

        collectAllSaveFiles() {
            if (!this.playModule || !this.playModule.FS) return [];
            const fs = this.playModule.FS;
            const allFiles = [];
            const visited = new Set();

            const walk = (currentPath, basePrefix) => {
                try {
                    if (!fs.analyzePath(currentPath).exists) return;
                    const entries = fs.readdir(currentPath);
                    for (const entry of entries) {
                        if (entry === '.' || entry === '..') continue;
                        const full = (currentPath === '/' ? '' : currentPath) + '/' + entry;
                        if (visited.has(full)) continue;
                        visited.add(full);

                        try {
                            const stat = fs.stat(full);
                            if (fs.isDir(stat.mode)) {
                                walk(full, basePrefix);
                            } else if (fs.isFile(stat.mode)) {
                                const data = fs.readFile(full);
                                if (data && data.byteLength > 0) {
                                    const relPath = full.startsWith(basePrefix) ? full.substring(basePrefix.length) : full;
                                    const cleanRel = relPath.startsWith('/') ? relPath.substring(1) : relPath;
                                    allFiles.push({
                                        fullPath: full,
                                        path: cleanRel,
                                        basePrefix: basePrefix,
                                        base64: uint8ToBase64(data),
                                        byteLength: data.byteLength
                                    });
                                }
                            }
                        } catch (e) {}
                    }
                } catch (e) {}
            };

            // Walk explicitly known memory card search paths
            for (const p of this.mcPaths) {
                walk(p, p);
            }

            // If nothing found in standard paths, search entire virtual filesystem (excluding /dev and /proc)
            if (allFiles.length === 0) {
                const deepWalk = (dir) => {
                    if (dir === '/dev' || dir === '/proc') return;
                    try {
                        if (!fs.analyzePath(dir).exists) return;
                        const entries = fs.readdir(dir);
                        for (const entry of entries) {
                            if (entry === '.' || entry === '..') continue;
                            const full = (dir === '/' ? '' : dir) + '/' + entry;
                            if (visited.has(full)) continue;
                            visited.add(full);

                            try {
                                const stat = fs.stat(full);
                                if (fs.isDir(stat.mode)) {
                                    deepWalk(full);
                                } else if (fs.isFile(stat.mode)) {
                                    // Match PS2 save formats or any non-empty data file created
                                    const isSaveLikely = full.includes('mc0') || full.includes('mc1') || full.includes('Play!') ||
                                        entry.startsWith('BA') || entry.startsWith('BI') || entry.startsWith('BE') || entry.startsWith('SC') || entry.startsWith('SL');
                                    if (isSaveLikely) {
                                        const data = fs.readFile(full);
                                        if (data && data.byteLength > 0) {
                                            const cleanRel = full.replace(/^.*\/mc[01]\//, '').replace(/^\//, '');
                                            allFiles.push({
                                                fullPath: full,
                                                path: cleanRel,
                                                basePrefix: dir,
                                                base64: uint8ToBase64(data),
                                                byteLength: data.byteLength
                                            });
                                        }
                                    }
                                }
                            } catch (e) {}
                        }
                    } catch (e) {}
                };

                deepWalk('/home');
                deepWalk('/work');
                deepWalk('/');
            }

            return allFiles;
        }

        async restoreMemoryCard() {
            if (!this.playModule || !this.playModule.FS) return;

            try {
                this.ensureDirectories();
                if (!this.saveGetUrl) {
                    console.log('[JellyEmu Play!] No saveGetUrl configured (guest session).');
                    return;
                }

                const url = `${this.saveGetUrl}?slot=${this.activeSlot}`;
                console.log('[JellyEmu Play!] Fetching user memory card from:', url);

                const headers = {};
                if (this.token) {
                    headers['Authorization'] = `MediaBrowser Token="${this.token}"`;
                    headers['X-Emby-Token'] = this.token;
                    headers['X-MediaBrowser-Token'] = this.token;
                }

                const response = await fetch(url, { headers });
                if (!response.ok) {
                    console.log('[JellyEmu Play!] No existing cloud save found (status:', response.status, ')');
                    return;
                }

                const rawText = await response.text();
                if (!rawText || rawText.trim().length === 0) return;

                const fs = this.playModule.FS;
                let parsed = null;
                try {
                    parsed = JSON.parse(rawText);
                } catch (e) {}

                const targetDirs = [
                    '/work/mc0',
                    '/work/vfs/mc0',
                    '/work/Play!/mc0',
                    '/home/web_user/.config/Play!/mc0',
                    '/home/web_user/.local/share/Play!/mc0',
                    '/home/web_user/Play!/mc0',
                    '/home/web_user/mc0',
                    '/vfs/mc0',
                    '/mc0'
                ];

                // Known cross-regional mapping table for popular PS2 titles
                const regionMap = [
                    { us: 'BASLUS-20238', eu: 'BESLES-50386', jp: 'BISLPM-65063' }, // Crash Wrath of Cortex
                    { us: 'BASLUS-20909', eu: 'BESLES-52568', jp: 'BISLPM-65809' }, // Crash Twinsanity
                    { us: 'BASCUS-97124', eu: 'BESCES-50284', jp: 'BISCPS-15021' }, // Jak and Daxter
                    { us: 'BASCUS-97194', eu: 'BESCES-50885', jp: 'BISCPS-15025' }, // Ape Escape 2
                    { us: 'BASCUS-97198', eu: 'BESCES-50916', jp: 'BISCPS-15043' }, // Sly Cooper
                    { us: 'BASCUS-97199', eu: 'BESCES-50916', jp: 'BISCPS-15037' }  // Ratchet & Clank
                ];

                const getAliases = (cleanPath) => {
                    const aliases = [];
                    for (const entry of regionMap) {
                        const keys = Object.values(entry);
                        for (const k of keys) {
                            if (cleanPath.startsWith(k)) {
                                for (const targetKey of keys) {
                                    if (targetKey !== k) {
                                        aliases.push(cleanPath.split(k).join(targetKey));
                                    }
                                }
                            }
                        }
                    }
                    if (aliases.length === 0 && /^B[AEIK][A-Z]{4}-\d+/.test(cleanPath)) {
                        const prefix = cleanPath.substring(0, 2);
                        const rest = cleanPath.substring(2);
                        const otherPrefixes = ['BA', 'BE', 'BI'].filter(p => p !== prefix);
                        for (const op of otherPrefixes) {
                            aliases.push(op + rest);
                        }
                    }
                    return aliases;
                };

                if (parsed && Array.isArray(parsed.files)) {
                    let restoredCount = 0;

                    for (const item of parsed.files) {
                        if (!item.path || !item.base64) continue;
                        const bytes = base64ToUint8(item.base64);
                        const cleanPath = item.path.replace(/^\/+/, '');
                        const allPathsToWrite = [cleanPath, ...getAliases(cleanPath)];

                        for (const pathToWrite of allPathsToWrite) {
                            for (const targetDir of targetDirs) {
                                const fullPath = targetDir + '/' + pathToWrite;
                                const parts = fullPath.split('/').filter(Boolean);
                                let curDir = '';
                                for (let i = 0; i < parts.length - 1; i++) {
                                    curDir += '/' + parts[i];
                                    try {
                                        if (typeof fs.mkdirTree === 'function') {
                                            fs.mkdirTree(curDir);
                                        } else {
                                            fs.mkdir(curDir);
                                        }
                                    } catch (err) {}
                                }
                                try {
                                    fs.writeFile(fullPath, bytes, { flags: 'w' });
                                    console.log('[JellyEmu Play!] Restored save file:', fullPath, `(${bytes.byteLength} bytes)`);
                                } catch (writeErr) {
                                    console.warn('[JellyEmu Play!] Failed to write save file:', fullPath, writeErr);
                                }
                            }
                        }
                        restoredCount++;
                    }
                    console.log(`[JellyEmu Play!] Restored ${restoredCount} memory card files across target paths with regional aliases.`);
                    if (window._jeToast && restoredCount > 0) window._jeToast(`Memory Card loaded (${restoredCount} save items)`, 2500);
                }
            } catch (err) {
                console.warn('[JellyEmu Play!] Failed to restore memory card:', err);
            }
        }

        async flushMemoryCard() {
            if (!this.playModule || !this.playModule.FS) {
                if (window._jeToast) window._jeToast('Emulation not ready yet', 2000);
                return;
            }

            try {
                this.ensureDirectories();
                const files = this.collectAllSaveFiles();

                if (files.length === 0) {
                    console.log('[JellyEmu Play!] Memory card directory is currently empty.');
                    if (window._jeToast) window._jeToast('Memory card is empty. Create a save inside the game first!', 3500);
                    return;
                }

                if (!this.savePostUrl) {
                    // Fallback for guest mode / no auth: Download local backup
                    const bundle = {
                        version: 1,
                        game: this.itemId,
                        timestamp: Date.now(),
                        files: files
                    };
                    const jsonStr = JSON.stringify(bundle, null, 2);
                    const blob = new Blob([jsonStr], { type: 'application/json' });
                    const a = document.createElement('a');
                    a.href = URL.createObjectURL(blob);
                    a.download = `PlayStation2_${this.itemId}_MemoryCard.json`;
                    a.click();
                    if (window._jeToast) window._jeToast(`Downloaded Memory Card backup (${files.length} files)`, 2500);
                    return;
                }

                const bundle = {
                    version: 1,
                    timestamp: Date.now(),
                    files: files.map(f => ({ path: f.path, base64: f.base64, size: f.byteLength }))
                };

                const jsonStr = JSON.stringify(bundle);
                const url = `${this.savePostUrl}?slot=${this.activeSlot}`;
                console.log(`[JellyEmu Play!] Uploading memory card bundle (${files.length} files, ${jsonStr.length} bytes) to:`, url);

                const blob = new Blob([jsonStr], { type: 'application/octet-stream' });
                const headers = { 'Content-Type': 'application/octet-stream' };
                if (this.token) {
                    headers['Authorization'] = `MediaBrowser Token="${this.token}"`;
                    headers['X-Emby-Token'] = this.token;
                    headers['X-MediaBrowser-Token'] = this.token;
                }

                const response = await fetch(url, {
                    method: 'POST',
                    headers: headers,
                    body: blob
                });

                if (response.ok) {
                    console.log('[JellyEmu Play!] Memory card bundle saved successfully.');
                    if (window._jeToast) window._jeToast(`Memory Card saved (${files.length} files)`, 2500);
                } else {
                    console.warn('[JellyEmu Play!] Server save returned error status:', response.status);
                    if (window._jeToast) window._jeToast(`Save upload failed (HTTP ${response.status})`, 3000);
                }
            } catch (err) {
                console.error('[JellyEmu Play!] Failed to save memory card:', err);
                if (window._jeToast) window._jeToast(`Save failed: ${err.message}`, 3000);
            }
        }

        setupAutoSave() {
            // Auto-save disabled to prevent overwriting cloud saves on tab switch/unload
        }
    }

    window.PlaySaveManager = PlaySaveManager;
})(window);
