export interface PluginConfiguration {
  IgdbClientId?: string;
  IgdbClientSecret?: string;
  RawgApiKey?: string;
  SteamGridDbApiKey?: string;
  TheGamesDbApiKey?: string;
  NetplayIceServers?: string;
  RommEnabled?: boolean;
  RommInstanceUrl?: string;
  RommUsername?: string;
  RommPassword?: string;
  RommSaveSyncEnabled?: boolean;
  RommPlaytimeReportEnabled?: boolean;
  RommCollectionSyncEnabled?: boolean;
  RommScreenshotPushEnabled?: boolean;
  MarketplaceProviders?: string[];
  MarketplaceConfigured?: boolean;
  MarketplaceFeedUrl?: string;
  GamesLibraryPath?: string;
  ScreenshotsFolder?: string;
  UseLoomInjector?: boolean;
  VantageEnabled?: boolean;
  EjsChannel?: 'stable' | 'latest' | 'nightly';
  BiosPath?: string;
  BiosAssignmentsJson?: string;
  BiosAssignments?: Record<string, string>;
  CommunityWorkerUrl?: string;
  ScreenScraperDevId?: string;
  ScreenScraperDevPassword?: string;
  ScreenScraperSoftName?: string;
  ScreenScraperUser?: string;
  ScreenScraperPassword?: string;
  ScreenScraperRegionPreference?: 'auto' | 'us' | 'eu' | 'jp' | 'wor';
  ScreenScraperLanguagePreference?: 'en' | 'fr' | 'de' | 'es' | 'it' | 'ja' | 'pt';
}

export interface SystemBiosDefinition {
  system: string;
  core: string;
  required: boolean;
  biosFiles: {
    filename: string;
    description: string;
    md5?: string;
    size?: number;
    found?: boolean;
    path?: string;
  }[];
}

export interface PlaytimeStats {
  totalSeconds: number;
  sessionsCount: number;
  gamesPlayedCount: number;
  recentSessions: {
    itemId: string;
    itemName: string;
    system: string;
    playedAt: string;
    durationSeconds: number;
  }[];
}
