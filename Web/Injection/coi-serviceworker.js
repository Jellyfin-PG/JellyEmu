/*! coi-serviceworker v0.1.7 (high-performance navigation-only filter) */
(function() {
    if (typeof window !== 'undefined') {
        var coi = window.JellyEmuCOI = { reloadPending: false };

        if ('serviceWorker' in navigator && window.isSecureContext) {
            var swUrl = (document.currentScript && document.currentScript.src)
                || (window.JellyEmu && window.JellyEmu.getUrl ? window.JellyEmu.getUrl('/jellyemu/assets/coi-serviceworker.js') : null)
                || (window.JellyEmuConfig && window.JellyEmuConfig.baseUrl ? window.JellyEmuConfig.baseUrl + '/jellyemu/assets/coi-serviceworker.js' : '/jellyemu/assets/coi-serviceworker.js');

            // Without an explicit scope the worker would only control /jellyemu/assets/, never the
            // player pages (/jellyemu/play/..., /jellyemu/ejs/play/...). The server sends
            // "Service-Worker-Allowed: /" so the wider /jellyemu/ scope is permitted.
            var scopeMatch = /^(.*\/jellyemu\/)assets\/coi-serviceworker\.js/.exec(new URL(swUrl, window.location.href).href);
            var options = scopeMatch ? { scope: scopeMatch[1] } : undefined;

            var reloadOnce = function () {
                if (window.crossOriginIsolated) return;
                if (sessionStorage.getItem('coi_reloaded')) {
                    console.warn('[JellyEmu COI] Cross-Origin Isolation could not be established. Stopping reload loop.');
                    return;
                }
                sessionStorage.setItem('coi_reloaded', 'true');
                window.location.reload();
            };

            if (window.crossOriginIsolated) {
                sessionStorage.removeItem('coi_reloaded');
            } else if (!sessionStorage.getItem('coi_reloaded')) {
                coi.reloadPending = true;
            }

            navigator.serviceWorker.register(swUrl, options)
                .then(function (reg) {
                    reg.addEventListener('updatefound', function () {
                        if (!navigator.serviceWorker.controller) return;
                        var installingWorker = reg.installing;
                        if (!installingWorker) return;
                        installingWorker.addEventListener('statechange', function () {
                            if (installingWorker.state === 'activated') reloadOnce();
                        });
                    });
                })
                .catch(function (err) {
                    coi.reloadPending = false;
                    console.warn('[JellyEmu COI] ServiceWorker registration failed:', err);
                });

            if (navigator.serviceWorker.controller) {
                // Controlled but not isolated: the page was served before the worker took over.
                reloadOnce();
            } else {
                // First visit: the worker claims this page once activated; reload so the
                // navigation response goes through it and gets the isolation headers.
                navigator.serviceWorker.addEventListener('controllerchange', reloadOnce);
            }
        }
    } else {
        // Service Worker context: Only intercept top-level page navigations for max performance
        self.addEventListener('install', function() {
            self.skipWaiting();
        });

        self.addEventListener('activate', function(event) {
            event.waitUntil(self.clients.claim());
        });

        self.addEventListener('fetch', function(event) {
            var r = event.request;
            // Only intercept same-origin requests; never hijack third-party scripts/beacons
            try {
                if (new URL(r.url).origin !== self.location.origin) return;
            } catch (e) {
                return;
            }
            // Intercept top-level HTML document navigations as well as workers and scripts for cross-origin isolation
            if (r.mode === 'navigate' || r.destination === 'document' || r.destination === 'worker' || r.destination === 'sharedworker' || r.destination === 'script') {
                if (r.cache === 'only-if-cached' && r.mode !== 'same-origin') return;

                event.respondWith(
                    fetch(r).then(function(res) {
                        if (res.status === 0) return res;

                        var newHeaders = new Headers(res.headers);
                        newHeaders.set('Cross-Origin-Embedder-Policy', 'credentialless');
                        newHeaders.set('Cross-Origin-Opener-Policy', 'same-origin');
                        newHeaders.set('Cross-Origin-Resource-Policy', 'cross-origin');

                        return new Response(res.body, {
                            status: res.status,
                            statusText: res.statusText,
                            headers: newHeaders
                        });
                    }).catch(function() {
                        return fetch(r);
                    })
                );
            }
        });
    }
})();
