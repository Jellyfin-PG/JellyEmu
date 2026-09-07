/**
 * JellyEmu Netplay Multiplayer UI & Engine Integration
 *
 * Provides a modern, responsive UI for browsing rooms, hosting games,
 * managing active sessions, and setting player nicknames via EmulatorJS.
 *
 * Depends on:
 *   - window.JellyEmuConfig       { itemId, userId, token, hasNetplay, netplayServer, gameId }
 *   - window.EJS_emulator         The active EmulatorJS instance
 *   - window._jeOpenPopup         Popup manager helper from ejs.html
 *   - window._jeClosePopup        Popup manager helper from ejs.html
 */
(function () {
    'use strict';

    var cfg = window.JellyEmuConfig || {};
    var netplayServer = window.location.origin + '/jellyemu/netplay';
    var gameId = cfg.gameId || window.EJS_gameID || 0;

    // State
    var state = {
        inRoom: false,
        isHost: false,
        roomName: '',
        roomId: '',
        password: '',
        players: {},
        autoRefreshTimer: null,
        activeTab: 'rooms'
    };

    function emu() {
        return window.EJS_emulator || null;
    }

    // Wrap window.io so EmulatorJS routes to /jellyemu/netplay/socket.io with the active Jellyfin token
    var _curIo = window.io;
    function wrapSocketIo() {
        var targetFn = _curIo || window.io;
        if (typeof targetFn === 'function' && !targetFn._jeWrapped) {
            var origIo = targetFn;
            var wrappedIo = function (url, opts) {
                if (typeof url === 'object' && url !== null && !opts) {
                    opts = url;
                    url = undefined;
                }
                opts = opts || {};
                var targetUrl = (typeof url === 'string') ? url : '';
                if (!targetUrl || targetUrl.indexOf('/jellyemu/netplay') !== -1 || targetUrl.indexOf(window.location.host) !== -1 || targetUrl.startsWith('/')) {
                    opts.path = '/jellyemu/netplay/socket.io';
                    opts.transports = ['websocket'];
                    opts.upgrade = false;
                    var token = window._jellyToken || (cfg && cfg.token) || '';
                    if (token) {
                        opts.query = opts.query || {};
                        opts.query.api_key = token;
                        opts.query.token = token;
                        opts.auth = opts.auth || {};
                        opts.auth.token = token;
                        opts.extraHeaders = opts.extraHeaders || {};
                        opts.extraHeaders['X-Emby-Token'] = token;
                        opts.extraHeaders['X-MediaBrowser-Token'] = token;
                        opts.extraHeaders['Authorization'] = 'MediaBrowser Token="' + token + '"';
                    }
                    var socket = origIo.call(this, window.location.origin, opts);
                    if (socket && typeof socket.on === 'function' && !socket._jeWrappedEvents) {
                        socket._jeWrappedEvents = true;
                        var handleExit = function (reason) {
                            if (!state.inRoom || _isLeavingRoom) return;
                            console.log('[JellyEmu Netplay] Room exit triggered by socket event:', reason);
                            showNetplayToast(reason || 'Host left the game. Returning to single player...', 'info');
                            performRoomLeftCleanup();
                        };

                        socket.on('host-left', function (data) {
                            handleExit((data && data.reason) || 'Host left the game. Returning to single player...');
                        });
                        socket.on('room-closed', function (data) {
                            handleExit((data && data.reason) || 'Room was closed by the host. Returning to single player...');
                        });
                        socket.on('data-message', function (data) {
                            if (data && (data.type === 'host-left' || data['host-left'])) {
                                handleExit('Host left the game. Returning to single player...');
                            }
                        });
                    }
                    return socket;
                }
                return origIo.call(this, url, opts);
            };
            wrappedIo._jeWrapped = true;
            for (var prop in origIo) {
                if (origIo.hasOwnProperty(prop)) {
                    wrappedIo[prop] = origIo[prop];
                }
            }
            _curIo = wrappedIo;
            try { window.io = wrappedIo; } catch (e) { }
        }
    }

    try {
        Object.defineProperty(window, 'io', {
            configurable: true,
            enumerable: true,
            get: function () {
                return _curIo;
            },
            set: function (val) {
                _curIo = val;
                wrapSocketIo();
            }
        });
    } catch (ex) { }
    wrapSocketIo();

    // Player Nickname (isolated per-session by default to avoid multi-window collisions)
    function getPlayerName() {
        try {
            var sess = sessionStorage.getItem('jellyemu-netplay-name');
            if (sess && sess.trim()) return sess.trim();

            var local = localStorage.getItem('jellyemu-netplay-name');
            // If user explicitly saved a custom nickname in localStorage (not a default Player_xxxx)
            if (local && local.trim() && local !== 'Player_2999' && !local.startsWith('Player_')) {
                sessionStorage.setItem('jellyemu-netplay-name', local.trim());
                return local.trim();
            }
        } catch (e) { }

        var randNum = Math.floor(1000 + Math.random() * 9000);
        var defaultName = 'Player_' + randNum;
        try {
            sessionStorage.setItem('jellyemu-netplay-name', defaultName);
        } catch (e) { }
        return defaultName;
    }

    function setPlayerName(name) {
        if (!name || !name.trim()) return;
        var trimmed = name.trim().substring(0, 20);
        try {
            sessionStorage.setItem('jellyemu-netplay-name', trimmed);
            localStorage.setItem('jellyemu-netplay-name', trimmed);
        } catch (e) { }
        var e = emu();
        if (e && e.netplay) {
            e.netplay.name = trimmed;
        }
        var nameInput = document.getElementById('je-np-player-name');
        if (nameInput) nameInput.value = trimmed;
    }

    // Binary buffer conversion helper for cross-browser Socket.IO payloads
    function toUint8Array(data) {
        if (!data) return new Uint8Array(0);
        if (data instanceof Uint8Array) return data;
        if (data instanceof ArrayBuffer) return new Uint8Array(data);
        if (data.buffer instanceof ArrayBuffer) {
            return new Uint8Array(data.buffer, data.byteOffset || 0, data.byteLength || data.length);
        }
        if (data.data && Array.isArray(data.data)) return new Uint8Array(data.data);
        if (Array.isArray(data)) return new Uint8Array(data);
        if (typeof data === 'object') {
            try {
                var vals = Object.values(data);
                if (vals.length > 0) return new Uint8Array(vals);
            } catch (e) { }
        }
        return new Uint8Array(0);
    }

    function showNetplayToast(msg, icon) {
        try {
            var existing = document.getElementById('je-netplay-toast');
            if (existing) existing.remove();
            var toast = document.createElement('div');
            toast.id = 'je-netplay-toast';
            toast.style.cssText = 'position:fixed;bottom:70px;left:50%;transform:translateX(-50%);background:rgba(20,22,30,0.92);color:#fff;padding:8px 16px;border-radius:20px;font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,sans-serif;font-size:13px;display:flex;align-items:center;gap:8px;z-index:999999;box-shadow:0 4px 16px rgba(0,0,0,0.5);border:1px solid rgba(255,255,255,0.15);backdrop-filter:blur(8px);pointer-events:none;transition:opacity 0.3s ease;';
            toast.innerHTML = '<span class="material-icons" style="font-size:16px;color:#00a4dc;">' + (icon || 'sync') + '</span> <span>' + msg + '</span>';
            document.body.appendChild(toast);
            setTimeout(function () {
                if (toast && toast.parentNode) {
                    toast.style.opacity = '0';
                    setTimeout(function () { if (toast && toast.parentNode) toast.remove(); }, 300);
                }
            }, 2500);
        } catch (e) {
            console.log('[JellyEmu Netplay]', msg);
        }
    }

    // WebRTC Live Video Streaming & Input Architecture
    var defaultIce = [
        { urls: 'stun:stun.l.google.com:19302' },
        { urls: 'stun:stun1.l.google.com:19302' },
        { urls: 'stun:stun2.l.google.com:19302' }
    ];

    function parseIceServers(raw) {
        if (!raw) return null;
        if (Array.isArray(raw)) return raw.length ? raw : null;
        if (typeof raw !== 'string') return null;

        var str = raw.trim();
        if (!str) return null;

        if (str.startsWith('[') && str.endsWith(']')) {
            try {
                var parsed = JSON.parse(str);
                if (Array.isArray(parsed) && parsed.length) return parsed;
            } catch (ex) { }
        }

        var lines = str.split(/[\r\n,;]+/).map(function (s) { return s.trim(); }).filter(Boolean);
        if (!lines.length) return null;

        var servers = [];
        lines.forEach(function (entry) {
            if (entry.startsWith('{') && entry.endsWith('}')) {
                try {
                    var obj = JSON.parse(entry);
                    if (obj.urls) {
                        servers.push(obj);
                        return;
                    }
                } catch (e) { }
            }
            servers.push({ urls: entry });
        });
        return servers.length ? servers : null;
    }

    var customIce = parseIceServers((window.JellyEmuConfig && (window.JellyEmuConfig.netplayIceServers || window.JellyEmuConfig.netplayICEServers)))
        || parseIceServers(window.EJS_netplayICEServers);
    var activeIceServers = customIce || defaultIce;
    var rtcConfig = {
        iceServers: activeIceServers,
        iceCandidatePoolSize: 4
    };
    window.EJS_netplayICEServers = activeIceServers;
    var hostPeerConnections = {};
    var hostIceQueues = {};
    var guestPeerConnection = null;
    var guestIceQueue = [];
    var guestInputDataChannel = null;
    var localHostStream = null;
    var hostAudioDestNode = null;
    var guestVideoWatchdogTimer = null;
    var _isLeavingRoom = false;

    function safelyClosePeerConnection(pc) {
        if (!pc) return;
        try {
            pc.onconnectionstatechange = null;
            pc.oniceconnectionstatechange = null;
            pc.onicecandidate = null;
            pc.ontrack = null;
            pc.ondatachannel = null;
            if (typeof pc.close === 'function') {
                pc.close();
            }
        } catch (ex) { }
    }

    // Real-Time Netplay Ping / Latency Tracking (Server Ping)
    var playerPings = {};
    var pingMeasurementTimer = null;
    var _isMeasuringPing = false;

    async function measureServerPing() {
        var start = (window.performance && performance.now) ? performance.now() : Date.now();
        var controller = (typeof AbortController !== 'undefined') ? new AbortController() : null;
        var timer = controller ? setTimeout(function () { controller.abort(); }, 3000) : null;
        try {
            var res = await fetch(netplayServer + '/ping?t=' + Date.now(), {
                cache: 'no-store',
                signal: controller ? controller.signal : undefined,
                headers: { 'Accept': 'application/json' }
            });
            if (timer) clearTimeout(timer);
            if (!res.ok) throw new Error('HTTP ' + res.status);
            await res.json();
            var end = (window.performance && performance.now) ? performance.now() : Date.now();
            return Math.max(1, Math.round(end - start));
        } catch (e) {
            if (timer) clearTimeout(timer);
            return null;
        }
    }

    function getPingClass(ms) {
        if (ms === null || ms === undefined || ms < 0) return 'je-np-ping-measuring';
        if (ms <= 60) return 'je-np-ping-good';
        if (ms <= 130) return 'je-np-ping-medium';
        return 'je-np-ping-poor';
    }

    function formatPingText(ms) {
        if (ms === null || ms === undefined || ms < 0) return '-- ms';
        return ms + ' ms';
    }

    function getPingForPlayer(p, pid) {
        if (!p) return null;
        if (p.socketId && playerPings[p.socketId] !== undefined) return playerPings[p.socketId];
        if (playerPings[pid] !== undefined) return playerPings[pid];
        if (p.playerId && playerPings[p.playerId] !== undefined) return playerPings[p.playerId];
        if (p.userid && playerPings[p.userid] !== undefined) return playerPings[p.userid];
        return null;
    }

    async function refreshPings() {
        if (!state.inRoom || _isLeavingRoom) return;
        if (_isMeasuringPing) return;
        _isMeasuringPing = true;

        try {
            var myPing = await measureServerPing();
            if (!state.inRoom || _isLeavingRoom) return;

            var e = emu();
            var myId = (e && e.netplay && e.netplay.playerID) || '';
            var mySocketId = (e && e.netplay && e.netplay.socket && e.netplay.socket.id) || '';

            if (myPing !== null) {
                playerPings['self'] = myPing;
                if (myId) playerPings[myId] = myPing;
                if (mySocketId) playerPings[mySocketId] = myPing;
            }

            // Broadcast own server ping to room peers via socket.io
            if (e && e.netplay && e.netplay.socket && e.netplay.socket.connected && myPing !== null) {
                try {
                    e.netplay.socket.emit('data-message', {
                        jeServerPing: true,
                        playerId: myId,
                        socketId: mySocketId,
                        ping: myPing
                    });
                } catch (ex) { }
            }

            // If host, notify server of host ping so room list stays up to date
            if (state.isHost && e && e.netplay && e.netplay.socket && e.netplay.socket.connected && myPing !== null) {
                try {
                    e.netplay.socket.emit('host-ping', { ping: myPing });
                } catch (ex) { }
            }

            // If host, sync complete ping map snapshot to guests so newly connected peers see all pings
            if (state.isHost && e && e.netplay && e.netplay.socket && e.netplay.socket.connected) {
                try {
                    e.netplay.socket.emit('data-message', {
                        jePingSync: true,
                        pings: playerPings
                    });
                } catch (ex) { }
            }

            updatePingBadgeElements();
        } finally {
            _isMeasuringPing = false;
        }
    }

    function updatePingBadgeElements() {
        var playersMap = state.players || {};
        var e = emu();
        var myId = (e && e.netplay && e.netplay.playerID) || '';
        var mySocketId = (e && e.netplay && e.netplay.socket && e.netplay.socket.id) || '';

        Object.keys(playersMap).forEach(function (pid) {
            var p = playersMap[pid];
            if (!p) return;
            var badgeId = 'je-np-ping-' + String(pid).replace(/[^a-zA-Z0-9_-]/g, '_');
            var badge = document.getElementById(badgeId);
            if (!badge) return;

            var isMe = (myId && (pid === myId || p.userid === myId || p.playerId === myId)) ||
                       (mySocketId && p.socketId === mySocketId);

            var pingVal = isMe
                ? (playerPings['self'] !== undefined ? playerPings['self'] : (myId ? playerPings[myId] : null))
                : getPingForPlayer(p, pid);

            var pingClass = getPingClass(pingVal);
            var pingText = formatPingText(pingVal);
            var pingTitle = (pingVal !== null && pingVal !== undefined) ? 'Server ping: ' + pingVal + ' ms' : 'Measuring ping…';

            badge.className = 'je-np-ping-badge ' + pingClass;
            badge.title = pingTitle;
            var valSpan = badge.querySelector('.je-np-ping-val');
            if (valSpan) {
                if (valSpan.textContent !== pingText) valSpan.textContent = pingText;
            } else if (badge.textContent !== pingText) {
                badge.textContent = pingText;
            }
        });
    }

    function startPingMeasurement() {
        if (pingMeasurementTimer) clearInterval(pingMeasurementTimer);
        pingMeasurementTimer = setInterval(refreshPings, 2000);
        setTimeout(refreshPings, 100);
    }

    function stopPingMeasurement() {
        if (pingMeasurementTimer) {
            clearInterval(pingMeasurementTimer);
            pingMeasurementTimer = null;
        }
        playerPings = {};
        _isMeasuringPing = false;
    }

    function getHostMediaStream(e) {
        if (localHostStream && localHostStream.active) {
            var vTracks = localHostStream.getVideoTracks();
            if (vTracks.length > 0 && vTracks[0].readyState === 'live') {
                return localHostStream;
            }
        }

        var canvas = (e && e.canvas) || document.querySelector('#canvas') || document.querySelector('canvas');
        if (!canvas) return null;

        var stream = null;
        if (typeof canvas.captureStream === 'function') {
            try {
                stream = canvas.captureStream(60);
                console.log('[JellyEmu Netplay] Host capturing canvas stream. Core native render resolution: ' + canvas.width + 'x' + canvas.height + ' (CSS display: ' + canvas.clientWidth + 'x' + canvas.clientHeight + ')');
            } catch (err) {
                console.warn('[JellyEmu Netplay] canvas.captureStream error:', err);
            }
        }
        if (!stream) return null;

        var vTracks = stream.getVideoTracks();
        if (vTracks && vTracks.length > 0) {
            try {
                vTracks[0].contentHint = 'motion';
            } catch (err) { }
        }

        // Capture Emulator audio track
        try {
            if (e && e.Module && e.Module.AL && e.Module.AL.currentCtx && e.Module.AL.currentCtx.audioCtx) {
                var alCtx = e.Module.AL.currentCtx;
                var audioCtx = alCtx.audioCtx;
                var dest = audioCtx.createMediaStreamDestination();
                hostAudioDestNode = dest;
                if (alCtx.sources) {
                    for (var s in alCtx.sources) {
                        if (alCtx.sources[s] && alCtx.sources[s].gain) {
                            try { alCtx.sources[s].gain.connect(dest); } catch (ex) { }
                        }
                    }
                }
                var audioTracks = dest.stream.getAudioTracks();
                if (audioTracks && audioTracks.length > 0) {
                    audioTracks.forEach(function (t) {
                        stream.addTrack(t);
                    });
                }
            }
        } catch (err) {
            console.warn('[JellyEmu Netplay] Audio capture error:', err);
        }

        localHostStream = stream;
        return stream;
    }

    function showGuestVideoOverlay(remoteStream, e) {
        if (!remoteStream) return;
        var existing = document.getElementById('je-netplay-video');
        if (!existing) {
            var video = document.createElement('video');
            video.id = 'je-netplay-video';
            video.autoplay = true;
            video.playsInline = true;
            video.muted = true;
            video.disablePictureInPicture = true;
            video.disableRemotePlayback = true;
            video.style.cssText = 'position:absolute;top:0;left:0;right:0;bottom:0;margin:auto;width:100%;height:100%;object-fit:contain;background:#000;z-index:1000;pointer-events:none;transform:translateZ(0);will-change:transform;';

            var container = null;
            if (e && e.elements && e.elements.container) {
                container = e.elements.container;
            } else {
                var c = document.querySelector('#canvas') || document.querySelector('canvas');
                container = (c && c.parentElement) || document.body;
            }
            if (container) {
                container.style.position = 'relative';
                container.appendChild(video);
            }
            existing = video;
        }

        if (existing.srcObject !== remoteStream) {
            existing.srcObject = remoteStream;
            var playPromise = existing.play();
            if (playPromise && typeof playPromise.catch === 'function') {
                playPromise.then(function () {
                    existing.muted = false;
                }).catch(function (err) {
                    if (err && err.name === 'AbortError') return;
                    console.warn('[JellyEmu Netplay] Video autoplay prevented, attempting muted playback:', err);
                    existing.muted = true;
                    existing.play().catch(function (mErr) {
                        if (mErr && mErr.name === 'AbortError') return;
                        console.warn('[JellyEmu Netplay] Muted video playback also prevented:', mErr);
                    });
                });
            }
        }

        var logRes = function () {
            if (existing && existing.videoWidth && existing.videoHeight) {
                console.log('[JellyEmu Netplay] Guest playing WebRTC stream. Received stream resolution: ' + existing.videoWidth + 'x' + existing.videoHeight + ' (Upscaled to display: ' + existing.clientWidth + 'x' + existing.clientHeight + ')');
                try {
                    window.dispatchEvent(new CustomEvent('jellyemu:netplay-video-mounted'));
                } catch (e) { }
            }
        };
        existing.onloadedmetadata = logRes;
        existing.onresize = logRes;
        try { window.dispatchEvent(new CustomEvent('jellyemu:netplay-video-mounted')); } catch (e) { }

        // Mute guest local core to avoid echoing audio
        if (e && typeof e.setVolume === 'function') {
            e.setVolume(0);
        }

        showNetplayToast('Connected to host!', 'videocam');
    }

    function removeGuestVideoOverlay() {
        var video = document.getElementById('je-netplay-video');
        if (video) {
            try {
                video.pause();
                if (video.srcObject) {
                    if (typeof video.srcObject.getTracks === 'function') {
                        video.srcObject.getTracks().forEach(function (t) {
                            try { t.stop(); } catch (e) { }
                        });
                    }
                    video.srcObject = null;
                }
                video.removeAttribute('src');
                try { video.load(); } catch (e) { }
            } catch (ex) { }
            try {
                if (video.parentNode) {
                    video.parentNode.removeChild(video);
                } else {
                    video.remove();
                }
            } catch (ex) { }
        }
    }

    function unfreezeGuestCompletely(e) {
        if (!e) return;

        // Restore local input handling if hijacked by EmulatorJS netplay guest stub
        if (e.netplay && e.netplay.originalSimulateInput && e.gameManager && e.gameManager.functions) {
            try {
                e.gameManager.functions.simulateInput = e.netplay.originalSimulateInput;
            } catch (err) { }
        }

        // Unfreeze guest state inside EmulatorJS engine
        if (e.netplay) {
            if (typeof e.netplay.unfreezeGuest === 'function') {
                try { e.netplay.unfreezeGuest(); } catch (err) { }
            }
            e.netplay.frozen = null;
            if (typeof e.netplay.stopDrawLoop === 'function') {
                try { e.netplay.stopDrawLoop(); } catch (err) { }
            }
            if (e.netplay.video) {
                try {
                    e.netplay.video.pause();
                    if (e.netplay.video.srcObject && typeof e.netplay.video.srcObject.getTracks === 'function') {
                        e.netplay.video.srcObject.getTracks().forEach(function (t) { try { t.stop(); } catch (err) { } });
                    }
                    e.netplay.video.srcObject = null;
                    e.netplay.video.remove();
                } catch (err) { }
                e.netplay.video = null;
            }
        }

        // Remove ALL WebRTC video overlays from DOM
        removeGuestVideoOverlay();
        try {
            var allVideos = document.querySelectorAll('#je-netplay-video, video[id^="je-"], video');
            allVideos.forEach(function (v) {
                try {
                    v.pause();
                    if (v.srcObject && typeof v.srcObject.getTracks === 'function') {
                        v.srcObject.getTracks().forEach(function (t) { try { t.stop(); } catch (err) { } });
                    }
                    v.srcObject = null;
                    v.removeAttribute('src');
                    v.remove();
                } catch (err) { }
            });
        } catch (err) { }

        // Clean up any remote audio elements injected by EmulatorJS
        try {
            var remoteAudios = document.querySelectorAll('audio[id^="ejs-remote-audio-"]');
            remoteAudios.forEach(function (a) {
                try { a.pause(); a.srcObject = null; a.remove(); } catch (err) { }
            });
        } catch (err) { }

        // Find main emulator canvas and completely unhide it
        var mainCanvas = document.getElementById('canvas') || e.canvas || (e.netplay && e.netplay.emu && e.netplay.emu.canvas);
        if (mainCanvas) {
            mainCanvas.classList.remove('ejs_netplay_offscreen_canvas');
            mainCanvas.style.removeProperty('display');
            mainCanvas.style.display = 'block';
            mainCanvas.style.visibility = 'visible';
            mainCanvas.style.opacity = '1';
        }
        if (e.canvas) {
            e.canvas.classList.remove('ejs_netplay_offscreen_canvas');
            e.canvas.style.removeProperty('display');
            e.canvas.style.display = 'block';
            e.canvas.style.visibility = 'visible';
            e.canvas.style.opacity = '1';
        }
        document.querySelectorAll('.ejs_netplay_offscreen_canvas').forEach(function (c) {
            c.classList.remove('ejs_netplay_offscreen_canvas');
            c.style.removeProperty('display');
            c.style.display = 'block';
            c.style.visibility = 'visible';
            c.style.opacity = '1';
        });

        // Completely destroy EmulatorJS netplayCanvas overlay if present
        if (e.netplayCanvas) {
            try {
                if (e.netplayCanvas.parentNode) e.netplayCanvas.parentNode.removeChild(e.netplayCanvas);
            } catch (err) { }
            e.netplayCanvas = null;
        }
        if (e.netplay && e.netplay.emu && e.netplay.emu.netplayCanvas) {
            var npc = e.netplay.emu.netplayCanvas;
            if (npc.parentNode) {
                try { npc.parentNode.removeChild(npc); } catch (err) { }
            }
            e.netplay.emu.netplayCanvas = null;
        }
        // Restore audio nodes and context safely
        if (e.gameManager && e.gameManager.audioNode && e.gameManager.audioContext) {
            try { e.gameManager.audioNode.connect(e.gameManager.audioContext.destination); } catch (err) { }
        }
        if (e.gameManager && e.gameManager.audioContext && typeof e.gameManager.audioContext.resume === 'function') {
            try { e.gameManager.audioContext.resume().catch(function () { }); } catch (err) { }
        }
        if (e.Module && e.Module.AL && e.Module.AL.currentCtx && e.Module.AL.currentCtx.audioCtx) {
            try { e.Module.AL.currentCtx.audioCtx.resume().catch(function () { }); } catch (err) { }
        }

        // Ensure main loop is running unconditionally
        e.isNetplay = false;
        e.paused = false;
        if (e.netplay) {
            e.netplay.isNetplay = false;
            e.netplay.owner = false;
            e.netplay.frozen = null;
        }
        if (e.gameManager) {
            try { e.gameManager.toggleMainLoop(1); } catch (err) { }
            if (typeof e.gameManager.resume === 'function') {
                try { e.gameManager.resume(); } catch (err) { }
            }
        }
        if (e.gameManager && e.gameManager.functions && typeof e.gameManager.functions.toggleMainLoop === 'function') {
            try { e.gameManager.functions.toggleMainLoop(1); } catch (err) { }
        }
        if (e.Module && typeof e.Module.resumeMainLoop === 'function') {
            try { e.Module.resumeMainLoop(); } catch (err) { }
        }
        if (typeof e.play === 'function') {
            try { e.play(true); } catch (err) { }
        }

        // Defer handleResize to avoid synchronous layout reflow during teardown
        requestAnimationFrame(function () {
            if (e && typeof e.handleResize === 'function') {
                try { e.handleResize(); } catch (err) { }
            }
        });
    }

    function armGuestVideoWatchdog(e) { }

    function requestStreamRenegotiateWithHost(e, reason) {
        if (!e || !e.netplay || !state.inRoom || state.isHost || _isLeavingRoom) return;

        var hostSid = '';
        var players = state.players || {};
        Object.keys(players).forEach(function (pid) {
            var p = players[pid];
            if (p && (p.isOwner || p.owner) && p.socketId) {
                hostSid = p.socketId;
            }
        });
        if (!hostSid && e.netplay.room && e.netplay.room.owner) {
            hostSid = e.netplay.room.owner;
        }

        console.log('[JellyEmu Netplay] Requesting stream renegotiation from host:', hostSid, 'reason:', reason);
        if (hostSid && typeof e.netplay.requestRenegotiate === 'function') {
            try {
                e.netplay.requestRenegotiate(hostSid, reason || 'renegotiate');
            } catch (err) { }
        }
        if (e.netplay.socket) {
            e.netplay.socket.emit('webrtc-signal', {
                target: hostSid || undefined,
                requestRenegotiate: true,
                reason: reason || 'renegotiate'
            });
        }
    }

    function closeAllWebRtc() {
        Object.keys(hostPeerConnections).forEach(function (id) {
            safelyClosePeerConnection(hostPeerConnections[id]);
        });
        hostPeerConnections = {};
        hostIceQueues = {};

        if (guestPeerConnection) {
            safelyClosePeerConnection(guestPeerConnection);
            guestPeerConnection = null;
        }
        guestIceQueue = [];
        guestInputDataChannel = null;
        removeGuestVideoOverlay();

        // Safe disposal of host media stream without calling tr.stop() on canvas capture track
        // Calling stop() on an HTMLCanvasElement.captureStream track freezes the source canvas in Chromium
        localHostStream = null;

        if (hostAudioDestNode) {
            try {
                var e = emu();
                if (e && e.Module && e.Module.AL && e.Module.AL.currentCtx && e.Module.AL.currentCtx.sources) {
                    var sources = e.Module.AL.currentCtx.sources;
                    for (var s in sources) {
                        if (sources[s] && sources[s].gain) {
                            try { sources[s].gain.disconnect(hostAudioDestNode); } catch (e) { }
                        }
                    }
                }
            } catch (ex) { }
            hostAudioDestNode = null;
        }

        var e = emu();
        if (e && e.netplay) {
            if (e.netplay.localStream) {
                try {
                    e.netplay.localStream.getTracks().forEach(function (tr) {
                        try { tr.stop(); } catch (err) { }
                    });
                } catch (err) { }
                e.netplay.localStream = null;
            }
            if (e.netplay.peerConnections) {
                Object.keys(e.netplay.peerConnections).forEach(function (sid) {
                    var conn = e.netplay.peerConnections[sid];
                    if (conn) {
                        if (conn.pc) {
                            safelyClosePeerConnection(conn.pc);
                        } else if (typeof conn.close === 'function') {
                            safelyClosePeerConnection(conn);
                        }
                    }
                });
                e.netplay.peerConnections = {};
            }
        }
    }

    var STREAM_QUALITY_PRESETS = {
        low: { targetHeight: 480, maxBitrate: 2000000, asBandwidth: 2000, tiasBandwidth: 2000000, minBitrate: 1000, startBitrate: 1500 },
        balanced: { targetHeight: 720, maxBitrate: 4500000, asBandwidth: 4500, tiasBandwidth: 4500000, minBitrate: 2000, startBitrate: 3500 },
        high: { targetHeight: 'source', maxBitrate: 8000000, asBandwidth: 8000, tiasBandwidth: 8000000, minBitrate: 3500, startBitrate: 6000 }
    };

    function getSelectedQualityPreset() {
        var saved = (window.localStorage && localStorage.getItem('je_np_stream_quality')) || 'high';
        if (saved === 'source' || saved === 'ultra') saved = 'high';
        if (saved === 'native') saved = 'low';
        return STREAM_QUALITY_PRESETS[saved] ? saved : 'high';
    }

    function getStreamingBitrate() {
        var key = getSelectedQualityPreset();
        return STREAM_QUALITY_PRESETS[key] || STREAM_QUALITY_PRESETS.high;
    }

    function calculateNativeResolutionScale() {
        var e = emu();
        var canvas = (e && e.canvas) || document.querySelector('#canvas') || document.querySelector('canvas');
        if (!canvas || !canvas.height) return 1.0;

        var q = getStreamingBitrate();
        var targetHeight = (q && q.targetHeight) || 'source';

        // 'high' / 'source' means 1:1 direct host canvas stream with ZERO downscaling
        if (targetHeight === 'source' || targetHeight === 0 || !targetHeight) {
            return 1.0;
        }

        if (typeof targetHeight === 'number' && canvas.height > targetHeight) {
            var scale = canvas.height / targetHeight;
            var rounded = Math.min(8.0, Math.max(1.0, Math.round(scale * 100) / 100));
            console.log('[JellyEmu Netplay] Downscaling canvas stream: canvas height ' + canvas.height + ' -> target ' + targetHeight + 'p (scaleDownBy: ' + rounded + ')');
            return rounded;
        }
        return 1.0;
    }

    function setCodecPreferences(pc) {
        try {
            if (!window.RTCRtpSender || !RTCRtpSender.getCapabilities) return;
            var caps = RTCRtpSender.getCapabilities('video');
            if (!caps || !caps.codecs) return;

            // Prioritize: H.264 (hardware encode/lowest CPU latency) -> VP9 -> AV1 -> VP8 -> remaining
            var h264 = caps.codecs.filter(function (c) { return (c.mimeType || '').toLowerCase() === 'video/h264'; });
            var vp9 = caps.codecs.filter(function (c) { return (c.mimeType || '').toLowerCase() === 'video/vp9'; });
            var av1 = caps.codecs.filter(function (c) { return (c.mimeType || '').toLowerCase() === 'video/av1'; });
            var vp8 = caps.codecs.filter(function (c) { return (c.mimeType || '').toLowerCase() === 'video/vp8'; });
            var rest = caps.codecs.filter(function (c) {
                var m = (c.mimeType || '').toLowerCase();
                return m !== 'video/h264' && m !== 'video/vp9' && m !== 'video/av1' && m !== 'video/vp8';
            });

            var preferredOrder = h264.concat(vp9).concat(av1).concat(vp8).concat(rest);

            var trans = pc.getTransceivers ? pc.getTransceivers().find(function (t) { return t && t.sender && t.sender.track && t.sender.track.kind === 'video'; }) : null;
            if (trans && trans.setCodecPreferences && preferredOrder.length > 0) {
                trans.setCodecPreferences(preferredOrder);
            }
        } catch (e) { }
    }
    var preferH264 = setCodecPreferences;

    function applyQualityToSender(pc) {
        if (!pc) return;
        try {
            var sender = pc.getSenders().find(function (s) { return s.track && s.track.kind === 'video'; });
            if (sender && typeof sender.getParameters === 'function') {
                var p = sender.getParameters();
                if (!p.encodings || !p.encodings.length) p.encodings = [{}];
                var q = getStreamingBitrate();
                p.encodings[0].maxBitrate = q.maxBitrate;
                p.encodings[0].scaleResolutionDownBy = calculateNativeResolutionScale();
                sender.setParameters(p).catch(function (err) {
                    console.warn('[JellyEmu Netplay] Dynamic bitrate update error:', err);
                });
            }
        } catch (e) { }
    }

    function updateAllActiveHostBitrates() {
        Object.keys(hostPeerConnections).forEach(function (sid) {
            applyQualityToSender(hostPeerConnections[sid]);
        });
    }

    function tuneVideoSender(pc) {
        try {
            var sender = pc.getSenders().find(function (s) { return s.track && s.track.kind === 'video'; });
            if (sender && typeof sender.getParameters === 'function') {
                var p = sender.getParameters();
                p.degradationPreference = 'maintain-framerate';
                if (!p.encodings || !p.encodings.length) p.encodings = [{}];
                var q = getStreamingBitrate();
                p.encodings[0].maxBitrate = q.maxBitrate;
                p.encodings[0].maxFramerate = 60;
                p.encodings[0].priority = 'high';
                p.encodings[0].networkPriority = 'high';
                p.encodings[0].scaleResolutionDownBy = calculateNativeResolutionScale();
                sender.setParameters(p).catch(function (err) {
                    console.warn('[JellyEmu Netplay] Failed to set video sender parameters:', err);
                });
            }
        } catch (e) { }
    }

    function tuneSdp(sdp) {
        if (!sdp || typeof sdp !== 'string') return sdp;
        try {
            var q = getStreamingBitrate();
            var minBitrate = q.minBitrate || Math.round(q.maxBitrate / 2000);
            var startBitrate = q.startBitrate || Math.round(q.maxBitrate / 1200);
            var maxBitrate = Math.round(q.maxBitrate / 1000);

            var lines = sdp.split(/\r\n|\n/);
            var result = [];
            var inVideo = false;

            for (var i = 0; i < lines.length; i++) {
                var line = lines[i];
                if (line.indexOf('m=video') === 0) {
                    inVideo = true;
                    result.push(line);
                    if (q.asBandwidth) result.push('b=AS:' + q.asBandwidth);
                    if (q.tiasBandwidth) result.push('b=TIAS:' + q.tiasBandwidth);
                    continue;
                }
                if (line.indexOf('m=audio') === 0 || line.indexOf('m=application') === 0) {
                    inVideo = false;
                }

                // Replace any redundant b=AS or b=TIAS in video section
                if (inVideo && (line.indexOf('b=AS:') === 0 || line.indexOf('b=TIAS:') === 0)) {
                    continue;
                }

                // Inject immediate bitrate parameters into video codec format lines (eliminates ramp-up delay)
                if (inVideo && line.indexOf('a=fmtp:') === 0) {
                    if (line.indexOf('x-google-min-bitrate') === -1) {
                        line += ';x-google-min-bitrate=' + minBitrate + ';x-google-start-bitrate=' + startBitrate + ';x-google-max-bitrate=' + maxBitrate;
                    }
                }

                result.push(line);
            }
            return result.join('\r\n');
        } catch (ex) {
            return sdp;
        }
    }

    function sendGuestNetplayInput(buttonId, value) {
        if (_isLeavingRoom || !state.inRoom) return false;
        var e = emu();
        if (!e || !e.isNetplay || !e.netplay || e.netplay.owner) return false;

        var userIdx = 1;
        if (typeof e.netplay.getUserIndex === 'function' && e.netplay.playerID) {
            var idx = e.netplay.getUserIndex(e.netplay.playerID);
            if (idx >= 0) userIdx = idx;
        }

        // Send via ultra low-latency WebRTC DataChannel (compact 6-byte binary payload)
        if (guestInputDataChannel && guestInputDataChannel.readyState === 'open') {
            try {
                var valInt = (typeof value === 'number') ? Math.round(value) : (value ? 1 : 0);
                guestInputDataChannel.send(new Int16Array([userIdx, buttonId, valInt]));
                return true;
            } catch (err) { }
        }

        // Fallback to Socket.IO data-message if DataChannel is not open
        if (e.netplay && typeof e.netplay.sendMessage === 'function') {
            try {
                e.netplay.sendMessage({
                    jeRemoteInput: [userIdx, buttonId, value]
                });
                return true;
            } catch (err) { }
        }
        return false;
    }
    window._jeSendNetplayInput = sendGuestNetplayInput;

    async function initiateHostWebRtc(e, guestSocketId) {
        if (!window.RTCPeerConnection || _isLeavingRoom || !state.inRoom || !state.isHost) {
            return;
        }

        try {
            if (hostPeerConnections[guestSocketId]) {
                safelyClosePeerConnection(hostPeerConnections[guestSocketId]);
                delete hostPeerConnections[guestSocketId];
            }
            hostIceQueues[guestSocketId] = [];

            var pc = new RTCPeerConnection(rtcConfig);
            hostPeerConnections[guestSocketId] = pc;

            var stream = getHostMediaStream(e);
            if (stream) {
                stream.getTracks().forEach(function (track) {
                    pc.addTrack(track, stream);
                });
            }

            preferH264(pc);

            var dc = pc.createDataChannel('jeInput', { ordered: true, priority: 'high' });
            pc._inputDc = dc;
            dc.binaryType = 'arraybuffer';
            dc.onmessage = function (evt) {
                try {
                    var pidx = 1, btn = 0, val = 0;
                    if (evt.data instanceof ArrayBuffer) {
                        var inp = new Int16Array(evt.data);
                        if (inp.length >= 3) {
                            pidx = inp[0];
                            btn = inp[1];
                            val = inp[2];
                        }
                    } else {
                        var msg = JSON.parse(evt.data);
                        if (msg && msg.input) {
                            pidx = msg.input[0];
                            btn = msg.input[1];
                            val = msg.input[2];
                        }
                    }
                    if (e.gameManager) {
                        if (e.gameManager.functions && typeof e.gameManager.functions.simulateInput === 'function') {
                            e.gameManager.functions.simulateInput(pidx, btn, val);
                        } else if (typeof e.gameManager.simulateInput === 'function') {
                            e.gameManager.simulateInput(pidx, btn, val);
                        }
                    }
                } catch (err) { }
            };

            pc.onicecandidate = function (evt) {
                if (evt.candidate && e.netplay && e.netplay.socket) {
                    var candObj = evt.candidate.toJSON ? evt.candidate.toJSON() : {
                        candidate: evt.candidate.candidate,
                        sdpMid: evt.candidate.sdpMid,
                        sdpMLineIndex: evt.candidate.sdpMLineIndex
                    };
                    console.log('[JellyEmu Netplay] Host sending ICE candidate to guest:', guestSocketId);
                    e.netplay.socket.emit('webrtc-signal', {
                        target: guestSocketId,
                        candidate: candObj
                    });
                }
            };

            pc.onconnectionstatechange = function () {
                if (_isLeavingRoom || !state.inRoom) return;
                console.log('[JellyEmu Netplay] Host WebRTC state with ' + guestSocketId + ':', pc.connectionState);
                if (pc.connectionState === 'connected') {
                    showNetplayToast('Peer connected!', 'videocam');
                    tuneVideoSender(pc);
                } else if (pc.connectionState === 'failed') {
                    console.warn('[JellyEmu Netplay] Host WebRTC connection failed with ' + guestSocketId);
                    safelyClosePeerConnection(pc);
                    delete hostPeerConnections[guestSocketId];
                    delete hostIceQueues[guestSocketId];
                } else if (pc.connectionState === 'disconnected' || pc.connectionState === 'closed') {
                    delete hostPeerConnections[guestSocketId];
                    delete hostIceQueues[guestSocketId];
                }
            };

            var offer = await pc.createOffer();
            offer.sdp = tuneSdp(offer.sdp);
            await pc.setLocalDescription(offer);
            tuneVideoSender(pc);

            console.log('[JellyEmu Netplay] Host broadcasting WebRTC offer to:', guestSocketId);
            e.netplay.socket.emit('webrtc-signal', {
                target: guestSocketId,
                offer: offer
            });
        } catch (err) {
            console.warn('[JellyEmu Netplay] Host initiate WebRTC error:', err);
        }
    }

    async function handleWebRtcSignal(e, signal) {
        if (!signal || !window.RTCPeerConnection || _isLeavingRoom || !state.inRoom) return;
        var sender = signal.sender;

        // GUEST receives OFFER from Host
        if (signal.offer) {
            console.log('[JellyEmu Netplay] Guest received WebRTC offer from host:', sender);
            try {
                if (guestPeerConnection) {
                    safelyClosePeerConnection(guestPeerConnection);
                }
                guestIceQueue = [];

                var pc = new RTCPeerConnection(rtcConfig);
                guestPeerConnection = pc;

                var guestStream = null;
                pc.ontrack = function (evt) {
                    console.log('[JellyEmu Netplay] Guest received stream track:', evt.track.kind);
                    if (evt.receiver) {
                        try {
                            if ('playoutDelayHint' in evt.receiver) evt.receiver.playoutDelayHint = 0;
                            if ('jitterBufferTarget' in evt.receiver) evt.receiver.jitterBufferTarget = 0;
                        } catch (rErr) { }
                    }
                    if (evt.streams && evt.streams[0]) {
                        showGuestVideoOverlay(evt.streams[0], e);
                    } else if (evt.track) {
                        if (!guestStream) guestStream = new MediaStream();
                        guestStream.addTrack(evt.track);
                        showGuestVideoOverlay(guestStream, e);
                    }
                };

                pc.ondatachannel = function (evt) {
                    guestInputDataChannel = evt.channel;
                    guestInputDataChannel.binaryType = 'arraybuffer';
                    guestInputDataChannel.onmessage = function (mEvt) {
                        try {
                            var d = typeof mEvt.data === 'string' ? JSON.parse(mEvt.data) : null;
                            if (d && (d.type === 'host-left' || d['host-left'])) {
                                console.log('[JellyEmu Netplay] Received host-left via WebRTC data channel');
                                showNetplayToast('Host left the game. Returning to single player...', 'info');
                                performRoomLeftCleanup();
                            }
                        } catch (ex) { }
                    };
                    console.log('[JellyEmu Netplay] Guest WebRTC input data channel open');
                };

                pc.onicecandidate = function (evt) {
                    if (evt.candidate && e.netplay && e.netplay.socket) {
                        var candObj = evt.candidate.toJSON ? evt.candidate.toJSON() : {
                            candidate: evt.candidate.candidate,
                            sdpMid: evt.candidate.sdpMid,
                            sdpMLineIndex: evt.candidate.sdpMLineIndex
                        };
                        console.log('[JellyEmu Netplay] Guest sending ICE candidate to host:', sender);
                        e.netplay.socket.emit('webrtc-signal', {
                            target: sender,
                            candidate: candObj
                        });
                    }
                };

                pc.onconnectionstatechange = function () {
                    if (_isLeavingRoom || !state.inRoom) return;
                    console.log('[JellyEmu Netplay] Guest WebRTC connection state:', pc.connectionState);
                    if (pc.connectionState === 'connected') {
                        showNetplayToast('Live Stream Active - Playing with Host!', 'sports_esports');
                    } else if (pc.connectionState === 'disconnected' || pc.connectionState === 'closed') {
                        console.warn('[JellyEmu Netplay] Guest WebRTC connection closed by host. Returning to single player.');
                        showNetplayToast('Host left the game. Returning to single player...', 'info');
                        performRoomLeftCleanup();
                    } else if (pc.connectionState === 'failed') {
                        console.warn('[JellyEmu Netplay] Guest WebRTC connection failed.');
                        showNetplayToast('Connection to host lost. Returning to single player...', 'info');
                        performRoomLeftCleanup();
                    }
                };

                pc.oniceconnectionstatechange = function () {
                    if (_isLeavingRoom || !state.inRoom) return;
                    console.log('[JellyEmu Netplay] Guest WebRTC ICE state:', pc.iceConnectionState);
                    if (pc.iceConnectionState === 'disconnected' || pc.iceConnectionState === 'closed') {
                        console.warn('[JellyEmu Netplay] Guest WebRTC ICE disconnected. Returning to single player.');
                        showNetplayToast('Host disconnected. Returning to single player...', 'info');
                        performRoomLeftCleanup();
                    }
                };

                preferH264(pc);
                await pc.setRemoteDescription(new RTCSessionDescription(signal.offer));

                // Process early candidates queued while setRemoteDescription was pending
                while (guestIceQueue.length > 0) {
                    var queuedCand = guestIceQueue.shift();
                    try {
                        await pc.addIceCandidate(new RTCIceCandidate(queuedCand));
                    } catch (cErr) {
                        console.warn('[JellyEmu Netplay] Error adding queued guest ICE candidate:', cErr);
                    }
                }

                var answer = await pc.createAnswer();
                answer.sdp = tuneSdp(answer.sdp);
                await pc.setLocalDescription(answer);

                console.log('[JellyEmu Netplay] Guest sending WebRTC answer to host:', sender);
                e.netplay.socket.emit('webrtc-signal', {
                    target: sender,
                    answer: answer
                });
            } catch (err) {
                console.error('[JellyEmu Netplay] Guest handle offer error:', err);
            }
            return;
        }

        // HOST receives ANSWER from Guest
        if (signal.answer) {
            console.log('[JellyEmu Netplay] Host received WebRTC answer from guest:', sender);
            var hostPc = hostPeerConnections[sender];
            if (hostPc) {
                try {
                    await hostPc.setRemoteDescription(new RTCSessionDescription(signal.answer));
                    tuneVideoSender(hostPc);

                    var q = hostIceQueues[sender];
                    if (q && q.length > 0) {
                        while (q.length > 0) {
                            var queuedCand = q.shift();
                            try {
                                await hostPc.addIceCandidate(new RTCIceCandidate(queuedCand));
                            } catch (cErr) {
                                console.warn('[JellyEmu Netplay] Error adding queued host ICE candidate:', cErr);
                            }
                        }
                    }
                } catch (err) {
                    console.warn('[JellyEmu Netplay] Host setRemoteDescription error:', err);
                }
            }
            return;
        }

        // ICE Candidate exchange with queuing
        if (signal.candidate) {
            var isHost = (e.netplay && (e.netplay.owner || state.isHost));
            var targetPc = isHost ? hostPeerConnections[sender] : guestPeerConnection;
            console.log('[JellyEmu Netplay] Received ICE candidate from:', sender, 'isHost:', isHost, 'targetPcReady:', !!(targetPc && targetPc.remoteDescription && targetPc.remoteDescription.type));

            if (targetPc && targetPc.remoteDescription && targetPc.remoteDescription.type) {
                try {
                    await targetPc.addIceCandidate(new RTCIceCandidate(signal.candidate));
                } catch (err) {
                    console.warn('[JellyEmu Netplay] addIceCandidate error:', err);
                }
            } else {
                if (isHost) {
                    if (!hostIceQueues[sender]) hostIceQueues[sender] = [];
                    hostIceQueues[sender].push(signal.candidate);
                } else {
                    guestIceQueue.push(signal.candidate);
                }
            }
            return;
        }

        // Host renegotiation (e.g. on host migration, re-join, or failed PC)
        if (signal.requestRenegotiate && e.netplay && (e.netplay.owner || state.isHost)) {
            var guestSid = sender || signal.target;
            console.log('[JellyEmu Netplay] Renegotiation requested by guest:', guestSid, 'reason:', signal.reason);
            var mySid = (e.netplay.socket && e.netplay.socket.id) || '';
            if (guestSid && guestSid !== mySid) {
                if (hostPeerConnections[guestSid]) {
                    try { hostPeerConnections[guestSid].close(); } catch (ex) { }
                    delete hostPeerConnections[guestSid];
                }
                delete hostIceQueues[guestSid];
                initiateHostWebRtc(e, guestSid);
            }
            return;
        }
    }

    function handleUsersUpdatedWebRtc(e, players) {
        if (!e || !e.netplay || !(e.netplay.owner || state.isHost) || !e.netplay.socket) return;
        if (!players || typeof players !== 'object') return;

        var myId = e.netplay.playerID;
        var mySid = (e.netplay.socket && e.netplay.socket.id) || '';

        // Prune connections for players who departed the room
        var activeGuestSids = {};
        Object.keys(players).forEach(function (pid) {
            var info = players[pid];
            if (info && info.socketId) activeGuestSids[info.socketId] = true;
        });
        Object.keys(hostPeerConnections).forEach(function (guestSid) {
            if (!activeGuestSids[guestSid]) {
                console.log('[JellyEmu Netplay] Guest departed room, closing WebRTC connection:', guestSid);
                if (hostPeerConnections[guestSid]) {
                    try { hostPeerConnections[guestSid].close(); } catch (err) { }
                    delete hostPeerConnections[guestSid];
                }
                delete hostIceQueues[guestSid];
            }
        });

        Object.keys(players).forEach(function (pid) {
            if (pid === myId) return;
            var guestInfo = players[pid];
            var guestSocketId = guestInfo ? guestInfo.socketId : null;
            if (!guestSocketId || guestSocketId === mySid) return;

            var existing = hostPeerConnections[guestSocketId];
            var isDead = existing && (existing.connectionState === 'failed' || existing.connectionState === 'closed' || existing.connectionState === 'disconnected');
            if (!existing || isDead) {
                if (isDead) {
                    try { existing.close(); } catch (err) { }
                    delete hostPeerConnections[guestSocketId];
                    delete hostIceQueues[guestSocketId];
                }
                console.log('[JellyEmu Netplay] Player detected in room, establishing WebRTC video stream:', guestSocketId);
                initiateHostWebRtc(e, guestSocketId);
            }
        });
    }

    function attachSocketWebRtcHandlers(e) {
        if (!e || !e.netplay || !e.netplay.socket) return;
        attachChatListener(e);
        attachRoomTerminationListeners(e);
        if (e.netplay.socket._jeWebRtcHooked) return;
        e.netplay.socket._jeWebRtcHooked = true;

        // If EmulatorJS already implements native WebRTC peer connections,
        // do not register duplicate handlers that collide with native offer/answer/candidate exchanges.
        if (typeof e.netplay.createPeerConnection === 'function') {
            console.log('[JellyEmu Netplay] Using native EmulatorJS WebRTC peer connection pipeline');
            return;
        }

        e.netplay.socket.on('webrtc-signal', function (signal) {
            handleWebRtcSignal(e, signal);
        });

        e.netplay.socket.on('users-updated', function (players) {
            handleUsersUpdatedWebRtc(e, players);
        });
    }

    function resetNetplayFrames(e) {
        if (!e || !e.netplay) return;
        var fn = 0;
        if (e.gameManager && typeof e.gameManager.getFrameNum === 'function') {
            try { fn = parseInt(e.gameManager.getFrameNum(), 10) || 0; } catch (err) { }
        }
        e.netplay.init_frame = fn;
        e.netplay.currentFrame = 0;
        e.netplay.inputsData = {};
        e.netplay.wait = false;
        e.netplay.syncing = false;
        if (typeof e.netplay.reset === 'function') {
            try { e.netplay.reset(); } catch (err) { }
        }
        e.netplay.init_frame = fn;
        e.netplay.currentFrame = 0;
        e.netplay.inputsData = {};
    }

    function hookPostMainLoop(e) {
        if (!e || !e.Module) return;
        if (e.Module._jePostMainLoopHooked) return;
        e.Module._jePostMainLoopHooked = true;
        var prevPostMainLoop = e.Module.postMainLoop;

        e.Module.postMainLoop = function () {
            if (typeof prevPostMainLoop === 'function') {
                try { prevPostMainLoop(); } catch (err) { }
            }
            if (!e.isNetplay || !e.netplay) return;

            var frameNum = 0;
            if (e.gameManager && typeof e.gameManager.getFrameNum === 'function') {
                try { frameNum = parseInt(e.gameManager.getFrameNum(), 10) || 0; } catch (err) { }
            }
            e.netplay.currentFrame = frameNum - (e.netplay.init_frame || 0);

            if (e.netplay.owner) {
                // When WebRTC video stream is active, guests watch host directly and send inputs via DataChannel.
                // Suppress redundant WebSocket sync-control broadcasting to eliminate network overhead.
                var hasActiveWebRtcPeers = Object.keys(hostPeerConnections).length > 0;
                if (!hasActiveWebRtcPeers) {
                    var prevFrame = e.netplay.currentFrame - 1;
                    if (e.netplay.inputsData && e.netplay.inputsData[prevFrame] && e.netplay.inputsData[prevFrame].length > 0) {
                        var t = [];
                        e.netplay.inputsData[prevFrame].forEach(function (inp) {
                            t.push({
                                frame: (inp.frame || prevFrame) + 10,
                                connected_input: inp.connected_input
                            });
                        });
                        if (typeof e.netplay.sendMessage === 'function') {
                            e.netplay.sendMessage({ 'sync-control': t });
                        }
                    }
                }
            } else {
                // Guest: execute inputs for the current frame
                var cur = e.netplay.currentFrame;
                if (!e.netplay.inputsData) e.netplay.inputsData = {};

                var inputs = e.netplay.inputsData[cur];
                if (inputs && Array.isArray(inputs)) {
                    inputs.forEach(function (inp) {
                        if (inp && inp.connected_input && inp.connected_input[0] >= 0) {
                            if (e.gameManager && e.gameManager.functions && typeof e.gameManager.functions.simulateInput === 'function') {
                                try {
                                    e.gameManager.functions.simulateInput(
                                        inp.connected_input[0],
                                        inp.connected_input[1],
                                        inp.connected_input[2]
                                    );
                                } catch (err) { }
                            }
                        }
                    });
                }
            }

            // Cleanup old frames from inputsData
            if (e.netplay.currentFrame % 100 === 0 && e.netplay.inputsData) {
                var threshold = e.netplay.currentFrame - 50;
                Object.keys(e.netplay.inputsData).forEach(function (k) {
                    if (parseInt(k, 10) < threshold) {
                        delete e.netplay.inputsData[k];
                    }
                });
            }
        };
    }

    // Ensure EmulatorJS netplay functions are initialized
    function ensureNetplaySubsystem(e) {
        if (!e) return false;
        if (!e.netplay) e.netplay = {};

        // Stub/mock EmulatorJS's internal UI elements so its internal handlers don't throw TypeError
        function dummyDiv() { return document.createElement('div'); }
        if (!e.netplay.table) e.netplay.table = document.createElement('tbody');
        if (!e.netplay.playerTable) e.netplay.playerTable = document.createElement('tbody');
        if (!e.netplay.passwordElem) e.netplay.passwordElem = dummyDiv();
        if (!e.netplay.roomNameElem) e.netplay.roomNameElem = dummyDiv();
        if (!e.netplay.createButton) e.netplay.createButton = document.createElement('button');
        if (!e.netplay.tabs || !Array.isArray(e.netplay.tabs)) {
            e.netplay.tabs = [dummyDiv(), dummyDiv()];
        }
        if (!e.netplay.oldStyles) e.netplay.oldStyles = [];

        if (!e.elements) e.elements = {};
        if (!e.elements.bottomBar) {
            e.elements.bottomBar = {
                cheat: [{ style: {} }],
                playPause: [{ style: {} }, { style: {} }],
                restart: [{ style: {} }],
                loadState: [{ style: {} }],
                saveState: [{ style: {} }],
                saveSavFiles: [{ style: {} }],
                loadSavFiles: [{ style: {} }]
            };
        }
        if (!e.elements.contextMenu) {
            e.elements.contextMenu = {
                save: { style: {} },
                load: { style: {} }
            };
        }

        e.config.netplayUrl = netplayServer;
        e.config.gameId = typeof gameId === 'number' ? gameId : parseInt(gameId, 10) || 1;
        e.netplay.name = getPlayerName();
        e.netplay.url = netplayServer;

        if (typeof e.defineNetplayFunctions === 'function' && typeof e.netplay.openRoom !== 'function') {
            try {
                e.defineNetplayFunctions();
            } catch (err) {
                console.warn('[JellyEmu] defineNetplayFunctions invocation:', err);
            }
        }

        // Re-ensure stubs after defineNetplayFunctions
        if (!e.netplay.table) e.netplay.table = document.createElement('tbody');
        if (!e.netplay.playerTable) e.netplay.playerTable = document.createElement('tbody');
        if (!e.netplay.passwordElem) e.netplay.passwordElem = dummyDiv();
        if (!e.netplay.roomNameElem) e.netplay.roomNameElem = dummyDiv();
        if (!e.netplay.createButton) e.netplay.createButton = document.createElement('button');
        if (!e.netplay.tabs || !Array.isArray(e.netplay.tabs)) {
            e.netplay.tabs = [dummyDiv(), dummyDiv()];
        }

        // Hook roomJoined / roomLeft / updatePlayersTable to sync with JellyEmu UI
        if (typeof e.netplay.roomJoined === 'function' && !e.netplay._jeHooked) {
            e.netplay._jeHooked = true;
            var origRoomJoined = e.netplay.roomJoined;
            e.netplay.roomJoined = function (isOwner, roomName, password, roomId) {
                try {
                    origRoomJoined.apply(this, arguments);
                } catch (err) {
                    console.warn('[JellyEmu] EmulatorJS origRoomJoined caught:', err);
                }
                onEjsRoomJoined(isOwner, roomName, password, roomId);
                attachChatListener(e);
            };

            var origLeaveRoom = e.netplay.leaveRoom;
            e.netplay.leaveRoom = function () {
                try {
                    if (typeof origLeaveRoom === 'function') origLeaveRoom.apply(this, arguments);
                } catch (err) {
                    console.warn('[JellyEmu] EmulatorJS origLeaveRoom caught:', err);
                }
                performRoomLeftCleanup();
            };

            var origRoomLeft = e.netplay.roomLeft;
            e.netplay.roomLeft = function () {
                try {
                    if (typeof origRoomLeft === 'function') origRoomLeft.apply(this, arguments);
                } catch (err) {
                    console.warn('[JellyEmu] EmulatorJS origRoomLeft caught:', err);
                }
                performRoomLeftCleanup();
            };

            var origUpdatePlayers = e.netplay.updatePlayersTable;
            e.netplay.updatePlayersTable = function () {
                try {
                    origUpdatePlayers.apply(this, arguments);
                } catch (err) {
                    console.warn('[JellyEmu] EmulatorJS origUpdatePlayers caught:', err);
                }
                onEjsPlayersUpdated(e.netplay.players);
                attachChatListener(e);
            };
        }

        // In remote streaming netplay, guests receive real-time video/audio and transmit inputs.
        // Save-state sync is disabled because pausing the host freezes WebRTC video streaming.
        if (typeof e.netplay.sync === 'function' && !e.netplay._jeSyncHooked) {
            e.netplay._jeSyncHooked = true;
            e.netplay.sync = function () {
                console.log('[JellyEmu] Netplay sync requested: refreshing active stream to guests');
                if (e.netplay && e.netplay.owner) {
                    startHostStreamingToGuests(e);
                    showNetplayToast('Refreshed video stream for guests', 'videocam');
                }
            };
        }

        // Hook dataMessage for safe remote input routing and unpause guarantees
        if (typeof e.netplay.dataMessage === 'function' && !e.netplay._jeDataMsgHooked) {
            e.netplay._jeDataMsgHooked = true;
            var origDataMessage = e.netplay.dataMessage;
            e.netplay.dataMessage = function (data) {
                if (!data) return;

                if (data.type === 'host-left' || data['host-left']) {
                    console.log('[JellyEmu Netplay] Host left detected in dataMessage');
                    showNetplayToast('Host left the game. Returning to single player...', 'info');
                    performRoomLeftCleanup();
                    return;
                }

                // Handle legacy data.state gracefully: guests in remote streaming mode do not load state
                if (data.state) {
                    if (e.netplay && typeof e.netplay.sendMessage === 'function') {
                        e.netplay.sendMessage({ ready: true });
                    }
                    return;
                }

                // Robust sync-control frame and input handling without pausing host
                if (data['sync-control'] && Array.isArray(data['sync-control'])) {
                    data['sync-control'].forEach(function (t) {
                        if (!t) return;
                        var frame = parseInt(t.frame, 10);
                        if (isNaN(frame)) return;
                        var cur = e.netplay.currentFrame || 0;

                        if (!e.netplay.inputsData) e.netplay.inputsData = {};
                        if (!e.netplay.inputsData[frame]) {
                            e.netplay.inputsData[frame] = [];
                        }

                        if (t.connected_input && Array.isArray(t.connected_input) && t.connected_input[0] >= 0) {
                            if (e.netplay.owner) {
                                // Host receives guest input and immediately simulates it
                                if (!e.netplay.inputsData[cur]) e.netplay.inputsData[cur] = [];
                                e.netplay.inputsData[cur].push(t);
                                if (e.gameManager && e.gameManager.functions && typeof e.gameManager.functions.simulateInput === 'function') {
                                    try {
                                        e.gameManager.functions.simulateInput(t.connected_input[0], t.connected_input[1], t.connected_input[2]);
                                    } catch (err) { }
                                }
                            } else {
                                // Guest receives host input
                                e.netplay.inputsData[frame].push(t);
                            }
                        }
                    });
                    return;
                }

                // Handle remote guest input
                if (data.jeRemoteInput && e.netplay && e.netplay.owner) {
                    var inp = data.jeRemoteInput;
                    if (Array.isArray(inp) && e.gameManager && e.gameManager.functions && typeof e.gameManager.functions.simulateInput === 'function') {
                        try {
                            e.gameManager.functions.simulateInput(inp[0], inp[1], inp[2]);
                        } catch (err) { }
                    }
                    return;
                }

                // Handle direct player server ping broadcast
                if (data.jeServerPing && typeof data.ping === 'number') {
                    if (data.socketId) playerPings[data.socketId] = data.ping;
                    if (data.playerId) playerPings[data.playerId] = data.ping;
                    updatePingBadgeElements();
                    return;
                }

                // Handle ping synchronization from host
                if (data.jePingSync && data.pings && typeof data.pings === 'object') {
                    var myId = (e.netplay && e.netplay.playerID) || '';
                    var mySocketId = (e.netplay && e.netplay.socket && e.netplay.socket.id) || '';
                    Object.keys(data.pings).forEach(function (k) {
                        if (k !== mySocketId && k !== myId && k !== 'self') {
                            playerPings[k] = data.pings[k];
                        }
                    });
                    updatePingBadgeElements();
                    return;
                }

                // Fix bug in EmulatorJS: EmulatorJS checked !this.owner instead of !this.netplay.owner
                var isOwner = e.netplay && !!e.netplay.owner;
                if (data.play && !isOwner && typeof e.play === 'function') {
                    e.play(true);
                }
                if (data.pause && !isOwner && typeof e.pause === 'function') {
                    e.pause(true);
                }

                try {
                    origDataMessage.apply(this, arguments);
                } catch (err) {
                    console.warn('[JellyEmu] Netplay dataMessage error:', err);
                }
            };
        }

        // Hook simulateInput so guest controller/keyboard inputs route directly to host
        if (typeof e.netplay.simulateInput === 'function' && !e.netplay._jeSimInputHooked) {
            e.netplay._jeSimInputHooked = true;
            var origSimulateInput = e.netplay.simulateInput;
            e.netplay.simulateInput = function (playerIndex, buttonId, value, n) {
                if (!e.isNetplay) return;

                if (e.netplay.owner) {
                    if (typeof origSimulateInput === 'function') {
                        origSimulateInput.apply(this, arguments);
                    }
                } else {
                    sendGuestNetplayInput(buttonId, value);
                }
            };
        }

        // Hook gameManager.functions.simulateInput to catch core inputs on guest
        if (e.gameManager && e.gameManager.functions && !e.gameManager.functions._jeSimHooked) {
            e.gameManager.functions._jeSimHooked = true;
            var origGmSim = e.gameManager.functions.simulateInput;
            e.gameManager.functions.simulateInput = function (playerIndex, buttonId, value) {
                if (e.isNetplay && e.netplay && !e.netplay.owner) {
                    if (sendGuestNetplayInput(buttonId, value)) {
                        return;
                    }
                }
                if (typeof origGmSim === 'function') {
                    origGmSim.apply(this, arguments);
                }
            };
        }
        if (e.gameManager && !e.gameManager._jeDirectSimHooked) {
            e.gameManager._jeDirectSimHooked = true;
            var origGmDirectSim = e.gameManager.simulateInput;
            e.gameManager.simulateInput = function (playerIndex, buttonId, value) {
                if (e.isNetplay && e.netplay && !e.netplay.owner) {
                    if (sendGuestNetplayInput(buttonId, value)) {
                        return;
                    }
                }
                if (typeof origGmDirectSim === 'function') {
                    origGmDirectSim.apply(this, arguments);
                }
            };
        }

        // Hook startSocketIO to attach WebRTC signaling listeners immediately upon socket creation
        if (typeof e.netplay.startSocketIO === 'function' && !e.netplay._jeStartSocketHooked) {
            e.netplay._jeStartSocketHooked = true;
            var origStartSocket = e.netplay.startSocketIO;
            e.netplay.startSocketIO = function (cb) {
                return origStartSocket.call(this, function () {
                    attachSocketWebRtcHandlers(e);
                    attachChatListener(e);
                    attachRoomTerminationListeners(e);
                    if (typeof cb === 'function') cb();
                });
            };
        }

        if (e.netplay && e.netplay.socket) {
            attachSocketWebRtcHandlers(e);
            attachChatListener(e);
            attachRoomTerminationListeners(e);
        }

        hookPostMainLoop(e);
        return true;
    }

    // DOM Elements
    var dockBtn = document.getElementById('je-btn-netplay');
    var topbtnNetplay = document.getElementById('je-topbtn-netplay');
    var topbtnNetplayText = document.getElementById('je-topbtn-netplay-text');
    var hdrStatus = document.getElementById('je-netplay-hdr-status');

    var tabRooms = document.getElementById('je-np-tab-rooms');
    var tabHost = document.getElementById('je-np-tab-host');
    var tabSession = document.getElementById('je-np-tab-session');
    var tabSettings = document.getElementById('je-np-tab-settings');

    var panelRooms = document.getElementById('je-tab-np-rooms');
    var panelHost = document.getElementById('je-tab-np-host');
    var panelSession = document.getElementById('je-tab-np-session');
    var panelSettings = document.getElementById('je-tab-np-settings');

    var roomsList = document.getElementById('je-np-rooms-list');
    var refreshBtn = document.getElementById('je-np-refresh-btn');
    var createRoomBtn = document.getElementById('je-np-create-btn');
    var leaveRoomBtn = document.getElementById('je-np-leave-btn');
    var syncStateBtn = document.getElementById('je-np-sync-btn');
    var restartGameBtn = document.getElementById('je-np-restart-btn');

    // Tab Management
    function switchTab(tabId) {
        state.activeTab = tabId;

        var allTabs = [tabRooms, tabHost, tabSession, tabSettings];
        var allPanels = [panelRooms, panelHost, panelSession, panelSettings];

        allTabs.forEach(function (t) { if (t) t.classList.remove('je-tab-active'); });
        allPanels.forEach(function (p) { if (p) { p.classList.remove('je-tab-active'); p.style.display = 'none'; } });

        if (tabId === 'rooms') {
            if (tabRooms) tabRooms.classList.add('je-tab-active');
            if (panelRooms) { panelRooms.classList.add('je-tab-active'); panelRooms.style.display = 'flex'; }
            startAutoRefresh();
            fetchRooms();
        } else if (tabId === 'host') {
            if (tabHost) tabHost.classList.add('je-tab-active');
            if (panelHost) { panelHost.classList.add('je-tab-active'); panelHost.style.display = 'flex'; }
            stopAutoRefresh();
            populateHostDefaults();
        } else if (tabId === 'session') {
            if (tabSession) tabSession.classList.add('je-tab-active');
            if (panelSession) { panelSession.classList.add('je-tab-active'); panelSession.style.display = 'flex'; }
            stopAutoRefresh();
            renderSessionInfo();
            var chatMsgs = document.getElementById('je-np-chat-messages');
            if (chatMsgs) chatMsgs.scrollTop = chatMsgs.scrollHeight;
        } else if (tabId === 'settings') {
            if (tabSettings) tabSettings.classList.add('je-tab-active');
            if (panelSettings) { panelSettings.classList.add('je-tab-active'); panelSettings.style.display = 'flex'; }
            stopAutoRefresh();
            renderSettingsTab();
        }
    }

    if (tabRooms) tabRooms.addEventListener('click', function () { switchTab('rooms'); });
    if (tabHost) tabHost.addEventListener('click', function () { switchTab('host'); });
    if (tabSession) tabSession.addEventListener('click', function () { switchTab('session'); });
    if (tabSettings) tabSettings.addEventListener('click', function () { switchTab('settings'); });

    // Populate Host Defaults
    function populateHostDefaults() {
        var roomNameInput = document.getElementById('je-np-host-roomname');
        if (roomNameInput && !roomNameInput.value) {
            roomNameInput.value = getPlayerName() + "'s Room";
        }
    }

    // Auto Refresh
    function startAutoRefresh() {
        stopAutoRefresh();
        state.autoRefreshTimer = setInterval(function () {
            if (state.activeTab === 'rooms' && document.getElementById('je-pop-netplay')?.classList.contains('je-open')) {
                fetchRooms(true);
            }
        }, 4000);
    }

    function stopAutoRefresh() {
        if (state.autoRefreshTimer) {
            clearInterval(state.autoRefreshTimer);
            state.autoRefreshTimer = null;
        }
    }

    // Server Health & Ping Check
    var serverStatus = {
        online: null,
        latencyMs: null,
        checking: false
    };

    function checkNetplayPing(callback) {
        if (serverStatus.checking) return;
        serverStatus.checking = true;
        var start = Date.now();
        fetch(netplayServer + '/ping')
            .then(function (res) {
                if (!res.ok) throw new Error('HTTP ' + res.status);
                return res.json();
            })
            .then(function () {
                serverStatus.checking = false;
                serverStatus.online = true;
                serverStatus.latencyMs = Date.now() - start;
                updateServerStatusDisplay();
                if (typeof callback === 'function') callback(true, serverStatus.latencyMs);
            })
            .catch(function () {
                serverStatus.checking = false;
                serverStatus.online = false;
                serverStatus.latencyMs = null;
                updateServerStatusDisplay();
                if (typeof callback === 'function') callback(false, null);
            });
    }

    function updateServerStatusDisplay() {
        var serverDisplay = document.getElementById('je-np-server-display');
        if (!serverDisplay) return;

        if (serverStatus.online === true) {
            serverDisplay.textContent = (serverStatus.latencyMs !== null ? serverStatus.latencyMs : 0) + 'ms';
            serverDisplay.style.color = '#81c784';
        } else if (serverStatus.online === false) {
            serverDisplay.textContent = 'Offline';
            serverDisplay.style.color = '#ff6b6b';
        } else {
            serverDisplay.textContent = 'Checking…';
            serverDisplay.style.color = '#ffb74d';
        }
    }

    // Fetch and render rooms
    function fetchRooms(isSilent) {
        if (!roomsList) return;

        if (!isSilent) {
            roomsList.innerHTML = '<div style="text-align:center;padding:32px 0;opacity:0.6;font-size:13px;">' +
                '<div class="je-spinner" style="width:24px;height:24px;margin:0 auto 10px;"></div>' +
                'Scanning for open rooms…</div>';
        }

        var url = netplayServer + '/list?domain=' + encodeURIComponent(window.location.host) + '&game_id=' + encodeURIComponent(gameId);

        fetch(url)
            .then(function (res) {
                if (!res.ok) throw new Error('Server returned ' + res.status);
                serverStatus.online = true;
                return res.json();
            })
            .then(function (rooms) {
                renderRooms(rooms);
            })
            .catch(function (err) {
                serverStatus.online = false;
                updateServerStatusDisplay();
                if (!isSilent) {
                    roomsList.innerHTML = '<div style="text-align:center;padding:24px 0;color:#ff6b6b;font-size:13px;">' +
                        'Could not connect to Netplay server.<br><span style="opacity:0.7;font-size:11px;">(' + (err.message || 'Network error') + ')</span></div>';
                }
            });
    }

    function renderRooms(rooms) {
        if (!roomsList) return;
        roomsList.innerHTML = '';

        var keys = Object.keys(rooms || {});
        if (keys.length === 0) {
            roomsList.innerHTML = '<div class="je-netplay-empty">' +
                '<svg viewBox="0 0 24 24" style="width:36px;height:36px;opacity:0.35;margin-bottom:10px;fill:currentColor;">' +
                '<path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm-1-13h2v6h-2zm0 8h2v2h-2z"/>' +
                '</svg>' +
                '<div style="font-weight:600;margin-bottom:4px;">No Active Rooms Found</div>' +
                '<div style="font-size:12px;opacity:0.6;margin-bottom:16px;">Be the first to host a multiplayer session for this game!</div>' +
                '<button class="je-btn je-btn-primary" id="je-np-empty-host-btn" style="padding:7px 18px;font-size:13px;">Host a Room</button>' +
                '</div>';
            var emptyHostBtn = document.getElementById('je-np-empty-host-btn');
            if (emptyHostBtn) emptyHostBtn.addEventListener('click', function () { switchTab('host'); });
            return;
        }

        keys.forEach(function (sessionId) {
            var room = rooms[sessionId];
            var card = document.createElement('div');
            card.className = 'je-netplay-room-card';

            var current = room.current || 1;
            var max = room.max || 2;
            var isFull = current >= max;
            var isLocked = !!room.has_password || !!room.hasPassword || !!room.password;

            var isHostingThisRoom = state.isHost && (!state.roomId || state.roomId === sessionId);
            var isCurrentRoom = state.inRoom && (!state.roomId || state.roomId === sessionId);
            var cannotJoin = isFull || isHostingThisRoom || isCurrentRoom || state.inRoom;
            var buttonText = isHostingThisRoom ? 'Hosting' : (isCurrentRoom ? 'In Room' : (isFull ? 'Full' : 'Join'));
            var buttonTitle = isHostingThisRoom
                ? 'You are hosting this room'
                : (isCurrentRoom ? 'You are already in this room' : (state.inRoom ? 'Leave current room before joining another' : (isFull ? 'Room is full' : 'Join Room')));

            var hostPing = (room.host_ping !== undefined && room.host_ping !== null)
                ? room.host_ping
                : ((room.ping !== undefined && room.ping !== null) ? room.ping : null);

            var hostPingBadgeHtml = '';
            if (hostPing !== null && hostPing !== undefined && hostPing >= 0) {
                var pClass = getPingClass(hostPing);
                hostPingBadgeHtml = '<span class="je-np-ping-badge ' + pClass + '" title="Host ping to server: ' + hostPing + ' ms">' + hostPing + ' ms</span>';
            } else {
                hostPingBadgeHtml = '<span class="je-np-ping-badge je-np-ping-measuring" title="Host ping measuring…">-- ms</span>';
            }

            card.innerHTML = '<div class="je-np-room-info">' +
                '<div class="je-np-room-title-wrap">' +
                '<span class="je-np-room-title">' + (room.room_name || 'Room #' + sessionId.substring(0, 5)) + '</span>' +
                (isLocked ? '<span class="material-icons" style="font-size:14px;color:#f5a623;vertical-align:middle;margin-left:4px;" title="Password Protected">lock</span>' : '') +
                '</div>' +
                '<div class="je-np-room-meta">' +
                '<span class="je-np-room-host">Host: ' + (room.player_name || 'Player') + '</span>' +
                '<span class="je-np-room-count ' + (isFull ? 'je-np-full' : '') + '">' + current + ' / ' + max + ' Players</span>' +
                '</div>' +
                '</div>' +
                '<div class="je-np-room-actions">' +
                hostPingBadgeHtml +
                '<button class="je-btn je-btn-primary je-np-join-btn" ' + (cannotJoin ? 'disabled' : '') + ' title="' + buttonTitle + '">' +
                buttonText +
                '</button>' +
                '</div>';

            var joinBtn = card.querySelector('.je-np-join-btn');
            if (joinBtn && !cannotJoin) {
                joinBtn.addEventListener('click', function () {
                    joinRoom(sessionId, room.room_name || 'Game Room', isLocked, max);
                });
            }

            roomsList.appendChild(card);
        });
    }

    if (refreshBtn) {
        refreshBtn.addEventListener('click', function () {
            fetchRooms(false);
        });
    }

    // Join room action
    function joinRoom(sessionId, roomName, isLocked, maxPlayers) {
        var e = emu();
        if (!e) {
            alert('Emulator is still initializing. Please wait a moment.');
            return;
        }

        if (state.inRoom) {
            if (state.isHost && (!state.roomId || state.roomId === sessionId)) {
                alert('You are already hosting this room.');
                return;
            }
            if (!state.roomId || state.roomId === sessionId) {
                alert('You are already in this room.');
                return;
            }
            alert('You are already in a netplay session. Please leave your current room before joining another.');
            return;
        }

        ensureNetplaySubsystem(e);

        var password = '';
        if (isLocked) {
            password = prompt('Enter password for room "' + roomName + '":') || '';
            if (password === null) return; // User cancelled
        }

        setPlayerName(getPlayerName());

        try {
            if (typeof e.netplay.joinRoom === 'function') {
                if (e.netplay.joinRoom.length >= 4) {
                    e.netplay.joinRoom(sessionId, roomName, maxPlayers || 4, password);
                } else {
                    e.netplay.joinRoom(sessionId, roomName, password);
                }
            } else {
                alert('EmulatorJS Netplay engine is not ready yet.');
            }
        } catch (err) {
            console.error('[JellyEmu] joinRoom failed:', err);
            alert('Failed to join room: ' + (err.message || 'Unknown error'));
        }
    }

    // Create room action
    if (createRoomBtn) {
        createRoomBtn.addEventListener('click', function () {
            var e = emu();
            if (!e) {
                alert('Emulator is still initializing. Please wait a moment.');
                return;
            }

            ensureNetplaySubsystem(e);

            var nameInput = document.getElementById('je-np-host-roomname');
            var maxSelect = document.getElementById('je-np-host-max');
            var pwInput = document.getElementById('je-np-host-password');

            var roomName = (nameInput && nameInput.value.trim()) ? nameInput.value.trim() : (getPlayerName() + "'s Room");
            var maxPlayers = (maxSelect && maxSelect.value) ? parseInt(maxSelect.value, 10) : 2;
            var password = (pwInput && pwInput.value.trim()) ? pwInput.value.trim() : '';

            setPlayerName(getPlayerName());

            try {
                if (typeof e.netplay.openRoom === 'function') {
                    e.netplay.openRoom(roomName, maxPlayers, password);
                } else {
                    alert('EmulatorJS Netplay engine is not ready yet.');
                }
            } catch (err) {
                console.error('[JellyEmu] openRoom failed:', err);
                alert('Failed to create room: ' + (err.message || 'Unknown error'));
            }
        });
    }

    // EmulatorJS Hook Callbacks
    function onEjsRoomJoined(isOwner, roomName, password, roomId) {
        state.inRoom = true;
        state.isHost = !!isOwner;
        state.roomName = roomName || 'Active Room';
        state.roomId = roomId || '';
        state.password = password || '';
        var e = emu();
        if (e && e.netplay) {
            e.netplay.owner = !!isOwner;
            if (e.netplay.socket) {
                attachRoomTerminationListeners(e);
                attachChatListener(e);
            }
            if (!isOwner) {
                // If re-joining as guest after having been host, clean up prior local stream
                if (e.netplay.localStream) {
                    try { e.netplay.localStream.getTracks().forEach(function (tr) { tr.stop(); }); } catch (err) { }
                    e.netplay.localStream = null;
                }
                armGuestVideoWatchdog(e);
            }
            if (e.netplay.players) {
                state.players = e.netplay.players;
            }
        }

        // Update topbar & dock buttons
        if (topbtnNetplay) {
            topbtnNetplay.style.display = 'flex';
            if (topbtnNetplayText) topbtnNetplayText.textContent = isOwner ? 'Host' : 'Connected';
        }
        if (dockBtn) dockBtn.classList.add('je-active');
        if (hdrStatus) {
            hdrStatus.style.display = 'inline-block';
            hdrStatus.textContent = isOwner ? 'Hosting' : 'Connected';
        }

        // Show session tab and switch to it
        if (tabSession) tabSession.style.display = 'block';
        switchTab('session');
        startPingMeasurement();
    }

    function performRoomLeftCleanup() {
        if (_isLeavingRoom) return;
        _isLeavingRoom = true;

        var wasHost = state.isHost;
        var wasGuest = !wasHost;

        // Immediately reset state so no subsequent socket or WebRTC callbacks treat us as in-room
        state.inRoom = false;
        state.isHost = false;
        state.roomName = '';
        state.roomId = '';
        state.password = '';
        state.players = {};

        // Clear any active watchdogs or timers
        if (guestVideoWatchdogTimer) {
            clearTimeout(guestVideoWatchdogTimer);
            guestVideoWatchdogTimer = null;
        }
        stopPingMeasurement();

        // Notify all peers over WebRTC data channel before closing connections
        if (wasHost) {
            Object.keys(hostPeerConnections).forEach(function (id) {
                var pc = hostPeerConnections[id];
                if (pc && pc._inputDc && pc._inputDc.readyState === 'open') {
                    try {
                        pc._inputDc.send(JSON.stringify({ type: 'host-left', reason: 'Host left the game' }));
                    } catch (ex) { }
                }
            });
        }

        // Cleanly close all WebRTC connections, media overlays, and streams
        closeAllWebRtc();

        // Restore local emulator state
        var e = emu();
        if (e) {
            resetNetplayFrames(e);

            if (e.netplay) {
                var sock = e.netplay.socket;
                if (sock && typeof sock.emit === 'function') {
                    try {
                        sock.emit('leave-room', {});
                    } catch (err) { }
                }
                e.netplay.owner = false;
                e.netplay.isNetplay = false;
                e.netplay.room = null;
                e.netplay.localStream = null;
            }

            // Unconditionally unfreeze and restore single player emulator state completely
            unfreezeGuestCompletely(e);

            // Restore user configured volume
            if (typeof e.setVolume === 'function') {
                var origVol = (window.JellyEmuConfig && window.JellyEmuConfig.volume) || 1.0;
                try { e.setVolume(parseFloat(origVol)); } catch (err) { }
            }
        }

        // Reset UI
        var chatContainer = document.getElementById('je-np-chat-messages');
        if (chatContainer) {
            chatContainer.innerHTML = '<div style="color:#777; font-style:italic; font-size:11px;">Say hello to the room!</div>';
        }

        if (topbtnNetplay) topbtnNetplay.style.display = 'none';
        if (dockBtn) dockBtn.classList.remove('je-active');
        if (hdrStatus) hdrStatus.style.display = 'none';

        if (tabSession) tabSession.style.display = 'none';
        switchTab('rooms');

        // Immediately fetch the fresh room list so departing user sees updated slots/rooms
        fetchRooms(false);

        // Reset leaving flag after disconnection events settle
        setTimeout(function () {
            _isLeavingRoom = false;
        }, 500);
    }

    function onEjsRoomLeft() {
        performRoomLeftCleanup();
    }

    function attachRoomTerminationListeners(e) {
        if (!e || !e.netplay || !e.netplay.socket) return;
        if (e.netplay.socket._jeTerminationHooked) return;
        e.netplay.socket._jeTerminationHooked = true;

        var handleHostLeft = function (reason) {
            if (_isLeavingRoom) return;
            console.log('[JellyEmu Netplay] Host left or room closed:', reason);
            showNetplayToast(reason || 'Host left the game. Returning to single player...', 'info');
            performRoomLeftCleanup();
        };

        e.netplay.socket.on('host-left', function (data) {
            var msg = (data && data.reason) || 'Host left the game. Returning to single player...';
            handleHostLeft(msg);
        });

        e.netplay.socket.on('room-closed', function (data) {
            var msg = (data && data.reason) || 'Room closed by host. Returning to single player...';
            handleHostLeft(msg);
        });
    }

    function startHostStreamingToGuests(e) {
        if (!e || !e.netplay || !state.isHost) return;

        var myId = e.netplay.playerID || '';
        var mySocketId = (e.netplay.socket && e.netplay.socket.id) || '';

        // EmulatorJS native pipeline
        if (typeof e.netplay.createPeerConnection === 'function') {
            if (typeof e.netplay.initWebRTCStream === 'function') {
                e.netplay.initWebRTCStream().then(function () {
                    var players = state.players || {};
                    Object.keys(players).forEach(function (pid) {
                        if (pid !== myId) {
                            var guestInfo = players[pid];
                            var sid = guestInfo ? guestInfo.socketId : null;
                            if (sid && sid !== mySocketId) {
                                var existing = e.netplay.peerConnections && e.netplay.peerConnections[sid];
                                var isDead = existing && existing.pc && (existing.pc.connectionState === 'failed' || existing.pc.connectionState === 'closed');
                                if (!existing || isDead) {
                                    if (isDead) {
                                        try { existing.pc.close(); } catch (err) { }
                                        delete e.netplay.peerConnections[sid];
                                    }
                                    e.netplay.createPeerConnection(sid);
                                }
                            }
                        }
                    });
                }).catch(function (err) {
                    console.warn('[JellyEmu Netplay] Host initWebRTCStream caught:', err);
                });
            }
            return;
        }

        // JellyEmu WebRTC pipeline (fallback only when EmulatorJS does not provide native WebRTC)
        var players = state.players || {};
        Object.keys(players).forEach(function (pid) {
            if (pid !== myId) {
                var guestInfo = players[pid];
                var sid = guestInfo ? guestInfo.socketId : null;
                if (sid && sid !== mySocketId) {
                    var existing = hostPeerConnections[sid];
                    var isDead = existing && (existing.connectionState === 'failed' || existing.connectionState === 'closed' || existing.connectionState === 'disconnected');
                    if (!existing || isDead) {
                        if (isDead) {
                            try { existing.close(); } catch (err) { }
                            delete hostPeerConnections[sid];
                            delete hostIceQueues[sid];
                        }
                        console.log('[JellyEmu Netplay] Host establishing stream with guest:', sid);
                        initiateHostWebRtc(e, sid);
                    }
                }
            }
        });
    }

    function onEjsPlayersUpdated(players) {
        if (!state.inRoom || _isLeavingRoom) return;
        state.players = players || {};
        var e = emu();

        // If we are a guest, verify that the host is still present in the roster
        if (e && e.netplay && !state.isHost) {
            var hasHost = Object.keys(state.players).some(function (k) {
                var p = state.players[k];
                return p && (p.isOwner || p.owner);
            });
            if (!hasHost && Object.keys(state.players).length > 0) {
                console.log('[JellyEmu Netplay] Host missing from room roster. Returning to single player.');
                showNetplayToast('Host left the game. Returning to single player...', 'info');
                performRoomLeftCleanup();
                return;
            }
        }

        if (state.isHost && e && e.netplay) {
            startHostStreamingToGuests(e);
        }

        if (state.inRoom) {
            renderSessionInfo();
        }
    }

    // In-Game Chat Functions
    function appendChatMessage(senderName, message, isSelf) {
        var msgContainer = document.getElementById('je-np-chat-messages');
        if (!msgContainer) return;

        var placeholder = msgContainer.querySelector('div[style*="italic"]');
        if (placeholder) placeholder.remove();

        var row = document.createElement('div');
        row.style.lineHeight = '1.3';
        row.style.wordBreak = 'break-word';

        var author = document.createElement('strong');
        author.style.color = isSelf ? '#81c784' : '#64b5f6';
        author.style.marginRight = '6px';
        author.textContent = (senderName || 'Player') + ':';

        var body = document.createElement('span');
        body.style.color = '#eee';
        body.textContent = message;

        row.appendChild(author);
        row.appendChild(body);
        msgContainer.appendChild(row);
        msgContainer.scrollTop = msgContainer.scrollHeight;

        if (!isSelf) {
            showNetplayToast((senderName || 'Player') + ': ' + message, 'chat');
        }
    }

    function sendChatMessage() {
        var input = document.getElementById('je-np-chat-input');
        if (!input) return;
        var text = (input.value || '').trim();
        if (!text) return;

        var e = emu();
        if (e && e.netplay && e.netplay.socket && typeof e.netplay.socket.emit === 'function') {
            attachChatListener(e);
            var myName = getPlayerName();
            e.netplay.socket.emit('chat-message', {
                message: text,
                to: 'all',
                player_name: myName
            });
            input.value = '';
            input.focus();
        } else {
            showNetplayToast('Not connected to Netplay server', 'warning');
        }
    }

    function attachChatListener(e) {
        if (!e || !e.netplay || !e.netplay.socket) return;
        attachRoomTerminationListeners(e);
        if (e.netplay.socket._jeChatHooked) return;
        e.netplay.socket._jeChatHooked = true;

        e.netplay.socket.on('chat-message', function (data) {
            if (data && data.message) {
                var myId = (e.netplay && e.netplay.playerID) || '';
                var isSelf = (data.userid && data.userid === myId);
                appendChatMessage(data.player_name, data.message, isSelf);
            }
        });

        e.netplay.socket.on('data-message', function (data) {
            if (data && data['chat-message']) {
                var cm = data['chat-message'];
                if (cm && cm.message) {
                    var myId = (e.netplay && e.netplay.playerID) || '';
                    var isSelf = (cm.from && cm.from === myId);
                    appendChatMessage(cm.player_name, cm.message, isSelf);
                }
            }
        });

        e.netplay.socket.on('users-updated', function (players) {
            if (e && e.netplay) {
                e.netplay.players = players;
            }
            onEjsPlayersUpdated(players);
            handleUsersUpdatedWebRtc(e, players);
        });
    }

    // Render Active Session UI
    function renderSessionInfo() {
        var e = emu();
        if (e) attachChatListener(e);

        var nameEl = document.getElementById('je-np-sess-name');
        var idEl = document.getElementById('je-np-sess-id');
        var pwEl = document.getElementById('je-np-sess-password');
        var hostControls = document.getElementById('je-np-host-controls');
        var playersContainer = document.getElementById('je-np-players-list');

        if (nameEl) nameEl.textContent = state.roomName || 'Multiplayer Match';
        if (idEl) idEl.textContent = state.roomId ? 'Room ID: ' + state.roomId : '';
        if (pwEl) {
            if (state.password) {
                pwEl.style.display = 'inline-block';
                pwEl.textContent = 'Password: ' + state.password;
            } else {
                pwEl.style.display = 'none';
            }
        }

        if (hostControls) {
            hostControls.style.display = state.isHost ? 'flex' : 'none';
        }

        if (playersContainer) {
            playersContainer.innerHTML = '';
            var playersMap = state.players || {};
            var keys = Object.keys(playersMap);

            if (keys.length === 0) {
                playersContainer.innerHTML = '<div style="opacity:0.6;font-size:12px;">Waiting for players to connect…</div>';
            } else {
                var e = emu();
                var myId = (e && e.netplay && e.netplay.playerID) || '';
                var mySocketId = (e && e.netplay && e.netplay.socket && e.netplay.socket.id) || '';

                // Sort keys by joinOrder so player list order is always stable and deterministic
                keys.sort(function (a, b) {
                    var orderA = (playersMap[a] && playersMap[a].joinOrder) || 0;
                    var orderB = (playersMap[b] && playersMap[b].joinOrder) || 0;
                    return orderA - orderB;
                });

                keys.forEach(function (pid, idx) {
                    var p = playersMap[pid];
                    var isHostPlayer = (p.isOwner === true || p.owner === true || idx === 0);
                    var isMe = (myId && (pid === myId || p.userid === myId || p.playerId === myId)) ||
                               (mySocketId && p.socketId === mySocketId);

                    var pingVal = isMe
                        ? (playerPings['self'] !== undefined ? playerPings['self'] : (myId ? playerPings[myId] : null))
                        : getPingForPlayer(p, pid);

                    var pingClass = getPingClass(pingVal);
                    var pingText = formatPingText(pingVal);
                    var pingTitle = (pingVal !== null && pingVal !== undefined) ? 'Server ping: ' + pingVal + ' ms' : 'Measuring ping…';
                    var badgeId = 'je-np-ping-' + String(pid).replace(/[^a-zA-Z0-9_-]/g, '_');

                    var row = document.createElement('div');
                    row.className = 'je-netplay-player-row';
                    row.innerHTML = '<div style="display:flex;align-items:center;gap:10px;">' +
                        '<span class="je-np-player-num">P' + (idx + 1) + '</span>' +
                        '<span class="je-np-player-name">' + (p.player_name || 'Player ' + (idx + 1)) + '</span>' +
                        (isHostPlayer ? '<span class="je-badge je-badge-host">Host</span>' : '') +
                        (isMe ? '<span class="je-badge" style="background:rgba(255,255,255,0.08);color:#aaa;font-size:10px;padding:2px 6px;">You</span>' : '') +
                        '</div>' +
                        '<div id="' + badgeId + '" class="je-np-ping-badge ' + pingClass + '" title="' + pingTitle + '">' +
                        '<span class="je-np-ping-val">' + pingText + '</span>' +
                        '</div>';
                    playersContainer.appendChild(row);
                });

                if (state.inRoom && !pingMeasurementTimer) {
                    startPingMeasurement();
                } else if (state.inRoom) {
                    setTimeout(refreshPings, 60);
                }
            }
        }
    }

    // Active session button bindings
    if (leaveRoomBtn) {
        leaveRoomBtn.addEventListener('click', function () {
            console.log('[JellyEmu Netplay] Leave Room clicked by user');
            performRoomLeftCleanup();
        });
    }

    if (syncStateBtn) {
        syncStateBtn.addEventListener('click', function () {
            var e = emu();
            if (!e || !e.netplay) return;
            if (e.netplay.owner || state.isHost) {
                startHostStreamingToGuests(e);
                showNetplayToast('Refreshed video stream for guests', 'videocam');
            } else {
                // Guest requests host to refresh/renegotiate stream
                var hostSid = null;
                var players = state.players || {};
                Object.keys(players).forEach(function (pid) {
                    var p = players[pid];
                    if (p && (p.isOwner || p.owner) && p.socketId) {
                        hostSid = p.socketId;
                    }
                });
                if (typeof e.netplay.requestRenegotiate === 'function' && hostSid) {
                    e.netplay.requestRenegotiate(hostSid, 'user-refresh');
                } else if (e.netplay.socket) {
                    e.netplay.socket.emit('webrtc-signal', {
                        requestRenegotiate: true,
                        sender: e.netplay.socket.id,
                        target: hostSid
                    });
                }
                showNetplayToast('Requested stream refresh from host…', 'sync');
            }
        });
    }

    if (restartGameBtn) {
        restartGameBtn.addEventListener('click', function () {
            var e = emu();
            if (e && e.gameManager) {
                e.gameManager.restart();
                if (e.netplay && typeof e.netplay.sendMessage === 'function') {
                    e.netplay.sendMessage({ restart: true });
                }
            }
        });
    }

    // Settings Tab
    function renderSettingsTab() {
        var nameInput = document.getElementById('je-np-player-name');
        var gameIdDisplay = document.getElementById('je-np-gameid-display');
        var iceDisplay = document.getElementById('je-np-ice-display');
        var qualityPresetSelect = document.getElementById('je-np-quality-preset');

        if (nameInput) nameInput.value = getPlayerName();
        if (qualityPresetSelect) qualityPresetSelect.value = getSelectedQualityPreset();
        updateServerStatusDisplay();
        checkNetplayPing();
        if (gameIdDisplay) gameIdDisplay.textContent = gameId.toString();
        if (iceDisplay) {
            var urls = (activeIceServers || []).map(function (s) {
                if (!s) return '';
                if (typeof s.urls === 'string') return s.urls;
                if (Array.isArray(s.urls)) return s.urls.join('\n');
                return JSON.stringify(s);
            }).filter(Boolean).join('\n');
            iceDisplay.textContent = urls || 'Default (Google STUN)';
        }
    }

    var qualityPresetSelect = document.getElementById('je-np-quality-preset');
    if (qualityPresetSelect) {
        qualityPresetSelect.addEventListener('change', function () {
            var val = qualityPresetSelect.value || 'high';
            try {
                if (window.localStorage) {
                    localStorage.setItem('je_np_stream_quality', val);
                }
            } catch (err) { }
            updateAllActiveHostBitrates();
            showNetplayToast('Streaming quality set to ' + val.charAt(0).toUpperCase() + val.slice(1), 'videocam');
        });
    }

    var saveNameBtn = document.getElementById('je-np-save-name-btn');
    if (saveNameBtn) {
        saveNameBtn.addEventListener('click', function () {
            var nameInput = document.getElementById('je-np-player-name');
            if (nameInput && nameInput.value) {
                setPlayerName(nameInput.value);
                saveNameBtn.textContent = 'Saved!';
                setTimeout(function () { saveNameBtn.textContent = 'Save Nickname'; }, 1500);
            }
        });
    }

    // Chat UI Listeners
    var chatSendBtn = document.getElementById('je-np-chat-send');
    var chatInput = document.getElementById('je-np-chat-input');
    var chatMessages = document.getElementById('je-np-chat-messages');

    if (chatSendBtn) {
        chatSendBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            sendChatMessage();
        });
    }

    if (chatInput) {
        ['keydown', 'keyup', 'keypress'].forEach(function (evName) {
            chatInput.addEventListener(evName, function (evt) {
                evt.stopPropagation();
                if (evName === 'keydown' && evt.key === 'Enter') {
                    evt.preventDefault();
                    sendChatMessage();
                }
            });
        });

        ['mousedown', 'mouseup', 'click', 'pointerdown'].forEach(function (evName) {
            chatInput.addEventListener(evName, function (evt) {
                evt.stopPropagation();
            });
        });
    }

    if (chatMessages) {
        ['mousedown', 'mouseup', 'click', 'wheel', 'scroll', 'pointerdown'].forEach(function (evName) {
            chatMessages.addEventListener(evName, function (evt) {
                evt.stopPropagation();
            });
        });
    }

    // Stop keystroke propagation on other popup inputs so typing is never blocked while game is running
    ['je-np-player-name', 'je-np-host-roomname', 'je-np-host-password'].forEach(function (id) {
        var el = document.getElementById(id);
        if (el) {
            ['keydown', 'keyup', 'keypress'].forEach(function (evName) {
                el.addEventListener(evName, function (evt) {
                    evt.stopPropagation();
                });
            });
            ['mousedown', 'mouseup', 'click', 'pointerdown'].forEach(function (evName) {
                el.addEventListener(evName, function (evt) {
                    evt.stopPropagation();
                });
            });
        }
    });

    // Dock button click listener
    if (dockBtn) {
        dockBtn.removeAttribute('disabled');
        dockBtn.addEventListener('click', function () {
            var e = emu();
            if (e) ensureNetplaySubsystem(e);

            if (typeof window._jeOpenPopup === 'function') {
                window._jeOpenPopup('je-pop-netplay');
            }

            if (state.inRoom) {
                switchTab('session');
            } else {
                switchTab('rooms');
            }
        });
    }

    if (topbtnNetplay) {
        topbtnNetplay.addEventListener('click', function () {
            if (typeof window._jeOpenPopup === 'function') {
                window._jeOpenPopup('je-pop-netplay');
            }
            switchTab('session');
        });
    }

    // On game start, ensure subsystem is bound & prime ping check
    window.addEventListener('jellyemu:gamestart', function (evt) {
        var e = (evt && evt.detail && evt.detail.emulator) || emu();
        if (e) ensureNetplaySubsystem(e);
        checkNetplayPing();
    });

    // Safe disposal on tab close, page navigation, or room exit
    function disposeNetplayCompletely() {
        try {
            var e = emu();
            if (e && e.netplay) {
                if (state.inRoom && e.netplay.socket && e.netplay.socket.connected) {
                    try {
                        if (e.netplay.owner || state.isHost) {
                            if (typeof e.netplay.sendMessage === 'function') {
                                e.netplay.sendMessage({ type: 'host-left' });
                            }
                        }
                        if (typeof e.netplay.leaveRoom === 'function') {
                            e.netplay.leaveRoom();
                        } else {
                            e.netplay.socket.emit('leave-room', {});
                        }
                    } catch (ex) { }
                }
            }
        } catch (ex) { }

        closeAllWebRtc();
        if (guestVideoWatchdogTimer) {
            clearTimeout(guestVideoWatchdogTimer);
            guestVideoWatchdogTimer = null;
        }
        if (state.autoRefreshTimer) {
            clearInterval(state.autoRefreshTimer);
            state.autoRefreshTimer = null;
        }
        stopPingMeasurement();
    }

    window.addEventListener('beforeunload', disposeNetplayCompletely);
    window.addEventListener('pagehide', disposeNetplayCompletely);

    window.JellyEmuNetplay = {
        get inRoom() { return state.inRoom; },
        get isHost() { return state.isHost; },
        get roomName() { return state.roomName; },
        get roomId() { return state.roomId; },
        isHosting: function () { return state.inRoom && state.isHost; },
        unfreeze: function () { unfreezeGuestCompletely(emu()); },
        dispose: function () { disposeNetplayCompletely(); }
    };

})();
