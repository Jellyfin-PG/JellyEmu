import { useState, useEffect, useCallback } from 'react';
import { PlayerBridge } from '../services/playerBridge';

export function usePlayerHUD(autoHideMs: number = 3000) {
  const [visible, setVisible] = useState<boolean>(true);
  const [isMinimized, setIsMinimized] = useState<boolean>(false);
  const [volume, setVolumeState] = useState<number>(100);
  const [isMuted, setIsMuted] = useState<boolean>(false);
  const [fpsMode, setFpsMode] = useState<string>('0');
  const [fps, setFps] = useState<number>(60);
  const [speedPercent, setSpeedPercent] = useState<number>(100);
  const [isPaused, setIsPaused] = useState<boolean>(false);
  const [isFastForward, setIsFastForward] = useState<boolean>(false);
  const [isSlowMotion, setIsSlowMotion] = useState<boolean>(false);
  const [isFullscreen, setIsFullscreen] = useState<boolean>(false);
  const [isNetplayActive, setIsNetplayActive] = useState<boolean>(false);
  const [isVrSupported, setIsVrSupported] = useState<boolean>(false);
  const [isArSupported, setIsArSupported] = useState<boolean>(false);
  const [hasOpenModal, setHasOpenModal] = useState<boolean>(false);

  const showHUD = useCallback(() => {
    setVisible(true);
  }, []);

  // Check VR and AR capability
  useEffect(() => {
    let isMounted = true;
    const checkXR = async () => {
      const vr = await PlayerBridge.isVrSupported();
      const ar = await PlayerBridge.isArSupported();
      if (isMounted) {
        setIsVrSupported(vr);
        setIsArSupported(ar);
      }
    };
    checkXR();

    const handleXrStatus = (e: any) => {
      if (e?.detail) {
        if (e.detail.vrSupported !== undefined) setIsVrSupported(Boolean(e.detail.vrSupported));
        if (e.detail.arSupported !== undefined) setIsArSupported(Boolean(e.detail.arSupported));
      }
    };
    window.addEventListener('jellyemu:xr-status', handleXrStatus);
    return () => {
      isMounted = false;
      window.removeEventListener('jellyemu:xr-status', handleXrStatus);
    };
  }, []);

  // Initialize audio and FPS mode from config
  useEffect(() => {
    const cfg = PlayerBridge.getConfig();
    if (cfg) {
      const vol = parseFloat(cfg.volume || '1.0');
      const muted = cfg.mute === '1';
      if (!isNaN(vol)) setVolumeState(Math.round(vol * 100));
      setIsMuted(muted);
    }
    const rawMode = cfg?.showFps || (window as any).JellyEmuConfig?.showFps || (window as any)._globPrefs?.showFps || '0';
    setFpsMode(String(rawMode));
  }, []);

  // Synchronize FPS mode when settings change
  useEffect(() => {
    const handleFpsChanged = (ev: any) => {
      const mode = ev?.detail?.mode !== undefined ? String(ev.detail.mode) : '0';
      setFpsMode(mode);
    };
    const handleSettingsChanged = () => {
      const cfg = PlayerBridge.getConfig();
      const rawMode = cfg?.showFps || (window as any).JellyEmuConfig?.showFps || (window as any)._globPrefs?.showFps || '0';
      setFpsMode(String(rawMode));
    };

    const handleSpeedChanged = (ev: any) => {
      if (ev?.detail) {
        if (typeof ev.detail.ff === 'boolean') setIsFastForward(ev.detail.ff);
        if (typeof ev.detail.slow === 'boolean') setIsSlowMotion(ev.detail.slow);
      }
    };

    window.addEventListener('jellyemu:fps-changed', handleFpsChanged);
    window.addEventListener('jellyemu:settings-changed', handleSettingsChanged);
    window.addEventListener('jellyemu:speed-changed', handleSpeedChanged);
    return () => {
      window.removeEventListener('jellyemu:fps-changed', handleFpsChanged);
      window.removeEventListener('jellyemu:settings-changed', handleSettingsChanged);
      window.removeEventListener('jellyemu:speed-changed', handleSpeedChanged);
    };
  }, []);

  // Live FPS & Speed calculation loop (active across EJS, PS2, and PICO-8)
  useEffect(() => {
    if (fpsMode === '0') {
      return;
    }

    let rafId: number | null = null;
    let frameCount = 0;
    let lastTime = performance.now();
    let lastPlayFrames = 0;

    const tick = (now: number) => {
      frameCount++;
      const elapsed = now - lastTime;

      // Calculate stats at 500ms intervals
      if (elapsed >= 500) {
        const dt = elapsed / 1000;
        let calculatedFps = 0;
        let calculatedSpeed = 100;

        // PS2 (Play!) frame tracking
        const playModule = (window as any).PlayModule;
        if (playModule && typeof playModule.getFrames === 'function') {
          const totalFrames = playModule.getFrames() || 0;
          const deltaFrames = Math.max(0, totalFrames - lastPlayFrames);
          lastPlayFrames = totalFrames;
          if (typeof playModule.clearStats === 'function') {
            try { playModule.clearStats(); } catch (_) {}
            lastPlayFrames = 0;
          }
          calculatedFps = dt > 0 ? Math.round((deltaFrames / dt) * 10) / 10 : 0;
          calculatedSpeed = Math.max(0, Math.round((calculatedFps / 59.94) * 100));
        } else {
          // EJS / PICO-8 RAF-based frame rate
          calculatedFps = dt > 0 ? Math.round((frameCount / dt) * 10) / 10 : 0;
          const targetFps = (calculatedFps <= 35 && calculatedFps >= 20) ? 30 : 60;
          calculatedSpeed = Math.max(0, Math.round((calculatedFps / targetFps) * 100));
        }

        setFps(calculatedFps);
        setSpeedPercent(calculatedSpeed);
        frameCount = 0;
        lastTime = now;
      }

      rafId = requestAnimationFrame(tick);
    };

    rafId = requestAnimationFrame(tick);

    return () => {
      if (rafId) cancelAnimationFrame(rafId);
    };
  }, [fpsMode]);

  // Auto-hide HUD on inactivity unless a modal is open or dock is minimized
  useEffect(() => {
    if (hasOpenModal) {
      setVisible(true);
      return;
    }

    let timer: any;
    const onActivity = () => {
      setVisible(true);
      clearTimeout(timer);
      timer = setTimeout(() => {
        if (!hasOpenModal) {
          setVisible(false);
        }
      }, autoHideMs);
    };

    window.addEventListener('mousemove', onActivity, { passive: true });
    window.addEventListener('touchstart', onActivity, { passive: true });
    window.addEventListener('keydown', onActivity, { passive: true });

    onActivity();

    return () => {
      clearTimeout(timer);
      window.removeEventListener('mousemove', onActivity);
      window.removeEventListener('touchstart', onActivity);
      window.removeEventListener('keydown', onActivity);
    };
  }, [autoHideMs, hasOpenModal]);

  // Fullscreen change listener
  useEffect(() => {
    const onFsChange = () => {
      setIsFullscreen(!!document.fullscreenElement);
    };
    document.addEventListener('fullscreenchange', onFsChange);
    return () => document.removeEventListener('fullscreenchange', onFsChange);
  }, []);

  // Netplay status listener
  useEffect(() => {
    const checkNetplay = () => {
      const np = (window as any).JellyEmuNetplay;
      setIsNetplayActive(!!(np && np.inRoom));
    };
    const interval = setInterval(checkNetplay, 1000);
    return () => clearInterval(interval);
  }, []);

  const setVolume = (val: number) => {
    setVolumeState(val);
    const floatVol = val / 100;
    PlayerBridge.setVolume(isMuted ? 0 : floatVol);
    const cfg = PlayerBridge.getConfig();
    if (cfg?.userId) {
      PlayerBridge.syncVolumeToServer(cfg.userId, floatVol, isMuted);
    }
  };

  const toggleMute = useCallback(() => {
    setIsMuted(prev => {
      const next = !prev;
      const floatVol = volume / 100;
      PlayerBridge.setVolume(next ? 0 : floatVol);
      const cfg = PlayerBridge.getConfig();
      if (cfg?.userId) {
        PlayerBridge.syncVolumeToServer(cfg.userId, floatVol, next);
      }
      PlayerBridge.notify({
        message: next ? 'Audio Muted' : 'Audio Unmuted',
        subtext: next ? 'Volume off' : `${Math.round(floatVol * 100)}% volume`,
        icon: next ? 'mute' : 'unmute',
        variant: next ? 'default' : 'info'
      });
      return next;
    });
  }, [volume]);

  const togglePause = useCallback(() => {
    setIsPaused(prev => {
      const next = !prev;
      if (next) {
        PlayerBridge.pause();
        PlayerBridge.notify({
          message: 'Emulation Paused',
          subtext: 'Press Space to resume',
          icon: 'pause',
          variant: 'warning'
        });
      } else {
        PlayerBridge.resume();
        PlayerBridge.notify({
          message: 'Emulation Resumed',
          subtext: 'Gameplay active',
          icon: 'play',
          variant: 'success'
        });
      }
      return next;
    });
  }, []);

  const toggleFastForward = useCallback(() => {
    setIsFastForward(prev => {
      const next = !prev;
      if (next) {
        setIsSlowMotion(false);
        PlayerBridge.notify({
          message: 'Fast Forward: ON',
          subtext: 'Speed boosted',
          icon: 'fastforward',
          variant: 'warning'
        });
      } else {
        PlayerBridge.notify({
          message: 'Fast Forward: OFF',
          subtext: 'Normal speed restored',
          icon: 'fastforward',
          variant: 'default'
        });
      }
      PlayerBridge.toggleFastForward(next);
      return next;
    });
  }, []);

  const toggleSlowMotion = useCallback(() => {
    setIsSlowMotion(prev => {
      const next = !prev;
      if (next) {
        setIsFastForward(false);
        PlayerBridge.notify({
          message: 'Slow Motion: ON',
          subtext: 'Speed reduced',
          icon: 'slowmotion',
          variant: 'accent'
        });
      } else {
        PlayerBridge.notify({
          message: 'Slow Motion: OFF',
          subtext: 'Normal speed restored',
          icon: 'slowmotion',
          variant: 'default'
        });
      }
      PlayerBridge.toggleSlowMotion(next);
      return next;
    });
  }, []);

  useEffect(() => {
    (window as any).togglePause = togglePause;
    (window as any).toggleMute = toggleMute;
    (window as any).toggleFastForward = toggleFastForward;
    (window as any).toggleSlowMotion = toggleSlowMotion;
    return () => {
      delete (window as any).togglePause;
      delete (window as any).toggleMute;
      delete (window as any).toggleFastForward;
      delete (window as any).toggleSlowMotion;
    };
  }, [togglePause, toggleMute, toggleFastForward, toggleSlowMotion]);

  return {
    visible: visible && !isMinimized,
    isMinimized,
    setIsMinimized,
    showHUD,
    volume,
    setVolume,
    isMuted,
    toggleMute,
    fpsMode,
    fps,
    setFps,
    speedPercent,
    setSpeedPercent,
    isPaused,
    togglePause,
    isFastForward,
    toggleFastForward,
    isSlowMotion,
    toggleSlowMotion,
    isFullscreen,
    isNetplayActive,
    isVrSupported,
    isArSupported,
    hasOpenModal,
    setHasOpenModal
  };
}
