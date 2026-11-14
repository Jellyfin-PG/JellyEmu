import React, { useEffect, useState } from 'react';
import { HeroUIProvider, useDisclosure } from '@heroui/react';
import { PlayerInitialData } from '../types/player';
import { usePlayerHUD } from './hooks/usePlayerHUD';
import { PlayerTopBar } from './components/PlayerTopBar';
import { PlayerBottomDock } from './components/PlayerBottomDock';
import { InGameSavesModal } from './components/InGameSavesModal';
import { InGameSettingsModal } from './components/InGameSettingsModal';
import { InGameCheatsModal } from './components/InGameCheatsModal';
import { InGameInputMapModal } from './components/InGameInputMapModal';
import { InGameNetplayModal } from './components/InGameNetplayModal';
import { InGameCoreOptionsModal } from './components/InGameCoreOptionsModal';
import { InGameDiscSelectModal } from './components/InGameDiscSelectModal';
import { PlayerNotification } from './components/PlayerNotification';
import { PlayerBridge } from './services/playerBridge';

interface PlayerAppProps {
  initialData: PlayerInitialData;
}

export const PlayerApp: React.FC<PlayerAppProps> = ({ initialData }) => {
  useEffect(() => {
    PlayerBridge.init(initialData);
  }, [initialData]);

  const {
    visible,
    isMinimized,
    setIsMinimized,
    volume,
    setVolume,
    isMuted,
    toggleMute,
    isPaused,
    togglePause,
    isFastForward,
    toggleFastForward,
    isSlowMotion,
    toggleSlowMotion,
    isFullscreen,
    isNetplayActive,
    isVrSupported,
    isArSupported,
    setHasOpenModal
  } = usePlayerHUD();

  // Modals disclosures
  const savesDisclosure = useDisclosure();
  const settingsDisclosure = useDisclosure();
  const cheatsDisclosure = useDisclosure();
  const inputMapDisclosure = useDisclosure();
  const [inputMapTab, setInputMapTab] = useState<string>('p0');
  const netplayDisclosure = useDisclosure();
  const coreOptsDisclosure = useDisclosure();
  const discSelectDisclosure = useDisclosure();

  // Track if any modal is open to keep HUD visible
  const anyModalOpen =
    savesDisclosure.isOpen ||
    settingsDisclosure.isOpen ||
    cheatsDisclosure.isOpen ||
    inputMapDisclosure.isOpen ||
    netplayDisclosure.isOpen ||
    coreOptsDisclosure.isOpen ||
    discSelectDisclosure.isOpen;

  useEffect(() => {
    setHasOpenModal(anyModalOpen);
  }, [anyModalOpen, setHasOpenModal]);

  const isPlay = Boolean(
    initialData.isPlay ||
    initialData.core === 'play' ||
    initialData.platformTag?.toLowerCase() === 'ps2' ||
    initialData.systemName?.toLowerCase().includes('playstation 2')
  );

  const isPico8 = Boolean(
    initialData.isPico8 ||
    initialData.core?.toLowerCase() === 'pico8' ||
    initialData.platformTag?.toLowerCase() === 'pico8' ||
    initialData.platformTag?.toLowerCase() === 'p8' ||
    initialData.systemName?.toLowerCase().includes('pico')
  );

  // Global hotkey listener for in-game shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT')) {
        return;
      }
      if (anyModalOpen) {
        return;
      }

      // Escape -> Exit game (available across all systems)
      if (e.key === 'Escape') {
        e.preventDefault();
        PlayerBridge.exit();
        return;
      }

      // Space -> Toggle Pause/Resume (available across all systems)
      if (e.code === 'Space' && !e.ctrlKey && !e.altKey && !e.metaKey) {
        e.preventDefault();
        togglePause();
        return;
      }

      // F4 -> Restart (available across all systems)
      if (e.key === 'F4') {
        e.preventDefault();
        PlayerBridge.restart();
        return;
      }

      // M -> Toggle Mute (available across all systems)
      if ((e.key === 'm' || e.key === 'M') && !e.ctrlKey && !e.altKey && !e.metaKey) {
        e.preventDefault();
        toggleMute();
        return;
      }

      // F12 -> Screenshot (available across all systems)
      if (e.key === 'F12') {
        e.preventDefault();
        PlayerBridge.takeScreenshot(initialData.itemId, initialData.gameName);
        return;
      }

      // Hotkeys that shouldn't clash with PS2 letter keys
      if (!isPlay) {
        // H or ? -> Open hotkeys tab in input mapping modal
        if ((e.key === 'h' || e.key === 'H' || e.key === '?') && !e.ctrlKey && !e.altKey && !e.metaKey) {
          e.preventDefault();
          setInputMapTab('hotkeys');
          inputMapDisclosure.onOpen();
          PlayerBridge.notify({ message: 'Input Mapping', subtext: 'Hotkeys overview opened', icon: 'gamepad', variant: 'accent' });
          return;
        }

        // F -> Fullscreen toggle
        if ((e.key === 'f' || e.key === 'F') && !e.ctrlKey && !e.altKey && !e.metaKey) {
          e.preventDefault();
          PlayerBridge.toggleFullscreen();
          return;
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [isPlay, anyModalOpen, inputMapDisclosure, togglePause, toggleMute, initialData]);

  return (
    <HeroUIProvider>
      <div className="dark text-foreground pointer-events-none select-none">
        {/* In-game OSD Hotkey & Action Notification Pill */}
        <PlayerNotification topBarVisible={visible} />

        {/* Top Bar */}
        <PlayerTopBar
          title={initialData.gameName}
          systemName={initialData.systemName || initialData.platformTag}
          visible={visible}
          isPlay={isPlay}
          isPico8={isPico8}
          isNetplayActive={isPlay || isPico8 ? false : isNetplayActive}
          onOpenNetplay={isPlay || isPico8 ? undefined : netplayDisclosure.onOpen}
          isVrSupported={isVrSupported}
          isArSupported={isArSupported}
        />

        {/* Bottom Dock */}
        <PlayerBottomDock
          visible={visible}
          isMinimized={isMinimized}
          onToggleMinimize={() => setIsMinimized(!isMinimized)}
          volume={volume}
          isMuted={isMuted}
          onVolumeChange={setVolume}
          onToggleMute={toggleMute}
          isPaused={isPaused}
          onTogglePause={togglePause}
          isFastForward={isFastForward}
          onToggleFastForward={toggleFastForward}
          isSlowMotion={isSlowMotion}
          onToggleSlowMotion={toggleSlowMotion}
          isFullscreen={isFullscreen}
          isM3u={initialData.isM3u}
          isPlay={isPlay}
          isPico8={isPico8}
          itemId={initialData.itemId}
          gameName={initialData.gameName}
          onOpenSaves={savesDisclosure.onOpen}
          onOpenSettings={settingsDisclosure.onOpen}
          onOpenCheats={cheatsDisclosure.onOpen}
          onOpenInputMap={() => {
            setInputMapTab('p0');
            inputMapDisclosure.onOpen();
          }}
          onOpenNetplay={netplayDisclosure.onOpen}
          onOpenCoreOpts={coreOptsDisclosure.onOpen}
          onOpenDiscSelect={discSelectDisclosure.onOpen}
        />

        {/* Saves & States Modal (Only on systems supporting save states) */}
        {!isPlay && !isPico8 && (
          <InGameSavesModal
            itemId={initialData.itemId}
            userId={initialData.userId}
            gameName={initialData.gameName}
            isOpen={savesDisclosure.isOpen}
            onOpenChange={savesDisclosure.onOpenChange}
          />
        )}

        {/* Settings Modal */}
        <InGameSettingsModal
          config={initialData}
          isOpen={settingsDisclosure.isOpen}
          onOpenChange={settingsDisclosure.onOpenChange}
        />

        {/* Cheats Modal (Not supported on PS2 or PICO-8) */}
        {!isPlay && !isPico8 && (
          <InGameCheatsModal
            itemId={initialData.itemId}
            isOpen={cheatsDisclosure.isOpen}
            onOpenChange={cheatsDisclosure.onOpenChange}
          />
        )}

        {/* Input Mapping Modal */}
        <InGameInputMapModal
          config={initialData}
          isOpen={inputMapDisclosure.isOpen}
          onOpenChange={inputMapDisclosure.onOpenChange}
          initialTab={inputMapTab}
        />

        {/* Netplay Modal (Not supported on PS2 or PICO-8) */}
        {!isPlay && !isPico8 && (
          <InGameNetplayModal
            config={initialData}
            isOpen={netplayDisclosure.isOpen}
            onOpenChange={netplayDisclosure.onOpenChange}
          />
        )}

        {/* Core Options Modal (Not supported on PS2 or PICO-8) */}
        {!isPlay && !isPico8 && (
          <InGameCoreOptionsModal
            isOpen={coreOptsDisclosure.isOpen}
            onOpenChange={coreOptsDisclosure.onOpenChange}
          />
        )}

        {/* Multi-Disc Modal */}
        <InGameDiscSelectModal
          isOpen={discSelectDisclosure.isOpen}
          onOpenChange={discSelectDisclosure.onOpenChange}
        />
      </div>
    </HeroUIProvider>
  );
};
