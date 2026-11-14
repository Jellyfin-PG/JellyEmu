import React, { useState, useEffect } from 'react';
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
  Settings,
  Save,
  RotateCcw,
  Trash2,
  Check,
  Tv,
  Gamepad2,
  Activity,
  Volume2
} from 'lucide-react';
import { PlayerBridge } from '../services/playerBridge';
import { PlayerInitialData } from '../../types/player';
import { CustomSelect } from '../../shared/CustomSelect';

interface InGameSettingsModalProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
  config: PlayerInitialData;
}

export const InGameSettingsModal: React.FC<InGameSettingsModalProps> = ({
  isOpen,
  onOpenChange,
  config
}) => {
  const [activeTab, setActiveTab] = useState<string>('sys');
  const [statusMessage, setStatusMessage] = useState<string>('');

  // System Settings State (Core Selection)
  const [selectedCore, setSelectedCore] = useState<string>(config.core || '');
  const [availableCores, setAvailableCores] = useState<Array<{ id: string; name: string }>>([]);

  useEffect(() => {
    if (!isOpen) return;
    const tag = config.platformTag || config.systemName || '';
    if (tag) {
      PlayerBridge.fetchSystemCores(tag).then((cores) => {
        if (cores && cores.length > 0) {
          setAvailableCores(cores);
        } else if (config.availableCores && config.availableCores.length > 0) {
          setAvailableCores(config.availableCores.map((c) => ({ id: c.id, name: c.name || c.id })));
        }
      });
    } else if (config.availableCores && config.availableCores.length > 0) {
      setAvailableCores(config.availableCores.map((c) => ({ id: c.id, name: c.name || c.id })));
    }
  }, [isOpen, config.platformTag, config.systemName, config.availableCores]);

  // Global Settings State
  const [shader, setShader] = useState<string>(config.activeShader || 'crt-easymode.glslp');
  const [screenSize, setScreenSize] = useState<string>(config.scale || 'fit');
  const [rotation, setRotation] = useState<string>(String(config.videoRotation || '0'));
  const [fpsOverlay, setFpsOverlay] = useState<string>(config.showFps || '0');
  const [volume, setVolume] = useState<string>(config.volume !== undefined ? String(config.volume) : '1');
  const [isMuted, setIsMuted] = useState<boolean>(config.mute === '1' || String(config.mute) === 'true');
  const [ffRate, setFfRate] = useState<string>(config.ffrate || '3');
  const [smRate, setSmRate] = useState<string>(config.smrate || '3');
  const [autosave, setAutosave] = useState<boolean>(true);
  const [haptics, setHaptics] = useState<boolean>(true);

  const handleSaveSystem = async () => {
    if (!config.userId || !config.platformTag) return;
    setStatusMessage('');
    try {
      const prefs: Record<string, any> = {
        core: selectedCore || null
      };

      await PlayerBridge.saveSystemPreferences(config.userId, config.platformTag, prefs);

      setStatusMessage(`${config.platformTag || 'System'} core preference saved`);
      setTimeout(() => setStatusMessage(''), 3000);
    } catch {
      setStatusMessage('Failed to save system settings');
    }
  };

  const handleResetSystem = async () => {
    if (!config.userId || !config.platformTag) return;
    setStatusMessage('');
    try {
      await PlayerBridge.saveSystemPreferences(config.userId, config.platformTag, {});
      setSelectedCore(availableCores?.[0]?.id || config.availableCores?.[0]?.id || 'default');
      setStatusMessage(`${config.platformTag} core reset to default`);
      setTimeout(() => setStatusMessage(''), 3000);
    } catch {
      setStatusMessage('Failed to reset system settings');
    }
  };

  const handleSaveGlobal = async () => {
    if (!config.userId) return;
    setStatusMessage('');
    try {
      await PlayerBridge.saveGlobalPreferences(config.userId, {
        shader,
        scale: screenSize,
        videoRotation: rotation,
        showFps: fpsOverlay,
        volume,
        mute: isMuted ? '1' : '0',
        ffrate: ffRate,
        smrate: smRate,
        autosave: autosave ? '1' : '0',
        haptics: haptics ? '1' : '0'
      });

      // Apply live updates directly into running emulator
      PlayerBridge.applyShader(shader);
      PlayerBridge.applyScaling(screenSize);
      PlayerBridge.applyRotation(parseInt(rotation) || 0);
      PlayerBridge.applyFpsOverlay(fpsOverlay);
      PlayerBridge.applyFastForwardRate(ffRate);
      PlayerBridge.applySlowMotionRate(smRate);
      PlayerBridge.setVolume(isMuted ? 0 : parseFloat(volume) || 1);

      setStatusMessage('Global settings saved & applied live');
      setTimeout(() => setStatusMessage(''), 3000);
    } catch {
      setStatusMessage('Failed to save global settings');
    }
  };

  const handleDeleteCache = async () => {
    setStatusMessage('');
    const ok = await PlayerBridge.deleteCachedRom(config.itemId);
    if (ok) {
      setStatusMessage('Game ROM cache deleted');
      setTimeout(() => setStatusMessage(''), 3000);
    } else {
      setStatusMessage('Failed to delete cache');
    }
  };

  const shaderOptions = [
    { value: 'disabled', label: 'Disabled (None)' },
    { value: 'crt-easymode.glslp', label: 'CRT Easymode' },
    { value: '2xScaleHQ.glslp', label: '2x ScaleHQ' },
    { value: '4xScaleHQ.glslp', label: '4x ScaleHQ' },
    { value: 'crt-aperture.glslp', label: 'CRT Aperture' },
    { value: 'crt-geom.glslp', label: 'CRT Geom' },
    { value: 'crt-mattias.glslp', label: 'CRT Mattias' },
    { value: 'sabr', label: 'SABR' },
    { value: 'bicubic', label: 'Bicubic' },
    { value: 'mix-frames', label: 'Mix Frames' }
  ];

  const scaleOptions = [
    { value: 'fit', label: 'Fit Screen (Aspect Ratio)' },
    { value: 'stretch', label: 'Stretch to Fill' },
    { value: '1', label: '1x Native Resolution' },
    { value: '2', label: '2x Integer Scale' },
    { value: '3', label: '3x Integer Scale' },
    { value: '4', label: '4x Integer Scale' }
  ];

  const rotationOptions = [
    { value: '0', label: '0° (Standard Landscape)' },
    { value: '1', label: '90° (Clockwise / TATE)' },
    { value: '2', label: '180° (Inverted)' },
    { value: '3', label: '270° (Counter-Clockwise)' }
  ];

  const fpsOptions = [
    { value: '0', label: 'Off (Disabled)' },
    { value: '1', label: 'FPS Counter Only' },
    { value: '2', label: 'Detailed Performance Metrics' }
  ];

  const volumeOptions = [
    { value: '1', label: '100% (Default)' },
    { value: '0.9', label: '90%' },
    { value: '0.8', label: '80%' },
    { value: '0.7', label: '70%' },
    { value: '0.6', label: '60%' },
    { value: '0.5', label: '50%' },
    { value: '0.4', label: '40%' },
    { value: '0.3', label: '30%' },
    { value: '0.2', label: '20%' },
    { value: '0.1', label: '10%' },
    { value: '0', label: 'Muted (0%)' }
  ];

  const ffRateOptions = [
    { value: '2', label: '2x Speed' },
    { value: '3', label: '3x Speed' },
    { value: '4', label: '4x Speed' },
    { value: '5', label: '5x Speed' },
    { value: '8', label: '8x Speed' },
    { value: '10', label: '10x Speed' },
    { value: 'unlimited', label: 'Unlimited (Max)' }
  ];

  const smRateOptions = [
    { value: '2', label: '2x Slowdown' },
    { value: '3', label: '3x Slowdown' },
    { value: '4', label: '4x Slowdown' },
    { value: '5', label: '5x Slowdown' }
  ];

  return (
    <Modal
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      size="2xl"
      backdrop="blur"
      placement="center"
      scrollBehavior="inside"
      classNames={{
        base: 'bg-[#121216] border border-white/10 text-white shadow-2xl rounded-2xl max-w-2xl',
        header: 'border-b border-white/10 px-6 py-4 flex items-center justify-between',
        body: 'p-6',
        closeButton: 'text-neutral-400 hover:text-white hover:bg-white/10 p-1.5 rounded-lg'
      }}
    >
      <ModalContent>
        {() => (
          <>
            <ModalHeader>
              <div className="flex items-center gap-2">
                <Settings className="w-5 h-5 text-white" />
                <h2 className="text-base font-semibold tracking-wide">Settings</h2>
              </div>
            </ModalHeader>

            <ModalBody className="flex flex-col gap-5 max-h-[32rem] overflow-y-auto pr-1">
              {/* Tab Navigation */}
              <Tabs
                selectedKey={activeTab}
                onSelectionChange={(key) => setActiveTab(key as string)}
                variant="solid"
                classNames={{
                  tabList: 'bg-black/50 p-1 rounded-xl border border-white/10 w-full',
                  cursor: 'bg-white text-black rounded-lg shadow-sm',
                  tab: 'h-8 text-xs font-medium text-neutral-400 data-[selected=true]:text-black transition-colors',
                  tabContent: 'group-data-[selected=true]:text-black'
                }}
              >
                <Tab key="sys" title={`${config.platformTag || 'System'} Settings`} />
                <Tab key="glob" title="Global Settings" />
              </Tabs>

              {statusMessage && (
                <div className="flex items-center gap-2 px-3 py-2 rounded-lg text-xs bg-white/5 border border-white/10 text-neutral-300">
                  <Check className="w-4 h-4 text-emerald-400" />
                  <span>{statusMessage}</span>
                </div>
              )}

              {/* System Settings */}
              {activeTab === 'sys' && (
                <div className="flex flex-col gap-5">
                  <div className="flex items-center justify-between p-3.5 rounded-xl bg-black/40 border border-white/10">
                    <div className="flex flex-col">
                      <span className="text-xs font-semibold text-white">{config.platformTag || 'Platform'} Configuration</span>
                      <span className="text-[11px] text-neutral-400">Select the emulation core for this system. All display and audio settings follow Global Settings.</span>
                    </div>
                    <span className="text-[10px] px-2 py-0.5 rounded-md bg-white/10 border border-white/15 text-neutral-300 font-mono">
                      {selectedCore ? `Core: ${selectedCore}` : 'Default Core'}
                    </span>
                  </div>

                  {/* Core Selection */}
                  <div className="flex flex-col gap-1.5">
                    <label className="text-xs text-neutral-300 font-medium">Emulation Core</label>
                    <CustomSelect
                      value={selectedCore}
                      onChange={setSelectedCore}
                      options={(availableCores.length > 0
                        ? availableCores
                        : (config.availableCores || [{ id: config.core || 'default', name: config.core || 'Default' }])
                      ).map((c) => ({
                        value: c.id,
                        label: c.name || c.id
                      }))}
                      size="sm"
                    />
                    <span className="text-[11px] text-neutral-400">Select the Libretro core used to emulate {config.platformTag || 'this system'}.</span>
                  </div>

                  <div className="flex gap-2 mt-1">
                    <Button
                      size="sm"
                      variant="solid"
                      onPress={handleSaveSystem}
                      className="flex-1 h-8 text-xs bg-white text-black hover:bg-neutral-200 font-medium cursor-pointer"
                      startContent={<Save className="w-3.5 h-3.5" />}
                    >
                      Save Core Preference
                    </Button>
                    <Button
                      size="sm"
                      variant="flat"
                      onPress={handleResetSystem}
                      className="h-8 text-xs bg-white/5 hover:bg-white/10 text-neutral-300 border border-white/10 cursor-pointer"
                      startContent={<RotateCcw className="w-3.5 h-3.5" />}
                    >
                      Reset to Default
                    </Button>
                  </div>

                  {/* ROM Cache Section */}
                  <div className="text-xs font-semibold text-neutral-400 uppercase tracking-wider mt-2">
                    Game Cache
                  </div>

                  <div className="flex items-center justify-between p-3.5 rounded-xl bg-black/40 border border-white/10">
                    <div className="flex flex-col pr-3">
                      <span className="text-xs font-medium text-white">Delete Cached ROM</span>
                      <span className="text-[11px] text-neutral-400 mt-0.5">
                        Forces re-download from server on next launch
                      </span>
                    </div>
                    <Button
                      size="sm"
                      variant="flat"
                      onPress={handleDeleteCache}
                      className="h-7 px-3 text-xs bg-red-500/10 hover:bg-red-500/20 text-red-400 border border-red-500/20 cursor-pointer"
                      startContent={<Trash2 className="w-3.5 h-3.5" />}
                    >
                      Delete Cache
                    </Button>
                  </div>
                </div>
              )}

              {/* Global Settings */}
              {activeTab === 'glob' && (
                <div className="flex flex-col gap-5">
                  <div className="space-y-3">
                    <div className="text-xs font-semibold text-neutral-400 uppercase tracking-wider flex items-center gap-1.5">
                      <Tv className="w-3.5 h-3.5" />
                      Display & Audio Defaults
                    </div>

                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                      {/* Shader */}
                      <div className="flex flex-col gap-1.5">
                        <label className="text-xs text-neutral-300 font-medium">Default Shader Filter</label>
                        <CustomSelect
                          value={shader}
                          onChange={setShader}
                          options={shaderOptions}
                          size="sm"
                        />
                      </div>

                      {/* Scaling */}
                      <div className="flex flex-col gap-1.5">
                        <label className="text-xs text-neutral-300 font-medium">Screen Scaling Mode</label>
                        <CustomSelect
                          value={screenSize}
                          onChange={setScreenSize}
                          options={scaleOptions}
                          size="sm"
                        />
                      </div>

                      {/* Rotation */}
                      <div className="flex flex-col gap-1.5">
                        <label className="text-xs text-neutral-300 font-medium">Screen Rotation</label>
                        <CustomSelect
                          value={rotation}
                          onChange={setRotation}
                          options={rotationOptions}
                          size="sm"
                        />
                      </div>

                      {/* Performance Overlay (FPS Level) */}
                      <div className="flex flex-col gap-1.5">
                        <label className="text-xs text-neutral-300 font-medium flex items-center gap-1">
                          <Activity className="w-3.5 h-3.5 text-neutral-400" />
                          Performance Overlay (FPS Display)
                        </label>
                        <CustomSelect
                          value={fpsOverlay}
                          onChange={setFpsOverlay}
                          options={fpsOptions}
                          size="sm"
                        />
                      </div>

                      {/* Volume */}
                      <div className="flex flex-col gap-1.5">
                        <label className="text-xs text-neutral-300 font-medium flex items-center gap-1">
                          <Volume2 className="w-3.5 h-3.5 text-neutral-400" />
                          Default Sound Volume
                        </label>
                        <CustomSelect
                          value={volume}
                          onChange={setVolume}
                          options={volumeOptions}
                          size="sm"
                        />
                      </div>

                      {/* Audio Mute */}
                      <div className="flex items-center justify-between p-3 rounded-xl bg-black/30 border border-white/10 self-end h-[38px]">
                        <span className="text-xs font-medium text-white">Default Audio Mute</span>
                        <Switch
                          size="sm"
                          isSelected={isMuted}
                          onValueChange={setIsMuted}
                          classNames={{
                            wrapper: 'bg-white/20 group-data-[selected=true]:bg-white',
                            thumb: 'bg-black'
                          }}
                        />
                      </div>
                    </div>
                  </div>

                  {/* Speed & Controls */}
                  <div className="space-y-3">
                    <div className="text-xs font-semibold text-neutral-400 uppercase tracking-wider flex items-center gap-1.5">
                      <Gamepad2 className="w-3.5 h-3.5" />
                      Performance, Emulation & Controls
                    </div>

                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                      <div className="flex flex-col gap-1.5">
                        <label className="text-xs text-neutral-300 font-medium">Fast Forward Speed</label>
                        <CustomSelect
                          value={ffRate}
                          onChange={setFfRate}
                          options={ffRateOptions}
                          size="sm"
                        />
                      </div>

                      <div className="flex flex-col gap-1.5">
                        <label className="text-xs text-neutral-300 font-medium">Slow Motion Speed</label>
                        <CustomSelect
                          value={smRate}
                          onChange={setSmRate}
                          options={smRateOptions}
                          size="sm"
                        />
                      </div>
                    </div>

                    {/* Toggles */}
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5 pt-1">
                      <div className="flex items-center justify-between p-3 rounded-xl bg-black/30 border border-white/10">
                        <span className="text-xs font-medium text-white">Autosave on Exit</span>
                        <Switch
                          size="sm"
                          isSelected={autosave}
                          onValueChange={setAutosave}
                          classNames={{
                            wrapper: 'bg-white/20 group-data-[selected=true]:bg-white',
                            thumb: 'bg-black'
                          }}
                        />
                      </div>

                      <div className="flex items-center justify-between p-3 rounded-xl bg-black/30 border border-white/10">
                        <span className="text-xs font-medium text-white">Gamepad Haptics / Rumble</span>
                        <Switch
                          size="sm"
                          isSelected={haptics}
                          onValueChange={setHaptics}
                          classNames={{
                            wrapper: 'bg-white/20 group-data-[selected=true]:bg-white',
                            thumb: 'bg-black'
                          }}
                        />
                      </div>
                    </div>
                  </div>

                  <div className="flex gap-2 mt-2">
                    <Button
                      size="sm"
                      variant="solid"
                      onPress={handleSaveGlobal}
                      className="flex-1 h-8 text-xs bg-white text-black hover:bg-neutral-200 font-medium cursor-pointer"
                      startContent={<Save className="w-3.5 h-3.5" />}
                    >
                      Save Global Settings
                    </Button>
                    <Button
                      size="sm"
                      variant="flat"
                      onPress={() => {
                        setShader('crt-easymode.glslp');
                        setScreenSize('fit');
                        setRotation('0');
                        setFpsOverlay('0');
                        setVolume('1');
                        setIsMuted(false);
                        setFfRate('3');
                        setSmRate('3');
                        setAutosave(true);
                        setHaptics(true);
                        setStatusMessage('Global settings reset in form');
                      }}
                      className="h-8 text-xs bg-white/5 hover:bg-white/10 text-neutral-300 border border-white/10 cursor-pointer"
                      startContent={<RotateCcw className="w-3.5 h-3.5" />}
                    >
                      Reset Form
                    </Button>
                  </div>
                </div>
              )}
            </ModalBody>
          </>
        )}
      </ModalContent>
    </Modal>
  );
};

