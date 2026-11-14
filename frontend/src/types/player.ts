export interface PlayerInitialData {
  itemId: string;
  userId?: string;
  gameName: string;
  systemName?: string;
  platformTag?: string;
  core?: string;
  romUrl: string;
  saveGetUrl?: string;
  savePostUrl?: string;
  activeSlot?: number;
  hasNetplay?: boolean;
  hasSaves?: boolean;
  isPlay?: boolean;
  isPico8?: boolean;
  netplayServer?: string;
  netplayIceServers?: string[];
  gameId?: number;
  isM3u?: boolean;
  activeShader?: string;
  videoRotation?: number;
  vsync?: string;
  ffrate?: string;
  smrate?: string;
  showFps?: string;
  scale?: string;
  volume?: string;
  mute?: string;
  virtualGamepad?: string;
  virtualGamepadLefty?: string;
  inputScheme?: any;
  customBindings?: any;
  availableCores?: Array<{ id: string; name: string; needsThreads?: boolean }>;
  screenshotsToLibrary?: boolean;
  version?: string;
}

export interface SaveSlotInfo {
  slot: number;
  hasSave: boolean;
  timestamp?: string;
  screenshotUrl?: string;
}

export interface SramBackupInfo {
  slot: number;
  hasSave: boolean;
  timestamp?: string;
}

export interface CheatItem {
  id?: string;
  desc: string;
  code: string;
  checked: boolean;
  fromDb?: boolean;
}

export interface NetplayRoomInfo {
  id: string;
  name: string;
  gameId: number;
  gameName?: string;
  hostPlayer: string;
  currentPlayers: number;
  maxPlayers: number;
  isPasswordProtected: boolean;
  pingMs?: number;
}

export interface NetplayPlayerInfo {
  peerId: string;
  username: string;
  playerIndex: number;
  pingMs?: number | null;
  isHost?: boolean;
  isMe?: boolean;
}

export interface NetplayChatMessage {
  id: string;
  author: string;
  text: string;
  timestamp: string;
  isSystem?: boolean;
}

export interface CoreOptionItem {
  key: string;
  name: string;
  value: string;
  options: Array<{ value: string; label: string }>;
  description?: string;
}

export interface DiscItem {
  index: number;
  name: string;
  path: string;
  current?: boolean;
}

export interface InputButtonDefinition {
  id: number;
  label: string;
  description?: string;
}

export type InputActivationType = 'press' | 'hold' | 'combo';

export interface InputBinding {
  kb1: number;
  kb2: number;
  gp1: string;
  gp2: string;
  activation?: InputActivationType;
  holdDuration?: number;
}

export interface PlatformControlScheme {
  id: string;
  name: string;
  maxPlayers?: number;
  buttons: InputButtonDefinition[];
  defaultBindings: Record<number, InputBinding>;
  playerDefaultBindings?: Record<number, Record<number, InputBinding>>;
  analogAxes?: number[];
}

export interface GamepadDetails {
  rawId: string;
  deviceName: string;
  vendorId: string;
  productId: string;
  mapping: string;
  index: number;
  buttonsCount: number;
  axesCount: number;
}
