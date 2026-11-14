import { jellyfinApi } from '../../shared/apiClient';

export interface UserPlaytimeItem {
  userId: string;
  username: string;
  totalSeconds: number;
}

export interface PlaytimeSummaryResponse {
  totalSeconds: number;
  users: UserPlaytimeItem[];
  topGames: GamePlaytimeItem[];
}

export interface GamePlaytimeItem {
  itemId: string;
  itemName: string;
  seconds: number;
}

export interface UserPlaytimeDetails {
  userId: string;
  totalSeconds: number;
  games: GamePlaytimeItem[];
}

export interface PlatformStat {
  platform: string;
  count: number;
  percentage: number;
  color: string;
}

export interface LibraryGameSummary {
  totalGames: number;
  platforms: PlatformStat[];
  unsupportedCount: number;
}

const CONSOLE_COLORS: Record<string, string> = {
  'NES': '#e74c3c',
  'SNES': '#9b59b6',
  'N64': '#2ecc71',
  'Game Boy': '#1abc9c',
  'Game Boy Color': '#16a085',
  'Game Boy Advance': '#3498db',
  'Nintendo DS': '#2980b9',
  'Nintendo 3DS': '#d35400',
  'Sega Genesis': '#e67e22',
  'Master System': '#d35400',
  'Game Gear': '#f39c12',
  'Sega Saturn': '#34495e',
  'Dreamcast': '#ff5a00',
  'PlayStation': '#3f51b5',
  'PlayStation 2': '#2e6db4',
  'PlayStation Portable': '#00bcd4',
  'PICO-8': '#e91e63',
  'Atari 2600': '#795548',
  'Atari 7800': '#8d6e63',
  'TurboGrafx-16': '#ff5722',
  'NeoGeo Pocket': '#00bcd4',
  'Virtual Boy': '#c0392b',
  'Sega CD': '#8e44ad',
  'Sega 32X': '#d35400',
  'WonderSwan': '#1abc9c',
  'ColecoVision': '#7f8c8d'
};

export function getConsoleColor(platformName: string): string {
  if (CONSOLE_COLORS[platformName]) return CONSOLE_COLORS[platformName];
  const c = platformName.toLowerCase();
  if (c.includes('nes') || c.includes('nintendo entertainment system') || c.includes('famicom')) return '#e60012';
  if (c.includes('snes') || c.includes('super nintendo') || c.includes('super famicom')) return '#8c52ff';
  if (c.includes('n64') || c.includes('nintendo 64')) return '#007cff';
  if (c.includes('game boy') || c.includes('gb') || c.includes('gba') || c.includes('gbc')) return '#52B54B';
  if (c.includes('nds') || c.includes('ds') || c.includes('3ds')) return '#2980b9';
  if (c.includes('playstation') || c.includes('psx') || c.includes('ps1') || c.includes('ps2') || c.includes('ps3') || c.includes('psp') || c.includes('vita')) return '#2e6db4';
  if (c.includes('pico')) return '#ff7e00';
  if (c.includes('dreamcast')) return '#ff5a00';
  if (c.includes('xbox')) return '#107c10';
  if (c.includes('wii') || c.includes('switch')) return '#00d2ff';
  if (c.includes('sega') || c.includes('genesis') || c.includes('megadrive') || c.includes('saturn') || c.includes('master system') || c.includes('game gear')) return '#005ecc';
  if (c.includes('atari')) return '#e21b22';
  if (c.includes('turbografx') || c.includes('pc engine')) return '#ff5722';
  if (c.includes('neogeo') || c.includes('neo geo')) return '#00bcd4';

  let hash = 0;
  for (let i = 0; i < platformName.length; i++) {
    hash = platformName.charCodeAt(i) + ((hash << 5) - hash);
  }
  const h = Math.abs(hash % 360);
  return `hsl(${h}, 70%, 55%)`;
}

const KNOWN_REGIONS = new Set([
  'USA', 'US', 'Europe', 'EU', 'Japan', 'JP', 'World', 'En', 'Fr', 'De', 'Es', 'It', 'NTSC', 'PAL',
  'NTSC-U', 'NTSC-J', 'PAL-E', 'North America', 'Australia', 'Rev 1', 'Rev 2', 'v1.0', 'v1.1', 'v1.2'
]);

const EJS_UNSUPPORTED_PLATFORMS = new Set([
  'Dreamcast',
  'PlayStation 2',
  'PlayStation 3',
  'Xbox',
  'Xbox 360',
  'GameCube',
  'Wii',
  'Wii U',
  'Nintendo Switch',
  'Nintendo 3DS',
  'PlayStation Vita',
  'Windows',
  'Linux',
  'MacOS',
  'Android',
  'Unsupported'
]);

