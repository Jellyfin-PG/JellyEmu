import React, { useState, useEffect, useRef, useCallback } from 'react';
import {
  Modal,
  ModalContent,
  ModalHeader,
  ModalBody,
  Tabs,
  Tab,
  Button,
  Switch
} from '@heroui/react';
import {
  Gamepad2,
  RotateCcw,
  Keyboard,
  Smartphone,
  Check,
  Cpu,
  User,
  Users,
  Flame
} from 'lucide-react';
import { PlayerBridge } from '../services/playerBridge';
import {
  PlayerInitialData,
  InputButtonDefinition,
  InputBinding,
  InputActivationType,
  PlatformControlScheme,
  GamepadDetails
} from '../../types/player';

interface InGameInputMapModalProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
  config: PlayerInitialData;
  initialTab?: string;
}

export const DEFAULT_HOTKEYS: InputButtonDefinition[] = [
  { id: 24, label: 'QUICK SAVE', description: 'Save state immediately' },
  { id: 25, label: 'QUICK LOAD', description: 'Load state immediately' },
  { id: 26, label: 'CHANGE SLOT', description: 'Cycle active save state slot' },
  { id: 27, label: 'FAST FORWARD', description: 'Toggle fast forward emulation' },
  { id: 28, label: 'REWIND', description: 'Rewind gameplay in real time' },
  { id: 29, label: 'SLOW MOTION', description: 'Toggle slow motion gameplay' },
  { id: 30, label: 'EXIT GAME', description: 'Exit emulation and return to Jellyfin' },
  { id: 31, label: 'PAUSE / RESUME', description: 'Pause or resume emulation' },
  { id: 32, label: 'RESTART GAME', description: 'Restart / reset current game' },
  { id: 33, label: 'MUTE AUDIO', description: 'Toggle audio mute on/off' },
  { id: 34, label: 'SCREENSHOT', description: 'Capture in-game screenshot' },
  { id: 35, label: 'FULLSCREEN', description: 'Toggle fullscreen display' }
];

export const HOTKEY_METADATA_MAP: Record<number, { label: string; description: string; kb1: number; kb2: number; gp1: string; gp2: string }> = {
  24: { label: 'QUICK SAVE', description: 'Save state immediately', kb1: 49, kb2: 0, gp1: '', gp2: '' },
  25: { label: 'QUICK LOAD', description: 'Load state immediately', kb1: 50, kb2: 0, gp1: '', gp2: '' },
  26: { label: 'CHANGE SLOT', description: 'Cycle active save state slot', kb1: 51, kb2: 0, gp1: '', gp2: '' },
  27: { label: 'FAST FORWARD', description: 'Toggle fast forward emulation', kb1: 107, kb2: 9, gp1: '', gp2: '' },
  28: { label: 'REWIND', description: 'Rewind gameplay in real time', kb1: 8, kb2: 0, gp1: '', gp2: '' },
  29: { label: 'SLOW MOTION', description: 'Toggle slow motion gameplay', kb1: 109, kb2: 0, gp1: '', gp2: '' },
  30: { label: 'EXIT GAME', description: 'Exit emulation and return to Jellyfin', kb1: 27, kb2: 0, gp1: '', gp2: '' },
  31: { label: 'PAUSE / RESUME', description: 'Pause or resume emulation', kb1: 32, kb2: 80, gp1: '', gp2: '' },
  32: { label: 'RESTART GAME', description: 'Restart / reset current game', kb1: 115, kb2: 0, gp1: '', gp2: '' },
  33: { label: 'MUTE AUDIO', description: 'Toggle audio mute on/off', kb1: 77, kb2: 0, gp1: '', gp2: '' },
  34: { label: 'SCREENSHOT', description: 'Capture in-game screenshot', kb1: 123, kb2: 0, gp1: '', gp2: '' },
  35: { label: 'FULLSCREEN', description: 'Toggle fullscreen display', kb1: 70, kb2: 122, gp1: '', gp2: '' }
};

export const normalizeButtonDef = (b: any): InputButtonDefinition => {
  const id = Number(b?.id !== undefined ? b.id : (b?.Id !== undefined ? b.Id : 0));
  const rawLabel = String(b?.label || b?.Label || '').trim();
  const rawDesc = String(b?.description || b?.Description || '').trim();
  const meta = HOTKEY_METADATA_MAP[id];

  return {
    id,
    label: rawLabel || meta?.label || (id >= 24 ? `HOTKEY ${id}` : `BUTTON ${id}`),
    description: rawDesc || meta?.description || ''
  };
};

const FALLBACK_SCHEME: PlatformControlScheme = {
  id: 'default',
  name: 'Standard Controller',
  maxPlayers: 2,
  buttons: [
    { id: 8, label: 'A' }, { id: 0, label: 'B' }, { id: 9, label: 'X' }, { id: 1, label: 'Y' },
    { id: 2, label: 'SELECT' }, { id: 3, label: 'START' },
    { id: 4, label: 'UP' }, { id: 5, label: 'DOWN' }, { id: 6, label: 'LEFT' }, { id: 7, label: 'RIGHT' },
    { id: 10, label: 'L1' }, { id: 11, label: 'R1' }
  ],
  defaultBindings: {
    8: { kb1: 90, kb2: 0, gp1: 'BUTTON_1', gp2: '' },
    0: { kb1: 88, kb2: 0, gp1: 'BUTTON_2', gp2: '' },
    9: { kb1: 65, kb2: 0, gp1: 'BUTTON_3', gp2: '' },
    1: { kb1: 83, kb2: 0, gp1: 'BUTTON_4', gp2: '' },
    2: { kb1: 86, kb2: 0, gp1: 'SELECT', gp2: '' },
    3: { kb1: 13, kb2: 0, gp1: 'START', gp2: '' },
    4: { kb1: 38, kb2: 0, gp1: 'DPAD_UP', gp2: 'LEFT_STICK_Y:-1' },
    5: { kb1: 40, kb2: 0, gp1: 'DPAD_DOWN', gp2: 'LEFT_STICK_Y:+1' },
    6: { kb1: 37, kb2: 0, gp1: 'DPAD_LEFT', gp2: 'LEFT_STICK_X:-1' },
    7: { kb1: 39, kb2: 0, gp1: 'DPAD_RIGHT', gp2: 'LEFT_STICK_X:+1' },
    10: { kb1: 81, kb2: 0, gp1: 'LEFT_TOP_SHOULDER', gp2: '' },
    11: { kb1: 69, kb2: 0, gp1: 'RIGHT_TOP_SHOULDER', gp2: '' },
    24: { kb1: 49, kb2: 0, gp1: '', gp2: '' },
    25: { kb1: 50, kb2: 0, gp1: '', gp2: '' },
    26: { kb1: 51, kb2: 0, gp1: '', gp2: '' },
    27: { kb1: 107, kb2: 9, gp1: '', gp2: '' },
    28: { kb1: 8, kb2: 0, gp1: '', gp2: '' },
    29: { kb1: 109, kb2: 0, gp1: '', gp2: '' },
    30: { kb1: 27, kb2: 0, gp1: '', gp2: '' },
    31: { kb1: 32, kb2: 80, gp1: '', gp2: '' },
    32: { kb1: 115, kb2: 0, gp1: '', gp2: '' },
    33: { kb1: 77, kb2: 0, gp1: '', gp2: '' },
    34: { kb1: 123, kb2: 0, gp1: '', gp2: '' },
    35: { kb1: 70, kb2: 122, gp1: '', gp2: '' }
  }
};

