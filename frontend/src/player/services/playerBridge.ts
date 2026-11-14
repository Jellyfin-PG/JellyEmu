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
  private static config: PlayerInitialData | null = null;

  public static init(config: PlayerInitialData) {
    this.config = config;
    (window as any).jeNotify = this.notify.bind(this);
    PlayerSessionTracker.init(config);
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
    const e = this.getEmulator();
    if (e && typeof e.pause === 'function') {
      try { e.pause(); } catch (err) { console.warn('[PlayerBridge] pause failed:', err); }
    } else if ((window as any).Module && typeof (window as any).Module.pico8TogglePaused === 'function') {
      try { (window as any).Module.pico8TogglePaused(); } catch (_) {}
    }
    if (typeof (window as any)._jePauseSessionTracking === 'function') {
      (window as any)._jePauseSessionTracking();
    }
  }

  public static resume(): void {
    const e = this.getEmulator();
    if (e && typeof e.play === 'function') {
      try { e.play(); } catch (err) { console.warn('[PlayerBridge] resume failed:', err); }
    } else if ((window as any).Module && typeof (window as any).Module.pico8TogglePaused === 'function') {
      try { (window as any).Module.pico8TogglePaused(); } catch (_) {}
    }
    if (typeof (window as any)._jeResumeSessionTracking === 'function') {
      (window as any)._jeResumeSessionTracking();
    }
  }

  public static restart(): void {
    this.notify({ message: 'Restarting Game...', subtext: 'Resetting emulator core', icon: 'restart', variant: 'warning' });
    const gm = this.getGameManager();
    if (gm && typeof gm.restart === 'function') {
      try { gm.restart(); } catch (err) { console.warn('[PlayerBridge] restart failed:', err); }
    } else {
      window.location.reload();
    }
  }

  public static toggleFastForward(active: boolean): void {
    const gm = this.getGameManager();
    if (active) {
      (window as any)._jeFFActive = true;
      (window as any)._jeSlowActive = false;
      if (gm && typeof gm.toggleSlowMotion === 'function') {
        try { gm.toggleSlowMotion(0); } catch (_) {}
      }
    } else {
      (window as any)._jeFFActive = false;
    }
    if (gm && typeof gm.toggleFastForward === 'function') {
      try { gm.toggleFastForward(active ? 1 : 0); } catch (err) { console.warn('[PlayerBridge] toggleFastForward failed:', err); }
    }
    try {
      window.dispatchEvent(new CustomEvent('jellyemu:speed-changed', {
        detail: { ff: Boolean((window as any)._jeFFActive), slow: Boolean((window as any)._jeSlowActive) }
      }));
    } catch (_) {}
  }

  public static toggleSlowMotion(active: boolean): void {
    const gm = this.getGameManager();
    if (active) {
      (window as any)._jeSlowActive = true;
      (window as any)._jeFFActive = false;
      if (gm && typeof gm.toggleFastForward === 'function') {
        try { gm.toggleFastForward(0); } catch (_) {}
      }
    } else {
      (window as any)._jeSlowActive = false;
    }
    if (gm && typeof gm.toggleSlowMotion === 'function') {
      try { gm.toggleSlowMotion(active ? 1 : 0); } catch (err) { console.warn('[PlayerBridge] toggleSlowMotion failed:', err); }
    }
    try {
      window.dispatchEvent(new CustomEvent('jellyemu:speed-changed', {
        detail: { ff: Boolean((window as any)._jeFFActive), slow: Boolean((window as any)._jeSlowActive) }
      }));
    } catch (_) {}
  }

  public static exit(): void {
    this.notify({ message: 'Exiting Game...', subtext: 'Closing session', icon: 'exit', variant: 'default' });
    PlayerSessionTracker.stop();
    if ((window as any).JellyEmuNetplay && typeof (window as any).JellyEmuNetplay.dispose === 'function') {
      try { (window as any).JellyEmuNetplay.dispose(); } catch (_) {}
    }
    const e = this.getEmulator();
    if (e && typeof e.exit === 'function') {
      try { e.exit(); return; } catch (_) {}
    }
    if (typeof (window as any).EJS_onExit === 'function') {
      (window as any).EJS_onExit();
      return;
    }
    if (typeof (window as any).jeExit === 'function') {
      (window as any).jeExit();
      return;
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

  // Fullscreen
  public static toggleFullscreen(): void {
    if (!document.fullscreenElement) {
      document.documentElement.requestFullscreen().then(() => {
        this.notify({ message: 'Fullscreen Enabled', icon: 'fullscreen', variant: 'info' });
      }).catch((err) => {
        console.warn('[PlayerBridge] Fullscreen failed:', err);
      });
    } else {
      if (document.exitFullscreen) {
        document.exitFullscreen().then(() => {
          this.notify({ message: 'Fullscreen Exited', icon: 'exitfullscreen', variant: 'default' });
        }).catch(() => {});
      }
    }
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
    const gm = this.getGameManager();
    if (!gm) return false;

    try {
      const resp = await fetch(this.getUrl(`/jellyemu/save/${itemId}/${userId}?slot=${slot}`), {
        headers: this.getAuthHeaders()
      });
      if (!resp.ok) throw new Error('Failed to download state');
      const buf = await resp.arrayBuffer();
      gm.loadState(new Uint8Array(buf));
      return true;
    } catch (err) {
      console.error('[PlayerBridge] Load state failed:', err);
      return false;
    }
  }

  public static async exportState(gameName: string): Promise<void> {
    const gm = this.getGameManager();
    if (!gm) return;

    try {
      const raw = await Promise.resolve(gm.getState());
      const blob = this.ensureBinary(raw);
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
    const gm = this.getGameManager();
    if (!gm) return false;

    try {
      const buf = await file.arrayBuffer();
      gm.loadState(new Uint8Array(buf));
      return true;
    } catch (err) {
      console.error('[PlayerBridge] Import state failed:', err);
      return false;
    }
  }

  // Audio / Volume
  public static setVolume(vol: number): void {
    const e = this.getEmulator();
    if (e) {
      try {
        if (typeof e.setVolume === 'function') e.setVolume(vol);
        e.volume = vol;
      } catch (_) {}
    }
    const pm = this.getPlayModule();
    if (pm && typeof pm.setVolume === 'function') {
      try { pm.setVolume(vol); } catch (_) {}
    }
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

  // Screenshots
  public static captureCanvas(format = 'image/jpeg', quality = 0.9): string | null {
    if (typeof (window as any)._jeCaptureCanvas === 'function') {
      return (window as any)._jeCaptureCanvas(format, quality);
    }
    try {
      const canvas = document.querySelector('canvas.ejs_canvas') as HTMLCanvasElement ||
                     document.querySelector('#game canvas') as HTMLCanvasElement ||
                     document.querySelector('canvas') as HTMLCanvasElement;
      if (canvas && canvas.width > 0 && canvas.height > 0) {
        return canvas.toDataURL(format, quality);
      }
    } catch (e) {
      console.warn('[PlayerBridge] Screenshot capture failed:', e);
    }
    return null;
  }

  public static async takeScreenshot(itemId: string, gameName: string): Promise<boolean> {
    const dataUrl = this.captureCanvas('image/png');
    if (!dataUrl) return false;

    try {
      const cfg = this.getConfig();
      if (cfg?.screenshotsToLibrary) {
        await fetch(this.getUrl(`/jellyemu/screenshot/${itemId}`), {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            ...this.getAuthHeaders()
          },
          body: JSON.stringify({ dataUrl, gameName })
        });
      } else {
        const a = document.createElement('a');
        a.href = dataUrl;
        a.download = `${gameName || 'screenshot'}-${Date.now()}.png`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
      }
      this.notify({ message: 'Screenshot Captured', subtext: 'Saved to library', icon: 'screenshot', variant: 'success' });
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
    if (typeof e.saveSettings === 'function') {
      try { e.saveSettings(); } catch (_) {}
    }
  }

  public static addCustomCheat(desc: string, code: string): void {
    const e = this.getEmulator();
    if (!e) return;
    e.cheats = e.cheats || [];
    e.cheats.push({ desc, code, checked: false });
    if (typeof e.saveSettings === 'function') {
      try { e.saveSettings(); } catch (_) {}
    }
  }

  public static removeCustomCheat(code: string): void {
    const e = this.getEmulator();
    if (!e || !e.cheats) return;
    const idx = e.cheats.findIndex((c: any) => c.code === code);
    if (idx !== -1) {
      if (typeof e.cheatChanged === 'function') {
        try { e.cheatChanged(false, code, idx); } catch (_) {}
      }
      e.cheats.splice(idx, 1);
      if (typeof e.saveSettings === 'function') {
        try { e.saveSettings(); } catch (_) {}
      }
    }
  }

  // Multi-Disc
  public static nextDisc(): void {
    const gm = this.getGameManager();
    if (gm && typeof gm.nextDisc === 'function') {
      try { gm.nextDisc(); } catch (err) { console.warn('[PlayerBridge] nextDisc failed:', err); }
    }
  }

  public static selectDisc(index: number): void {
    const gm = this.getGameManager();
    if (gm && typeof gm.loadDisc === 'function') {
      try { gm.loadDisc(index); } catch (err) { console.warn('[PlayerBridge] loadDisc failed:', err); }
    }
  }

  // Settings & Display
  public static applyShader(shader: string): void {
    const e = this.getEmulator();
    const gm = this.getGameManager();
    if (gm) {
      if (shader === 'disabled' || shader === 'none') {
        if (typeof gm.disableShader === 'function') try { gm.disableShader(); } catch (_) {}
      } else {
        if (typeof gm.enableShader === 'function') try { gm.enableShader(shader); } catch (_) {}
      }
    }
    if (e && typeof e.changeSettingOption === 'function') {
      try { e.changeSettingOption('shader', shader); } catch (_) {}
    }
  }

  public static applyRotation(rot: number): void {
    const e = this.getEmulator();
    if (e && typeof e.changeSettingOption === 'function') {
      try { e.changeSettingOption('videoRotation', rot); } catch (_) {}
    }
  }

  public static applyFastForwardRate(rate: string): void {
    const e = this.getEmulator();
    if (e && typeof e.changeSettingOption === 'function') {
      try { e.changeSettingOption('ff-ratio', rate); } catch (_) {}
    }
  }

  public static applySlowMotionRate(rate: string): void {
    const e = this.getEmulator();
    if (e && typeof e.changeSettingOption === 'function') {
      try { e.changeSettingOption('sm-ratio', rate); } catch (_) {}
    }
  }

  public static applyFpsOverlay(mode: string): void {
    const cfg = this.getConfig();
    if (cfg) (cfg as any).showFps = mode;
    if ((window as any).JellyEmuConfig) (window as any).JellyEmuConfig.showFps = mode;
    if ((window as any)._globPrefs) (window as any)._globPrefs.showFps = mode;
    window.dispatchEvent(new CustomEvent('jellyemu:fps-changed', { detail: { mode } }));
    if (typeof (window as any).JellyEmuSettings?.applyLiveFps === 'function') {
      try { (window as any).JellyEmuSettings.applyLiveFps(); } catch (_) {}
    }
  }

  public static applyScaling(scale: string): void {
    const cfg = this.getConfig();
    if (cfg) (cfg as any).scale = scale;
    if ((window as any).JellyEmuConfig) (window as any).JellyEmuConfig.scale = scale;
    if ((window as any)._globPrefs) (window as any)._globPrefs.scale = scale;
    if (typeof (window as any).JellyEmuSettings?.applyLiveScreenSize === 'function') {
      try { (window as any).JellyEmuSettings.applyLiveScreenSize(); } catch (_) {}
    }
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
        if (found && found.cores && found.cores.length > 0) {
          return found.cores.map((c: any) => ({
            id: c.id,
            name: c.name || c.id
          }));
        }
      }
    } catch (err) {
      console.warn('[PlayerBridge] fetchSystemCores failed:', err);
    }
    return [];
  }

  // Core Options (Libretro & Local Storage)
  public static getCoreName(): string {
    return (window as any).EJS_core || (window as any).JellyEmuConfig?.platformTag || 'default';
  }

  public static loadSavedCoreOptions(coreName?: string): Record<string, string> {
    const core = coreName || this.getCoreName();
    const key = 'jellyemu_coreopts_' + core;
    try {
      const raw = localStorage.getItem(key);
      if (raw) {
        const parsed = JSON.parse(raw);
        if (parsed && typeof parsed === 'object') return parsed;
      }
    } catch (e) {
      console.warn('[PlayerBridge] Failed to load core options:', e);
    }
    return {};
  }

  public static saveCoreOption(optKey: string, optVal: string, coreName?: string): void {
    const core = coreName || this.getCoreName();
    const key = 'jellyemu_coreopts_' + core;
    try {
      const opts = this.loadSavedCoreOptions(core);
      opts[optKey] = optVal;
      localStorage.setItem(key, JSON.stringify(opts));
    } catch (e) {
      console.warn('[PlayerBridge] Failed to save core option:', e);
    }
  }

  public static clearSavedCoreOptions(coreName?: string): void {
    const core = coreName || this.getCoreName();
    const key = 'jellyemu_coreopts_' + core;
    try {
      localStorage.removeItem(key);
    } catch (e) {
      console.warn('[PlayerBridge] Failed to clear core options:', e);
    }
  }

  public static getCoreOptionsList(): { key: string; name: string; value: string; options: { value: string; label: string }[]; description?: string }[] {
    const gm = this.getGameManager();
    const coreName = this.getCoreName();
    const saved = this.loadSavedCoreOptions(coreName);

    if (!gm || typeof gm.getCoreOptions !== 'function') {
      return [];
    }

    const optsRaw = gm.getCoreOptions();
    if (!optsRaw || typeof optsRaw !== 'string') {
      return [];
    }

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
    const gm = this.getGameManager();
    if (gm && typeof gm.setVariable === 'function') {
      try {
        gm.setVariable(optKey, optVal);
      } catch (e) {
        console.warn('[PlayerBridge] setVariable failed:', e);
      }
    }
    this.saveCoreOption(optKey, optVal, coreName);
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

  // Input Mapping & Controls
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
        return {
          hotkeys: rawHotkeys,
          scheme: matchedScheme
        };
      }
    } catch (err) {
      console.warn('[PlayerBridge] fetchInputScheme failed:', err);
    }
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
          // Check if parsed has player indices { 0: {...}, 1: {...} } or legacy flat { 8: {...} }
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
    } catch (err) {
      console.warn('[PlayerBridge] fetchEffectiveBindings failed:', err);
    }
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

      // Also update in-memory window._jeBindings & window._jePlayerBindings for real-time play
      (window as any)._jePlayerBindings = JSON.parse(JSON.stringify(playerBindings));
      (window as any)._jeBindings = JSON.parse(JSON.stringify(playerBindings[0] || {}));
      if ((window as any).JellyEmuConfig) {
        (window as any).JellyEmuConfig.customBindings = JSON.parse(JSON.stringify(playerBindings));
      }

      const resp = await fetch(this.getUrl(url), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...this.getAuthHeaders()
        },
        body: JSON.stringify(payload)
      });
      return resp.ok;
    } catch (err) {
      console.warn('[PlayerBridge] saveBindings failed:', err);
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
    // Check combos without splitting :+1 analog descriptors
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

  private static ensureBinary(data: any): Blob | null {
    if (typeof (window as any)._jeEnsureBinary === 'function') {
      return (window as any)._jeEnsureBinary(data);
    }
    if (!data) return null;
    if (data instanceof Blob) return data;
    if (data instanceof Uint8Array) return new Blob([data], { type: 'application/octet-stream' });
    if (data instanceof ArrayBuffer) return new Blob([new Uint8Array(data)], { type: 'application/octet-stream' });
    if (typeof data === 'string') {
      try {
        const binary = window.atob(data);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return new Blob([bytes], { type: 'application/octet-stream' });
      } catch {
        return new Blob([new TextEncoder().encode(data)], { type: 'application/octet-stream' });
      }
    }
    return new Blob([new TextEncoder().encode(JSON.stringify(data))], { type: 'application/octet-stream' });
  }
}
