// JellyEmu Play! Settings & Options Manager
// Handles Aspect ratio, Frameskip, Audio volume, Screenshot capturing, and Stats HUD.
(function(window) {
    'use strict';

    class PlaySettingsManager {
        constructor(config) {
            this.itemId = config.itemId || '';
            this.userId = config.userId || '';
            this.screenshotsToLibrary = !!config.screenshotsToLibrary;
            this.playModule = null;
            this.statsInterval = null;
        }

        setPlayModule(playModule) {
            this.playModule = playModule;
        }

        init() {
            this.setupPopups();
            this.setupSettingsHandlers();
            this.setupStatsHUD();
        }

        setupPopups() {
            window._jeOpenPopup = (popupId) => {
                if (window.JellyEmu && typeof window.JellyEmu.openModal === 'function') {
                    window.JellyEmu.openModal(popupId);
                } else {
                    const el = document.getElementById(popupId);
                    if (el) el.classList.add('je-active');
                }
            };

            window._jeClosePopup = (popupId) => {
                if (window.JellyEmu && typeof window.JellyEmu.closeModal === 'function') {
                    window.JellyEmu.closeModal(popupId);
                } else {
                    const el = document.getElementById(popupId);
                    if (el) el.classList.remove('je-active');
                }
            };

            window._jeToast = (msg, durationMs = 2500) => {
                if (window.JellyEmu && typeof window.JellyEmu.toast === 'function') {
                    window.JellyEmu.toast(msg, durationMs);
                }
            };

            if (window.JellyEmu && typeof window.JellyEmu.initModals === 'function') {
                window.JellyEmu.initModals();
            }
        }

        setupSettingsHandlers() {
            // Aspect ratio
            const aspectSelect = document.getElementById('je-setting-aspect');
            if (aspectSelect) {
                aspectSelect.addEventListener('change', (e) => {
                    const canvas = document.getElementById('outputCanvas');
                    if (!canvas) return;
                    if (e.target.value === '16:9') {
                        canvas.style.aspectRatio = '16/9';
                    } else if (e.target.value === 'stretch') {
                        canvas.style.width = '100%';
                        canvas.style.height = '100%';
                        canvas.style.objectFit = 'fill';
                    } else {
                        canvas.style.aspectRatio = '4/3';
                        canvas.style.objectFit = 'contain';
                    }
                });
            }

            // Frameskip
            const frameskipSelect = document.getElementById('je-setting-frameskip');
            if (frameskipSelect) {
                frameskipSelect.addEventListener('change', (e) => {
                    const val = parseInt(e.target.value, 10) || 0;
                    if (this.playModule && typeof this.playModule.setGsFrameskip === 'function') {
                        this.playModule.setGsFrameskip(val);
                        window._jeToast(`Frameskip set to ${val}`);
                    }
                });
            }

            // Audio Sync mode
            const audioSyncSelect = document.getElementById('je-setting-audiosync');
            if (audioSyncSelect) {
                audioSyncSelect.addEventListener('change', (e) => {
                    const mode = e.target.value;
                    window.__audioSyncMode = mode;
                    if (window.__activeAudioSources) {
                        window.__activeAudioSources.forEach(src => {
                            try {
                                if (mode === 'dynamic') {
                                    src.playbackRate.value = Math.max(0.12, Math.min(1.0, window.__currentSpeedRatio || 1.0));
                                } else if (mode === 'realtime') {
                                    src.playbackRate.value = 1.0;
                                }
                            } catch(err) {}
                        });
                    }
                    window._jeToast(`Audio Sync: ${mode === 'dynamic' ? 'Dynamic' : 'Real-time'}`);
                });
            }

            // Stats HUD toggle
            const hudToggle = document.getElementById('je-setting-hud');
            if (hudToggle) {
                hudToggle.addEventListener('change', (e) => {
                    const hud = document.getElementById('je-stats-hud');
                    if (hud) {
                        hud.classList.toggle('je-visible', e.target.checked);
                    }
                });
            }

            // Volume
            const volSlider = document.getElementById('je-volume-slider');
            if (volSlider) {
                const storedVol = localStorage.getItem('jellyemu_play_volume');
                const initialVol = storedVol !== null ? Math.max(0, Math.min(1, parseFloat(storedVol) || 0)) : 1.0;
                volSlider.value = initialVol;
                if (typeof window.setPlayVolume === 'function') {
                    window.setPlayVolume(initialVol);
                }

                volSlider.addEventListener('input', (e) => {
                    const vol = parseFloat(e.target.value);
                    try { localStorage.setItem('jellyemu_play_volume', String(vol)); } catch(err) {}
                    if (this.playModule && typeof this.playModule.setVolume === 'function') {
                        this.playModule.setVolume(vol);
                    } else if (typeof window.setPlayVolume === 'function') {
                        window.setPlayVolume(vol);
                    }
                });
            }
        }

        setupStatsHUD() {
            const hud = document.getElementById('je-stats-hud');

            let lastTime = performance.now();
            this.statsInterval = setInterval(() => {
                if (!this.playModule) return;

                const now = performance.now();
                const dt = (now - lastTime) / 1000;
                lastTime = now;

                let frames = 0;
                try {
                    if (typeof this.playModule.getFrames === 'function') {
                        frames = this.playModule.getFrames() || 0;
                        if (typeof this.playModule.clearStats === 'function') {
                            this.playModule.clearStats();
                        }
                    }
                } catch (e) {}

                const fps = dt > 0 ? Math.round((frames / dt) * 10) / 10 : 0;
                const speed = Math.round((fps / 59.94) * 100);

                // Update dynamic audio speed ratio
                const rawRatio = dt > 0 ? (frames / dt) / 59.94 : 1.0;
                window.__currentSpeedRatio = Math.max(0.12, Math.min(1.0, rawRatio));

                if (window.__audioSyncMode === 'dynamic' && window.__activeAudioSources) {
                    window.__activeAudioSources.forEach(src => {
                        try {
                            src.playbackRate.value = window.__currentSpeedRatio;
                        } catch(err) {}
                    });
                }

                if (hud) {
                    hud.textContent = `FPS: ${fps} | Speed: ${speed}%`;
                }
            }, 1000);
        }

        async captureScreenshot() {
            const canvas = document.getElementById('outputCanvas');
            if (!canvas) return;

            try {
                const dataUrl = canvas.toDataURL('image/png');
                if (window.JellyEmu && typeof window.JellyEmu.uploadScreenshot === 'function') {
                    await window.JellyEmu.uploadScreenshot(this.itemId, dataUrl, {
                        toLibrary: this.screenshotsToLibrary,
                        gameName: `PlayStation2_${this.itemId}`
                    });
                }
            } catch (err) {
                console.error('[JellyEmu Play!] Screenshot failed:', err);
                window._jeToast('Screenshot capture failed');
            }
        }
    }

    window.PlaySettingsManager = PlaySettingsManager;
})(window);
