export interface SaveStateEntry {
  itemId: string;
  gameName?: string;
  itemName?: string;
  platform?: string;
  region?: string;
  disc?: string;
  slot: number;
  lastModified?: string;
  sizeBytes?: number;
  hasArt?: boolean;
  hasScreenshot?: boolean;
  screenshotUrl?: string;
  downloadUrl?: string;
}

export interface CardMeta {
  tags?: string[];
  communityRating?: number | null;
  providerIds?: Record<string, string>;
}

export interface GameDetailsContext {
  itemId: string;
  itemName: string;
  platformTag?: string;
  tags: string[];
  providerIds: Record<string, string>;
  timeToBeat?: {
    main?: string;
    extras?: string;
    completionist?: string;
  };
}

export interface SettingOption {
  id: string | number;
  label: string;
}

export interface SettingOptionsData {
  shaders?: SettingOption[];
  scaling?: SettingOption[];
  rotation?: SettingOption[];
  fastForwardRates?: SettingOption[];
  slowMotionRates?: SettingOption[];
  volume?: SettingOption[];
  mute?: SettingOption[];
  fps?: SettingOption[];
  autosave?: SettingOption[];
  haptics?: SettingOption[];
  virtualGamepad?: SettingOption[];
  virtualGamepadLefty?: SettingOption[];
}

export interface GlobalUserPreferences {
  shader?: string;
  scale?: string;
  videoRotation?: string;
  volume?: string;
  mute?: string;
  showFps?: string;
  ffrate?: string;
  smrate?: string;
  autosave?: string;
  haptics?: string;
  virtualGamepad?: string;
  virtualGamepadLefty?: string;
}

export interface SystemUserPreferences {
  core?: string;
}

export interface RetroAchievementsPreferences {
  username?: string;
  token?: string;
}

export interface SystemCoreInfo {
  id: string;
  name: string;
}

export interface SystemInfo {
  name: string;
  cores: SystemCoreInfo[];
  defaultCore?: string;
}

export interface RetroAchievementsProgress {
  error?: string;
  numUnlocked?: number;
  numTotal?: number;
  progressPercent?: number;
  raGameUrl?: string;
  gameTitle?: string;
}

export interface GameGuideData {
  manualUrl?: string | null;
  mapUrl?: string | null;
  guideUrl?: string | null;
}
