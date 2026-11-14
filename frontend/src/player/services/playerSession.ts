import { PlayerBridge } from './playerBridge';
import { PlayerInitialData } from '../../types/player';

export class PlayerSessionTracker {
  private static config: PlayerInitialData | null = null;
  private static sessionStarted = false;
  private static isSessionActive = false;
  private static totalActivePlaytimeMs = 0;
  private static lastActiveResumeTime = 0;
  private static pingInterval: any = null;
  private static deviceId = '';
  private static sessionStopped = false;
  private static isLegacyEjs = false;

  public static init(config: PlayerInitialData): void {
    this.config = config;
    if (!config.itemId || !config.userId) return;

    this.deviceId = 'jellyemu-' + Math.random().toString(36).slice(2, 10);

    // If EJS template already started legacy session tracking, note it so we don't duplicate pings
    if ((window as any)._jellyEmuSessionStarted) {
      this.isLegacyEjs = true;
      this.sessionStarted = true;
    }

    // Expose pause/resume/stop tracking helpers globally
    const prevPause = (window as any)._jePauseSessionTracking;
    (window as any)._jePauseSessionTracking = () => {
      if (typeof prevPause === 'function') {
        try { prevPause(); } catch (_) {}
      }
      this.pause();
    };

    const prevResume = (window as any)._jeResumeSessionTracking;
    (window as any)._jeResumeSessionTracking = () => {
      if (typeof prevResume === 'function') {
        try { prevResume(); } catch (_) {}
      }
      this.resume();
    };

    (window as any)._jeStopSessionTracking = () => this.stop();

    // Listen for gamestart event across all emulators
    const onGameStart = () => {
      // If EJS template already handled the start, just flag it
      if ((window as any)._jellyEmuSessionStarted && !this.sessionStarted) {
        this.isLegacyEjs = true;
        this.sessionStarted = true;
        return;
      }
      this.start();
    };
    window.addEventListener('jellyemu:gamestart', onGameStart);

    // Auto-pause when tab is hidden
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) {
        this.pause();
      } else {
        this.resume();
      }
    });

    // Handle unload / navigation to guarantee session stop & playtime flush across all emulators
    const onUnload = () => {
      this.stop();
    };
    window.addEventListener('pagehide', onUnload);
    window.addEventListener('beforeunload', onUnload);
  }

  public static getActivePlaytimeSeconds(): number {
    let total = this.totalActivePlaytimeMs;
    if (this.isSessionActive && this.lastActiveResumeTime > 0) {
      total += (Date.now() - this.lastActiveResumeTime);
    }
    return Math.max(0, Math.round(total / 1000));
  }

  public static async start(): Promise<void> {
    if (this.sessionStarted || !this.config?.itemId || !this.config?.userId) return;
    this.sessionStarted = true;
    (window as any)._jellyEmuSessionStarted = true;
    this.totalActivePlaytimeMs = 0;
    this.lastActiveResumeTime = Date.now();
    this.isSessionActive = true;

    const itemId = this.config.itemId;
    const userId = this.config.userId;
    const deviceName = navigator.userAgent.indexOf('Mobi') !== -1 ? 'JellyEmu Mobile' : 'JellyEmu Browser';

    // Mark item as played in Jellyfin
    try {
      await fetch(PlayerBridge.getUrl(`/Users/${userId}/PlayedItems/${itemId}`), {
        method: 'POST',
        headers: PlayerBridge.getAuthHeaders()
      });
    } catch (e) {
      console.warn('[PlayerSessionTracker] Could not mark item as played:', e);
    }

    // Open Jellyfin playback session (appears in Dashboard → Active Sessions)
    try {
      await fetch(PlayerBridge.getUrl(`/jellyemu/session/start/${itemId}/${userId}`), {
        method: 'POST',
        headers: {
          ...PlayerBridge.getAuthHeaders(),
          'X-JellyEmu-DeviceId': this.deviceId,
          'X-JellyEmu-DeviceName': deviceName
        }
      });
    } catch (e) {
      console.warn('[PlayerSessionTracker] Could not open session:', e);
    }

    // Start 30s ping ticker
    if (this.pingInterval) clearInterval(this.pingInterval);
    this.pingInterval = setInterval(() => {
      if (document.hidden || !this.isSessionActive) return;
      this.sendPing(false);
    }, 30000);

    try {
      window.dispatchEvent(new CustomEvent('jellyemu:session:start', {
        detail: { itemId, userId, deviceId: this.deviceId }
      }));
    } catch (_) {}
  }

  public static pause(): void {
    if (this.isSessionActive) {
      this.totalActivePlaytimeMs += (Date.now() - this.lastActiveResumeTime);
      this.isSessionActive = false;
      this.sendPing(true);
    }
  }

  public static resume(): void {
    if (!this.isSessionActive && this.sessionStarted) {
      this.lastActiveResumeTime = Date.now();
      this.isSessionActive = true;
      this.sendPing(false);
    }
  }

  private static sendPing(isPaused: boolean): void {
    if (!this.sessionStarted || !this.config?.itemId || !this.config?.userId || this.isLegacyEjs) return;
    const secs = this.getActivePlaytimeSeconds();
    const query = `?isPaused=${isPaused ? 'true' : 'false'}&seconds=${secs}`;
    fetch(PlayerBridge.getUrl(`/jellyemu/session/ping/${this.config.itemId}/${this.config.userId}${query}`), {
      method: 'POST',
      headers: PlayerBridge.getAuthHeaders()
    }).catch(() => {});
  }

  public static stop(): void {
    if (this.sessionStopped || !this.sessionStarted || !this.config?.itemId || !this.config?.userId) return;
    this.sessionStopped = true;

    if (this.pingInterval) {
      clearInterval(this.pingInterval);
      this.pingInterval = null;
    }

    if (this.isSessionActive) {
      this.totalActivePlaytimeMs += (Date.now() - this.lastActiveResumeTime);
      this.isSessionActive = false;
    }

    const secs = this.getActivePlaytimeSeconds();
    const itemId = this.config.itemId;
    const userId = this.config.userId;

    // Stop session in Jellyfin Dashboard
    try {
      fetch(PlayerBridge.getUrl(`/jellyemu/session/stop/${itemId}/${userId}?seconds=${secs}`), {
        method: 'POST',
        headers: PlayerBridge.getAuthHeaders(),
        keepalive: true
      }).catch(() => {});
    } catch (_) {}

    // Flush playtime to SQLite database
    if (secs > 0) {
      try {
        fetch(PlayerBridge.getUrl(`/jellyemu/playtime/${itemId}/${userId}`), {
          method: 'POST',
          headers: {
            'Content-Type': 'text/plain',
            ...PlayerBridge.getAuthHeaders()
          },
          body: String(secs),
          keepalive: true
        }).catch(() => {});
      } catch (_) {}
    }

    try {
      window.parent.postMessage({ type: 'jellyemu-session-end', itemId, seconds: secs }, '*');
    } catch (_) {}
  }
}
