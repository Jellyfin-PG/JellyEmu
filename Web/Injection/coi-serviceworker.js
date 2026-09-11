/*! coi-serviceworker v0.1.7 (high-performance navigation-only filter) */
(function() {
    if (typeof window !== 'undefined') {
        if ('serviceWorker' in navigator && window.isSecureContext) {
            navigator.serviceWorker.register('/jellyemu/assets/coi-serviceworker.js', { scope: '/' })
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
            // Only intercept top-level HTML document navigations (COOP/COEP only required on documents)
            if (r.mode === 'navigate' || r.destination === 'document') {
                if (r.cache === 'only-if-cached' && r.mode !== 'same-origin') return;

                event.respondWith(
                    fetch(r).then(function(res) {
                        if (res.status === 0) return res;

                        var newHeaders = new Headers(res.headers);
                        newHeaders.set('Cross-Origin-Embedder-Policy', 'credentialless');
                        newHeaders.set('Cross-Origin-Opener-Policy', 'same-origin');

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
