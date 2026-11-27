import { RetroKit, StandardAxis, StandardButton } from '@grimmdev/retrokit';
import {
  PlayerInitialData,
  CheatItem,
  InputButtonDefinition,
  PlatformControlScheme,
  InputBinding,
  GamepadDetails
} from '../../types/player';
import { PlayerSessionTracker } from './playerSession';

export class PlayerBridge {
  private static emu: RetroKit | null = null;
  private static config: PlayerInitialData | null = null;
  private static sramSyncTimer: any = null;
  private static _activeSlot = 1;
  private static _boundGameCodes = new Set<string>();
  private static _activeHotkeys: Record<number, { kb1: number; kb2: number }> = {
    24: { kb1: 49, kb2: 0 },   // QUICK SAVE
    25: { kb1: 50, kb2: 0 },   // QUICK LOAD
    26: { kb1: 51, kb2: 0 },   // CHANGE SLOT
    27: { kb1: 107, kb2: 9 },  // FAST FORWARD
    28: { kb1: 8, kb2: 0 },    // REWIND
    29: { kb1: 109, kb2: 0 },  // SLOW MOTION
    30: { kb1: 27, kb2: 0 },   // EXIT GAME
    31: { kb1: 32, kb2: 80 },  // PAUSE / RESUME
    32: { kb1: 115, kb2: 0 },  // RESTART GAME
    33: { kb1: 77, kb2: 0 },   // MUTE AUDIO
    34: { kb1: 123, kb2: 0 },  // SCREENSHOT
    35: { kb1: 70, kb2: 122 }  // FULLSCREEN
  };

  public static init(config: PlayerInitialData) {
    this.config = config;
    if (config.activeSlot && config.activeSlot > 0) {
      this._activeSlot = config.activeSlot;
    }
    (window as any).jeNotify = this.notify.bind(this);
    PlayerSessionTracker.init(config);

    if (typeof window !== 'undefined') {
      window.addEventListener('beforeunload', () => {
        this.flushSramImmediate();
      });
    }
  }

  public static getEngine(): RetroKit | null {
    return this.emu;
  }

  public static async boot(canvas: HTMLCanvasElement, config: PlayerInitialData): Promise<RetroKit> {
    this.config = config;
    this.init(config);

    // Determine custom data directory in JellyEmu for WASM cores and engine assets
    const dataBase = (config.dataBaseUrl || this.getUrl('/jellyemu/data')).replace(/\/$/, '');
    const playBase = this.getUrl('/jellyemu/play/runtime').replace(/\/$/, '');
    const assetPaths = {
      libretro: `${dataBase}/cores`,
      play: playBase,
      pico8: `${dataBase}/pico8`,
      love: `${dataBase}/love`,
      ruffle: `${dataBase}/ruffle`
    };
    const coreLower = (config.core || '').toLowerCase();
    let assetSubdir = 'cores';
    if (coreLower === 'play') assetSubdir = 'play';
    else if (coreLower === 'pico8') assetSubdir = 'pico8';
    else if (coreLower === 'love') assetSubdir = 'love';
    else if (coreLower === 'ruffle') assetSubdir = 'ruffle';
    const assetBaseUrl = coreLower === 'play' ? playBase : `${dataBase}/${assetSubdir}`;

    // Ensure any WebGL/WebGL2 context preserves its drawing buffer for screenshots and uses low-latency desynchronized presentation
    if (typeof HTMLCanvasElement !== 'undefined' && !(HTMLCanvasElement.prototype as any).__jePreserveDrawingBufferPatched) {
      const origProtoGetContext = HTMLCanvasElement.prototype.getContext;
      (HTMLCanvasElement.prototype as any).getContext = function (type: string, attrs?: any) {
        if (type === 'webgl' || type === 'webgl2' || type === 'experimental-webgl') {
          return origProtoGetContext.call(this, type, {
            desynchronized: true,
            ...(attrs || {}),
            preserveDrawingBuffer: true
          });
        }
        if (type === '2d') {
          return origProtoGetContext.call(this, type, {
            desynchronized: true,
            ...(attrs || {})
          });
        }
        return origProtoGetContext.call(this, type, attrs);
      };
      (HTMLCanvasElement.prototype as any).__jePreserveDrawingBufferPatched = true;
    }

    // Fetch RetroAchievements credentials if user is logged in
    let retroAchievements: { username?: string; token?: string; hardcore?: boolean } | undefined;
    if (config.userId) {
      try {
        const raResp = await fetch(this.getUrl(`/jellyemu/prefs/${config.userId}?scope=ra`), {
          headers: this.getAuthHeaders()
        });
        if (raResp.ok) {
          const raData = await raResp.json();
          const prefs = raData?.preferences || raData;
          const username = prefs?.raUsername || prefs?.username;
          const token = prefs?.raToken || prefs?.token;
          if (username && token) {
            retroAchievements = {
              username,
              token,
              hardcore: prefs?.raHardcore === true || prefs?.raHardcore === 'true' || prefs?.hardcore === true
            };
          }
        }
      } catch (_) {}
    }

    // Instantiate RetroKit universal orchestrator with custom data directory and per-engine paths
    this.emu = new RetroKit({
      canvas,
      assetBaseUrl,
      assetPaths,
      fetchHeaders: this.getAuthHeaders(),
      retroAchievements,
      debug: false
    });
    this.emu.setCoresDirectory(assetBaseUrl);
    (window as any).emu = this.emu;

    // Core Lifecycle Event Listeners
    const urlSlotParam = Number(new URLSearchParams(window.location.search).get('slot') || '0');
    const launchSlot = (config.launchSlot && config.launchSlot > 0)
      ? config.launchSlot
      : (urlSlotParam > 0 ? urlSlotParam : 0);

    this.emu.on('ready', ({ core }) => {
      this.notify({
        message: `Core Loaded: ${core.toUpperCase()}`,
        subtext: config.gameName || 'Ready to play',
        icon: 'gamepad',
        variant: 'success'
      });
      const loader = document.getElementById('je-loader');
      if (loader) loader.classList.add('je-dismiss');

      if (launchSlot > 0 && config.itemId && config.userId) {
        setTimeout(() => {
          this.loadState(config.itemId, config.userId!, launchSlot)
            .then((ok) => {
              if (ok) {
                this.notify({
                  message: `Loaded Save State Slot ${launchSlot}`,
                  icon: 'save',
                  variant: 'success'
                });
              } else {
                this.notify({
                  message: `Could not load Save State Slot ${launchSlot}`,
                  icon: 'alert',
                  variant: 'warning'
                });
              }
            })
            .catch(() => {});
        }, 1500);
      }
    });

    this.emu.on('fps', ({ fps }) => {
      window.dispatchEvent(new CustomEvent('jellyemu:fps-update', { detail: { fps } }));
    });

    this.emu.on('sramModified', () => {
      this.queueSramSync();
    });

    this.emu.on('achievementUnlocked', ({ achievement, hardcore }) => {
      this.playAchievementSound();
      this.notify({
        message: `Achievement Unlocked: ${achievement.title}`,
        subtext: `${achievement.points} Points${hardcore ? ' [Hardcore]' : ''} - ${achievement.description}`,
        icon: 'trophy',
        variant: 'warning',
        duration: 6000
      });
    });

    this.emu.on('error', ({ error }) => {
      console.error('[PlayerBridge] RetroKit error:', error);
      this.notify({
        message: 'Emulation Error',
        subtext: error?.message || 'Unexpected emulation error',
        icon: 'alert',
        variant: 'danger',
        duration: 8000
      });
    });

    // Derive file extension and clean filename
    const urlClean = (config.romUrl || '').split('?')[0];
    const ext = urlClean.includes('.') ? urlClean.split('.').pop()! : 'bin';
    const cleanGameName = (config.gameName || config.itemId || 'game').replace(/[^a-zA-Z0-9_-]/g, '_');
    let primaryRomUrl = config.romUrl;
    let resolvedFileName = `${cleanGameName}.${ext}`;
    let additionalFiles: Array<{ name: string; data: Uint8Array }> | undefined;

    // Fetch multi-file / folder manifest if applicable
    if (config.hasAdditionalFiles && config.itemId) {
      try {
        const userQuery = config.userId ? `?userId=${encodeURIComponent(config.userId)}` : '';
        const filesResp = await fetch(this.getUrl(`/jellyemu/rom/${config.itemId}/files${userQuery}`), {
          headers: this.getAuthHeaders()
        });
        if (filesResp.ok) {
          const manifest = await filesResp.json();
          const fileList: Array<{ name: string; url: string }> = Array.isArray(manifest?.files) ? manifest.files : [];
          if (manifest?.primaryFile && manifest?.primaryUrl) {
            primaryRomUrl = this.getUrl(manifest.primaryUrl);
            resolvedFileName = manifest.primaryFile;
          }
          const companions = fileList.filter((f) => f.name && f.name !== resolvedFileName && f.url);
          if (companions.length > 0) {
            const loaded = await Promise.all(
              companions.map(async (f) => {
                const r = await fetch(this.getUrl(f.url), { headers: this.getAuthHeaders() });
                if (!r.ok) return null;
                return { name: f.name, data: new Uint8Array(await r.arrayBuffer()) };
              })
            );
            additionalFiles = loaded.filter((x): x is { name: string; data: Uint8Array } => x !== null);
          }
        }
      } catch (err) {
        console.warn('[PlayerBridge] Failed to fetch multi-file ROM manifest:', err);
      }
    }

    // Slot 1 in UI maps to index 0 in RetroArch/RetroKit
    const slotIndex = config.activeSlot && config.activeSlot > 0 ? config.activeSlot - 1 : 0;

    // Fetch BIOS file if configured for this system
    let biosMap: Record<string, Uint8Array> | undefined;
    if (config.biosUrl && config.biosUrl.trim().length > 0) {
      try {
        const biosResp = await fetch(this.getUrl(config.biosUrl), { headers: this.getAuthHeaders() });
        if (biosResp.ok) {
          const biosBuf = await biosResp.arrayBuffer();
          const biosFileName = config.biosUrl.split('?')[0].split('/').pop() || 'bios.bin';
          biosMap = { [biosFileName]: new Uint8Array(biosBuf) };
        }
      } catch (err) {
        console.warn('[PlayerBridge] Failed to fetch BIOS:', err);
      }
    }

    // Fetch cloud battery SRAM if available for this user + item
    let initialSram: Uint8Array | Record<string, Uint8Array> | undefined;
    if (config.itemId && config.userId) {
      try {
        const sramResp = await fetch(this.getUrl(`/jellyemu/sram/${config.itemId}/${config.userId}`), {
          headers: this.getAuthHeaders(),
          cache: 'no-store'
        });
        if (sramResp.ok) {
          const contentType = sramResp.headers.get('content-type') || '';
          if (contentType.includes('application/json')) {
            const jsonObj = await sramResp.json();
            if (jsonObj && typeof jsonObj === 'object') {
              const map: Record<string, Uint8Array> = {};
              for (const [k, v] of Object.entries(jsonObj)) {
                if (Array.isArray(v)) map[k] = new Uint8Array(v);
              }
              initialSram = map;
            }
          } else {
            const buf = await sramResp.arrayBuffer();
            if (buf.byteLength > 0) {
              initialSram = new Uint8Array(buf);
            }
          }
        }
      } catch (err) {
        console.warn('[PlayerBridge] Failed to fetch initial SRAM:', err);
      }
    }

    // Load Game via RetroKit
    await this.emu.load(primaryRomUrl, {
      core: config.core,
      system: config.platformTag,
      platformTag: config.platformTag,
      fileName: resolvedFileName,
      gameId: config.itemId,
      coresDirectory: assetBaseUrl,
      bios: biosMap,
      additionalFiles,
      initialSram,
      initialStateSlot: slotIndex
    });

    // Apply Live Preferences
    if (config.activeShader) {
      this.applyShader(config.activeShader);
    }
    if (config.scale) {
      this.applyScaling(config.scale);
    }
    if (config.videoRotation !== undefined) {
      this.applyRotation(Number(config.videoRotation));
    }
    if (config.volume !== undefined) {
      this.setVolume(Number(config.volume));
    } else {
      this.setVolume(1.0);
    }
    if (config.mute !== undefined) {
      this.toggleMute(config.mute === '1' || config.mute === 'true' || config.mute === 1 as any);
    }
    if ((config as any).audioEq && typeof (config as any).audioEq === 'object') {
      const eq = (config as any).audioEq as Record<string, number>;
      for (const band of ['subBass', 'bass', 'mid', 'presence', 'treble'] as const) {
        if (typeof eq[band] === 'number') {
          this.setAudioEqBand(band, eq[band]);
        }
      }
    }
    if (config.ffrate) {
      this.applyFastForwardRate(String(config.ffrate));
    }
    if (config.smrate) {
      this.applySlowMotionRate(String(config.smrate));
    }

    // Load per-user saved core options from server endpoint
    if (config.userId) {
      const coreKey = config.platformTag || this.getCoreName();
      fetch(this.getUrl(`/jellyemu/prefs/${config.userId}?scope=system&targetId=${encodeURIComponent(coreKey)}`), {
        headers: this.getAuthHeaders()
      })
        .then((r) => (r.ok ? r.json() : null))
        .then((data) => {
          const rawJson = data?.preferences?.coreOptionsJson;
          if (rawJson && typeof rawJson === 'string') {
            const parsed = JSON.parse(rawJson);
            for (const [k, v] of Object.entries(parsed)) {
              if (typeof v === 'string') {
                this.emu?.options.setOption(k, v);
              }
            }
          }
        })
        .catch(() => {});
    }

    // Load and apply per-user (or default) input bindings into RetroKit
    const targetPlatform = config.platformTag || config.core || 'default';
    Promise.all([
      this.fetchInputScheme(targetPlatform),
      config.userId
        ? this.fetchEffectiveBindings(config.userId, config.itemId || '', targetPlatform)
        : Promise.resolve(null)
    ])
      .then(([schemeRes, savedRes]) => {
        const merged: Record<number, Record<number, InputBinding>> = {};
        const defaults = schemeRes?.scheme?.playerDefaultBindings;
        if (defaults) {
          for (const [pKey, binds] of Object.entries(defaults)) {
            merged[Number(pKey)] = { ...(binds as Record<number, InputBinding>) };
          }
        }
        if (savedRes?.playerBindings) {
          for (const [pKey, binds] of Object.entries(savedRes.playerBindings)) {
            const pIdx = Number(pKey);
            merged[pIdx] = { ...(merged[pIdx] || {}), ...binds };
          }
        }
        if (Object.keys(merged).length > 0) {
          (window as any)._jePlayerBindings = merged;
          (window as any)._jeBindings = merged[0] || {};
          this.applyInputBindings(merged);
        }
      })
      .catch(() => {});

    return this.emu;
  }