const BUTTON_INDEX_TO_LABEL: string[] = [
  'BUTTON_1','BUTTON_2','BUTTON_3','BUTTON_4',
  'LEFT_TOP_SHOULDER','RIGHT_TOP_SHOULDER',
  'LEFT_BOTTOM_SHOULDER','RIGHT_BOTTOM_SHOULDER',
  'SELECT','START','LEFT_STICK','RIGHT_STICK',
  'DPAD_UP','DPAD_DOWN','DPAD_LEFT','DPAD_RIGHT'
];

const AXIS_NAMES: string[] = ['LEFT_STICK_X','LEFT_STICK_Y','RIGHT_STICK_X','RIGHT_STICK_Y'];

const getNativeButtons = (s: any): InputButtonDefinition[] => {
  const rawButtons = s?.buttons || s?.Buttons;
  if (Array.isArray(rawButtons) && rawButtons.length > 0) {
    const native = rawButtons
      .filter((b: any) => Number(b?.id !== undefined ? b.id : b?.Id) < 24)
      .map(normalizeButtonDef);
    if (native.length > 0) return native;
  }
  return FALLBACK_SCHEME.buttons.map(normalizeButtonDef);
};

const getHotkeyButtons = (h: any, s?: any): InputButtonDefinition[] => {
  let list: any[] = [];
  if (Array.isArray(h) && h.length > 0) {
    list = h;
  } else {
    const rawButtons = s?.buttons || s?.Buttons;
    if (Array.isArray(rawButtons)) {
      list = rawButtons.filter((b: any) => Number(b?.id !== undefined ? b.id : b?.Id) >= 24);
    }
  }
  if (!list || list.length === 0) {
    list = DEFAULT_HOTKEYS;
  }
  return list.map(normalizeButtonDef);
};

const safeDefaultBindings = (s: any): Record<number, InputBinding> => {
  const res: Record<number, InputBinding> = {};

  // Seed fallback native default bindings
  Object.keys(FALLBACK_SCHEME.defaultBindings).forEach((k) => {
    const id = Number(k);
    res[id] = { ...FALLBACK_SCHEME.defaultBindings[id] };
  });

  // Ensure hotkeys have default bindings seeded
  Object.keys(HOTKEY_METADATA_MAP).forEach((k) => {
    const id = Number(k);
    const m = HOTKEY_METADATA_MAP[id];
    res[id] = {
      kb1: m.kb1,
      kb2: m.kb2,
      gp1: m.gp1,
      gp2: m.gp2,
      activation: 'press',
      holdDuration: 5
    };
  });

  // Overlay scheme-specific default bindings from backend
  const rawDefaults = s?.defaultBindings || s?.DefaultBindings;
  if (rawDefaults && typeof rawDefaults === 'object') {
    Object.keys(rawDefaults).forEach((k) => {
      const id = Number(k);
      const b = rawDefaults[k];
      if (b) {
        res[id] = {
          kb1: Number(b.kb1 !== undefined ? b.kb1 : (b.Kb1 !== undefined ? b.Kb1 : (res[id]?.kb1 || 0))),
          kb2: Number(b.kb2 !== undefined ? b.kb2 : (b.Kb2 !== undefined ? b.Kb2 : (res[id]?.kb2 || 0))),
          gp1: String(b.gp1 !== undefined ? b.gp1 : (b.Gp1 !== undefined ? b.Gp1 : (res[id]?.gp1 || ''))),
          gp2: String(b.gp2 !== undefined ? b.gp2 : (b.Gp2 !== undefined ? b.Gp2 : (res[id]?.gp2 || ''))),
          activation: b.activation || b.Activation || res[id]?.activation || 'press',
          holdDuration: Number(b.holdDuration !== undefined ? b.holdDuration : (b.HoldDuration !== undefined ? b.HoldDuration : (res[id]?.holdDuration || 5)))
        };
      }
    });
  }

  return res;
};

const resolveTargetPlatform = (cfg: PlayerInitialData): string => {
  const win = typeof window !== 'undefined' ? (window as any) : {};
  return (
    cfg.platformTag ||
    cfg.inputScheme?.id ||
    win.JellyEmuConfig?.platformTag ||
    win.JellyEmuConfig?.inputScheme?.id ||
    win.EJS_platformTag ||
    cfg.core ||
    win.JellyEmuConfig?.core ||
    win.EJS_core ||
    cfg.systemName ||
    win.JellyEmuConfig?.systemName ||
    win.__JELLYEMU_PLAYER_DATA__?.systemName ||
    win._activeBackendScheme?.id ||
    ''
  );
};

const renderButtonLabel = (label: string) => {
  if (label.startsWith('△') || label.includes('TRIANGLE')) {
    return (
      <span className="flex items-center gap-1.5">
        <span className="w-5 h-5 rounded-full bg-emerald-500/15 border border-emerald-500/40 text-emerald-400 flex items-center justify-center text-[12px] font-bold shadow-sm">
          △
        </span>
        <span className="font-semibold text-white">{label.replace(/^[△\s]+/, '')}</span>
      </span>
    );
  }
  if (label.startsWith('○') || label.includes('CIRCLE')) {
    return (
      <span className="flex items-center gap-1.5">
        <span className="w-5 h-5 rounded-full bg-red-500/15 border border-red-500/40 text-red-400 flex items-center justify-center text-[12px] font-bold shadow-sm">
          ○
        </span>
        <span className="font-semibold text-white">{label.replace(/^[○\s]+/, '')}</span>
      </span>
    );
  }
  if (label.startsWith('⨯') || label.includes('CROSS')) {
    return (
      <span className="flex items-center gap-1.5">
        <span className="w-5 h-5 rounded-full bg-blue-500/15 border border-blue-500/40 text-blue-400 flex items-center justify-center text-[12px] font-bold shadow-sm">
          ✕
        </span>
        <span className="font-semibold text-white">{label.replace(/^[⨯✕\s]+/, '')}</span>
      </span>
    );
  }
  if (label.startsWith('□') || label.includes('SQUARE')) {
    return (
      <span className="flex items-center gap-1.5">
        <span className="w-5 h-5 rounded-full bg-pink-500/15 border border-pink-500/40 text-pink-400 flex items-center justify-center text-[12px] font-bold shadow-sm">
          □
        </span>
        <span className="font-semibold text-white">{label.replace(/^[□\s]+/, '')}</span>
      </span>
    );
  }
  return <span className="font-semibold text-white">{label}</span>;
};