export const statsService = {
  getPlaytimeSummary: async (): Promise<PlaytimeSummaryResponse> => {
    try {
      const userId = (window as any).ApiClient?.getCurrentUserId?.() || 'default';
      const url = jellyfinApi.getApiUrl(`jellyemu/playtime/all/${userId}`);
      const res = await fetch(url, {
        headers: {
          'Content-Type': 'application/json',
          ...jellyfinApi.getAuthHeaders()
        }
      });
      if (!res.ok) throw new Error(`HTTP error ${res.status}`);
      const data = await res.json();
      const users: UserPlaytimeItem[] = Array.isArray(data?.users) ? data.users : [];
      // Sort users by playtime descending
      users.sort((a, b) => (b.totalSeconds || 0) - (a.totalSeconds || 0));

      const topGames: GamePlaytimeItem[] = Array.isArray(data?.topGames) ? data.topGames : [];
      topGames.sort((a, b) => (b.seconds || 0) - (a.seconds || 0));

      return {
        totalSeconds: data?.totalSeconds || 0,
        users,
        topGames
      };
    } catch (err) {
      console.warn('[JellyEmu] statsService: getPlaytimeSummary error:', err);
      return { totalSeconds: 0, users: [], topGames: [] };
    }
  },

  getUserPlaytimeDetails: async (targetUserId?: string): Promise<UserPlaytimeDetails | null> => {
    try {
      const userId = targetUserId || (window as any).ApiClient?.getCurrentUserId?.();
      if (!userId) return null;

      const url = jellyfinApi.getApiUrl(`jellyemu/playtime/${userId}`);
      const res = await fetch(url, {
        headers: {
          'Content-Type': 'application/json',
          ...jellyfinApi.getAuthHeaders()
        }
      });
      if (!res.ok) return null;
      const data = await res.json();
      const games: GamePlaytimeItem[] = Array.isArray(data?.games) ? data.games : [];
      games.sort((a, b) => (b.seconds || 0) - (a.seconds || 0));

      return {
        userId: data?.userId || userId,
        totalSeconds: data?.totalSeconds || 0,
        games
      };
    } catch (err) {
      console.warn('[JellyEmu] getUserPlaytimeDetails error:', err);
      return null;
    }
  },

  getLibraryGameStats: async (): Promise<LibraryGameSummary> => {
    try {
      const userId = (window as any).ApiClient?.getCurrentUserId?.();
      let items: any[] = [];

      // Primary: ApiClient.getItems()
      if ((window as any).ApiClient?.getItems && userId) {
        try {
          const res = await (window as any).ApiClient.getItems(userId, {
            Recursive: true,
            IncludeItemTypes: 'Book,Game',
            Fields: 'Tags,Path'
          });
          items = (res?.Items || []).filter((i: any) =>
            (i.Tags && i.Tags.includes('JellyEmu')) || i.Type === 'Game'
          );
        } catch (e) {
          console.warn('[JellyEmu] ApiClient.getItems failed:', e);
        }
      }

      // Fallback: Items endpoint
      if (items.length === 0) {
        try {
          const url = jellyfinApi.getApiUrl(`Items?Recursive=true&IncludeItemTypes=Book,Game&Fields=Tags,Path${userId ? `&UserId=${userId}` : ''}`);
          const res = await fetch(url, {
            headers: {
              'Content-Type': 'application/json',
              ...jellyfinApi.getAuthHeaders()
            }
          });
          if (res.ok) {
            const data = await res.json();
            items = (data?.Items || []).filter((i: any) =>
              (i.Tags && i.Tags.includes('JellyEmu')) || i.Type === 'Game'
            );
          }
        } catch (e) {
          console.warn('[JellyEmu] Fallback getLibraryGameStats failed:', e);
        }
      }

      const totalGames = items.length;
      const consoleCounts: Record<string, number> = {};
      let unsupportedCount = 0;

      items.forEach((item) => {
        let consoleTag: string | null = null;
        let isUnsupported = false;

        if (item.Tags && Array.isArray(item.Tags)) {
          if (item.Tags.includes('Unsupported')) {
            isUnsupported = true;
          }

          item.Tags.forEach((tag: string) => {
            if (tag === 'JellyEmu' || tag === 'Game' || tag === 'Unsupported' || tag === 'MultiDisc') return;
            if (KNOWN_REGIONS.has(tag)) return;
            if (tag.startsWith('Disc ') || tag.startsWith('Side ') || tag.startsWith('Tape ')) return;

            if (EJS_UNSUPPORTED_PLATFORMS.has(tag)) {
              isUnsupported = true;
            }
            consoleTag = tag;
          });
        }

        if (isUnsupported) {
          unsupportedCount++;
        }

        const finalPlatform = consoleTag || 'Unknown Platform';
        consoleCounts[finalPlatform] = (consoleCounts[finalPlatform] || 0) + 1;
      });

      const platforms: PlatformStat[] = Object.keys(consoleCounts)
        .sort((a, b) => consoleCounts[b] - consoleCounts[a])
        .map((plat) => {
          const count = consoleCounts[plat];
          const rawPct = totalGames > 0 ? (count / totalGames) * 100 : 0;
          return {
            platform: plat,
            count,
            percentage: totalGames > 0 ? Number(rawPct.toFixed(1)) : 0,
            color: getConsoleColor(plat)
          };
        });

      return {
        totalGames,
        platforms,
        unsupportedCount
      };
    } catch (err) {
      console.warn('[JellyEmu] getLibraryGameStats error:', err);
      return { totalGames: 0, platforms: [], unsupportedCount: 0 };
    }
  },

  formatDuration: (seconds: number): string => {
    if (!seconds || seconds <= 0) return '0m';
    const hrs = Math.floor(seconds / 3600);
    const mins = Math.floor((seconds % 3600) / 60);
    if (hrs > 0) {
      return `${hrs}h ${mins}m`;
    }
    return `${mins}m`;
  }
};