  public static notify(
    message: string | { message: string; subtext?: string; icon?: string; variant?: string; duration?: number },
    options?: { subtext?: string; icon?: string; variant?: string; duration?: number }
  ): void {
    const detail = typeof message === 'string'
      ? { message, ...options }
      : message;
    try {
      window.dispatchEvent(new CustomEvent('jellyemu:notify', { detail }));
    } catch (_) {}
  }

  public static getConfig(): PlayerInitialData | null {
    return this.config || ((window as any).JellyEmuConfig as PlayerInitialData) || null;
  }

  public static getEmulator(): any {
    return (window as any).EJS_emulator || (window as any).emulator || null;
  }

  public static getGameManager(): any {
    const e = this.getEmulator();
    return (e && e.gameManager) ? e.gameManager : ((window as any).gameManager || (window as any).EJS_gameManager || (e && e.Module ? e : null));
  }

  public static getPlayModule(): any {
    return (window as any).PlayModule || null;
  }

  public static getBaseUrl(): string {
    const cfg = this.getConfig();
    return (window as any).JellyEmu?.baseUrl || (window as any).JellyEmuConfig?.baseUrl || (cfg as any)?.baseUrl || '';
  }

  public static getUrl(path: string): string {
    if (path.startsWith('http://') || path.startsWith('https://')) return path;
    const base = this.getBaseUrl();
    if (!path.startsWith('/')) path = '/' + path;
    return base ? `${base}${path}` : path;
  }

  public static getAuthToken(): string {
    const cfg = this.getConfig();
    return (window as any)._jellyToken || cfg?.userId || '';
  }

  public static getAuthHeaders(): Record<string, string> {
    if (typeof (window as any).JellyEmu?.getAuthHeaders === 'function') {
      try {
        const h = (window as any).JellyEmu.getAuthHeaders();
        if (h && Object.keys(h).length > 0) return h;
      } catch (_) {}
    }
    const token = this.getAuthToken();
    if (!token) return {};
    return {
      'Authorization': `MediaBrowser Token="${token}"`,
      'X-MediaBrowser-Token': token,
      'X-Emby-Token': token,
      'X-Emby-Authorization': `MediaBrowser Client="JellyEmu", Device="Browser", DeviceId="jellyemu", Version="1.0", Token="${token}"`
    };
  }

  // Lifecycle & Playback Controls
  public static pause(): void {
    if (this.emu) {
      this.emu.pause();
    } else {
      const e = this.getEmulator();
      if (e && typeof e.pause === 'function') {
        try { e.pause(); } catch (err) { console.warn('[PlayerBridge] pause failed:', err); }
      } else if ((window as any).Module && typeof (window as any).Module.pico8TogglePaused === 'function') {
        try { (window as any).Module.pico8TogglePaused(); } catch (_) {}
      }
    }
    if (typeof (window as any)._jePauseSessionTracking === 'function') {
      (window as any)._jePauseSessionTracking();
    }
  }

