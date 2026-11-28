import React, { useEffect, useRef, useState } from 'react';
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
import { PlayerPerformanceOverlay } from './components/PlayerPerformanceOverlay';
import { PlayerBridge } from './services/playerBridge';

interface PlayerAppProps {
  initialData: PlayerInitialData;
}

const KEYBOARD_PASSTHROUGH_SYSTEMS = new Set([
  'dos',
  'amiga',
  'c64',
  'pc88',
  'x1',
  'thomson',
  'mac',
  'palmos',
  'ti83',
  'bk',
  'galaksija',
  'p2000',
  'scummvm',
  'flash',
  'love',
  'chailove',
  'craft',
  'doom',
  'quake',
  'quake2',
  'ecwolf'
]);

export const PlayerApp: React.FC<PlayerAppProps> = ({ initialData }) => {
  const [supportsCheats, setSupportsCheats] = useState<boolean | undefined>(undefined);
  const [supportsSaveStates, setSupportsSaveStates] = useState<boolean | undefined>(undefined);
  const [supportsSram, setSupportsSram] = useState<boolean | undefined>(undefined);

  useEffect(() => {
    PlayerBridge.init(initialData);

    let booted = false;
    const attemptBoot = async () => {
      if (booted || PlayerBridge.getEngine()) return;
      const canvas = (document.getElementById('retrokit-canvas') ||
        document.querySelector('canvas.ejs_canvas') ||
        document.querySelector('#game canvas') ||
        document.querySelector('canvas')) as HTMLCanvasElement | null;

      if (canvas && initialData.romUrl) {
        booted = true;
        try {
          const emu = await PlayerBridge.boot(canvas, initialData);
          const caps = emu.capabilities;
          if (caps) {
            setSupportsCheats(caps.cheats);
            setSupportsSaveStates(caps.saveStates);
            setSupportsSram(caps.sram);
          }
        } catch (err) {
          console.error('[PlayerApp] RetroKit boot error:', err);
        }
      }
    };

    attemptBoot();
    const timer = setTimeout(attemptBoot, 300);
    return () => clearTimeout(timer);
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
    if (!anyModalOpen) {
      const canvas = document.getElementById('retrokit-canvas') as HTMLElement | null;
      if (canvas) {
        requestAnimationFrame(() => canvas.focus({ preventScroll: true }));
      }
    }
  }, [anyModalOpen, setHasOpenModal]);

  // Ensure clicking/tapping anywhere on the game screen always focuses #retrokit-canvas and blurs any dock/topbar button
  useEffect(() => {
    const canvas = document.getElementById('retrokit-canvas') as HTMLElement | null;
    if (!canvas) return;
    if (!canvas.hasAttribute('tabindex')) {
      canvas.setAttribute('tabindex', '0');
    }
    const handlePointerDown = (e: PointerEvent) => {
      const target = e.target as HTMLElement | null;
      if (target && (target.closest('header') || target.closest('[data-player-dock]') || target.closest('[role="dialog"]'))) {
        return;
      }
      if (document.activeElement && document.activeElement !== canvas) {
        (document.activeElement as HTMLElement).blur?.();
      }
      canvas.focus({ preventScroll: true });
    };
    document.addEventListener('pointerdown', handlePointerDown, true);
    canvas.focus({ preventScroll: true });
    return () => {
      document.removeEventListener('pointerdown', handlePointerDown, true);
    };
  }, []);

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

  // Global hotkey listener for in-game shortcuts (registered once in capture phase so it stays ahead of RetroKit InputManager)
  const hotkeyCtxRef = useRef({
    isPlay,
    anyModalOpen,
    inputMapDisclosure,
    togglePause,
    toggleMute,
    toggleFastForward,
    toggleSlowMotion,
    initialData
  });
  hotkeyCtxRef.current = {
    isPlay,
    anyModalOpen,
    inputMapDisclosure,
    togglePause,
    toggleMute,
    toggleFastForward,
    toggleSlowMotion,
    initialData
  };

  useEffect(() => {
    let activeRewindHotkey = false;

    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e as any)._isRetroKitSynthetic) return;
      const target = e.target as HTMLElement | null;
      if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT' || target.isContentEditable)) {
        return;
      }
      const ctx = hotkeyCtxRef.current;
      if (ctx.anyModalOpen) {
        return;
      }

      // If focus is stuck on a dock/header button (outside a popover/dialog), return focus to the game canvas
      const activeEl = document.activeElement as HTMLElement | null;
      if (activeEl && activeEl.tagName === 'BUTTON' && !activeEl.closest('[role="dialog"]')) {
        activeEl.blur();
        const canvas = document.getElementById('retrokit-canvas') as HTMLElement | null;
        canvas?.focus({ preventScroll: true });
      }

      const sysTag = (ctx.initialData.platformTag || '').toLowerCase();
      const isKeyboardPassthrough = KEYBOARD_PASSTHROUGH_SYSTEMS.has(sysTag);
      const isPlainTypingKey =
        !e.ctrlKey &&
        !e.altKey &&
        !e.metaKey &&
        (e.key.length === 1 || e.code === 'Space' || e.code === 'Backspace');

      const hotkeyId =
        isKeyboardPassthrough && isPlainTypingKey
          ? null
          : PlayerBridge.matchHotkey(e);
      if (hotkeyId !== null) {
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();

        if (e.repeat && hotkeyId !== 28) return;

        switch (hotkeyId) {
          case 24: {
            const slot = PlayerBridge.getActiveSlot();
            if (ctx.initialData.itemId && ctx.initialData.userId) {
              PlayerBridge.saveState(ctx.initialData.itemId, ctx.initialData.userId, slot).then((ok) => {
                PlayerBridge.notify({
                  message: ok ? `Quick Saved (Slot ${slot})` : `Quick Save Failed (Slot ${slot})`,
                  icon: 'save',
                  variant: ok ? 'success' : 'danger'
                });
              });
            }
            return;
          }
          case 25: {
            const slot = PlayerBridge.getActiveSlot();
            if (ctx.initialData.itemId && ctx.initialData.userId) {
              PlayerBridge.loadState(ctx.initialData.itemId, ctx.initialData.userId, slot).then((ok) => {
                PlayerBridge.notify({
                  message: ok ? `Quick Loaded (Slot ${slot})` : `No Save in Slot ${slot}`,
                  icon: 'save',
                  variant: ok ? 'success' : 'warning'
                });
              });
            }
            return;
          }
          case 26: {
            const nextSlot = PlayerBridge.cycleActiveSlot();
            PlayerBridge.notify({ message: `Save Slot ${nextSlot}`, icon: 'save', variant: 'accent' });
            return;
          }
          case 27:
            ctx.toggleFastForward();
            return;
          case 28:
            if (!activeRewindHotkey) {
              activeRewindHotkey = true;
              PlayerBridge.startRewind();
            }
            return;
          case 29:
            ctx.toggleSlowMotion();
            return;
          case 30:
            PlayerBridge.exit();
            return;
          case 31:
            ctx.togglePause();
            return;
          case 32:
            PlayerBridge.restart();
            return;
          case 33:
            ctx.toggleMute();
            return;
          case 34:
            PlayerBridge.takeScreenshot(ctx.initialData.itemId, ctx.initialData.gameName);
            return;
          case 35:
            PlayerBridge.toggleFullscreen();
            return;
        }
      }

      // Hotkeys that shouldn't clash with PS2 or computer/DOS letter keys
      if (!ctx.isPlay && !isKeyboardPassthrough) {
        // H or ? -> Open hotkeys tab in input mapping modal
        if ((e.key === 'h' || e.key === 'H' || e.key === '?') && !e.ctrlKey && !e.altKey && !e.metaKey) {
          e.preventDefault();
          e.stopPropagation();
          e.stopImmediatePropagation();
          setInputMapTab('hotkeys');
          ctx.inputMapDisclosure.onOpen();
          PlayerBridge.notify({ message: 'Input Mapping', subtext: 'Hotkeys overview opened', icon: 'gamepad', variant: 'accent' });
          return;
        }
      }
    };

    const handleKeyUp = (e: KeyboardEvent) => {
      if ((e as any)._isRetroKitSynthetic) return;
      if (activeRewindHotkey) {
        const hotkeyId = PlayerBridge.matchHotkey(e);
        if (hotkeyId === 28) {
          e.preventDefault();
          e.stopPropagation();
          e.stopImmediatePropagation();
          activeRewindHotkey = false;
          PlayerBridge.stopRewind();
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown, true);
    window.addEventListener('keyup', handleKeyUp, true);
    return () => {
      window.removeEventListener('keydown', handleKeyDown, true);
      window.removeEventListener('keyup', handleKeyUp, true);
    };
  }, []);

  return (
    <HeroUIProvider>
      <div className="dark text-foreground pointer-events-none select-none">
        {/* Real-time Performance HUD Overlay */}
        <PlayerPerformanceOverlay initialMode={initialData.showFps} />

        {/* In-game OSD Hotkey & Action Notification Pill */}
        <PlayerNotification topBarVisible={visible} />

        {/* Top Bar */}
        <PlayerTopBar
          title={initialData.gameName}
          systemName={initialData.systemName || initialData.platformTag}
          visible={visible && !anyModalOpen}
          isPlay={isPlay}
          isPico8={isPico8}
          isNetplayActive={isPlay || isPico8 ? false : isNetplayActive}
          onOpenNetplay={isPlay || isPico8 ? undefined : netplayDisclosure.onOpen}
          isVrSupported={isVrSupported}
          isArSupported={isArSupported}
        />

        {/* Bottom Dock */}
        <PlayerBottomDock
          visible={visible && !anyModalOpen}
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
          supportsCheats={supportsCheats}
          supportsSaveStates={supportsSaveStates}
          supportsSram={supportsSram}
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

        {/* Saves & States Modal (Shown whenever core supports saveStates or sram) */}
        {(supportsSaveStates || supportsSram || (!isPlay && !isPico8)) && savesDisclosure.isOpen && (
          <InGameSavesModal
            itemId={initialData.itemId}
            userId={initialData.userId}
            gameName={initialData.gameName}
            supportsSaveStates={supportsSaveStates ?? (!isPlay && !isPico8)}
            supportsSram={supportsSram ?? true}
            isOpen={savesDisclosure.isOpen}
            onOpenChange={savesDisclosure.onOpenChange}
          />
        )}

        {/* Settings Modal */}
        {settingsDisclosure.isOpen && (
          <InGameSettingsModal
            config={initialData}
            isOpen={settingsDisclosure.isOpen}
            onOpenChange={settingsDisclosure.onOpenChange}
          />
        )}

        {/* Cheats Modal (Not supported on PS2 or PICO-8) */}
        {!isPlay && !isPico8 && cheatsDisclosure.isOpen && (
          <InGameCheatsModal
            itemId={initialData.itemId}
            isOpen={cheatsDisclosure.isOpen}
            onOpenChange={cheatsDisclosure.onOpenChange}
          />
        )}

        {/* Input Mapping Modal */}
        {inputMapDisclosure.isOpen && (
          <InGameInputMapModal
            config={initialData}
            isOpen={inputMapDisclosure.isOpen}
            onOpenChange={inputMapDisclosure.onOpenChange}
            initialTab={inputMapTab}
          />
        )}

        {/* Netplay Modal (Not supported on PS2 or PICO-8) */}
        {!isPlay && !isPico8 && netplayDisclosure.isOpen && (
          <InGameNetplayModal
            config={initialData}
            isOpen={netplayDisclosure.isOpen}
            onOpenChange={netplayDisclosure.onOpenChange}
          />
        )}

        {/* Core Options Modal (Not supported on PS2 or PICO-8) */}
        {!isPlay && !isPico8 && coreOptsDisclosure.isOpen && (
          <InGameCoreOptionsModal
            isOpen={coreOptsDisclosure.isOpen}
            onOpenChange={coreOptsDisclosure.onOpenChange}
          />
        )}

        {/* Multi-Disc Modal */}
        {discSelectDisclosure.isOpen && (
          <InGameDiscSelectModal
            isOpen={discSelectDisclosure.isOpen}
            onOpenChange={discSelectDisclosure.onOpenChange}
          />
        )}
      </div>
    </HeroUIProvider>
  );
};
