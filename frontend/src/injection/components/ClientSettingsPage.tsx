import React, { useEffect, useState } from 'react';
import {
  Button,
  Spinner,
  Tabs,
  Tab,
  Card,
  CardBody
} from '@heroui/react';
import {
  Settings,
  Tv,
  Trophy,
  Cpu,
  Save,
  Gamepad2,
  RotateCcw,
  CheckCircle2,
  AlertCircle
} from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { injectionService } from '../services/injectionService';
import {
  SettingOptionsData,
  SystemInfo,
  GlobalUserPreferences,
  SystemUserPreferences,
  RetroAchievementsPreferences
} from '../../types/injection';
import { CustomSelect } from '../../shared/CustomSelect';

const VALID_TABS = ['global', 'system', 'ra'];

export const DEFAULT_GLOBAL_PREFS: GlobalUserPreferences = {
  shader: 'crt-easymode.glslp',
  scale: 'fit',
  videoRotation: '0',
  volume: '1',
  mute: '0',
  showFps: '0',
  ffrate: '3',
  smrate: '3',
  autosave: '0',
  haptics: '1'
};

export const getTabFromHash = (hash: string): string => {
  const h = hash || (typeof window !== 'undefined' ? window.location.hash : '') || '';
  if (!h.includes('jellyemu-settings')) return 'global';

  const queryIdx = h.indexOf('?');
  if (queryIdx !== -1) {
    const params = new URLSearchParams(h.substring(queryIdx));
    const tabParam = params.get('tab');
    if (tabParam && VALID_TABS.includes(tabParam.toLowerCase())) {
      return tabParam.toLowerCase();
    }
  }

  const clean = h.split('?')[0];
  const parts = clean.split('/');
  const lastPart = parts[parts.length - 1]?.toLowerCase();
  if (VALID_TABS.includes(lastPart)) {
    return lastPart;
  }

  return 'global';
};