  public static resume(): void {
    if (this.emu) {
      this.emu.play();
    } else {
      const e = this.getEmulator();
      if (e && typeof e.play === 'function') {
        try { e.play(); } catch (err) { console.warn('[PlayerBridge] resume failed:', err); }
      } else if ((window as any).Module && typeof (window as any).Module.pico8TogglePaused === 'function') {
        try { (window as any).Module.pico8TogglePaused(); } catch (_) {}
      }
    }
    if (typeof (window as any)._jeResumeSessionTracking === 'function') {
      (window as any)._jeResumeSessionTracking();
    }
  }

  public static restart(): void {
    this.notify({ message: 'Restarting Game...', subtext: 'Resetting emulator core', icon: 'restart', variant: 'warning' });
    if (this.emu) {
      this.emu.restart();
      return;
    }
    const gm = this.getGameManager();
    if (gm && typeof gm.restart === 'function') {
      try { gm.restart(); } catch (err) { console.warn('[PlayerBridge] restart failed:', err); }
    } else {
      window.location.reload();
    }
  }

  private static _fastForwardRate = 2.5;
  private static _slowMotionRate = 0.5;

  public static toggleFastForward(active: boolean): void {
    if (this.emu) {
      this.emu.timing.setFastForward(active, this._fastForwardRate);
    } else {
      const gm = this.getGameManager();
      if (gm && typeof gm.toggleFastForward === 'function') {
        try { gm.toggleFastForward(active ? 1 : 0); } catch (err) { console.warn('[PlayerBridge] toggleFastForward failed:', err); }
      }
    }
    try {
      window.dispatchEvent(new CustomEvent('jellyemu:speed-changed', {
        detail: { ff: active, slow: false }
      }));
    } catch (_) {}
  }

  public static toggleSlowMotion(active: boolean): void {
    if (this.emu) {
      this.emu.timing.setSlowMotion(active, this._slowMotionRate);
    } else {
      const gm = this.getGameManager();
      if (gm && typeof gm.toggleSlowMotion === 'function') {
        try { gm.toggleSlowMotion(active ? 1 : 0); } catch (err) { console.warn('[PlayerBridge] toggleSlowMotion failed:', err); }
      }
    }
    try {
      window.dispatchEvent(new CustomEvent('jellyemu:speed-changed', {
        detail: { ff: false, slow: active }
      }));
    } catch (_) {}
  }

  public static exit(): void {
    this.notify({ message: 'Exiting Game...', subtext: 'Closing session', icon: 'exit', variant: 'default' });
    this.flushSramImmediate();
    PlayerSessionTracker.stop();

    if (this.emu) {
      this.emu.destroy();
    }
    const e = this.getEmulator();
    if (e && typeof e.exit === 'function') {
      try { e.exit(); return; } catch (_) {}
    }
    if (window.opener) {
      window.close();
    } else if (window.history.length > 1) {
      window.history.back();
    } else {
      window.parent.postMessage('close-jellyemu', '*');
    }
  }

  // VR / AR
  public static async isVrSupported(): Promise<boolean> {
    if ((window as any).JellyEmuXR && typeof (window as any).JellyEmuXR.isVrSupported === 'function') {
      return (window as any).JellyEmuXR.isVrSupported();
    }
    if (typeof navigator !== 'undefined' && 'xr' in navigator && (navigator as any).xr?.isSessionSupported) {
      try {
        return await (navigator as any).xr.isSessionSupported('immersive-vr');
      } catch {
        return false;
      }
    }
    return false;
  }

  public static async isArSupported(): Promise<boolean> {
    if ((window as any).JellyEmuXR && typeof (window as any).JellyEmuXR.isArSupported === 'function') {
      return (window as any).JellyEmuXR.isArSupported();
    }
    if (typeof navigator !== 'undefined' && 'xr' in navigator && (navigator as any).xr?.isSessionSupported) {
      try {
        return await (navigator as any).xr.isSessionSupported('immersive-ar');
      } catch {
        return false;
      }
    }
    return false;
  }

  public static enterVR(): void {
    if ((window as any).JellyEmuXR && typeof (window as any).JellyEmuXR.enterVR === 'function') {
      (window as any).JellyEmuXR.enterVR();
      return;
    }
    const btn = document.getElementById('je-vr-btn');
    if (btn) btn.click();
  }

  public static enterAR(): void {
    if ((window as any).JellyEmuXR && typeof (window as any).JellyEmuXR.enterAR === 'function') {
      (window as any).JellyEmuXR.enterAR();
      return;
    }
    const btn = document.getElementById('je-ar-btn');
    if (btn) btn.click();
  }

  public static applyFastForwardRate(rate: string): void {
    const parsed = parseFloat(rate);
    if (!Number.isNaN(parsed) && parsed > 1) {
      this._fastForwardRate = parsed;
      if (this.emu?.timing.isFastForward) {
        this.emu.timing.setFastForward(true, parsed);
      }
    }
    const e = this.getEmulator();
    if (e && typeof e.changeSettingOption === 'function') {
      try { e.changeSettingOption('ff-ratio', rate); } catch (_) {}
    }
  }

  public static applySlowMotionRate(rate: string): void {
    const parsed = parseFloat(rate);
    if (!Number.isNaN(parsed) && parsed > 0) {
      // EJS sm-ratio is often "3.0" meaning 1/3 speed, or "0.5" meaning half speed
      const speedRatio = parsed > 1 ? 1 / parsed : parsed;
      this._slowMotionRate = speedRatio;
      if (this.emu?.timing.isSlowMotion) {
        this.emu.timing.setSlowMotion(true, speedRatio);
      }
    }
    const e = this.getEmulator();
    if (e && typeof e.changeSettingOption === 'function') {
      try { e.changeSettingOption('sm-ratio', rate); } catch (_) {}
    }
  }

  public static async deleteCachedRom(_itemId?: string): Promise<boolean> {
    try {
      const e = this.getEmulator();
      if (e && e.gameManager && typeof e.gameManager.deleteCache === 'function') {
        e.gameManager.deleteCache();
      }
      if ('caches' in window) {
        const keys = await caches.keys();
        for (const k of keys) {
          if (k.includes('jellyemu') || k.includes('emulatorjs')) {
            await caches.delete(k);
          }
        }
      }
      return true;
    } catch {
      return false;
    }
  }

