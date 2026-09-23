/*! coi-serviceworker v0.1.7 (high-performance navigation-only filter) */
(function() {
    if (typeof window !== 'undefined') {
        if ('serviceWorker' in navigator && window.isSecureContext) {
            var swUrl = (document.currentScript && document.currentScript.src)
                || (window.JellyEmu && window.JellyEmu.getUrl ? window.JellyEmu.getUrl('/jellyemu/assets/coi-serviceworker.js') : null)
                || (window.JellyEmuConfig && window.JellyEmuConfig.baseUrl ? window.JellyEmuConfig.baseUrl + '/jellyemu/assets/coi-serviceworker.js' : '/jellyemu/assets/coi-serviceworker.js');
            navigator.serviceWorker.register(swUrl)
                .then(function(reg) {
                    reg.addEventListener('updatefound', function() {
                        if (!navigator.serviceWorker.controller) return;
                        var installingWorker = reg.installing;
                        if (!installingWorker) return;
                        installingWorker.addEventListener('statechange', function() {
                            if (installingWorker.state === 'activated' && !window.crossOriginIsolated) {
                                if (!sessionStorage.getItem('coi_reloaded')) {
                                    sessionStorage.setItem('coi_reloaded', 'true');
                                    window.location.reload();
                                }
                            }
                        });
                    });
                })
                .catch(function(err) {
                    console.warn('[JellyEmu COI] ServiceWorker registration failed:', err);
                });

            if (!window.crossOriginIsolated && navigator.serviceWorker.controller) {
                if (!sessionStorage.getItem('coi_reloaded')) {
                    sessionStorage.setItem('coi_reloaded', 'true');
                    window.location.reload();
                } else {
                    console.warn('[JellyEmu COI] Cross-Origin Isolation could not be established. Stopping reload loop.');
                }
            } else if (window.crossOriginIsolated) {
                sessionStorage.removeItem('coi_reloaded');
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
