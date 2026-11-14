import { jellyfinApi } from '../../shared/apiClient';

export interface BiosFileEntry {
  filename: string;
  description: string;
  md5?: string;
  sha1?: string;
  size?: number;
  found: boolean;
  status?: string;
  activePath?: string;
}

export interface SystemBiosGroup {
  system: string;
  rawSystem?: string;
  activeAssignment?: string;
  files: BiosFileEntry[];
}

export interface BiosStatusResponse {
  directory: string;
  installedCount: number;
  systems: SystemBiosGroup[];
}

export const biosService = {
  scanBiosFiles: async (): Promise<SystemBiosGroup[]> => {
    try {
      const url = jellyfinApi.getApiUrl('jellyemu/bios/status');
      const res = await fetch(url);
      if (!res.ok) throw new Error(`HTTP error ${res.status}`);
      const data = await res.json();
      return Array.isArray(data?.systems) ? data.systems : (Array.isArray(data) ? data : []);
    } catch (err) {
      console.warn('[JellyEmu] Bios scan failed, using empty list:', err);
      return [];
    }
  },

  setActiveBios: async (system: string, relativePath: string): Promise<boolean> => {
    try {
      const url = jellyfinApi.getApiUrl('jellyemu/bios/active');
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ system, relativePath })
      });
      return res.ok;
    } catch {
      return false;
    }
  }
};