export const ClientSettingsPage: React.FC = () => {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [verifyingRa, setVerifyingRa] = useState(false);
  const [raVerifyStatus, setRaVerifyStatus] = useState<{ success: boolean; message: string } | null>(null);

  const [options, setOptions] = useState<SettingOptionsData>({});
  const [systems, setSystems] = useState<SystemInfo[]>([]);
  const [selectedSystem, setSelectedSystem] = useState<string>('');
  const [activeTab, setActiveTab] = useState<string>(() => getTabFromHash(typeof window !== 'undefined' ? window.location.hash : ''));

  // Preferences State
  const [globalPrefs, setGlobalPrefs] = useState<GlobalUserPreferences>({ ...DEFAULT_GLOBAL_PREFS });

  const [systemPrefs, setSystemPrefs] = useState<SystemUserPreferences>({
    core: ''
  });

  const [raPrefs, setRaPrefs] = useState<RetroAchievementsPreferences>({
    username: '',
    token: ''
  });

  const loadInitialData = async () => {
    setLoading(true);
    const userId = injectionService.getCurrentUserId();

    const [optsData, sysData, gPrefs, rPrefs] = await Promise.all([
      injectionService.getSettingOptions(),
      injectionService.getSystems(),
      userId ? injectionService.getScopedPrefs(userId, 'global') : Promise.resolve({}),
      userId ? injectionService.getScopedPrefs(userId, 'ra') : Promise.resolve({})
    ]);

    setOptions(optsData);
    setSystems(sysData);
    if (sysData.length > 0) {
      setSelectedSystem(sysData[0].name);
    }

    const mergedGlobal: GlobalUserPreferences = {
      shader: gPrefs?.shader || DEFAULT_GLOBAL_PREFS.shader,
      scale: gPrefs?.scale || DEFAULT_GLOBAL_PREFS.scale,
      videoRotation: (gPrefs?.videoRotation !== undefined && gPrefs?.videoRotation !== null && gPrefs?.videoRotation !== '')
        ? String(gPrefs.videoRotation)
        : DEFAULT_GLOBAL_PREFS.videoRotation,
      volume: (gPrefs?.volume !== undefined && gPrefs?.volume !== null && gPrefs?.volume !== '')
        ? String(gPrefs.volume)
        : DEFAULT_GLOBAL_PREFS.volume,
      mute: (gPrefs?.mute !== undefined && gPrefs?.mute !== null && gPrefs?.mute !== '')
        ? String(gPrefs.mute)
        : DEFAULT_GLOBAL_PREFS.mute,
      showFps: (gPrefs?.showFps !== undefined && gPrefs?.showFps !== null && gPrefs?.showFps !== '')
        ? String(gPrefs.showFps)
        : DEFAULT_GLOBAL_PREFS.showFps,
      ffrate: gPrefs?.ffrate || DEFAULT_GLOBAL_PREFS.ffrate,
      smrate: gPrefs?.smrate || DEFAULT_GLOBAL_PREFS.smrate,
      autosave: (gPrefs?.autosave !== undefined && gPrefs?.autosave !== null && gPrefs?.autosave !== '')
        ? String(gPrefs.autosave)
        : DEFAULT_GLOBAL_PREFS.autosave,
      haptics: (gPrefs?.haptics !== undefined && gPrefs?.haptics !== null && gPrefs?.haptics !== '')
        ? String(gPrefs.haptics)
        : DEFAULT_GLOBAL_PREFS.haptics
    };
    setGlobalPrefs(mergedGlobal);

    if (rPrefs) {
      setRaPrefs({
        username: rPrefs.username || '',
        token: rPrefs.token || ''
      });
    }

    if (userId && sysData.length > 0) {
      const sPrefs = await injectionService.getScopedPrefs(userId, 'system', sysData[0].name);
      setSystemPrefs(sPrefs && sPrefs.core ? { core: sPrefs.core } : { core: '' });
    } else {
      setSystemPrefs({ core: '' });
    }

    setLoading(false);
  };

  useEffect(() => {
    loadInitialData();
  }, []);

  useEffect(() => {
    const handleHashChange = () => {
      const tab = getTabFromHash(window.location.hash);
      setActiveTab(tab);
    };

    window.addEventListener('hashchange', handleHashChange);
    window.addEventListener('popstate', handleHashChange);
    return () => {
      window.removeEventListener('hashchange', handleHashChange);
      window.removeEventListener('popstate', handleHashChange);
    };
  }, []);

  const handleTabChange = (key: string) => {
    setActiveTab(key);
    if (window.location.hash.startsWith('#/jellyemu-settings')) {
      const userId = injectionService.getCurrentUserId();
      const userParam = userId ? `userId=${userId}&` : '';
      const newHash = `#/jellyemu-settings?${userParam}tab=${key}`;
      if (window.location.hash !== newHash) {
        window.location.hash = newHash;
      }
    }
  };

  const handleSystemChange = async (sysName: string) => {
    setSelectedSystem(sysName);
    const userId = injectionService.getCurrentUserId();
    if (userId) {
      const sPrefs = await injectionService.getScopedPrefs(userId, 'system', sysName);
      setSystemPrefs(sPrefs && sPrefs.core ? { core: sPrefs.core } : { core: '' });
    }
  };

  const handleSaveGlobal = async () => {
    setSaving(true);
    const userId = injectionService.getCurrentUserId();
    if (userId) {
      const mergedToSave: GlobalUserPreferences = {
        shader: globalPrefs.shader || DEFAULT_GLOBAL_PREFS.shader,
        scale: globalPrefs.scale || DEFAULT_GLOBAL_PREFS.scale,
        videoRotation: globalPrefs.videoRotation !== undefined && globalPrefs.videoRotation !== '' ? globalPrefs.videoRotation : DEFAULT_GLOBAL_PREFS.videoRotation,
        volume: globalPrefs.volume !== undefined && globalPrefs.volume !== '' ? globalPrefs.volume : DEFAULT_GLOBAL_PREFS.volume,
        mute: globalPrefs.mute !== undefined && globalPrefs.mute !== '' ? globalPrefs.mute : DEFAULT_GLOBAL_PREFS.mute,
        showFps: globalPrefs.showFps !== undefined && globalPrefs.showFps !== '' ? globalPrefs.showFps : DEFAULT_GLOBAL_PREFS.showFps,
        ffrate: globalPrefs.ffrate || DEFAULT_GLOBAL_PREFS.ffrate,
        smrate: globalPrefs.smrate || DEFAULT_GLOBAL_PREFS.smrate,
        autosave: globalPrefs.autosave !== undefined && globalPrefs.autosave !== '' ? globalPrefs.autosave : DEFAULT_GLOBAL_PREFS.autosave,
        haptics: globalPrefs.haptics !== undefined && globalPrefs.haptics !== '' ? globalPrefs.haptics : DEFAULT_GLOBAL_PREFS.haptics
      };
      const ok = await injectionService.saveScopedPrefs(userId, 'global', mergedToSave);
      if (ok) {
        setGlobalPrefs(mergedToSave);
        injectionService.toast('Global settings saved successfully!');
      } else {
        injectionService.toast('Failed to save global settings.');
      }
    }
    setSaving(false);
  };

  const handleSaveSystem = async () => {
    if (!selectedSystem) return;
    setSaving(true);
    const userId = injectionService.getCurrentUserId();
    if (userId) {
      const payload: SystemUserPreferences = {
        core: systemPrefs.core || undefined
      };
      const ok = await injectionService.saveScopedPrefs(userId, 'system', payload, selectedSystem);
      if (ok) {
        injectionService.toast(`${selectedSystem} core preference saved successfully!`);
      } else {
        injectionService.toast(`Failed to save ${selectedSystem} core preference.`);
      }
    }
    setSaving(false);
  };

  const handleSaveRa = async () => {
    setSaving(true);
    const userId = injectionService.getCurrentUserId();
    if (userId) {
      const ok = await injectionService.saveScopedPrefs(userId, 'ra', raPrefs);
      if (ok) {
        injectionService.toast('RetroAchievements settings saved successfully!');
      } else {
        injectionService.toast('Failed to save RetroAchievements settings.');
      }
    }
    setSaving(false);
  };

  const handleVerifyRa = async () => {
    if (!raPrefs.username || !raPrefs.token) {
      setRaVerifyStatus({ success: false, message: 'Please enter both username and web API key' });
      return;
    }
    setVerifyingRa(true);
    setRaVerifyStatus(null);
    const res = await injectionService.verifyRetroAchievements(raPrefs.username, raPrefs.token);
    setRaVerifyStatus({
      success: res.success,
      message: res.message || (res.success ? 'Successfully connected to RetroAchievements!' : 'Invalid username or token')
    });
    setVerifyingRa(false);
  };

  const handleResetGlobal = async () => {
    if (!window.confirm('Reset global emulator defaults back to factory settings?')) return;
    const userId = injectionService.getCurrentUserId();
    if (userId) {
      setSaving(true);
      const ok = await injectionService.deleteScopedPrefs(userId, 'global');
      if (ok) {
        setGlobalPrefs({ ...DEFAULT_GLOBAL_PREFS });
        injectionService.toast('Global settings reset to factory defaults.');
      } else {
        await injectionService.saveScopedPrefs(userId, 'global', DEFAULT_GLOBAL_PREFS);
        setGlobalPrefs({ ...DEFAULT_GLOBAL_PREFS });
        injectionService.toast('Global settings reset to factory defaults.');
      }
      setSaving(false);
    }
  };

  const handleResetSystem = async () => {
    if (!selectedSystem) return;
    if (!window.confirm(`Reset custom core selection for ${selectedSystem} back to default?`)) return;
    const userId = injectionService.getCurrentUserId();
    if (userId) {
      setSaving(true);
      const ok = await injectionService.deleteScopedPrefs(userId, 'system', selectedSystem);
      if (ok) {
        setSystemPrefs({ core: '' });
        injectionService.toast(`${selectedSystem} core reset to default.`);
      } else {
        await injectionService.saveScopedPrefs(userId, 'system', { core: undefined }, selectedSystem);
        setSystemPrefs({ core: '' });
        injectionService.toast(`${selectedSystem} core reset to default.`);
      }
      setSaving(false);
    }
  };

  const handleResetRa = async () => {
    if (!window.confirm('Clear saved RetroAchievements credentials?')) return;
    const userId = injectionService.getCurrentUserId();
    if (userId) {
      setSaving(true);
      await injectionService.deleteScopedPrefs(userId, 'ra');
      setRaPrefs({ username: '', token: '' });
      setRaVerifyStatus(null);
      injectionService.toast('RetroAchievements credentials cleared.');
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="flex flex-col items-center justify-center py-28 space-y-3">
        <Spinner size="lg" color="primary" />
        <p className="text-xs text-default-400">Loading user preferences...</p>
      </div>
    );
  }

  const currentSystemObj = systems.find(s => s.name === selectedSystem);

  return (
    <motion.div
      initial={{ opacity: 0, y: 15 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.35, ease: 'easeOut' }}
      className="max-w-5xl mx-auto p-4 sm:p-6 lg:p-8 space-y-6"
    >
      {/* Header Bar */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-5 border-b border-white/10">
        <div className="flex items-center gap-3">
          <motion.div
            initial={{ scale: 0.8, rotate: -10 }}
            animate={{ scale: 1, rotate: 0 }}
            transition={{ type: 'spring', stiffness: 400, damping: 20 }}
            className="w-10 h-10 rounded-xl bg-white/10 border border-white/15 flex items-center justify-center text-white shadow-md"
          >
            <Settings className="w-5 h-5" />
          </motion.div>
          <div>
            <h1 className="text-xl sm:text-2xl font-bold text-white flex items-center gap-2">
              JellyEmu Settings
            </h1>
            <p className="text-xs sm:text-sm text-default-400">
              Configure global defaults, preferred system cores, and RetroAchievements
            </p>
          </div>
        </div>
      </div>

      {/* Settings Tabs */}
      <Card className="bg-[#121216]/90 border border-white/10 shadow-2xl backdrop-blur-md">
        <CardBody className="p-4 sm:p-6">
          <Tabs
            aria-label="Settings Categories"
            variant="underlined"
            selectedKey={activeTab}
            onSelectionChange={k => handleTabChange(String(k))}
            classNames={{
              tabList: "gap-6 border-b border-white/10 pb-0",
              cursor: "w-full bg-white",
              tab: "max-w-fit px-1 h-10 text-neutral-400 data-[selected=true]:text-white font-medium text-xs sm:text-sm",
              tabContent: "group-data-[selected=true]:text-white"
            }}
          >
            {/* Global Settings */}
            <Tab
              key="global"
              title={
                <div className="flex items-center gap-2">
                  <Tv className="w-4 h-4 text-white" />
                  <span className="font-semibold text-xs sm:text-sm">Global Settings</span>
                </div>
              }
            >
              <AnimatePresence mode="wait">
                <motion.div
                  key="global-content"
                  initial={{ opacity: 0, x: -8 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: 8 }}
                  transition={{ duration: 0.2 }}
                  className="space-y-8 py-4"
                >
                  {/* Section: Display & Audio */}
                  <div className="space-y-4">
                    <div className="border-b border-white/10 pb-2">
                      <h2 className="text-sm font-bold text-white flex items-center gap-2">
                        <Tv className="w-4 h-4 text-white" />
                        Display & Audio Defaults
                      </h2>
                      <p className="text-xs text-default-400 mt-0.5">
                        Default visual, audio, and scaling settings applied to all games across every system.
                      </p>
                    </div>

                    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                      <CustomSelect
                        label="Default Shader Filter"
                        value={globalPrefs.shader || DEFAULT_GLOBAL_PREFS.shader}
                        onChange={val => setGlobalPrefs(p => ({ ...p, shader: val || DEFAULT_GLOBAL_PREFS.shader }))}
                        options={options.shaders || []}
                      />

                      <CustomSelect
                        label="Screen Scaling Mode"
                        value={globalPrefs.scale || DEFAULT_GLOBAL_PREFS.scale}
                        onChange={val => setGlobalPrefs(p => ({ ...p, scale: val || DEFAULT_GLOBAL_PREFS.scale }))}
                        options={options.scaling || []}
                      />

                      <CustomSelect
                        label="Default Screen Rotation"
                        value={globalPrefs.videoRotation !== undefined && globalPrefs.videoRotation !== '' ? globalPrefs.videoRotation : DEFAULT_GLOBAL_PREFS.videoRotation}
                        onChange={val => setGlobalPrefs(p => ({ ...p, videoRotation: val }))}
                        options={options.rotation || []}
                      />

                      <CustomSelect
                        label="Default Audio Volume"
                        value={globalPrefs.volume !== undefined && globalPrefs.volume !== '' ? globalPrefs.volume : DEFAULT_GLOBAL_PREFS.volume}
                        onChange={val => setGlobalPrefs(p => ({ ...p, volume: val }))}
                        options={options.volume || []}
                      />

                      <CustomSelect
                        label="Default Audio Mute"
                        value={globalPrefs.mute !== undefined && globalPrefs.mute !== '' ? globalPrefs.mute : DEFAULT_GLOBAL_PREFS.mute}
                        onChange={val => setGlobalPrefs(p => ({ ...p, mute: val }))}
                        options={options.mute || []}
                      />

                      <CustomSelect
                        label="Performance Overlay (FPS)"
                        value={globalPrefs.showFps !== undefined && globalPrefs.showFps !== '' ? globalPrefs.showFps : DEFAULT_GLOBAL_PREFS.showFps}
                        onChange={val => setGlobalPrefs(p => ({ ...p, showFps: val }))}
                        options={options.fps || []}
                      />
                    </div>
                  </div>

                  {/* Section: Performance & Controls */}
                  <div className="space-y-4">
                    <div className="border-b border-white/10 pb-2">
                      <h2 className="text-sm font-bold text-white flex items-center gap-2">
                        <Gamepad2 className="w-4 h-4 text-white" />
                        Performance, Emulation & Controls
                      </h2>
                      <p className="text-xs text-default-400 mt-0.5">
                        Fast forward speeds, state persistence, controller vibration, and touch gamepad preferences.
                      </p>
                    </div>

                    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                      <CustomSelect
                        label="Fast Forward Speed"
                        value={globalPrefs.ffrate || DEFAULT_GLOBAL_PREFS.ffrate}
                        onChange={val => setGlobalPrefs(p => ({ ...p, ffrate: val || DEFAULT_GLOBAL_PREFS.ffrate }))}
                        options={options.fastForwardRates || []}
                      />

                      <CustomSelect
                        label="Slow Motion Speed"
                        value={globalPrefs.smrate || DEFAULT_GLOBAL_PREFS.smrate}
                        onChange={val => setGlobalPrefs(p => ({ ...p, smrate: val || DEFAULT_GLOBAL_PREFS.smrate }))}
                        options={options.slowMotionRates || []}
                      />

                      <CustomSelect
                        label="Autosave on Exit"
                        value={globalPrefs.autosave !== undefined && globalPrefs.autosave !== '' ? globalPrefs.autosave : DEFAULT_GLOBAL_PREFS.autosave}
                        onChange={val => setGlobalPrefs(p => ({ ...p, autosave: val }))}
                        options={options.autosave || []}
                      />

                      <CustomSelect
                        label="Gamepad Haptics / Rumble"
                        value={globalPrefs.haptics !== undefined && globalPrefs.haptics !== '' ? globalPrefs.haptics : DEFAULT_GLOBAL_PREFS.haptics}
                        onChange={val => setGlobalPrefs(p => ({ ...p, haptics: val }))}
                        options={options.haptics || []}
                      />
                    </div>
                  </div>

                  <div className="flex items-center justify-end gap-3 pt-2">
                    <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.96 }}>
                      <Button
                        variant="flat"
                        disabled={saving}
                        startContent={<RotateCcw className="w-3.5 h-3.5" />}
                        onPress={handleResetGlobal}
                        className="font-medium text-xs px-4 bg-white/10 hover:bg-white/20 text-white border border-white/15 cursor-pointer"
                      >
                        Reset Defaults
                      </Button>
                    </motion.div>
                    <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.96 }}>
                      <Button
                        variant="solid"
                        isLoading={saving}
                        startContent={!saving ? <Save className="w-4 h-4 text-black" /> : null}
                        onPress={handleSaveGlobal}
                        className="font-semibold text-xs px-5 bg-white hover:bg-neutral-200 text-black border border-white/40 shadow-lg cursor-pointer"
                      >
                        Save
                      </Button>
                    </motion.div>
                  </div>
                </motion.div>
              </AnimatePresence>
            </Tab>

            {/* System Settings */}
            <Tab
              key="system"
              title={
                <div className="flex items-center gap-2">
                  <Cpu className="w-4 h-4 text-white" />
                  <span className="font-semibold text-xs sm:text-sm">System Settings</span>
                </div>
              }
            >
              <AnimatePresence mode="wait">
                <motion.div
                  key="system-content"
                  initial={{ opacity: 0, x: -8 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: 8 }}
                  transition={{ duration: 0.2 }}
                  className="space-y-6 py-4"
                >
                  <div className="p-4 rounded-xl bg-[#16161a] border border-white/10 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
                    <div className="space-y-1">
                      <label className="text-xs font-semibold text-white">Select Console / System</label>
                      <p className="text-[11px] text-default-400">Choose a platform to configure its preferred emulation core. All display and performance options follow your Global Settings.</p>
                    </div>
                    <div className="min-w-[220px]">
                      <CustomSelect
                        value={selectedSystem}
                        onChange={handleSystemChange}
                        options={systems.map(sys => ({ value: sys.name, label: sys.name }))}
                      />
                    </div>
                  </div>

                  {selectedSystem && (
                    <div className="space-y-6">
                      {/* Core Selection */}
                      {currentSystemObj && currentSystemObj.cores?.length > 0 ? (
                        <div className="space-y-3 p-5 rounded-xl bg-white/5 border border-white/15">
                          <div>
                            <label className="text-xs font-bold text-white flex items-center gap-1.5">
                              <Cpu className="w-4 h-4 text-white" />
                              Preferred Core for {selectedSystem}
                            </label>
                            <p className="text-[11px] text-default-400 mt-1">
                              Select the Libretro core used to emulate {selectedSystem} games. All display, control, and performance options are managed globally.
                            </p>
                          </div>
                          <CustomSelect
                            value={systemPrefs.core || ''}
                            onChange={val => setSystemPrefs({ core: val })}
                            options={[
                              { value: '', label: `Default (${currentSystemObj.defaultCore || currentSystemObj.cores[0]?.id})` },
                              ...currentSystemObj.cores.map(c => ({ value: c.id, label: c.name || c.id }))
                            ]}
                          />
                        </div>
                      ) : (
                        <div className="p-4 rounded-xl bg-white/5 border border-white/10 text-neutral-400 text-xs">
                          No alternative cores available for {selectedSystem}. All preferences follow your Global Settings.
                        </div>
                      )}

                      <div className="flex items-center justify-end gap-3 pt-2">
                        <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.96 }}>
                          <Button
                            variant="flat"
                            disabled={saving}
                            startContent={<RotateCcw className="w-3.5 h-3.5" />}
                            onPress={handleResetSystem}
                            className="font-medium text-xs px-4 bg-white/10 hover:bg-white/20 text-white border border-white/15 cursor-pointer"
                          >
                            Reset {selectedSystem || 'System'} Core
                          </Button>
                        </motion.div>
                        <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.96 }}>
                          <Button
                            variant="solid"
                            isLoading={saving}
                            startContent={!saving ? <Save className="w-4 h-4 text-black" /> : null}
                            onPress={handleSaveSystem}
                            className="font-semibold text-xs px-5 bg-white hover:bg-neutral-200 text-black border border-white/40 shadow-lg cursor-pointer"
                          >
                            Save
                          </Button>
                        </motion.div>
                      </div>
                    </div>
                  )}
                </motion.div>
              </AnimatePresence>
            </Tab>

            {/* RetroAchievements */}
            <Tab
              key="ra"
              title={
                <div className="flex items-center gap-2">
                  <Trophy className="w-4 h-4 text-white" />
                  <span className="font-semibold text-xs sm:text-sm">RetroAchievements</span>
                </div>
              }
            >
              <AnimatePresence mode="wait">
                <motion.div
                  key="ra-content"
                  initial={{ opacity: 0, x: -8 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: 8 }}
                  transition={{ duration: 0.2 }}
                  className="space-y-6 py-4"
                >
                  <div className="p-4 rounded-xl bg-white/5 border border-white/15 text-neutral-300 text-xs space-y-1">
                    <p className="font-bold text-white flex items-center gap-1.5">
                      <Trophy className="w-4 h-4 text-white" />
                      RetroAchievements.org Account Link
                    </p>
                    <p className="text-neutral-400">
                      Connect your account to earn trophies, track retro gameplay statistics, and sync achievements directly in EmulatorJS.
                    </p>
                  </div>

                  <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
                    <div className="space-y-2">
                      <label className="text-xs font-semibold text-white">RA Username</label>
                      <input
                        type="text"
                        placeholder="Your RetroAchievements username"
                        value={raPrefs.username || ''}
                        onChange={e => setRaPrefs({ ...raPrefs, username: e.target.value })}
                        className="w-full px-3.5 py-2.5 rounded-xl bg-[#16161a] border border-white/15 text-xs text-white focus:outline-none focus:border-white/50 focus:ring-1 focus:ring-white/25 transition-colors"
                      />
                    </div>

                    <div className="space-y-2">
                      <label className="text-xs font-semibold text-white">Web API Key / Token</label>
                      <input
                        type="password"
                        placeholder="Found under RetroAchievements Settings -> Keys"
                        value={raPrefs.token || ''}
                        onChange={e => setRaPrefs({ ...raPrefs, token: e.target.value })}
                        className="w-full px-3.5 py-2.5 rounded-xl bg-[#16161a] border border-white/15 text-xs text-white focus:outline-none focus:border-white/50 focus:ring-1 focus:ring-white/25 transition-colors"
                      />
                    </div>
                  </div>

                  {raVerifyStatus && (
                    <motion.div
                      initial={{ opacity: 0, y: -6 }}
                      animate={{ opacity: 1, y: 0 }}
                      className={`p-3 rounded-xl flex items-center gap-2 text-xs font-medium ${
                        raVerifyStatus.success
                          ? 'bg-emerald-500/15 border border-emerald-500/30 text-emerald-300'
                          : 'bg-danger-500/15 border border-danger-500/30 text-danger-300'
                      }`}
                    >
                      {raVerifyStatus.success ? (
                        <CheckCircle2 className="w-4 h-4 shrink-0 text-emerald-400" />
                      ) : (
                        <AlertCircle className="w-4 h-4 shrink-0 text-danger-400" />
                      )}
                      <span>{raVerifyStatus.message}</span>
                    </motion.div>
                  )}

                  <div className="flex items-center justify-between pt-2">
                    <motion.div whileHover={{ scale: 1.03 }} whileTap={{ scale: 0.95 }}>
                      <Button
                        size="sm"
                        variant="flat"
                        isLoading={verifyingRa}
                        onPress={handleVerifyRa}
                        className="text-xs font-semibold bg-white/10 hover:bg-white/20 text-white border border-white/15 cursor-pointer"
                      >
                        Test Connection
                      </Button>
                    </motion.div>
                    <div className="flex items-center gap-3">
                      <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.96 }}>
                        <Button
                          variant="flat"
                          disabled={saving}
                          startContent={<RotateCcw className="w-3.5 h-3.5" />}
                          onPress={handleResetRa}
                          className="font-medium text-xs px-4 bg-white/10 hover:bg-white/20 text-white border border-white/15 cursor-pointer"
                        >
                          Clear Credentials
                        </Button>
                      </motion.div>
                      <motion.div whileHover={{ scale: 1.02 }} whileTap={{ scale: 0.96 }}>
                        <Button
                          variant="solid"
                          isLoading={saving}
                          startContent={!saving ? <Save className="w-4 h-4 text-black" /> : null}
                          onPress={handleSaveRa}
                          className="font-semibold text-xs px-5 bg-white hover:bg-neutral-200 text-black border border-white/40 shadow-lg cursor-pointer"
                        >
                          Save
                        </Button>
                      </motion.div>
                    </div>
                  </div>
                </motion.div>
              </AnimatePresence>
            </Tab>
          </Tabs>
        </CardBody>
      </Card>
    </motion.div>
  );
};
