import { jellyfinApi } from '../../shared/apiClient';
import {
  SaveStateEntry,
  CardMeta,
  RetroAchievementsProgress,
  GameGuideData,
  SystemInfo,
  SettingOptionsData,
  GlobalUserPreferences,
  SystemUserPreferences,
  RetroAchievementsPreferences
} from '../../types/injection';

export const injectionService = {
  getCurrentUserId: (): string | null => {
    try {
      if ((window as any).ApiClient && typeof (window as any).ApiClient.getCurrentUserId === 'function') {
        const id = (window as any).ApiClient.getCurrentUserId();
        if (id) return id;
      }
    } catch { }

    try {
      const hash = window.location.hash || '';
      const qIdx = hash.indexOf('?');
      if (qIdx !== -1) {
        const params = new URLSearchParams(hash.substring(qIdx));
        const uid = params.get('userId') || params.get('userid') || params.get('u');
        if (uid) return uid;
      }
      const sParams = new URLSearchParams(window.location.search || '');
      const sUid = sParams.get('userId') || sParams.get('userid') || sParams.get('u');
      if (sUid) return sUid;
    } catch { }

    try {
      const creds = localStorage.getItem('jellyfin_credentials');
      if (creds) {
        const parsed = JSON.parse(creds);
        const server = parsed.Servers?.[0];
        if (server?.UserId) return server.UserId;
      }
    } catch { }

    return null;
  },

  getAuthHeaders: (): Record<string, string> => {
    const token = (window as any).ApiClient ? (window as any).ApiClient.accessToken() : '';
    const headers: Record<string, string> = {
      'Content-Type': 'application/json'
    };
    if (token) {
      headers['Authorization'] = `MediaBrowser Token="${token}"`;
    }
    return headers;
  },

  getItem: async (userId: string, itemId: string): Promise<any> => {
    if ((window as any).ApiClient && typeof (window as any).ApiClient.getItem === 'function') {
      return (window as any).ApiClient.getItem(userId, itemId);
    }
    try {
      const url = jellyfinApi.getApiUrl(`Items/${itemId}?userId=${userId}`);
      const res = await fetch(url);
      if (!res.ok) return null;
      return await res.json();
    } catch {
      return null;
    }
  },

  toast: (msg: string, durationMs = 3500): void => {
    const existing = document.getElementById('je-injection-toast');
    if (existing) existing.remove();

    const toast = document.createElement('div');
    toast.id = 'je-injection-toast';
    toast.textContent = msg;
    toast.style.cssText = `
      position: fixed;
      bottom: 72px;
      left: 50%;
      transform: translateX(-50%);
      background: rgba(15, 23, 42, 0.95);
      border: 1px solid rgba(255, 255, 255, 0.2);
      color: #fff;
      padding: 10px 22px;
      border-radius: 9999px;
      font-size: 13px;
      font-weight: 600;
      box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.6), 0 8px 10px -6px rgba(0, 0, 0, 0.6);
      backdrop-filter: blur(12px);
      z-index: 200000;
      pointer-events: none;
      transition: opacity 0.3s ease, transform 0.3s ease;
    `;
    document.body.appendChild(toast);

    setTimeout(() => {
      toast.style.opacity = '0';
      toast.style.transform = 'translateX(-50%) translateY(8px)';
      setTimeout(() => toast.remove(), 350);
    }, durationMs);
  },

  getCardMetaBatch: async (itemIds: string[]): Promise<Record<string, CardMeta>> => {
    if (!itemIds.length) return {};
    try {
      const url = jellyfinApi.getApiUrl(`jellyemu/cardmeta?ids=${encodeURIComponent(itemIds.join(','))}`);
      const res = await fetch(url, { headers: injectionService.getAuthHeaders() });
      if (!res.ok) return {};
      return await res.json();
    } catch {
      return {};
    }
  },

  getSaveSlots: async (itemId: string, userId: string): Promise<SaveStateEntry[]> => {
    try {
      const url = jellyfinApi.getApiUrl(`jellyemu/save-slots/${itemId}/${userId}`);
      const res = await fetch(url, { headers: injectionService.getAuthHeaders() });
      if (!res.ok) return [];
      const data = await res.json();
      return Array.isArray(data) ? data : [];
    } catch {
      return [];
    }
  },

  getAllUserSaves: async (userId: string): Promise<SaveStateEntry[]> => {
    try {
      const url = jellyfinApi.getApiUrl(`jellyemu/saves/${userId}`);
      const res = await fetch(url, { headers: injectionService.getAuthHeaders() });
      if (!res.ok) return [];
      const data = await res.json();
      return Array.isArray(data) ? data : [];
    } catch {
      return [];
    }
  },

  getSaveScreenshot: async (itemId: string, userId: string, slot: number): Promise<string | null> => {
    try {
      const url = jellyfinApi.getApiUrl(`jellyemu/save-screenshot/${itemId}/${userId}/${slot}`);
      const res = await fetch(url, { headers: injectionService.getAuthHeaders() });
      if (!res.ok) return null;
      const data = await res.json();
      return data?.dataUrl || null;
    } catch {
      return null;
    }
  },

  deleteSaveSlot: async (itemId: string, userId: string, slot: number): Promise<boolean> => {
    try {
      const url = jellyfinApi.getApiUrl(`jellyemu/save/${itemId}/${userId}?slot=${slot}`);
      const res = await fetch(url, {
        method: 'DELETE',
        headers: injectionService.getAuthHeaders()
      });
      return res.ok;
    } catch {
      return false;
    }
  },

  getPlaytime: async (itemId: string, userId: string): Promise<{ seconds: number } | null> => {
    try {
      const url = jellyfinApi.getApiUrl(`jellyemu/playtime/${itemId}/${userId}`);
      const res = await fetch(url, { headers: injectionService.getAuthHeaders() });
      if (!res.ok) return null;
      return await res.json();
    } catch {
      return null;
    }
  },

  getRetroAchievementsProgress: async (itemId: string, userId: string): Promise<RetroAchievementsProgress | null> => {
    try {
      const url = jellyfinApi.getApiUrl(`jellyemu/retroachievements/progress/${itemId}/${userId}`);
      const res = await fetch(url, { headers: injectionService.getAuthHeaders() });
      if (res.status === 401) return { error: 'unauthorized' };
      if (!res.ok) return null;
      return await res.json();
    } catch {
      return null;
    }
  },

  getGuides: async (itemId: string, gameId?: string): Promise<GameGuideData | null> => {
    try {
      let path = `jellyemu/meta/guide?itemId=${encodeURIComponent(itemId)}`;
      if (gameId) path += `&gameId=${encodeURIComponent(gameId)}`;
      const url = jellyfinApi.getApiUrl(path);
      const res = await fetch(url, { headers: injectionService.getAuthHeaders() });
      if (!res.ok) return null;
      return await res.json();
    } catch {
      return null;
    }
  },

  getSystems: async (): Promise<SystemInfo[]> => {
    try {
      const url = jellyfinApi.getApiUrl('jellyemu/systems');
      const res = await fetch(url, { headers: injectionService.getAuthHeaders() });
      if (!res.ok) return [];
      const data = await res.json();
      return data.systems || [];
    } catch {
      return [];
    }
  },

  getSettingOptions: async (): Promise<SettingOptionsData> => {
    try {
      const url = jellyfinApi.getApiUrl('jellyemu/setting-options');
      const res = await fetch(url, { headers: injectionService.getAuthHeaders() });
      if (!res.ok) return {};
      return await res.json();
    } catch {
      return {};
    }
  },

  getScopedPrefs: async (userId: string, scope: 'global' | 'system' | 'ra', targetId?: string): Promise<any> => {
    try {
      let path = `jellyemu/prefs/${userId}?scope=${scope}`;
      if (targetId) path += `&targetId=${encodeURIComponent(targetId)}`;
      const url = jellyfinApi.getApiUrl(path);
      const res = await fetch(url, { headers: injectionService.getAuthHeaders() });
      if (!res.ok) return {};
      const data = await res.json();
      return data.preferences || {};
    } catch {
      return {};
    }
  },

  saveScopedPrefs: async (
    userId: string,
    scope: 'global' | 'system' | 'ra',
    preferences: GlobalUserPreferences | SystemUserPreferences | RetroAchievementsPreferences,
    targetId?: string
  ): Promise<boolean> => {
    try {
      const url = jellyfinApi.getApiUrl(`jellyemu/prefs/${userId}`);
      const payload: any = {
        scope,
        preferences
      };
      if (targetId) payload.targetId = targetId;

      const res = await fetch(url, {
        method: 'POST',
        headers: injectionService.getAuthHeaders(),
        body: JSON.stringify(payload)
      });
      return res.ok;
    } catch {
      return false;
    }
  },

  deleteScopedPrefs: async (userId: string, scope: 'global' | 'system' | 'ra', targetId?: string, key?: string): Promise<boolean> => {
    try {
      let path = `jellyemu/prefs/${userId}?scope=${scope}`;
      if (targetId) path += `&targetId=${encodeURIComponent(targetId)}`;
      if (key) path += `&key=${encodeURIComponent(key)}`;
      const url = jellyfinApi.getApiUrl(path);
      const res = await fetch(url, {
        method: 'DELETE',
        headers: injectionService.getAuthHeaders()
      });
      return res.ok;
    } catch {
      return false;
    }
  },

  resetUserPrefs: async (userId: string): Promise<boolean> => {
    try {
      const url = jellyfinApi.getApiUrl(`jellyemu/prefs/${userId}/reset`);
      const res = await fetch(url, {
        method: 'DELETE',
        headers: injectionService.getAuthHeaders()
      });
      return res.ok;
    } catch {
      return false;
    }
  },

  verifyRetroAchievements: async (username: string, token: string): Promise<{ success: boolean; message?: string }> => {
    try {
      const url = jellyfinApi.getApiUrl('jellyemu/retroachievements/verify');
      const res = await fetch(url, {
        method: 'POST',
        headers: injectionService.getAuthHeaders(),
        body: JSON.stringify({ username, token })
      });
      if (!res.ok) return { success: false, message: 'Verification failed' };
      const data = await res.json();
      return { success: !!data.success, message: data.message };
    } catch {
      return { success: false, message: 'Network error verifying account' };
    }
  },

  launchEmulator: (itemId: string, slot?: number, core?: string): void => {
    document.querySelectorAll('.dialogBackdrop, .dialogContainer, .focuscontainer-fullscreen').forEach(el => el.remove());
    document.body.style.overflow = '';
    document.documentElement.style.overflow = '';

    const userId = injectionService.getCurrentUserId();
    const safeItemId = encodeURIComponent(itemId || '');
    let path = `jellyemu/play/${safeItemId}`;
    const params = new URLSearchParams();
    if (userId) params.append('userId', encodeURIComponent(userId));
    if (slot && slot > 1) params.append('slot', String(Math.floor(slot)));
    if (core) params.append('core', encodeURIComponent(core));

    const query = params.toString();
    const rawUrl = jellyfinApi.getApiUrl(query ? `${path}?${query}` : path);

    try {
      const parsedUrl = new URL(rawUrl, window.location.origin);
      if (parsedUrl.origin === window.location.origin && (parsedUrl.protocol === 'http:' || parsedUrl.protocol === 'https:')) {
        window.location.href = parsedUrl.href;
        return;
      }
    } catch { }

    if (rawUrl.startsWith('/') && !rawUrl.startsWith('//') && !rawUrl.includes('\\')) {
      window.location.href = rawUrl;
    }
  },

  launchVantage: (itemId: string): void => {
    const safeItemId = encodeURIComponent(itemId || '');
    const vantageUrl = `vantage://launch?itemId=${safeItemId}`;
    try {
      const parsed = new URL(vantageUrl);
      if (parsed.protocol === 'vantage:') {
        window.location.href = parsed.href;
        return;
      }
    } catch { }
    window.location.href = vantageUrl;
  }
};