export const InGameInputMapModal: React.FC<InGameInputMapModalProps> = ({
  isOpen,
  onOpenChange,
  config,
  initialTab = 'p0'
}) => {
  const [activeTab, setActiveTab] = useState<string>(initialTab);
  const [inputSubMode, setInputSubMode] = useState<'kb' | 'gp'>('kb');
  const [scheme, setScheme] = useState<PlatformControlScheme>(FALLBACK_SCHEME);
  const [hotkeys, setHotkeys] = useState<InputButtonDefinition[]>(DEFAULT_HOTKEYS);
  const [playerBindings, setPlayerBindings] = useState<Record<number, Record<number, InputBinding>>>({ 0: {} });
  const [gamepadDetails, setGamepadDetails] = useState<GamepadDetails | null>(null);
  const [enableVirtual, setEnableVirtual] = useState<boolean>(
    config.inputScheme?.virtualGamepad === 'true' || (window as any).JellyEmuConfig?.virtualGamepad === 'true'
  );
  const [leftyMode, setLeftyMode] = useState<boolean>(
    config.inputScheme?.virtualGamepadLefty === 'true' || (window as any).JellyEmuConfig?.virtualGamepadLefty === 'true'
  );
  const [statusMessage, setStatusMessage] = useState<string>('');

  // Interactive listening state
  const [listeningKey, setListeningKey] = useState<{ playerIdx: number; id: number; field: 'kb1' | 'kb2' } | null>(null);
  const [listeningGp, setListeningGp] = useState<{ playerIdx: number; id: number; field: 'gp1' | 'gp2'; initialAxes?: Record<number, number[]> } | null>(null);
  const [currentGpCombo, setCurrentGpCombo] = useState<string[]>([]);
  const gpCommitTimerRef = useRef<any>(null);

  useEffect(() => {
    if (isOpen && initialTab) {
      setActiveTab(initialTab);
    }
  }, [isOpen, initialTab]);

  const targetPlatform = resolveTargetPlatform(config);

  // Fetch system scheme and effective player bindings on modal open
  useEffect(() => {
    if (!isOpen) {
      setListeningKey(null);
      setListeningGp(null);
      return;
    }

    const loadData = async () => {
      let activeScheme: PlatformControlScheme = FALLBACK_SCHEME;
      let activeHotkeys: InputButtonDefinition[] = DEFAULT_HOTKEYS;

      const embeddedScheme = config.inputScheme || (window as any).JellyEmuConfig?.inputScheme || (window as any)._activeBackendScheme;
      if (embeddedScheme && typeof embeddedScheme === 'object' && Array.isArray(embeddedScheme.buttons) && embeddedScheme.buttons.length > 0) {
        activeScheme = {
          id: embeddedScheme.id || 'default',
          name: embeddedScheme.name || 'Standard Controller',
          maxPlayers: Math.min(2, embeddedScheme.maxPlayers || 2),
          buttons: embeddedScheme.buttons,
          defaultBindings: embeddedScheme.defaultBindings || FALLBACK_SCHEME.defaultBindings,
          playerDefaultBindings: embeddedScheme.playerDefaultBindings || undefined,
          analogAxes: embeddedScheme.analogAxes || []
        };
      }

      try {
        const schemeResp = await PlayerBridge.fetchInputScheme(targetPlatform);
        if (schemeResp?.scheme && Array.isArray(schemeResp.scheme.buttons) && schemeResp.scheme.buttons.length > 0) {
          activeScheme = schemeResp.scheme;
        }
        if (schemeResp?.hotkeys && Array.isArray(schemeResp.hotkeys) && schemeResp.hotkeys.length > 0) {
          activeHotkeys = schemeResp.hotkeys;
        }
      } catch (err) {
        console.warn('[InGameInputMapModal] fetchInputScheme failed, using active scheme:', err);
      }

      const safeNativeBtns = getNativeButtons(activeScheme);
      const safeHks = getHotkeyButtons(activeHotkeys, activeScheme);
      const safeDefs = safeDefaultBindings(activeScheme);
      const maxP = Math.max(1, Math.min(2, activeScheme.maxPlayers || 2));

      setScheme({
        ...activeScheme,
        maxPlayers: maxP,
        buttons: activeScheme.buttons || [...safeNativeBtns, ...safeHks],
        defaultBindings: safeDefs
      });
      setHotkeys(safeHks);

      // Initialize all player defaults
      const allButtons = [...safeNativeBtns, ...safeHks];
      const initialPlayerMap: Record<number, Record<number, InputBinding>> = {};

      for (let p = 0; p < maxP; p++) {
        const pDefs = activeScheme.playerDefaultBindings?.[p] || (p === 0 ? safeDefs : {});
        initialPlayerMap[p] = {};
        allButtons.forEach((btn) => {
          const btnId = btn.id;
          const defBind = pDefs[btnId] || safeDefs[btnId] || HOTKEY_METADATA_MAP[btnId] || {
            kb1: 0,
            kb2: 0,
            gp1: '',
            gp2: '',
            activation: 'press',
            holdDuration: 5
          };
          initialPlayerMap[p][btnId] = {
            kb1: defBind.kb1 || 0,
            kb2: defBind.kb2 || 0,
            gp1: defBind.gp1 || '',
            gp2: defBind.gp2 || '',
            activation: defBind.activation || 'press',
            holdDuration: defBind.holdDuration || 5
          };
        });
      }

      // Fetch saved preferences from SQLite / in-memory bridge (single source of truth: controls)
      let loadedRaw: any = (window as any)._jePlayerBindings || (window as any)._jeBindings || config.customBindings || (window as any).JellyEmuConfig?.customBindings || null;
      if (typeof loadedRaw === 'string') {
        try {
          loadedRaw = JSON.parse(loadedRaw);
        } catch { }
      }

      if (config.userId) {
        const prefData = await PlayerBridge.fetchEffectiveBindings(
          config.userId,
          config.itemId,
          targetPlatform
        );
        if (prefData) {
          if (prefData.playerBindings && Object.keys(prefData.playerBindings).length > 0) {
            loadedRaw = prefData.playerBindings;
          } else if (prefData.bindings && Object.keys(prefData.bindings).length > 0) {
            loadedRaw = { 0: prefData.bindings };
          }
        }
      }

      const mergedPlayerMap: Record<number, Record<number, InputBinding>> = { ...initialPlayerMap };

      if (loadedRaw && typeof loadedRaw === 'object') {
        const isMulti = loadedRaw['0'] && typeof loadedRaw['0'] === 'object' && loadedRaw['0'].kb1 === undefined && loadedRaw['0'].gp1 === undefined;
        if (isMulti) {
          Object.keys(loadedRaw).forEach((pKey) => {
            const pIdx = parseInt(pKey, 10);
            if (!isNaN(pIdx) && mergedPlayerMap[pIdx]) {
              const pObj = loadedRaw[pKey];
              Object.keys(pObj).forEach((bKey) => {
                const bId = parseInt(bKey, 10);
                if (!isNaN(bId) && mergedPlayerMap[pIdx][bId]) {
                  mergedPlayerMap[pIdx][bId] = {
                    ...mergedPlayerMap[pIdx][bId],
                    ...pObj[bId]
                  };
                }
              });
            }
          });
        } else {
          // Flat legacy single-player bindings
          Object.keys(loadedRaw).forEach((k) => {
            const numKey = parseInt(k, 10);
            if (!isNaN(numKey) && mergedPlayerMap[0] && mergedPlayerMap[0][numKey]) {
              mergedPlayerMap[0][numKey] = {
                ...mergedPlayerMap[0][numKey],
                ...loadedRaw[numKey]
              };
            }
          });
        }
      }

      setPlayerBindings(mergedPlayerMap);
      (window as any)._jeBindings = mergedPlayerMap[0];
      (window as any)._jePlayerBindings = mergedPlayerMap;
    };

    loadData();
  }, [isOpen, targetPlatform, config]);

  // Gamepad telemetry poller
  useEffect(() => {
    if (!isOpen) return;

    const pollGamepads = () => {
      const gps = navigator.getGamepads ? navigator.getGamepads() : [];
      const gp = Array.from(gps).find((g) => g !== null && g.connected !== false) || null;
      setGamepadDetails(PlayerBridge.parseGamepadDetails(gp));
    };

    pollGamepads();
    const interval = setInterval(pollGamepads, 500);
    window.addEventListener('gamepadconnected', pollGamepads);
    window.addEventListener('gamepaddisconnected', pollGamepads);

    return () => {
      clearInterval(interval);
      window.removeEventListener('gamepadconnected', pollGamepads);
      window.removeEventListener('gamepaddisconnected', pollGamepads);
    };
  }, [isOpen]);

  // Persist multi-player bindings to controls preference
  const handleSavePlayerBindings = useCallback(
    async (updatedPlayerMap: Record<number, Record<number, InputBinding>>) => {
      setPlayerBindings(updatedPlayerMap);
      (window as any)._jePlayerBindings = updatedPlayerMap;
      (window as any)._jeBindings = updatedPlayerMap[0] || {};

      if (config.userId) {
        await PlayerBridge.saveBindings(
          config.userId,
          targetPlatform,
          updatedPlayerMap
        );
      }
    },
    [config.userId, targetPlatform]
  );

  // Cycle trigger mode: Press -> Hold (5s, 3s, 2s, 1s) -> Combo
  const cycleActivation = useCallback(
    (playerIdx: number, btnId: number) => {
      const currentPBinds = { ...(playerBindings[playerIdx] || {}) };
      const currentBtnBind = currentPBinds[btnId] || { kb1: 0, kb2: 0, gp1: '', gp2: '', activation: 'press', holdDuration: 5 };
      const curType = currentBtnBind.activation || 'press';
      let nextType: InputActivationType = 'press';
      let nextHold = currentBtnBind.holdDuration || 5;

      if (curType === 'press') {
        nextType = 'hold';
        nextHold = 5;
      } else if (curType === 'hold') {
        if (nextHold === 5) nextHold = 3;
        else if (nextHold === 3) nextHold = 2;
        else if (nextHold === 2) nextHold = 1;
        else {
          nextType = 'combo';
          nextHold = 5;
        }
      } else if (curType === 'combo') {
        nextType = 'press';
        nextHold = 5;
      }

      const updatedPBinds = {
        ...currentPBinds,
        [btnId]: {
          ...currentBtnBind,
          activation: nextType,
          holdDuration: nextHold
        }
      };
      const updatedPlayerMap = {
        ...playerBindings,
        [playerIdx]: updatedPBinds
      };
      handleSavePlayerBindings(updatedPlayerMap);
    },
    [playerBindings, handleSavePlayerBindings]
  );

  const renderTriggerBadge = (playerIdx: number, btnId: number, bind: InputBinding) => {
    const act = bind.activation || 'press';
    const dur = bind.holdDuration || 5;

    if (act === 'hold') {
      return (
        <button
          type="button"
          title="Trigger: Hold. Click to cycle duration or switch mode"
          onClick={() => cycleActivation(playerIdx, btnId)}
          className="w-full py-1 px-1.5 rounded-lg text-[10px] font-semibold bg-amber-500/20 hover:bg-amber-500/30 text-amber-300 border border-amber-500/40 transition-all text-center flex items-center justify-center gap-1 shadow-sm"
        >
          <span>Hold ({dur}s)</span>
        </button>
      );
    }
    if (act === 'combo') {
      return (
        <button
          type="button"
          title="Trigger: Combo. Click to switch to Press"
          onClick={() => cycleActivation(playerIdx, btnId)}
          className="w-full py-1 px-1.5 rounded-lg text-[10px] font-semibold bg-purple-500/20 hover:bg-purple-500/30 text-purple-300 border border-purple-500/40 transition-all text-center flex items-center justify-center gap-1 shadow-sm"
        >
          <span>Combo</span>
        </button>
      );
    }
    return (
      <button
        type="button"
        title="Trigger: Press (instant). Click to switch to Hold (5s)"
        onClick={() => cycleActivation(playerIdx, btnId)}
        className="w-full py-1 px-1.5 rounded-lg text-[10px] font-medium bg-white/5 hover:bg-white/10 text-neutral-300 hover:text-white border border-white/10 transition-all text-center flex items-center justify-center gap-1"
      >
        <span>Press</span>
      </button>
    );
  };

  // Keyboard input capture listener
  useEffect(() => {
    if (!listeningKey) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      e.preventDefault();
      e.stopPropagation();

      if (e.keyCode === 27) { // Escape cancels
        setListeningKey(null);
        return;
      }

      const { playerIdx, id, field } = listeningKey;
      const currentPBinds = { ...(playerBindings[playerIdx] || {}) };
      const currentBtnBind = currentPBinds[id] || { kb1: 0, kb2: 0, gp1: '', gp2: '' };

      const updatedPBinds: Record<number, InputBinding> = {
        ...currentPBinds,
        [id]: {
          ...currentBtnBind,
          [field]: e.keyCode
        }
      };

      const updatedPlayerMap = {
        ...playerBindings,
        [playerIdx]: updatedPBinds
      };

      handleSavePlayerBindings(updatedPlayerMap);
      setListeningKey(null);
      setStatusMessage(`Mapped ${PlayerBridge.getKeyName(e.keyCode)} to P${playerIdx + 1} ${field.toUpperCase()}`);
    };

    window.addEventListener('keydown', handleKeyDown, true);
    return () => {
      window.removeEventListener('keydown', handleKeyDown, true);
    };
  }, [listeningKey, playerBindings, handleSavePlayerBindings]);

  // Gamepad polling & combo capture listener
  useEffect(() => {
    if (!listeningGp) return;

    let animFrame: number;

    const pollGamepadInput = () => {
      const pads = navigator.getGamepads ? navigator.getGamepads() : [];
      const currentlyHeld: string[] = [];

      for (let g = 0; g < pads.length; g++) {
        const p = pads[g];
        if (!p || !p.connected) continue;

        // Check Buttons
        const bCount = p.buttons ? p.buttons.length : 0;
        for (let bIdx = 0; bIdx < bCount; bIdx++) {
          const btn = p.buttons[bIdx];
          const isPressed = typeof btn === 'object' && btn !== null
            ? (!!btn.pressed || btn.value > 0.4)
            : (btn > 0.4);

          if (isPressed) {
            const label = bIdx < BUTTON_INDEX_TO_LABEL.length
              ? BUTTON_INDEX_TO_LABEL[bIdx]
              : `GAMEPAD_${bIdx}`;
            if (!currentlyHeld.includes(label)) {
              currentlyHeld.push(label);
            }
          }
        }

        // Check Axes
        const aCount = p.axes ? p.axes.length : 0;
        const initAxes = listeningGp.initialAxes?.[p.index];
        for (let aIdx = 0; aIdx < aCount; aIdx++) {
          const aVal = p.axes[aIdx];
          const baseA = initAxes?.[aIdx] !== undefined ? initAxes[aIdx] : 0;
          if (Math.abs(aVal - baseA) > 0.5) {
            const axisName = aIdx < AXIS_NAMES.length ? AXIS_NAMES[aIdx] : `EXTRA_STICK_${aIdx}`;
            const directionLabel = `${axisName}:${aVal - baseA > 0 ? '+1' : '-1'}`;
            if (!currentlyHeld.includes(directionLabel)) {
              currentlyHeld.push(directionLabel);
            }
          }
        }
      }

      if (currentlyHeld.length > 0) {
        setCurrentGpCombo((prev) => {
          if (currentlyHeld.length > prev.length) {
            return currentlyHeld.slice();
          }
          return prev;
        });

        if (gpCommitTimerRef.current) {
          clearTimeout(gpCommitTimerRef.current);
          gpCommitTimerRef.current = null;
        }
      } else if (currentGpCombo.length > 0 && !gpCommitTimerRef.current) {
        // Released -> Commit combo after short debounce
        gpCommitTimerRef.current = setTimeout(() => {
          const comboStr = currentGpCombo.join('+');
          const { playerIdx, id, field } = listeningGp;
          const currentPBinds = { ...(playerBindings[playerIdx] || {}) };
          const currentBtnBind = currentPBinds[id] || { kb1: 0, kb2: 0, gp1: '', gp2: '' };

          const updatedPBinds: Record<number, InputBinding> = {
            ...currentPBinds,
            [id]: {
              ...currentBtnBind,
              [field]: comboStr
            }
          };

          const updatedPlayerMap = {
            ...playerBindings,
            [playerIdx]: updatedPBinds
          };

          handleSavePlayerBindings(updatedPlayerMap);
          setListeningGp(null);
          setCurrentGpCombo([]);
          setStatusMessage(`Mapped ${PlayerBridge.getGpName(comboStr)} to P${playerIdx + 1} ${field.toUpperCase()}`);
        }, 120);
      }

      animFrame = requestAnimationFrame(pollGamepadInput);
    };

    animFrame = requestAnimationFrame(pollGamepadInput);

    return () => {
      cancelAnimationFrame(animFrame);
      if (gpCommitTimerRef.current) {
        clearTimeout(gpCommitTimerRef.current);
        gpCommitTimerRef.current = null;
      }
    };
  }, [listeningGp, currentGpCombo, playerBindings, handleSavePlayerBindings]);

  // Start Gamepad Listen helper
  const startListeningGp = (playerIdx: number, id: number, field: 'gp1' | 'gp2') => {
    setListeningKey(null);
    const pads = navigator.getGamepads ? navigator.getGamepads() : [];
    const initAxes: Record<number, number[]> = {};
    for (let i = 0; i < pads.length; i++) {
      if (pads[i]?.axes) {
        initAxes[pads[i]!.index] = Array.from(pads[i]!.axes);
      }
    }
    setCurrentGpCombo([]);
    setListeningGp({ playerIdx, id, field, initialAxes: initAxes });
  };

  // Reset to Defaults
  const handleResetDefaults = async () => {
    const safeNativeBtns = getNativeButtons(scheme);
    const safeHks = getHotkeyButtons(hotkeys, scheme);
    const safeDefs = safeDefaultBindings(scheme);
    const allButtons = [...safeNativeBtns, ...safeHks];
    const maxP = Math.max(1, Math.min(2, scheme.maxPlayers || 2));

    const resetPlayerMap: Record<number, Record<number, InputBinding>> = {};
    for (let p = 0; p < maxP; p++) {
      const pDefs = scheme.playerDefaultBindings?.[p] || (p === 0 ? safeDefs : {});
      resetPlayerMap[p] = {};
      allButtons.forEach((btn) => {
        const btnId = btn.id;
        const defBind = pDefs[btnId] || safeDefs[btnId] || HOTKEY_METADATA_MAP[btnId] || {
          kb1: 0,
          kb2: 0,
          gp1: '',
          gp2: '',
          activation: 'press',
          holdDuration: 5
        };
        resetPlayerMap[p][btnId] = {
          kb1: defBind.kb1 || 0,
          kb2: defBind.kb2 || 0,
          gp1: defBind.gp1 || '',
          gp2: defBind.gp2 || '',
          activation: defBind.activation || 'press',
          holdDuration: defBind.holdDuration || 5
        };
      });
    }

    await handleSavePlayerBindings(resetPlayerMap);
    setStatusMessage('All player controls reset to platform scheme defaults');
  };

  // Virtual Gamepad toggle handlers
  const handleVirtualToggle = (val: boolean) => {
    setEnableVirtual(val);
    if (config.userId) {
      PlayerBridge.syncVirtualControls(config.userId, val, leftyMode);
    }
  };

  const handleLeftyToggle = (val: boolean) => {
    setLeftyMode(val);
    if (config.userId) {
      PlayerBridge.syncVirtualControls(config.userId, enableVirtual, val);
    }
  };

  const isPlay =
    config.core === 'play' ||
    config.platformTag?.toLowerCase() === 'ps2' ||
    scheme?.id?.toLowerCase() === 'ps2' ||
    config.isPlay === true;

  const nativeButtons = getNativeButtons(scheme);
  const hotkeyButtonsList = isPlay ? [] : getHotkeyButtons(hotkeys, scheme);
  const maxPlayers = isPlay ? 1 : Math.max(1, Math.min(2, scheme.maxPlayers || 2));

  // Determine current active player index from tab
  const activePlayerIdx = isPlay ? 0 : (activeTab.startsWith('p') ? parseInt(activeTab.slice(1), 10) : 0);
  const isPlayerTab = isPlay || (activeTab.startsWith('p') && !isNaN(activePlayerIdx));
  const isHotkeysTab = !isPlay && activeTab === 'hotkeys';
  const isVirtualTab = !isPlay && activeTab === 'vg';

  const currentInputsToDisplay = isHotkeysTab
    ? hotkeyButtonsList
    : nativeButtons;

  const currentBindingsForActivePlayer = playerBindings[activePlayerIdx] || playerBindings[0] || {};

  return (
    <Modal
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      size="3xl"
      backdrop="blur"
      placement="center"
      scrollBehavior="inside"
      classNames={{
        base: 'bg-[#121216] border border-white/10 text-white shadow-2xl rounded-2xl max-w-3xl max-h-[90vh]',
        header: 'border-b border-white/10 px-6 py-4 flex items-center justify-between',
        body: 'p-6 flex flex-col gap-4',
        closeButton: 'text-neutral-400 hover:text-white hover:bg-white/10 p-1.5 rounded-lg'
      }}
    >
      <ModalContent>
        {() => (
          <>
            <ModalHeader>
              <div className="flex items-center gap-2.5">
                <Gamepad2 className="w-5 h-5 text-white" />
                <div className="flex items-center gap-2">
                  <h2 className="text-base font-semibold tracking-wide">Input Mapping</h2>
                  <span className="text-[11px] font-mono font-medium px-2 py-0.5 rounded bg-white/10 text-neutral-300 border border-white/10">
                    {scheme.name || 'System Controls'}
                  </span>
                </div>
              </div>
            </ModalHeader>

            <ModalBody className="flex flex-col gap-4">
              {/* System Scheme & Platform Layout Banner */}
              <div className="flex items-center justify-between px-4 py-3 rounded-xl bg-[#16161a] border border-white/10 text-xs">
                <div className="flex items-center gap-3">
                  <div className="p-1.5 rounded-lg bg-white/10 border border-white/10">
                    <Cpu className="w-4 h-4 text-white" />
                  </div>
                  <div className="flex flex-col">
                    <div className="flex items-center gap-2">
                      <span className="font-semibold text-white text-sm">{scheme.name || 'Standard Controller'}</span>
                      <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-white/5 text-neutral-400 border border-white/10 uppercase">
                        {scheme.id}
                      </span>
                    </div>
                    <span className="text-[11px] text-neutral-400 mt-0.5">
                      {nativeButtons.length} Native System Inputs • {maxPlayers} Player{maxPlayers > 1 ? 's' : ''} Supported
                    </span>
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  <span className="text-[11px] font-medium text-neutral-300 bg-white/5 px-2.5 py-1 rounded-lg border border-white/10 flex items-center gap-1.5">
                    <Users className="w-3.5 h-3.5 text-neutral-400" />
                    <span>{maxPlayers}P Scheme</span>
                  </span>
                </div>
              </div>

              {/* Status Banner */}
              {statusMessage && (
                <div className="flex items-center gap-2 px-3.5 py-2 rounded-xl text-xs bg-emerald-500/10 border border-emerald-500/20 text-emerald-300">
                  <Check className="w-4 h-4 text-emerald-400 shrink-0" />
                  <span>{statusMessage}</span>
                </div>
              )}

              {/* Top-Level Tabs: Player 1..N, Hotkeys, Virtual Controls */}
              <Tabs
                selectedKey={isPlay ? 'p0' : activeTab}
                onSelectionChange={(key) => {
                  setActiveTab(key as string);
                  setListeningKey(null);
                  setListeningGp(null);
                }}
                variant="solid"
                classNames={{
                  tabList: 'bg-black/50 p-1 rounded-xl border border-white/10 w-full overflow-x-auto',
                  cursor: 'bg-white text-black rounded-lg shadow-sm',
                  tab: 'h-8 text-xs font-medium text-neutral-400 data-[selected=true]:text-black transition-colors',
                  tabContent: 'group-data-[selected=true]:text-black font-semibold'
                }}
              >
                {Array.from({ length: maxPlayers }).map((_, pIdx) => (
                  <Tab
                    key={`p${pIdx}`}
                    title={
                      <div className="flex items-center gap-1.5">
                        <User className="w-3.5 h-3.5" />
                        <span>Player {pIdx + 1}</span>
                      </div>
                    }
                  />
                ))}

                {!isPlay && (
                  <Tab
                    key="hotkeys"
                    title={
                      <div className="flex items-center gap-1.5">
                        <Flame className="w-3.5 h-3.5" />
                        <span>Hotkeys</span>
                      </div>
                    }
                  />
                )}

                {!isPlay && (
                  <Tab
                    key="vg"
                    title={
                      <div className="flex items-center gap-1.5">
                        <Smartphone className="w-3.5 h-3.5" />
                        <span>Touch Controls</span>
                      </div>
                    }
                  />
                )}
              </Tabs>

              {/* Player Configuration View */}
              {isPlayerTab && (
                <div className="flex flex-col gap-3">
                  {/* Player & Sub-mode Selector Header */}
                  <div className="flex items-center justify-between px-1">
                    <div className="flex items-center gap-2">
                      <span className="text-xs font-semibold text-white uppercase tracking-wider">
                        Player {activePlayerIdx + 1} System Inputs
                      </span>
                      <span className="text-[10px] px-2 py-0.5 rounded font-mono bg-white/10 text-neutral-300 border border-white/10">
                        {inputSubMode === 'kb' ? 'Keyboard Input' : 'Gamepad Controller'}
                      </span>
                    </div>

                    {/* Mode Toggle: Keyboard vs Gamepad */}
                    <div className="flex items-center bg-black/60 p-1 rounded-xl border border-white/10">
                      <button
                        type="button"
                        onClick={() => {
                          setInputSubMode('kb');
                          setListeningKey(null);
                          setListeningGp(null);
                        }}
                        className={`flex items-center gap-1.5 px-3 py-1 rounded-lg text-xs font-medium transition-all ${
                          inputSubMode === 'kb'
                            ? 'bg-white text-black font-semibold shadow-sm'
                            : 'text-neutral-400 hover:text-white'
                        }`}
                      >
                        <Keyboard className="w-3.5 h-3.5" />
                        <span>Keyboard</span>
                      </button>

                      <button
                        type="button"
                        onClick={() => {
                          setInputSubMode('gp');
                          setListeningKey(null);
                          setListeningGp(null);
                        }}
                        className={`flex items-center gap-1.5 px-3 py-1 rounded-lg text-xs font-medium transition-all ${
                          inputSubMode === 'gp'
                            ? 'bg-white text-black font-semibold shadow-sm'
                            : 'text-neutral-400 hover:text-white'
                        }`}
                      >
                        <Gamepad2 className="w-3.5 h-3.5" />
                        <span>Gamepad</span>
                      </button>
                    </div>
                  </div>

                  {/* Gamepad Telemetry Banner (when in Gamepad mode) */}
                  {inputSubMode === 'gp' && (
                    <>
                      {gamepadDetails ? (
                        <div className="p-3.5 rounded-xl bg-black/40 border border-white/15 flex flex-col gap-2">
                          <div className="flex items-center justify-between">
                            <div className="flex items-center gap-2">
                              <Gamepad2 className="w-4 h-4 text-white" />
                              <span className="text-xs font-bold text-white">{gamepadDetails.deviceName}</span>
                            </div>
                            <span className="text-[10px] uppercase font-bold px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-400 border border-emerald-500/30">
                              Controller #{activePlayerIdx} Ready
                            </span>
                          </div>
                          <div className="text-[11px] text-neutral-400 font-mono break-all">
                            {gamepadDetails.rawId}
                          </div>
                          <div className="flex flex-wrap gap-2 pt-2 border-t border-white/10 text-[11px] text-neutral-300">
                            <span>VID: <code className="text-white font-mono bg-white/10 px-1 py-0.5 rounded">{gamepadDetails.vendorId}</code></span>
                            <span>PID: <code className="text-white font-mono bg-white/10 px-1 py-0.5 rounded">{gamepadDetails.productId}</code></span>
                            <span>Mapping: <strong className="text-white">{gamepadDetails.mapping}</strong></span>
                            <span>Index: <strong className="text-white">#{gamepadDetails.index}</strong></span>
                            <span>Inputs: <strong className="text-white">{gamepadDetails.buttonsCount} buttons, {gamepadDetails.axesCount} axes</strong></span>
                          </div>
                        </div>
                      ) : (
                        <div className="p-3 rounded-xl bg-black/40 border border-white/10 text-xs text-neutral-400 text-center flex items-center justify-center gap-2">
                          <Gamepad2 className="w-4 h-4 text-neutral-500" />
                          <span>No controller detected. Connect a gamepad or press any button to map Player {activePlayerIdx + 1}.</span>
                        </div>
                      )}
                    </>
                  )}

                  {/* Hotkeys Quick Link Banner */}
                  {!isPlay && (
                    <div className="flex items-center justify-between px-3.5 py-2 rounded-xl bg-amber-500/10 border border-amber-500/20 text-xs">
                      <div className="flex items-center gap-2">
                        <Flame className="w-3.5 h-3.5 text-amber-400 shrink-0" />
                        <span className="text-amber-200">
                          Looking for Pause/Resume, Fast Forward, Slow-Mo, or Exit?
                        </span>
                      </div>
                      <button
                        type="button"
                        onClick={() => setActiveTab('hotkeys')}
                        className="px-2.5 py-1 rounded-lg bg-amber-500/20 hover:bg-amber-500/30 text-amber-300 font-semibold text-[11px] border border-amber-500/30 transition-all shrink-0"
                      >
                        View Hotkeys →
                      </button>
                    </div>
                  )}

                  {/* Inputs Table Header */}
                  <div className="grid grid-cols-[1fr_95px_130px_130px] px-3 py-1.5 text-[11px] font-semibold text-neutral-400 uppercase tracking-wider">
                    <span>{scheme.name} Action (ID)</span>
                    <span className="text-center">Trigger</span>
                    <span className="text-center">{inputSubMode === 'kb' ? 'KB 1 (Primary)' : 'GP 1 (Primary)'}</span>
                    <span className="text-center">{inputSubMode === 'kb' ? 'KB 2 (Secondary)' : 'GP 2 (Secondary)'}</span>
                  </div>

                  {/* Inputs Table Body */}
                  <div className="flex flex-col gap-1.5 max-h-[380px] overflow-y-auto pr-1">
                    {currentInputsToDisplay.map((btn) => {
                      const bind = currentBindingsForActivePlayer[btn.id] || { kb1: 0, kb2: 0, gp1: '', gp2: '' };

                      return (
                        <div
                          key={btn.id}
                          className="grid grid-cols-[1fr_95px_130px_130px] items-center p-2.5 rounded-xl bg-black/30 border border-white/10 hover:border-white/20 transition-all text-xs"
                        >
                          <div className="flex items-center gap-2">
                            {renderButtonLabel(btn.label)}
                            <span className="text-[10px] px-1.5 py-0.5 rounded font-mono bg-white/5 text-neutral-400 border border-white/10">
                              ID {btn.id}
                            </span>
                          </div>

                          {/* Trigger Type Badge */}
                          <div className="flex justify-center px-1">
                            {renderTriggerBadge(activePlayerIdx, btn.id, bind)}
                          </div>

                          {inputSubMode === 'kb' ? (
                            <>
                              {/* KB 1 Button */}
                              <div className="flex justify-center px-1">
                                <button
                                  type="button"
                                  onClick={() => {
                                    setListeningGp(null);
                                    setListeningKey({ playerIdx: activePlayerIdx, id: btn.id, field: 'kb1' });
                                  }}
                                  className={`w-full py-1.5 px-2 rounded-lg text-xs font-mono font-medium transition-all text-center ${
                                    listeningKey?.playerIdx === activePlayerIdx && listeningKey?.id === btn.id && listeningKey?.field === 'kb1'
                                      ? 'bg-white text-black animate-pulse shadow-lg font-bold'
                                      : 'bg-white/5 hover:bg-white/10 border border-white/10 text-white'
                                  }`}
                                >
                                  {listeningKey?.playerIdx === activePlayerIdx && listeningKey?.id === btn.id && listeningKey?.field === 'kb1'
                                    ? 'Press key…'
                                    : PlayerBridge.getKeyName(bind.kb1)}
                                </button>
                              </div>

                              {/* KB 2 Button */}
                              <div className="flex justify-center px-1">
                                <button
                                  type="button"
                                  onClick={() => {
                                    setListeningGp(null);
                                    setListeningKey({ playerIdx: activePlayerIdx, id: btn.id, field: 'kb2' });
                                  }}
                                  className={`w-full py-1.5 px-2 rounded-lg text-xs font-mono font-medium transition-all text-center ${
                                    listeningKey?.playerIdx === activePlayerIdx && listeningKey?.id === btn.id && listeningKey?.field === 'kb2'
                                      ? 'bg-white text-black animate-pulse shadow-lg font-bold'
                                      : 'bg-white/5 hover:bg-white/10 border border-white/10 text-white'
                                  }`}
                                >
                                  {listeningKey?.playerIdx === activePlayerIdx && listeningKey?.id === btn.id && listeningKey?.field === 'kb2'
                                    ? 'Press key…'
                                    : PlayerBridge.getKeyName(bind.kb2)}
                                </button>
                              </div>
                            </>
                          ) : (
                            <>
                              {/* GP 1 Button */}
                              <div className="flex justify-center px-1">
                                <button
                                  type="button"
                                  onClick={() => startListeningGp(activePlayerIdx, btn.id, 'gp1')}
                                  className={`w-full py-1.5 px-2 rounded-lg text-xs font-mono font-medium transition-all text-center ${
                                    listeningGp?.playerIdx === activePlayerIdx && listeningGp?.id === btn.id && listeningGp?.field === 'gp1'
                                      ? 'bg-white text-black animate-pulse shadow-lg font-bold'
                                      : 'bg-white/5 hover:bg-white/10 border border-white/10 text-white'
                                  }`}
                                >
                                  {listeningGp?.playerIdx === activePlayerIdx && listeningGp?.id === btn.id && listeningGp?.field === 'gp1'
                                    ? (currentGpCombo.length > 0 ? PlayerBridge.getGpName(currentGpCombo.join('+')) : 'Hold button…')
                                    : PlayerBridge.getGpName(bind.gp1)}
                                </button>
                              </div>

                              {/* GP 2 Button */}
                              <div className="flex justify-center px-1">
                                <button
                                  type="button"
                                  onClick={() => startListeningGp(activePlayerIdx, btn.id, 'gp2')}
                                  className={`w-full py-1.5 px-2 rounded-lg text-xs font-mono font-medium transition-all text-center ${
                                    listeningGp?.playerIdx === activePlayerIdx && listeningGp?.id === btn.id && listeningGp?.field === 'gp2'
                                      ? 'bg-white text-black animate-pulse shadow-lg font-bold'
                                      : 'bg-white/5 hover:bg-white/10 border border-white/10 text-white'
                                  }`}
                                >
                                  {listeningGp?.playerIdx === activePlayerIdx && listeningGp?.id === btn.id && listeningGp?.field === 'gp2'
                                    ? (currentGpCombo.length > 0 ? PlayerBridge.getGpName(currentGpCombo.join('+')) : 'Hold button…')
                                    : PlayerBridge.getGpName(bind.gp2)}
                                </button>
                              </div>
                            </>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* Hotkeys Configuration View */}
              {isHotkeysTab && (
                <div className="flex flex-col gap-3">
                  <div className="flex items-center justify-between px-1">
                    <span className="text-xs font-semibold text-white uppercase tracking-wider">
                      Global Emulator Hotkeys
                    </span>
                    <span className="text-[10px] text-amber-400/90 font-mono bg-amber-500/10 px-2 py-0.5 rounded border border-amber-500/20">
                      Applied System-Wide
                    </span>
                  </div>

                  <div className="grid grid-cols-[1fr_95px_130px_130px] px-3 py-1.5 text-[11px] font-semibold text-neutral-400 uppercase tracking-wider">
                    <span>Hotkey Action (ID)</span>
                    <span className="text-center">Trigger</span>
                    <span className="text-center">Keyboard Key</span>
                    <span className="text-center">Gamepad Combo</span>
                  </div>

                  <div className="flex flex-col gap-1.5 max-h-[380px] overflow-y-auto pr-1">
                    {hotkeyButtonsList.map((btn) => {
                      const btnId = btn.id;
                      const meta = HOTKEY_METADATA_MAP[btnId];
                      const displayLabel = btn.label || meta?.label || `HOTKEY ${btnId}`;
                      const displayDesc = btn.description || meta?.description || '';
                      const defaultBind = safeDefaultBindings(scheme)[btnId] || { kb1: 0, kb2: 0, gp1: '', gp2: '' };
                      const currentBind = playerBindings[0]?.[btnId];
                      const bind = currentBind && (currentBind.kb1 !== 0 || currentBind.kb2 !== 0 || currentBind.gp1 !== '' || currentBind.gp2 !== '')
                        ? currentBind
                        : (currentBind || defaultBind);

                      const displayKb = (bind.kb1 !== undefined && bind.kb1 !== 0) ? bind.kb1 : defaultBind.kb1;
                      const displayGp = (bind.gp1 !== undefined && bind.gp1 !== '') ? bind.gp1 : defaultBind.gp1;

                      return (
                        <div
                          key={btnId}
                          className="grid grid-cols-[1fr_95px_130px_130px] items-center p-2.5 rounded-xl bg-black/30 border border-white/10 hover:border-white/20 transition-all text-xs"
                        >
                          <div className="flex flex-col">
                            <div className="flex items-center gap-2">
                              <span className="font-semibold text-white">{displayLabel}</span>
                              <span className="text-[10px] px-1.5 py-0.2 rounded font-mono bg-amber-500/15 text-amber-300 border border-amber-500/30">
                                ID {btnId}
                              </span>
                            </div>
                            {displayDesc && (
                              <span className="text-[11px] text-neutral-400 mt-0.5">{displayDesc}</span>
                            )}
                          </div>

                          {/* Trigger Type Badge */}
                          <div className="flex justify-center px-1">
                            {renderTriggerBadge(0, btnId, bind)}
                          </div>

                          {/* Keyboard Hotkey Button */}
                          <div className="flex justify-center px-1">
                            <button
                              type="button"
                              onClick={() => {
                                setListeningGp(null);
                                setListeningKey({ playerIdx: 0, id: btnId, field: 'kb1' });
                              }}
                              className={`w-full py-1.5 px-2 rounded-lg text-xs font-mono font-medium transition-all text-center ${
                                listeningKey?.playerIdx === 0 && listeningKey?.id === btnId && listeningKey?.field === 'kb1'
                                  ? 'bg-white text-black animate-pulse shadow-lg font-bold'
                                  : 'bg-white/5 hover:bg-white/10 border border-white/10 text-white'
                              }`}
                            >
                              {listeningKey?.playerIdx === 0 && listeningKey?.id === btnId && listeningKey?.field === 'kb1'
                                ? 'Press key…'
                                : PlayerBridge.getKeyName(displayKb)}
                            </button>
                          </div>

                          {/* Gamepad Combo Hotkey Button */}
                          <div className="flex justify-center px-1">
                            <button
                              type="button"
                              onClick={() => startListeningGp(0, btnId, 'gp1')}
                              className={`w-full py-1.5 px-2 rounded-lg text-xs font-mono font-medium transition-all text-center ${
                                listeningGp?.playerIdx === 0 && listeningGp?.id === btnId && listeningGp?.field === 'gp1'
                                  ? 'bg-white text-black animate-pulse shadow-lg font-bold'
                                  : 'bg-white/5 hover:bg-white/10 border border-white/10 text-white'
                              }`}
                            >
                              {listeningGp?.playerIdx === 0 && listeningGp?.id === btnId && listeningGp?.field === 'gp1'
                                ? (currentGpCombo.length > 0 ? PlayerBridge.getGpName(currentGpCombo.join('+')) : 'Hold combo…')
                                : PlayerBridge.getGpName(displayGp)}
                            </button>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* Virtual Controls View */}
              {isVirtualTab && (
                <div className="flex flex-col gap-3">
                  <div className="flex items-center justify-between p-4 rounded-xl bg-black/40 border border-white/10">
                    <div className="flex flex-col">
                      <span className="text-xs font-semibold text-white">Enable Virtual Touch Overlay</span>
                      <span className="text-[11px] text-neutral-400 mt-0.5">
                        Displays an on-screen gamepad on touch-enabled devices and mobile browsers
                      </span>
                    </div>
                    <Switch
                      size="sm"
                      isSelected={enableVirtual}
                      onValueChange={handleVirtualToggle}
                      classNames={{
                        wrapper: 'bg-white/20 group-data-[selected=true]:bg-white',
                        thumb: 'bg-black'
                      }}
                    />
                  </div>

                  <div className="flex items-center justify-between p-4 rounded-xl bg-black/40 border border-white/10">
                    <div className="flex flex-col">
                      <span className="text-xs font-semibold text-white">Left-Handed Virtual Controller</span>
                      <span className="text-[11px] text-neutral-400 mt-0.5">
                        Swaps the virtual D-Pad and action buttons for left-handed gameplay ergonomics
                      </span>
                    </div>
                    <Switch
                      size="sm"
                      isSelected={leftyMode}
                      onValueChange={handleLeftyToggle}
                      classNames={{
                        wrapper: 'bg-white/20 group-data-[selected=true]:bg-white',
                        thumb: 'bg-black'
                      }}
                    />
                  </div>
                </div>
              )}

              {/* Footer Actions */}
              <div className="pt-3 border-t border-white/10 flex gap-2">
                <Button
                  size="sm"
                  variant="flat"
                  onPress={handleResetDefaults}
                  className="flex-1 h-8 text-xs bg-white/5 hover:bg-white/10 text-neutral-300 border border-white/10 font-medium"
                  startContent={<RotateCcw className="w-3.5 h-3.5" />}
                >
                  Reset All Controls to {scheme.name || 'Platform'} Defaults
                </Button>
              </div>
            </ModalBody>
          </>
        )}
      </ModalContent>
    </Modal>
  );
};
