import { PluginConfiguration } from '../types/config';

export const JELLYEMU_PLUGIN_ID = '9bab105e-9af0-4e25-a87d-876713b60962';

declare global {
  interface Window {
    ApiClient?: {
      getPluginConfiguration: (pluginId: string) => Promise<PluginConfiguration>;
      updatePluginConfiguration: (pluginId: string, config: PluginConfiguration) => Promise<void>;
      getCurrentUserId: () => string;
      accessToken?: () => string;
      getUrl: (path: string, params?: Record<string, string>) => string;
      fetch: (options: { url: string; type?: string; data?: string | FormData; headers?: Record<string, string>; contentType?: string }) => Promise<any>;
    };
  }
}

export const jellyfinApi = {
  getAuthHeaders: (): Record<string, string> => {
    const headers: Record<string, string> = {};
    if (window.ApiClient?.accessToken) {
      try {
        const token = window.ApiClient.accessToken();
        if (token) {
          headers['Authorization'] = `MediaBrowser Token="${token}"`;
        }
      } catch {
        // Fallback
      }
    }
    return headers;
  },

  getPluginConfig: async (): Promise<PluginConfiguration> => {
    if (window.ApiClient?.getPluginConfiguration) {
      return await window.ApiClient.getPluginConfiguration(JELLYEMU_PLUGIN_ID);
    }
    const res = await fetch(`/Plugins/${JELLYEMU_PLUGIN_ID}/Configuration`);
    return await res.json();
  },

  updatePluginConfig: async (config: PluginConfiguration): Promise<void> => {
    if (window.ApiClient?.updatePluginConfiguration) {
      await window.ApiClient.updatePluginConfiguration(JELLYEMU_PLUGIN_ID, config);
      return;
    }
    await fetch(`/Plugins/${JELLYEMU_PLUGIN_ID}/Configuration`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(config)
    });
  },

  getApiUrl: (path: string): string => {
    const cleanPath = (path || '').replace(/^\/+/, '');
    if (window.ApiClient?.getUrl && cleanPath) {
      try {
        return window.ApiClient.getUrl(cleanPath);
      } catch {
        // Fallback below
      }
    }
    return `/${cleanPath}`;
  }
};
