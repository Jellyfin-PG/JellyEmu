/**
 * JellyEmu Shared Client Utilities
 * Single source of truth for URL resolution, dynamic Base URL detection,
 * authenticated API requests, and cross-module helper methods.
 */
(function (global) {
    'use strict';

    global.JellyEmu = global.JellyEmu || {};
    var JE = global.JellyEmu;

    /**
     * Resolves the active base URL (e.g. "/jellyfin" or "") from any available runtime source.
     * @returns {string}
     */
    JE.getBaseUrl = function () {
        if (typeof JE.baseUrl === 'string' && JE.baseUrl !== '') {
            return JE.baseUrl;
        }

        var base = '';
        if (typeof global.ApiClient !== 'undefined') {
            if (typeof global.ApiClient.baseUrl === 'function') {
                base = global.ApiClient.baseUrl() || '';
            } else if (typeof global.ApiClient.serverAddress === 'function') {
                try {
                    var s = global.ApiClient.serverAddress();
                    if (s) {
                        base = (new URL(s, global.location.origin)).pathname;
                    }
                } catch (e) { }
            } else if (global.ApiClient._serverAddress) {
                try {
                    base = (new URL(global.ApiClient._serverAddress, global.location.origin)).pathname;
                } catch (e) { }
            }
        }

        if (!base && global.location && global.location.pathname) {
            var idx = global.location.pathname.indexOf('/web');
            if (idx > 0) {
                base = global.location.pathname.substring(0, idx);
            } else {
                var emuIdx = global.location.pathname.indexOf('/jellyemu');
                if (emuIdx > 0) {
                    base = global.location.pathname.substring(0, emuIdx);
                }
            }
        }

        if (!base && global.JellyEmuConfig && global.JellyEmuConfig.baseUrl) {
            base = global.JellyEmuConfig.baseUrl;
        }

        JE.baseUrl = (base || '').replace(/\/+$/, '');
        return JE.baseUrl;
    };

    /**
     * Retrieves the Jellyfin authentication token from runtime ApiClient, parent ApiClient, or localStorage.
     * @param {string} [userId]
     * @returns {string}
     */
    JE.getAuthToken = function (userId) {
        if (global._jellyToken) return global._jellyToken;
        if (global.JellyEmuConfig && global.JellyEmuConfig.token) return global.JellyEmuConfig.token;
        if (global.JELLYEMU_CONFIG && global.JELLYEMU_CONFIG.token) return global.JELLYEMU_CONFIG.token;

        try {
            if (global.parent && global.parent !== global && global.parent.ApiClient && typeof global.parent.ApiClient.accessToken === 'function') {
                var pt = global.parent.ApiClient.accessToken();
                if (pt) return pt;
            }
        } catch (e) { }

        try {
            if (global.ApiClient && typeof global.ApiClient.accessToken === 'function') {
                var t = global.ApiClient.accessToken();
                if (t) return t;
            }
        } catch (e) { }

        try {
            var creds = JSON.parse(global.localStorage.getItem('jellyfin_credentials') || '{}');
            var servers = creds.Servers || [];
            if (userId) {
                var normUserId = String(userId).replace(/-/g, '').toLowerCase();
                var matched = servers.find(function (s) {
                    return s.UserId && String(s.UserId).replace(/-/g, '').toLowerCase() === normUserId;
                });
                if (matched && matched.AccessToken) return matched.AccessToken;
            }
            var firstValid = servers.find(function (s) { return s.AccessToken; });
            if (firstValid && firstValid.AccessToken) return firstValid.AccessToken;
        } catch (e) { }

        return '';
    };

    /**
     * Constructs authorization headers for Jellyfin API endpoints.
     * @param {string} [token]
     * @param {Object} [customHeaders]
     * @returns {Object}
     */
    JE.getAuthHeaders = function (token, customHeaders) {
        var t = token || JE.getAuthToken();
        var h = Object.assign({}, customHeaders || {});
        if (t) {
            h['Authorization'] = 'MediaBrowser Token="' + t + '"';
            h['X-MediaBrowser-Token'] = t;
            h['X-Emby-Token'] = t;
            h['X-Emby-Authorization'] = 'MediaBrowser Client="JellyEmu", Device="Browser", DeviceId="jellyemu", Version="1.0", Token="' + t + '"';
        }
        return h;
    };

    /**
     * Constructs a fully-qualified or base-prefixed URL for any backend endpoint.
     * @param {string} endpoint - The relative endpoint path (e.g. "/jellyemu/rom/123" or "jellyemu/systems")
     * @returns {string}
     */
    JE.getUrl = function (endpoint) {
        if (!endpoint) return JE.getBaseUrl();
        if (/^https?:\/\//i.test(endpoint)) return endpoint;

        var clean = endpoint.replace(/^\//, '');

        if (typeof global.ApiClient !== 'undefined' && typeof global.ApiClient.getUrl === 'function') {
            return global.ApiClient.getUrl(clean);
        }

        var b = JE.getBaseUrl();
        return (b ? b : '') + '/' + clean;
    };

    /**
     * Executes an authenticated fetch request to a JellyEmu or Jellyfin endpoint with automatic Base URL prefixing.
     * @param {string} endpoint
     * @param {RequestInit} [options]
     * @returns {Promise<Response>}
     */
    JE.fetch = function (endpoint, options) {
        var opts = Object.assign({}, options || {});
        var token = JE.getAuthToken();
        if (token) {
            opts.headers = JE.getAuthHeaders(token, opts.headers);
        }
        return fetch(JE.getUrl(endpoint), opts);
    };

    /**
     * Fetches JSON from an endpoint with automatic Base URL and auth headers.
     * @param {string} endpoint
     * @param {RequestInit} [options]
     * @returns {Promise<any|null>}
     */
    JE.json = async function (endpoint, options) {
        try {
            var res = await JE.fetch(endpoint, options);
            if (!res.ok) return null;
            return await res.json();
        } catch (err) {
            console.warn('[JellyEmu] JSON fetch failed for ' + endpoint, err);
            return null;
        }
    };

    /**
     * Displays a lightweight toast notification on the active screen.
     * @param {string} message
     * @param {number|string} [durationOrType=2500]
     */
    JE.toast = function (message, durationOrType) {
        if (!global.document || !global.document.body) return;
        var duration = typeof durationOrType === 'number' ? durationOrType : 2500;
        var toast = global.document.createElement('div');
        toast.className = 'je-toast' + (typeof durationOrType === 'string' ? ' je-toast-' + durationOrType : '');
        toast.textContent = message;
        global.document.body.appendChild(toast);

        setTimeout(function () {
            toast.style.opacity = '0';
            toast.style.transform = 'translateX(-50%) translateY(10px)';
            setTimeout(function () {
                if (toast.parentNode) toast.remove();
            }, 300);
        }, duration);
    };

    /**
     * Uploads a screenshot to the Jellyfin screenshot library or falls back to local download.
     * @param {string} itemId
     * @param {string|Blob} dataUrlOrBlob
     * @param {Object} [options]
     * @param {boolean} [options.toLibrary=true]
     * @param {string} [options.gameName='screenshot']
     * @returns {Promise<boolean>}
     */
    JE.uploadScreenshot = async function (itemId, dataUrlOrBlob, options) {
        var opts = options || {};
        var toLibrary = opts.toLibrary !== false;
        var gameName = (opts.gameName || 'screenshot').replace(/[^a-z0-9]/gi, '_');

        if (toLibrary && itemId) {
            try {
                var res;
                if (typeof dataUrlOrBlob === 'string' && dataUrlOrBlob.startsWith('data:')) {
                    res = await JE.fetch('/jellyemu/screenshot/' + itemId, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ dataUrl: dataUrlOrBlob })
                    });
                } else {
                    var blob = dataUrlOrBlob;
                    if (typeof dataUrlOrBlob === 'string') {
                        var bRes = await fetch(dataUrlOrBlob);
                        blob = await bRes.blob();
                    }
                    var formData = new FormData();
                    formData.append('file', blob, itemId + '_' + Date.now() + '.png');
                    res = await JE.fetch('/jellyemu/screenshot/' + itemId, {
                        method: 'POST',
                        body: formData
                    });
                }

                if (res && res.ok) {
                    JE.toast('Screenshot saved to Jellyfin library');
                    return true;
                }
            } catch (err) {
                console.warn('[JellyEmu] Screenshot library upload failed:', err);
            }
        }

        // Local browser download fallback
        try {
            var a = global.document.createElement('a');
            a.href = typeof dataUrlOrBlob === 'string' ? dataUrlOrBlob : URL.createObjectURL(dataUrlOrBlob);
            a.download = gameName + '_' + Date.now() + '.png';
            global.document.body.appendChild(a);
            a.click();
            a.remove();
            JE.toast('Screenshot downloaded');
            return true;
        } catch (err) {
            console.error('[JellyEmu] Screenshot download failed:', err);
            JE.toast('Screenshot capture failed');
            return false;
        }
    };

    /**
     * Formats an ISO date string into a user-friendly localized date string.
     * @param {string} iso
     * @returns {string}
     */
    JE.formatDate = function (iso) {
        if (!iso) return '';
        try {
            var d = new Date(iso);
            return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) +
                   ' ' + d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
        } catch (e) {
            return String(iso);
        }
    };

    /**
     * Formats a byte count into a human-readable string (e.g. "2.4 MB").
     * @param {number} bytes
     * @returns {string}
     */
    JE.formatBytes = function (bytes) {
        if (!bytes || bytes === 0) return '0 B';
        var k = 1024;
        var sizes = ['B', 'KB', 'MB', 'GB'];
        var i = Math.floor(Math.log(bytes) / Math.log(k));
        return (bytes / Math.pow(k, i)).toFixed(1) + ' ' + sizes[i];
    };

    /**
     * Opens a popup/modal element by ID.
     * @param {string} popupId
     */
    JE.openModal = function (popupId) {
        var el = global.document.getElementById(popupId);
        if (el) el.classList.add('je-active');
    };

    /**
     * Closes a popup/modal element by ID.
     * @param {string} popupId
     */
    JE.closeModal = function (popupId) {
        var el = global.document.getElementById(popupId);
        if (el) el.classList.remove('je-active');
    };

    /**
     * Binds backdrop click listeners on .je-popup elements.
     */
    JE.initModals = function () {
        if (!global.document) return;
        global.document.querySelectorAll('.je-popup').forEach(function (popup) {
            popup.addEventListener('click', function (e) {
                if (e.target === popup) {
                    popup.classList.remove('je-active');
                }
            });
        });
    };

})(typeof window !== 'undefined' ? window : this);

