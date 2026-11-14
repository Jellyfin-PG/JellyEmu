import { jellyfinApi } from '../../shared/apiClient';
import { FeedProvider, MarketplaceRom, DownloadResponse } from '../../types/marketplace';

export const marketplaceService = {
  getFeedUrl: async (): Promise<string> => {
    try {
      const config = await jellyfinApi.getPluginConfig();
      return config.MarketplaceFeedUrl || '';
    } catch {
      return '';
    }
  },

  updateFeedUrl: async (url: string): Promise<boolean> => {
    try {
      const apiUrl = jellyfinApi.getApiUrl('jellyemu/marketplace/feed-url');
      const res = await fetch(apiUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...jellyfinApi.getAuthHeaders()
        },
        body: JSON.stringify({ url })
      });
      return res.ok;
    } catch (err) {
      console.error('[JellyEmu] Failed to update feed URL:', err);
      return false;
    }
  },

  getFeedProviders: async (): Promise<FeedProvider[]> => {
    try {
      const url = jellyfinApi.getApiUrl('jellyemu/marketplace/feed-providers');
      const res = await fetch(url, {
        headers: {
          'Content-Type': 'application/json',
          ...jellyfinApi.getAuthHeaders()
        }
      });
      if (!res.ok) throw new Error(`HTTP error ${res.status}`);
      const data = await res.json();
      return Array.isArray(data) ? data : [];
    } catch (err) {
      console.warn('[JellyEmu] Failed to load feed providers:', err);
      return [];
    }
  },

  addProvider: async (providerUrl: string): Promise<boolean> => {
    try {
      const url = jellyfinApi.getApiUrl('jellyemu/marketplace/providers');
      const res = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...jellyfinApi.getAuthHeaders()
        },
        body: JSON.stringify({ url: providerUrl })
      });
      return res.ok;
    } catch (err) {
      console.error('[JellyEmu] Failed to add provider:', err);
      return false;
    }
  },

  removeProvider: async (providerUrl: string): Promise<boolean> => {
    try {
      const url = jellyfinApi.getApiUrl(`jellyemu/marketplace/providers?url=${encodeURIComponent(providerUrl)}`);
      const res = await fetch(url, {
        method: 'DELETE',
        headers: {
          'Content-Type': 'application/json',
          ...jellyfinApi.getAuthHeaders()
        }
      });
      return res.ok;
    } catch (err) {
      console.error('[JellyEmu] Failed to remove provider:', err);
      return false;
    }
  },

  search: async (query: string, system = '', letter = ''): Promise<MarketplaceRom[]> => {
    try {
      const params = new URLSearchParams();
      if (query) {
        params.append('query', query);
        params.append('q', query);
      }
      if (system) params.append('system', system);
      if (letter) params.append('letter', letter);

      const url = jellyfinApi.getApiUrl(`jellyemu/marketplace/search?${params.toString()}`);
      const res = await fetch(url, {
        headers: {
          'Content-Type': 'application/json',
          ...jellyfinApi.getAuthHeaders()
        }
      });
      if (!res.ok) throw new Error(`HTTP error ${res.status}`);
      const data = await res.json();
      if (!Array.isArray(data)) return [];

      return data.map((item: any) => ({
        title: item.title ?? item.Title ?? '',
        system: item.system ?? item.System ?? '',
        providerName: item.providerName ?? item.ProviderName ?? '',
        detailUrl: item.detailUrl ?? item.DetailUrl ?? '',
        thumbnailUrl: item.thumbnailUrl ?? item.ThumbnailUrl ?? '',
        region: item.region ?? item.Region ?? null,
        version: item.version ?? item.Version ?? null,
        languages: item.languages ?? item.Languages ?? null,
        extraFlags: item.extraFlags ?? item.ExtraFlags ?? null,
        hasManual: item.hasManual ?? item.HasManual ?? false
      }));
    } catch (err) {
      console.error('[JellyEmu] Marketplace search failed:', err);
      throw err;
    }
  },

  browse: async (provider: string, system: string, letter = ''): Promise<MarketplaceRom[]> => {
    try {
      const params = new URLSearchParams();
      params.append('provider', provider);
      params.append('system', system);
      if (letter) params.append('letter', letter);

      const url = jellyfinApi.getApiUrl(`jellyemu/marketplace/browse?${params.toString()}`);
      const res = await fetch(url, {
        headers: {
          'Content-Type': 'application/json',
          ...jellyfinApi.getAuthHeaders()
        }
      });
      if (!res.ok) throw new Error(`HTTP error ${res.status}`);
      const data = await res.json();
      if (!Array.isArray(data)) return [];

      return data.map((item: any) => ({
        title: item.title ?? item.Title ?? '',
        system: item.system ?? item.System ?? '',
        providerName: item.providerName ?? item.ProviderName ?? '',
        detailUrl: item.detailUrl ?? item.DetailUrl ?? '',
        thumbnailUrl: item.thumbnailUrl ?? item.ThumbnailUrl ?? '',
        region: item.region ?? item.Region ?? null,
        version: item.version ?? item.Version ?? null,
        languages: item.languages ?? item.Languages ?? null,
        extraFlags: item.extraFlags ?? item.ExtraFlags ?? null,
        hasManual: item.hasManual ?? item.HasManual ?? false
      }));
    } catch (err) {
      console.error('[JellyEmu] Marketplace browse failed:', err);
      throw err;
    }
  },

  downloadRom: async (detailUrl: string, system: string): Promise<DownloadResponse> => {
    try {
      const url = jellyfinApi.getApiUrl('jellyemu/marketplace/download');
      const res = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...jellyfinApi.getAuthHeaders()
        },
        body: JSON.stringify({ detailUrl, system })
      });
      const data = await res.json();
      if (!res.ok) {
        return {
          success: false,
          message: data?.message || `Download failed with HTTP ${res.status}`
        };
      }
      return {
        success: data?.success ?? true,
        filename: data?.filename,
        message: data?.message || 'Downloaded successfully'
      };
    } catch (err: any) {
      console.error('[JellyEmu] Download request error:', err);
      return {
        success: false,
        message: err?.message || 'Network error occurred while requesting download.'
      };
    }
  }
};