  public static async syncVirtualControls(userId: string, enabled: boolean, lefty: boolean): Promise<void> {
    const e = this.getEmulator();
    if (e) {
      if (typeof e.toggleVirtualGamepad === 'function') e.toggleVirtualGamepad(enabled);
      if (typeof e.toggleVirtualGamepadLeftHanded === 'function') e.toggleVirtualGamepadLeftHanded(lefty);
    }
    if ((window as any).JellyEmuConfig) {
      (window as any).JellyEmuConfig.virtualGamepad = String(enabled);
      (window as any).JellyEmuConfig.virtualGamepadLefty = String(lefty);
    }
    if (!userId) return;
    try {
      await fetch(this.getUrl(`/jellyemu/prefs/${userId}`), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...this.getAuthHeaders()
        },
        body: JSON.stringify({
          virtualGamepad: String(enabled),
          virtualGamepadLefty: String(lefty)
        })
      });
    } catch (err) {
      console.warn('[PlayerBridge] syncVirtualControls failed:', err);
    }
  }

  // Audio / Volume
  public static setVolume(vol: number): void {
    const normalized = Math.max(0, Math.min(1, vol > 1 ? vol / 100 : vol));
    if (this.emu) {
      this.emu.audio.setVolume(normalized);
    }
    const e = this.getEmulator();
    if (e) {
      try {
        if (typeof e.setVolume === 'function') e.setVolume(normalized);
        e.volume = normalized;
      } catch (_) {}
    }
    const pm = this.getPlayModule();
    if (pm && typeof pm.setVolume === 'function') {
      try { pm.setVolume(normalized); } catch (_) {}
    }
  }

  public static toggleMute(muted: boolean): void {
    if (this.emu) {
      this.emu.audio.setMute(muted);
    }
  }

  public static setAudioEqBand(band: 'subBass' | 'bass' | 'mid' | 'presence' | 'treble', gainDb: number): void {
    if (this.emu?.audio && typeof this.emu.audio.setEqBand === 'function') {
      this.emu.audio.setEqBand(band, gainDb);
    }
  }

  public static getAudioEqBands(): Record<'subBass' | 'bass' | 'mid' | 'presence' | 'treble', number> {
    if (this.emu?.audio && typeof this.emu.audio.getEqBands === 'function') {
      return this.emu.audio.getEqBands();
    }
    return { subBass: 0, bass: 0, mid: 0, presence: 0, treble: 0 };
  }

  public static resetAudioEq(): void {
    if (this.emu?.audio && typeof this.emu.audio.resetEq === 'function') {
      this.emu.audio.resetEq();
    }
  }

  public static syncAudioEqToServer(userId: string, bands: Record<string, number>): void {
    if (!userId) return;
    const cfg = this.getConfig() as any;
    if (cfg) cfg.audioEq = { ...bands };
    fetch(this.getUrl(`/jellyemu/prefs/${userId}`), {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...this.getAuthHeaders()
      },
      body: JSON.stringify({
        scope: 'global',
        targetId: '',
        preferences: {
          audioEq: JSON.stringify(bands)
        }
      })
    }).catch(() => {});
  }

  public static syncVolumeToServer(userId: string, volume: number, isMuted: boolean): void {
    if (!userId) return;
    fetch(this.getUrl(`/jellyemu/prefs/${userId}`), {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...this.getAuthHeaders()
      },
      body: JSON.stringify({
        scope: 'global',
        targetId: '',
        preferences: {
          volume: volume.toString(),
          mute: isMuted ? '1' : '0'
        }
      })
    }).catch(() => {});
  }

  // Saves & States
  public static async checkSlotSave(itemId: string, userId: string, slot: number): Promise<boolean> {
    try {
      const resp = await fetch(this.getUrl(`/jellyemu/save/${itemId}/${userId}?slot=${slot}`), {
        method: 'HEAD',
        headers: this.getAuthHeaders()
      });
      return resp.ok;
    } catch {
      return false;
    }
  }

  public static async getSlotScreenshot(itemId: string, userId: string, slot: number): Promise<string | null> {
    try {
      const resp = await fetch(this.getUrl(`/jellyemu/save-screenshot/${itemId}/${userId}/${slot}`), {
        headers: this.getAuthHeaders()
      });
      if (resp.ok) {
        const data = await resp.json();
        return data?.dataUrl || null;
      }
    } catch {}
    return null;
  }

  public static async saveState(itemId: string, userId: string, slot: number): Promise<boolean> {
    const slotIndex = Math.max(0, slot > 0 ? slot - 1 : 0);

    if (this.emu) {
      try {
        const record = await this.emu.state.saveState(slotIndex);
        if (!record || !record.data) return false;

        const resp = await fetch(this.getUrl(`/jellyemu/save/${itemId}/${userId}?slot=${slot}`), {
          method: 'POST',
          headers: {
            'Content-Type': 'application/octet-stream',
            ...this.getAuthHeaders()
          },
          body: record.data
        });
        if (!resp.ok) throw new Error('Save rejected');

        // Screenshot upload (prefer direct preserved-buffer canvas capture to avoid core cmd_take_screenshot glitches)
        const canvasShot = this.captureCanvas('image/png');
        if (canvasShot) {
          fetch(this.getUrl(`/jellyemu/save-screenshot/${itemId}/${userId}/${slot}`), {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              ...this.getAuthHeaders()
            },
            body: JSON.stringify({ dataUrl: canvasShot })
          }).catch(() => {});
        } else if (record.metadata?.screenshot) {
          const shot = record.metadata.screenshot;
          if (typeof shot === 'string') {
            fetch(this.getUrl(`/jellyemu/save-screenshot/${itemId}/${userId}/${slot}`), {
              method: 'POST',
              headers: {
                'Content-Type': 'application/json',
                ...this.getAuthHeaders()
              },
              body: JSON.stringify({ dataUrl: shot })
            }).catch(() => {});
          } else if (shot instanceof Blob) {
            const reader = new FileReader();
            reader.onload = () => {
              const dataUrl = reader.result as string;
              if (dataUrl) {
                fetch(this.getUrl(`/jellyemu/save-screenshot/${itemId}/${userId}/${slot}`), {
                  method: 'POST',
                  headers: {
                    'Content-Type': 'application/json',
                    ...this.getAuthHeaders()
                  },
                  body: JSON.stringify({ dataUrl })
                }).catch(() => {});
              }
            };
            reader.readAsDataURL(shot);
          }
        }

        return true;
      } catch (err) {
        console.error('[PlayerBridge] Save state failed:', err);
        return false;
      }
    }

    // Legacy Fallback
    const gm = this.getGameManager();
    if (!gm) return false;

    try {
      const rawState = await Promise.resolve(gm.getState());
      const stateBlob = this.ensureBinary(rawState);
      if (!stateBlob) return false;

      const resp = await fetch(this.getUrl(`/jellyemu/save/${itemId}/${userId}?slot=${slot}`), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/octet-stream',
          ...this.getAuthHeaders()
        },
        body: stateBlob
      });

      if (!resp.ok) throw new Error('Save rejected');

      // Upload screenshot
      const dataUrl = this.captureCanvas();
      if (dataUrl) {
        fetch(this.getUrl(`/jellyemu/save-screenshot/${itemId}/${userId}/${slot}`), {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            ...this.getAuthHeaders()
          },
          body: JSON.stringify({ dataUrl })
        }).catch(() => {});
      }

      return true;
    } catch (err) {
      console.error('[PlayerBridge] Save state failed:', err);
      return false;
    }
  }

  public static async loadState(itemId: string, userId: string, slot: number): Promise<boolean> {
    try {
      const resp = await fetch(this.getUrl(`/jellyemu/save/${itemId}/${userId}?slot=${slot}`), {
        headers: this.getAuthHeaders(),
        cache: 'no-store'
      });
      if (!resp.ok) throw new Error('Failed to download state');
      const buf = await resp.arrayBuffer();

      if (this.emu) {
        return await this.emu.state.loadState(new Uint8Array(buf));
      }

      const gm = this.getGameManager();
      if (!gm) return false;
      gm.loadState(new Uint8Array(buf));
      return true;
    } catch (err) {
      console.error('[PlayerBridge] Load state failed:', err);
      return false;
    }
  }

  public static async exportState(gameName: string): Promise<void> {
    try {
      let blob: Blob | null = null;
      if (this.emu) {
        const record = await this.emu.state.saveState(0);
        if (record?.data) {
          blob = new Blob([record.data], { type: 'application/octet-stream' });
        }
      } else {
        const gm = this.getGameManager();
        if (gm) {
          const raw = await Promise.resolve(gm.getState());
          blob = this.ensureBinary(raw);
        }
      }

      if (!blob) return;
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${gameName || 'game'}.state`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (err) {
      console.error('[PlayerBridge] Export state failed:', err);
    }
  }

  public static async importState(file: File): Promise<boolean> {
    try {
      const buf = await file.arrayBuffer();
      if (this.emu) {
        return await this.emu.state.loadState(new Uint8Array(buf));
      }
      const gm = this.getGameManager();
      if (!gm) return false;
      gm.loadState(new Uint8Array(buf));
      return true;
    } catch (err) {
      console.error('[PlayerBridge] Import state failed:', err);
      return false;
    }
  }

  // Battery SRAM Synchronization
  public static queueSramSync(): void {
    if (this.sramSyncTimer) clearTimeout(this.sramSyncTimer);
    this.sramSyncTimer = setTimeout(() => {
      this.flushSramImmediate();
    }, 10000); // 10 second debounce
  }

  public static async flushSramImmediate(): Promise<void> {
    if (!this.emu) return;
    try {
      await this.emu.sram.flushSram();
      const sramData = await this.emu.sram.getSram();
      if (!sramData) return;

      const cfg = this.getConfig();
      if (!cfg?.itemId || !cfg?.userId) return;

      let body: BodyInit;
      let contentType = 'application/octet-stream';

      if (sramData instanceof Uint8Array) {
        body = sramData;
      } else {
        // Multi-file memory card (Play! PS2)
        body = JSON.stringify(sramData);
        contentType = 'application/json';
      }

      await fetch(this.getUrl(`/jellyemu/sram/${cfg.itemId}/${cfg.userId}`), {
        method: 'POST',
        headers: {
          'Content-Type': contentType,
          ...this.getAuthHeaders()
        },
        body
      });
      console.log('[PlayerBridge] Battery SRAM synchronized successfully.');
    } catch (err) {
      console.warn('[PlayerBridge] Battery SRAM flush failed:', err);
    }
  }

  public static async exportSram(gameName: string): Promise<boolean> {
    if (!this.emu) return false;
    try {
      await this.emu.sram.flushSram();
      const sramData = await this.emu.sram.getSram();
      if (!sramData) return false;

      let blob: Blob;
      let ext = 'sav';
      if (sramData instanceof Uint8Array) {
        if (sramData.byteLength === 0) return false;
        blob = new Blob([sramData], { type: 'application/octet-stream' });
      } else {
        blob = new Blob([JSON.stringify(sramData)], { type: 'application/json' });
        ext = 'json';
      }

      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${(gameName || 'save').replace(/[^a-zA-Z0-9_-]/g, '_')}.${ext}`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      return true;
    } catch (err) {
      console.error('[PlayerBridge] Export SRAM failed:', err);
      return false;
    }
  }

  public static async importSram(file: File): Promise<boolean> {
    if (!this.emu) return false;
    try {
      const buf = await file.arrayBuffer();
      const bytes = new Uint8Array(buf);
      if (bytes.byteLength === 0) return false;
      const ok = await this.emu.sram.setSram(bytes, { restart: true });
      if (ok) {
        await this.flushSramImmediate();
      }
      return ok;
    } catch (err) {
      console.error('[PlayerBridge] Import SRAM failed:', err);
      return false;
    }
  }

  // Screenshots
  public static captureCanvas(format = 'image/jpeg', quality = 0.9): string | null {
    try {
      const canvas = (document.getElementById('retrokit-canvas') ||
        document.querySelector('canvas:not([data-hud-canvas])')) as HTMLCanvasElement | null;
      if (canvas && canvas.width > 0 && canvas.height > 0) {
        const dpr = window.devicePixelRatio || 1;
        const displayW = Math.round((canvas.clientWidth || canvas.width) * dpr);
        const displayH = Math.round((canvas.clientHeight || canvas.height) * dpr);
        if (displayW > canvas.width || displayH > canvas.height) {
          const offscreen = document.createElement('canvas');
          offscreen.width = displayW;
          offscreen.height = displayH;
          const ctx = offscreen.getContext('2d');
          if (ctx) {
            ctx.imageSmoothingEnabled = false;
            ctx.drawImage(canvas, 0, 0, displayW, displayH);
            return offscreen.toDataURL(format, quality);
          }
        }
        return canvas.toDataURL(format, quality);
      }
    } catch (e) {
      console.warn('[PlayerBridge] Screenshot capture failed:', e);
    }
    return null;
  }

  public static async takeScreenshot(itemId: string, gameName: string): Promise<boolean> {
    let blob: Blob | null = null;
    let dataUrl: string | null = this.captureCanvas('image/png');

    if (dataUrl) {
      try {
        const res = await fetch(dataUrl);
        blob = await res.blob();
      } catch (_) {}
    }

    if (!blob && this.emu) {
      try {
        blob = await this.emu.video.takeScreenshot('image/png');
      } catch (err) {
        console.warn('[PlayerBridge] RetroKit takeScreenshot failed:', err);
      }
    }

    if (!blob && !dataUrl) {
      this.notify({ message: 'Screenshot Failed', subtext: 'Canvas not ready', icon: 'screenshot', variant: 'danger' });
      return false;
    }

    try {
      const cfg = this.getConfig();
      if (cfg?.screenshotsToLibrary) {
        if (!dataUrl && blob) {
          dataUrl = await new Promise<string | null>((resolve) => {
            const reader = new FileReader();
            reader.onload = () => resolve((reader.result as string) || null);
            reader.onerror = () => resolve(null);
            reader.readAsDataURL(blob!);
          });
        }
        if (!dataUrl) throw new Error('Failed to encode screenshot');
        const resp = await fetch(this.getUrl(`/jellyemu/screenshot/${itemId}`), {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            ...this.getAuthHeaders()
          },
          body: JSON.stringify({ dataUrl, gameName })
        });
        if (!resp.ok) throw new Error(`Upload failed (${resp.status})`);
      } else {
        const downloadUrl = blob ? URL.createObjectURL(blob) : dataUrl!;
        const a = document.createElement('a');
        a.href = downloadUrl;
        a.download = `${gameName || 'screenshot'}-${Date.now()}.png`;
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        if (blob) {
          setTimeout(() => URL.revokeObjectURL(downloadUrl), 5000);
        }
      }
      this.notify({ message: 'Screenshot Captured', subtext: cfg?.screenshotsToLibrary ? 'Saved to library' : 'Downloaded PNG', icon: 'screenshot', variant: 'success' });
      return true;
    } catch (err) {
      console.error('[PlayerBridge] Take screenshot failed:', err);
      this.notify({ message: 'Screenshot Failed', subtext: 'Capture failed', icon: 'screenshot', variant: 'danger' });
      return false;
    }
  }

  // Cheats
  public static async fetchCheats(itemId: string): Promise<CheatItem[]> {
    try {
      const resp = await fetch(this.getUrl(`/jellyemu/cheats/${itemId}`), {
        headers: this.getAuthHeaders()
      });
      if (resp.ok) {
        const data = await resp.json();
        return (data || []).map((entry: any) => ({
          desc: entry[0],
          code: entry[1],
          checked: false,
          fromDb: true
        }));
      }
    } catch {}
    return [];
  }

  public static toggleCheat(cheat: CheatItem, enabled: boolean): void {
    if (this.emu) {
      this.emu.cheats.toggleCheat(cheat.code, enabled);
      return;
    }
    const e = this.getEmulator();
    if (!e) return;
    e.cheats = e.cheats || [];
    let idx = e.cheats.findIndex((c: any) => c.code === cheat.code);
    if (enabled) {
      if (idx === -1) {
        e.cheats.push({ desc: cheat.desc, code: cheat.code, checked: true });
        idx = e.cheats.length - 1;
      } else {
        e.cheats[idx].checked = true;
      }
    } else {
      if (idx !== -1) e.cheats[idx].checked = false;
    }
    if (typeof e.cheatChanged === 'function') {
      try { e.cheatChanged(enabled, cheat.code, idx === -1 ? 0 : idx); } catch (_) {}
    }
  }

  public static addCustomCheat(desc: string, code: string): void {
    if (this.emu) {
      this.emu.cheats.addCheat({ name: desc, code, enabled: true });
      return;
    }
    const e = this.getEmulator();
    if (!e) return;
    e.cheats = e.cheats || [];
    e.cheats.push({ desc, code, checked: false });
  }

  public static removeCustomCheat(code: string): void {
    if (this.emu) {
      this.emu.cheats.removeCheat(code);
      return;
    }
    const e = this.getEmulator();
    if (!e || !e.cheats) return;
    const idx = e.cheats.findIndex((c: any) => c.code === code);
    if (idx !== -1) {
      if (typeof e.cheatChanged === 'function') {
        try { e.cheatChanged(false, code, idx); } catch (_) {}
      }
      e.cheats.splice(idx, 1);
    }
  }

  // Multi-Disc
  public static nextDisc(): void {
    if (this.emu) {
      this.emu.discs.nextDisc();
      return;
    }
    const gm = this.getGameManager();
    if (gm && typeof gm.nextDisc === 'function') {
      try { gm.nextDisc(); } catch (err) { console.warn('[PlayerBridge] nextDisc failed:', err); }
    }
  }

  public static selectDisc(index: number): void {
    if (this.emu) {
      this.emu.discs.changeDisc(index);
      return;
    }
    const gm = this.getGameManager();
    if (gm && typeof gm.loadDisc === 'function') {
      try { gm.loadDisc(index); } catch (err) { console.warn('[PlayerBridge] loadDisc failed:', err); }
    }
  }

  // Settings & Display
  public static applyShader(shader: string): void {
    if (this.emu) {
      this.emu.video.setShader(shader);
    }
    const gm = this.getGameManager();
    if (gm) {
      if (shader === 'disabled' || shader === 'none') {
        if (typeof gm.disableShader === 'function') try { gm.disableShader(); } catch (_) {}
      } else {
        if (typeof gm.enableShader === 'function') try { gm.enableShader(shader); } catch (_) {}
      }
    }
  }

  public static applyRotation(rot: number): void {
    const degrees = (rot >= 0 && rot <= 3 ? rot * 90 : rot) % 360;
    if (this.emu) {
      this.emu.video.setRotation(degrees as any);
    }
    const e = this.getEmulator();
    if (e && typeof e.changeSettingOption === 'function') {
      try { e.changeSettingOption('videoRotation', rot); } catch (_) {}
    }
  }

  public static applyScaling(scale: string): void {
    if (this.emu) {
      const normalized =
        scale === 'fit'
          ? 'auto'
          : scale === 'fill'
            ? 'stretch'
            : scale === '1' || scale === '2' || scale === '3' || scale === '4' || scale === 'integer'
              ? 'pixel-perfect'
              : scale;
      this.emu.video.setAspectRatio(normalized as any);
    }
    const cfg = this.getConfig();
    if (cfg) (cfg as any).scale = scale;
  }

  public static applyFpsOverlay(mode: string): void {
    const cfg = this.getConfig();
    if (cfg) (cfg as any).showFps = mode;
    localStorage.setItem('jellyemu:fps-overlay-mode', mode);
    window.dispatchEvent(new CustomEvent('jellyemu:fps-changed', { detail: { mode } }));
  }

  // Core Options
  public static getCoreName(): string {
    return (window as any).EJS_core || (window as any).JellyEmuConfig?.platformTag || 'default';
  }

  public static getCoreOptionsList(): { key: string; name: string; value: string; options: { value: string; label: string }[]; description?: string }[] {
    if (this.emu) {
      const opts = this.emu.options.getOptions();
      if (opts && opts.length > 0) {
        return opts.map(o => ({
          key: o.id,
          name: o.label || o.id,
          value: o.currentValue,
          description: o.description,
          options: (o.choices || []).map((c: any) => ({ value: c.value, label: c.label || c.value }))
        }));
      }
    }

    // Fallback to legacy string parsing
    const gm = this.getGameManager();
    const coreName = this.getCoreName();
    const saved = this.loadSavedCoreOptions(coreName);

    if (!gm || typeof gm.getCoreOptions !== 'function') return [];
    const optsRaw = gm.getCoreOptions();
    if (!optsRaw || typeof optsRaw !== 'string') return [];

    const result: { key: string; name: string; value: string; options: { value: string; label: string }[]; description?: string }[] = [];
    const lines = optsRaw.split('\n');

    lines.forEach((line: string) => {
      if (!line.trim()) return;
      const parts = line.split(/;\s*/);
      if (parts.length < 2) return;

      const keyVal = parts[0].split('|');
      const optKey = keyVal[0]?.trim();
      const currentVal = keyVal[1]?.trim();
      const optionList = parts[1].split('|').map((o: string) => o.trim()).filter(Boolean);

      if (!optKey) return;
      const effectiveVal = (saved && saved[optKey] !== undefined) ? saved[optKey] : currentVal;

      let displayKey = optKey.replace(/^[^_]+_/, '').replace(/_/g, ' ');
      displayKey = displayKey.charAt(0).toUpperCase() + displayKey.slice(1);

      result.push({
        key: optKey,
        name: displayKey,
        value: effectiveVal,
        options: optionList.map((opt: string) => ({
          value: opt,
          label: opt.charAt(0).toUpperCase() + opt.slice(1)
        }))
      });
    });

    return result;
  }

  public static setCoreOption(optKey: string, optVal: string, coreName?: string): void {
    if (this.emu) {
      this.emu.options.setOption(optKey, optVal);
    }
    const gm = this.getGameManager();
    if (gm && typeof gm.setVariable === 'function') {
      try { gm.setVariable(optKey, optVal); } catch (_) {}
    }
    this.saveCoreOption(optKey, optVal, coreName);
  }

  public static loadSavedCoreOptions(coreName?: string): Record<string, string> {
    const core = coreName || this.getCoreName();
    const key = 'jellyemu_coreopts_' + core;
    try {
      const raw = localStorage.getItem(key);
      if (raw) return JSON.parse(raw);
    } catch (_) {}
    return {};
  }

  public static saveCoreOption(optKey: string, optVal: string, coreName?: string): void {
    const core = coreName || this.getCoreName();
    const key = 'jellyemu_coreopts_' + core;
    try {
      const opts = this.loadSavedCoreOptions(core);
      opts[optKey] = optVal;
      localStorage.setItem(key, JSON.stringify(opts));

      const cfg = this.getConfig();
      if (cfg?.userId) {
        const target = cfg.platformTag || core;
        fetch(this.getUrl(`/jellyemu/prefs/${cfg.userId}`), {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            ...this.getAuthHeaders()
          },
          body: JSON.stringify({
            scope: 'system',
            targetId: target,
            preferences: {
              coreOptionsJson: JSON.stringify(opts)
            }
          })
        }).catch(() => {});
      }
    } catch (_) {}
  }

  public static clearSavedCoreOptions(coreName?: string): void {
    const core = coreName || this.getCoreName();
    try { localStorage.removeItem('jellyemu_coreopts_' + core); } catch (_) {}
  }

  public static async fetchSystemCores(platformTag: string): Promise<Array<{ id: string; name: string }>> {
    try {
      const resp = await fetch(this.getUrl('/jellyemu/systems'), {
        headers: this.getAuthHeaders()
      });
      if (resp.ok) {
        const data = await resp.json();
        const pTag = (platformTag || '').toLowerCase().trim();
        const found = (data.systems || []).find((s: any) => (s.name || '').toLowerCase() === pTag);
        if (found?.cores?.length > 0) {
          return found.cores.map((c: any) => ({
            id: c.id,
            name: c.name || c.id
          }));
        }
      }
    } catch (_) {}
    return [];
  }

  public static saveSystemPreferences(userId: string, platformTag: string, prefs: Record<string, any>): Promise<Response> {
    return fetch(this.getUrl(`/jellyemu/prefs/${userId}`), {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...this.getAuthHeaders()
      },
      body: JSON.stringify({
        scope: 'system',
        targetId: platformTag,
        preferences: prefs
      })
    });
  }

  public static saveGlobalPreferences(userId: string, prefs: Record<string, any>): Promise<Response> {
    return fetch(this.getUrl(`/jellyemu/prefs/${userId}`), {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...this.getAuthHeaders()
      },
      body: JSON.stringify({
        scope: 'global',
        targetId: '',
        preferences: prefs
      })
    });
  }

  // Input & Controls
  public static async fetchInputScheme(platformOrCore: string): Promise<{ hotkeys: InputButtonDefinition[]; scheme: PlatformControlScheme | null } | null> {
    try {
      const endpoint = platformOrCore ? `/jellyemu/input/schemes/${encodeURIComponent(platformOrCore)}` : '/jellyemu/input/schemes';
      const resp = await fetch(this.getUrl(endpoint), {
        headers: this.getAuthHeaders()
      });
      if (resp.ok) {
        const data = await resp.json();
        const rawHotkeys = data?.hotkeys || data?.Hotkeys || [];
        let matchedScheme: PlatformControlScheme | null = data?.scheme || data?.Scheme || null;
        const rawSchemes = data?.schemes || data?.Schemes;
        if (!matchedScheme && rawSchemes) {
          if (platformOrCore && rawSchemes[platformOrCore]) {
            matchedScheme = rawSchemes[platformOrCore];
          } else if (platformOrCore && rawSchemes[platformOrCore.toLowerCase()]) {
            matchedScheme = rawSchemes[platformOrCore.toLowerCase()];
          } else {
            const firstKey = Object.keys(rawSchemes)[0];
            if (firstKey) matchedScheme = rawSchemes[firstKey];
          }
        }
        return { hotkeys: rawHotkeys, scheme: matchedScheme };
      }
    } catch (_) {}
    return null;
  }

  public static async fetchEffectiveBindings(
    userId: string,
    itemId: string,
    platform: string
  ): Promise<{
    bindings: Record<number, InputBinding>;
    playerBindings: Record<number, Record<number, InputBinding>>;
  } | null> {
    if (!userId) return null;
    try {
      const cItemId = itemId ? encodeURIComponent(itemId) : '';
      const cPlatform = platform ? encodeURIComponent(platform) : '';
      const resp = await fetch(this.getUrl(`/jellyemu/prefs/${userId}/effective?itemId=${cItemId}&platform=${cPlatform}`), {
        headers: this.getAuthHeaders()
      });
      if (resp.ok) {
        const data = await resp.json();
        const raw = data?.controls || data?.jeBindings;
        let parsed: any = null;
        if (raw) {
          parsed = typeof raw === 'string' ? JSON.parse(raw) : raw;
        }

        if (parsed && typeof parsed === 'object') {
          const isMultiPlayer = parsed['0'] && typeof parsed['0'] === 'object' && parsed['0'].kb1 === undefined && parsed['0'].gp1 === undefined;
          let playerBindings: Record<number, Record<number, InputBinding>> = {};
          let p0Bindings: Record<number, InputBinding> = {};

          if (isMultiPlayer) {
            playerBindings = parsed;
            p0Bindings = parsed['0'] || {};
          } else {
            p0Bindings = parsed;
            playerBindings = { 0: parsed };
          }

          return { bindings: p0Bindings, playerBindings };
        }
      }
    } catch (_) {}
    return null;
  }

  public static async saveBindings(
    userId: string,
    platform: string,
    playerBindings: Record<number, Record<number, InputBinding>>
  ): Promise<boolean> {
    if (!userId) return false;
    try {
      const targetConsole = platform || '';
      const url = `/jellyemu/prefs/${userId}${targetConsole ? `?scope=system&targetId=${encodeURIComponent(targetConsole)}` : ''}`;
      const payload: any = {
        scope: 'system',
        targetId: targetConsole,
        preferences: {
          controls: JSON.stringify(playerBindings),
          jeBindings: JSON.stringify(playerBindings)
        },
        controls: JSON.stringify(playerBindings),
        jeBindings: JSON.stringify(playerBindings)
      };

      (window as any)._jePlayerBindings = JSON.parse(JSON.stringify(playerBindings));
      (window as any)._jeBindings = JSON.parse(JSON.stringify(playerBindings[0] || {}));

      const resp = await fetch(this.getUrl(url), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...this.getAuthHeaders()
        },
        body: JSON.stringify(payload)
      });
      return resp.ok;
    } catch (_) {
      return false;
    }
  }

  private static keyCodeToCodes(keyCode: number): string[] {
    if (!keyCode || keyCode <= 0) return [];
    if (keyCode >= 65 && keyCode <= 90) return [`Key${String.fromCharCode(keyCode)}`];
    if (keyCode >= 48 && keyCode <= 57) return [`Digit${String.fromCharCode(keyCode)}`];
    if (keyCode >= 96 && keyCode <= 105) return [`Numpad${keyCode - 96}`];
    if (keyCode >= 112 && keyCode <= 123) return [`F${keyCode - 111}`];
    switch (keyCode) {
      case 8: return ['Backspace'];
      case 9: return ['Tab'];
      case 13: return ['Enter', 'NumpadEnter'];
      case 16: return ['ShiftLeft', 'ShiftRight'];
      case 17: return ['ControlLeft', 'ControlRight'];
      case 18: return ['AltLeft', 'AltRight'];
      case 27: return ['Escape'];
      case 32: return ['Space'];
      case 37: return ['ArrowLeft'];
      case 38: return ['ArrowUp'];
      case 39: return ['ArrowRight'];
      case 40: return ['ArrowDown'];
      case 106: return ['NumpadMultiply'];
      case 107: return ['NumpadAdd'];
      case 109: return ['NumpadSubtract'];
      case 110: return ['NumpadDecimal'];
      case 111: return ['NumpadDivide'];
      case 186: return ['Semicolon'];
      case 187: return ['Equal'];
      case 188: return ['Comma'];
      case 189: return ['Minus'];
      case 190: return ['Period'];
      case 191: return ['Slash'];
      case 192: return ['Backquote'];
      case 219: return ['BracketLeft'];
      case 220: return ['Backslash'];
      case 221: return ['BracketRight'];
      case 222: return ['Quote'];
      default: return [];
    }
  }

  private static readonly GP_LABEL_TO_INDEX: Record<string, number> = {
    BUTTON_1: 0,
    BUTTON_2: 1,
    BUTTON_3: 2,
    BUTTON_4: 3,
    LEFT_TOP_SHOULDER: 4,
    RIGHT_TOP_SHOULDER: 5,
    LEFT_BOTTOM_SHOULDER: 6,
    RIGHT_BOTTOM_SHOULDER: 7,
    SELECT: 8,
    START: 9,
    LEFT_STICK: 10,
    RIGHT_STICK: 11,
    DPAD_UP: 12,
    DPAD_DOWN: 13,
    DPAD_LEFT: 14,
    DPAD_RIGHT: 15,
    HOME: 16
  };

  private static readonly JE_ID_TO_BUTTON: Record<number, StandardButton> = {
    0: 'ACTION_DOWN',
    1: 'ACTION_LEFT',
    2: 'SELECT',
    3: 'START',
    4: 'DPAD_UP',
    5: 'DPAD_DOWN',
    6: 'DPAD_LEFT',
    7: 'DPAD_RIGHT',
    8: 'ACTION_RIGHT',
    9: 'ACTION_UP',
    10: 'L1',
    11: 'R1',
    12: 'L2',
    13: 'R2',
    14: 'L3',
    15: 'R3'
  };

  private static readonly JE_ID_TO_AXIS: Record<number, { axis: StandardAxis; direction: 1 | -1 }> = {
    16: { axis: 'L_STICK_X', direction: 1 },
    17: { axis: 'L_STICK_X', direction: -1 },
    18: { axis: 'L_STICK_Y', direction: 1 },
    19: { axis: 'L_STICK_Y', direction: -1 },
    20: { axis: 'R_STICK_X', direction: 1 },
    21: { axis: 'R_STICK_X', direction: -1 },
    22: { axis: 'R_STICK_Y', direction: 1 },
    23: { axis: 'R_STICK_Y', direction: -1 }
  };

  private static resolveGamepadAxis(label?: string): { gpAxisIndex: number; gpDir: 1 | -1 } | null {
    if (!label) return null;
    const trimmed = label.trim().toUpperCase();
    const axisMap: Record<string, number> = {
      LEFT_STICK_X: 0,
      LEFT_STICK_Y: 1,
      RIGHT_STICK_X: 2,
      RIGHT_STICK_Y: 3
    };
    const m = /^([A-Z0-9_]+):([+-]?1)$/.exec(trimmed);
    if (!m) return null;
    const name = m[1];
    const dir: 1 | -1 = m[2] === '-1' ? -1 : 1;
    if (name in axisMap) {
      return { gpAxisIndex: axisMap[name], gpDir: dir };
    }
    const extra = /^EXTRA_STICK_(\d+)$/.exec(name);
    if (extra) {
      return { gpAxisIndex: Number(extra[1]), gpDir: dir };
    }
    return null;
  }

  private static resolveGamepadIndex(label?: string): number | null {
    if (!label) return null;
    const trimmed = label.trim().toUpperCase();
    if (trimmed in this.GP_LABEL_TO_INDEX) return this.GP_LABEL_TO_INDEX[trimmed];
    const m = /^GAMEPAD_(\d+)$/.exec(trimmed);
    return m ? Number(m[1]) : null;
  }

  public static getActiveSlot(): number {
    return this._activeSlot;
  }

  public static cycleActiveSlot(): number {
    this._activeSlot = (this._activeSlot % 5) + 1;
    return this._activeSlot;
  }

  public static startRewind(): void {
    if (this.emu?.timing?.isRewindSupported()) {
      this.emu.timing.startRewind();
    }
  }

  public static stopRewind(): void {
    if (this.emu?.timing?.isRewindSupported()) {
      this.emu.timing.stopRewind();
    }
  }

  public static matchHotkey(e: KeyboardEvent): number | null {
    if (e.ctrlKey || e.altKey || e.metaKey) return null;
    // Do not hijack a key that is actively bound to a Player 1 game button
    if (e.code && this._boundGameCodes.has(e.code)) {
      return null;
    }
    for (const [idStr, bind] of Object.entries(this._activeHotkeys)) {
      const id = Number(idStr);
      for (const kc of [bind.kb1, bind.kb2]) {
        if (!kc || kc <= 0) continue;
        if (e.keyCode === kc) return id;
        const codes = this.keyCodeToCodes(kc);
        if (e.code && codes.includes(e.code)) return id;
      }
    }
    return null;
  }

  public static applyInputBindings(playerBindings: Record<number, Record<number, InputBinding>>): void {
    // Update active keyboard hotkeys from Player 1's bindings (IDs 24-35); gamepad actions stay unmapped
    const p0Binds = playerBindings[0];
    this._boundGameCodes.clear();
    if (p0Binds) {
      for (const [idKey, bind] of Object.entries(p0Binds)) {
        const btnId = Number(idKey);
        if (!bind || Number.isNaN(btnId)) continue;
        if (btnId >= 24 && btnId <= 35) {
          this._activeHotkeys[btnId] = {
            kb1: Number(bind.kb1) || 0,
            kb2: Number(bind.kb2) || 0
          };
        } else if (btnId < 24) {
          for (const kc of [bind.kb1, bind.kb2]) {
            if (kc && kc > 0) {
              for (const code of this.keyCodeToCodes(kc)) {
                this._boundGameCodes.add(code);
              }
            }
          }
        }
      }
    }

    if (!this.emu?.input) return;
    const maxP = this.emu.input.getMaxPlayers();
    for (const [pKey, binds] of Object.entries(playerBindings)) {
      const pIdx = Number(pKey);
      if (Number.isNaN(pIdx) || pIdx < 0 || pIdx >= maxP || !binds) continue;
      const channel = this.emu.input.player(pIdx);
      channel.clearBindings();

      for (const [idKey, bind] of Object.entries(binds)) {
        const btnId = Number(idKey);
        if (!bind || Number.isNaN(btnId) || btnId >= 24) continue;

        const stdBtn = this.JE_ID_TO_BUTTON[btnId];
        const stdAxis = this.JE_ID_TO_AXIS[btnId];
        if (!stdBtn && !stdAxis) continue;

        for (const kc of [bind.kb1, bind.kb2]) {
          if (kc && kc > 0) {
            for (const code of this.keyCodeToCodes(kc)) {
              if (stdBtn) channel.setKeyBinding(stdBtn, code);
              else if (stdAxis) channel.setKeyAxisBinding(stdAxis.axis, stdAxis.direction, code);
            }
          }
        }

        for (const gp of [bind.gp1, bind.gp2]) {
          if (!gp || !gp.trim()) continue;
          const parts = gp.split(/(?<!:)\+/).filter(Boolean);
          for (const part of parts) {
            const gpIdx = this.resolveGamepadIndex(part);
            if (gpIdx !== null) {
              if (stdBtn) channel.setGamepadBinding(stdBtn, gpIdx);
              else if (stdAxis) channel.setGamepadAxisButtonBinding(stdAxis.axis, stdAxis.direction, gpIdx);
              continue;
            }
            const gpAxis = this.resolveGamepadAxis(part);
            if (gpAxis) {
              if (stdBtn) {
                channel.setGamepadAxisToButtonBinding(gpAxis.gpAxisIndex, gpAxis.gpDir, stdBtn);
              } else if (stdAxis) {
                const isDefaultSameAxis =
                  (stdAxis.axis === 'L_STICK_X' && gpAxis.gpAxisIndex === 0 && gpAxis.gpDir === stdAxis.direction) ||
                  (stdAxis.axis === 'L_STICK_Y' && gpAxis.gpAxisIndex === 1 && gpAxis.gpDir === stdAxis.direction) ||
                  (stdAxis.axis === 'R_STICK_X' && gpAxis.gpAxisIndex === 2 && gpAxis.gpDir === stdAxis.direction) ||
                  (stdAxis.axis === 'R_STICK_Y' && gpAxis.gpAxisIndex === 3 && gpAxis.gpDir === stdAxis.direction);
                if (!isDefaultSameAxis) {
                  channel.setGamepadAxisToAxisBinding(gpAxis.gpAxisIndex, gpAxis.gpDir, stdAxis.axis, stdAxis.direction);
                }
              }
            }
          }
        }
      }
    }
  }

  public static parseGamepadDetails(rawGp: Gamepad | null): GamepadDetails | null {
    if (!rawGp) return null;
    const rawId = rawGp.id || 'Standard Gamepad';
    let vendorId = '';
    let productId = '';

    const vidMatch = rawId.match(/(?:Vendor|VID)[_:\s]+([0-9a-fA-F]{4})/i) || rawId.match(/\b([0-9a-fA-F]{4})[:\-\/]([0-9a-fA-F]{4})\b/);
    const pidMatch = rawId.match(/(?:Product|PID)[_:\s]+([0-9a-fA-F]{4})/i);

    if (vidMatch) {
      vendorId = vidMatch[1];
      if (!pidMatch && vidMatch[2]) {
        productId = vidMatch[2];
      }
    }
    if (pidMatch && !productId) {
      productId = pidMatch[1];
    }

    const cleanName = rawId
      .replace(/\(STANDARD GAMEPAD.*?\)/gi, '')
      .replace(/\(Vendor:.*?\)/gi, '')
      .replace(/Vendor:.*$/gi, '')
      .replace(/VID_.*?PID_.*?/gi, '')
      .replace(/[\(\)]/g, '')
      .trim();

    return {
      rawId,
      deviceName: cleanName || 'Standard Controller',
      vendorId: vendorId ? (`0x${vendorId.toUpperCase()}`) : 'N/A',
      productId: productId ? (`0x${productId.toUpperCase()}`) : 'N/A',
      mapping: rawGp.mapping || 'standard',
      index: rawGp.index !== undefined ? rawGp.index : 0,
      buttonsCount: rawGp.buttons ? rawGp.buttons.length : 0,
      axesCount: rawGp.axes ? rawGp.axes.length : 0
    };
  }

  public static getKeyName(code: number): string {
    if (!code) return '—';
    const KEY_CODE_MAP: Record<number, string> = {
      8:'Backspace',9:'Tab',13:'Enter',16:'Shift',17:'Ctrl',18:'Alt',
      19:'Pause',20:'Caps Lock',27:'Escape',32:'Space',33:'Page Up',
      34:'Page Down',35:'End',36:'Home',37:'← Left',38:'↑ Up',
      39:'→ Right',40:'↓ Down',45:'Insert',46:'Delete',
      48:'0',49:'1',50:'2',51:'3',52:'4',53:'5',54:'6',55:'7',56:'8',57:'9',
      65:'A',66:'B',67:'C',68:'D',69:'E',70:'F',71:'G',72:'H',73:'I',74:'J',
      75:'K',76:'L',77:'M',78:'N',79:'O',80:'P',81:'Q',82:'R',83:'S',84:'T',
      85:'U',86:'V',87:'W',88:'X',89:'Y',90:'Z',
      96:'Num 0',97:'Num 1',98:'Num 2',99:'Num 3',100:'Num 4',
      101:'Num 5',102:'Num 6',103:'Num 7',104:'Num 8',105:'Num 9',
      106:'Num *',107:'Num +',109:'Num -',110:'Num .',111:'Num /',
      112:'F1',113:'F2',114:'F3',115:'F4',116:'F5',117:'F6',
      118:'F7',119:'F8',120:'F9',121:'F10',122:'F11',123:'F12',
      144:'Num Lock',145:'Scroll Lock',
      186:';',187:'=',188:',',189:'-',190:'.',191:'/',
      192:'`',219:'[',220:'\\',221:']',222:"'"
    };
    return KEY_CODE_MAP[code] || `Key ${code}`;
  }

  public static getGpName(s: string): string {
    if (!s) return '—';
    const GP_LABELS: Record<string, string> = {
      'BUTTON_1':'A / Cross','BUTTON_2':'B / Circle',
      'BUTTON_3':'X / Square','BUTTON_4':'Y / Triangle',
      'LEFT_TOP_SHOULDER':'LB / L1','RIGHT_TOP_SHOULDER':'RB / R1',
      'LEFT_BOTTOM_SHOULDER':'LT / L2','RIGHT_BOTTOM_SHOULDER':'RT / R2',
      'SELECT':'Select / Back','START':'Start',
      'LEFT_STICK':'L3','RIGHT_STICK':'R3',
      'DPAD_UP':'D-Up','DPAD_DOWN':'D-Down','DPAD_LEFT':'D-Left','DPAD_RIGHT':'D-Right',
      'LEFT_STICK_X:+1':'L-Stick →','LEFT_STICK_X:-1':'L-Stick ←',
      'LEFT_STICK_Y:+1':'L-Stick ↓','LEFT_STICK_Y:-1':'L-Stick ↑',
      'RIGHT_STICK_X:+1':'R-Stick →','RIGHT_STICK_X:-1':'R-Stick ←',
      'RIGHT_STICK_Y:+1':'R-Stick ↓','RIGHT_STICK_Y:-1':'R-Stick ↑'
    };
    if (GP_LABELS[s]) return GP_LABELS[s];
    const parts: string[] = [];
    let current = '';
    for (let i = 0; i < s.length; i++) {
      if (s[i] === '+' && (i === 0 || s[i - 1] !== ':')) {
        if (current) parts.push(current);
        current = '';
      } else {
        current += s[i];
      }
    }
    if (current) parts.push(current);
    if (parts.length > 1) {
      return parts.map((p) => GP_LABELS[p] || p).join(' + ');
    }
    return GP_LABELS[s] || s;
  }

  public static toggleFullscreen(): void {
    if (!document.fullscreenElement) {
      document.documentElement.requestFullscreen().catch(() => {});
    } else {
      if (document.exitFullscreen) document.exitFullscreen().catch(() => {});
    }
  }

  // Audio Fanfare Chime for RetroAchievements
  private static playAchievementSound(): void {
    try {
      const AudioCtx = window.AudioContext || (window as any).webkitAudioContext;
      if (!AudioCtx) return;
      const ctx = new AudioCtx();
      const now = ctx.currentTime;
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();

      osc.type = 'triangle';
      osc.connect(gain);
      gain.connect(ctx.destination);

      // Chime: C5 -> E5 -> G5 -> C6
      osc.frequency.setValueAtTime(523.25, now);
      osc.frequency.setValueAtTime(659.25, now + 0.08);
      osc.frequency.setValueAtTime(783.99, now + 0.16);
      osc.frequency.setValueAtTime(1046.5, now + 0.24);

      gain.gain.setValueAtTime(0.25, now);
      gain.gain.exponentialRampToValueAtTime(0.001, now + 0.65);

      osc.start(now);
      osc.stop(now + 0.65);
    } catch (_) {}
  }

  private static ensureBinary(data: any): Blob | null {
    if (!data) return null;
    if (data instanceof Blob) return data;
    if (data instanceof Uint8Array) return new Blob([data], { type: 'application/octet-stream' });
    if (data instanceof ArrayBuffer) return new Blob([new Uint8Array(data)], { type: 'application/octet-stream' });
    return new Blob([new TextEncoder().encode(JSON.stringify(data))], { type: 'application/octet-stream' });
  }
}
