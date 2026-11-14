import React from 'react';
import {
  Button,
  Popover,
  PopoverTrigger,
  PopoverContent,
  Slider,
  Tooltip
} from '@heroui/react';
import {
  Play,
  Pause,
  RotateCcw,
  FastForward,
  Rewind,
  Save,
  Volume2,
  VolumeX,
  Sparkles,
  Gamepad2,
  Globe,
  Camera,
  Settings,
  Sliders,
  Disc,
  Maximize2,
  Minimize2,
  ChevronDown,
  ChevronUp
} from 'lucide-react';
import { PlayerBridge } from '../services/playerBridge';

interface PlayerBottomDockProps {
  visible: boolean;
  isMinimized: boolean;
  onToggleMinimize: () => void;
  volume: number;
  isMuted: boolean;
  onVolumeChange: (val: number) => void;
  onToggleMute: () => void;
  isPaused: boolean;
  onTogglePause: () => void;
  isFastForward: boolean;
  onToggleFastForward: () => void;
  isSlowMotion: boolean;
  onToggleSlowMotion: () => void;
  isFullscreen: boolean;
  isM3u?: boolean;
  isPlay?: boolean;
  isPico8?: boolean;
  itemId?: string;
  gameName?: string;
  onOpenSaves: () => void;
  onOpenSettings: () => void;
  onOpenCheats: () => void;
  onOpenInputMap: () => void;
  onOpenNetplay: () => void;
  onOpenCoreOpts: () => void;
  onOpenDiscSelect: () => void;
}

export const PlayerBottomDock: React.FC<PlayerBottomDockProps> = ({
  visible,
  isMinimized,
  onToggleMinimize,
  volume,
  isMuted,
  onVolumeChange,
  onToggleMute,
  isPaused,
  onTogglePause,
  isFastForward,
  onToggleFastForward,
  isSlowMotion,
  onToggleSlowMotion,
  isFullscreen,
  isM3u = false,
  isPlay = false,
  isPico8 = false,
  itemId = '',
  gameName = '',
  onOpenSaves,
  onOpenSettings,
  onOpenCheats,
  onOpenInputMap,
  onOpenNetplay,
  onOpenCoreOpts,
  onOpenDiscSelect
}) => {
  return (
    <>
      {/* Minimized Expand FAB */}
      {isMinimized && (
        <div className="fixed bottom-4 left-1/2 -translate-x-1/2 z-40 pointer-events-auto">
          <Button
            isIconOnly
            size="sm"
            onPress={onToggleMinimize}
            title="Expand Controls"
            className="w-10 h-10 rounded-full bg-black/90 hover:bg-neutral-900 border border-white/20 text-white shadow-xl backdrop-blur-md transition-all"
          >
            <ChevronUp className="w-5 h-5" />
          </Button>
        </div>
      )}

      {/* Floating Bottom Dock */}
      <div
        className={`fixed bottom-4 left-1/2 -translate-x-1/2 z-40 transition-all duration-300 ease-in-out pointer-events-auto ${
          visible && !isMinimized
            ? 'translate-y-0 opacity-100'
            : 'translate-y-16 opacity-0 pointer-events-none'
        }`}
      >
        <div className="flex items-center gap-1.5 p-1.5 rounded-2xl bg-black/90 backdrop-blur-md border border-white/10 shadow-2xl text-white">
          {/* Play / Pause Toggle */}
          <Tooltip content={isPaused ? 'Resume' : 'Pause'} placement="top">
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={onTogglePause}
              className="w-9 h-9 min-w-9 text-neutral-300 hover:text-white hover:bg-white/10 rounded-xl"
            >
              {isPaused ? <Play className="w-4 h-4 text-emerald-400" /> : <Pause className="w-4 h-4" />}
            </Button>
          </Tooltip>

          {/* Restart */}
          <Tooltip content="Restart Game" placement="top">
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => PlayerBridge.restart()}
              className="w-9 h-9 min-w-9 text-neutral-300 hover:text-white hover:bg-white/10 rounded-xl"
            >
              <RotateCcw className="w-4 h-4" />
            </Button>
          </Tooltip>

          {!isPlay && !isPico8 && (
            <>
              {/* Separator */}
              <div className="w-[1px] h-5 bg-white/10 mx-0.5" />

              {/* Fast Forward */}
              <Tooltip content="Fast Forward" placement="top">
                <Button
                  isIconOnly
                  size="sm"
                  variant="light"
                  onPress={onToggleFastForward}
                  className={`w-9 h-9 min-w-9 rounded-xl transition-colors ${
                    isFastForward
                      ? 'bg-white text-black hover:bg-neutral-200'
                      : 'text-neutral-300 hover:text-white hover:bg-white/10'
                  }`}
                >
                  <FastForward className="w-4 h-4" />
                </Button>
              </Tooltip>

              {/* Slow Motion */}
              <Tooltip content="Slow Motion" placement="top">
                <Button
                  isIconOnly
                  size="sm"
                  variant="light"
                  onPress={onToggleSlowMotion}
                  className={`w-9 h-9 min-w-9 rounded-xl transition-colors ${
                    isSlowMotion
                      ? 'bg-white text-black hover:bg-neutral-200'
                      : 'text-neutral-300 hover:text-white hover:bg-white/10'
                  }`}
                >
                  <Rewind className="w-4 h-4" />
                </Button>
              </Tooltip>
            </>
          )}

          {/* Separator */}
          <div className="w-[1px] h-5 bg-white/10 mx-0.5" />

          {/* Saves & States */}
          {!isPlay && !isPico8 && (
            <Tooltip content="Saves & States" placement="top">
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={onOpenSaves}
                className="w-9 h-9 min-w-9 text-neutral-300 hover:text-white hover:bg-white/10 rounded-xl"
              >
                <Save className="w-4 h-4" />
              </Button>
            </Tooltip>
          )}

          {/* Volume Popover */}
          <Popover placement="top" offset={12}>
            <PopoverTrigger>
              <Button
                isIconOnly
                size="sm"
                variant="light"
                title="Audio Volume"
                className="w-9 h-9 min-w-9 text-neutral-300 hover:text-white hover:bg-white/10 rounded-xl"
              >
                {isMuted || volume === 0 ? (
                  <VolumeX className="w-4 h-4 text-neutral-500" />
                ) : (
                  <Volume2 className="w-4 h-4" />
                )}
              </Button>
            </PopoverTrigger>
            <PopoverContent className="bg-neutral-900 border border-white/10 p-3 rounded-xl shadow-2xl text-white min-w-[220px]">
              <div className="flex flex-col gap-3 w-full">
                <div className="flex items-center justify-between text-xs text-neutral-300 font-medium">
                  <span>Audio Volume</span>
                  <span>{isMuted ? 'Muted' : `${volume}%`}</span>
                </div>
                <Slider
                  size="sm"
                  step={1}
                  maxValue={100}
                  minValue={0}
                  aria-label="Volume Slider"
                  value={isMuted ? 0 : volume}
                  onChange={(val) => onVolumeChange(val as number)}
                  classNames={{
                    track: 'bg-white/10',
                    filler: 'bg-white',
                    thumb: 'bg-white shadow-md'
                  }}
                />
                <Button
                  size="sm"
                  variant="flat"
                  onPress={onToggleMute}
                  className="w-full h-8 text-xs bg-white/10 hover:bg-white/15 text-white border border-white/10"
                >
                  {isMuted ? 'Unmute Audio' : 'Mute Audio'}
                </Button>
              </div>
            </PopoverContent>
          </Popover>

          {/* Cheats */}
          {!isPlay && !isPico8 && (
            <Tooltip content="Cheats" placement="top">
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={onOpenCheats}
                className="w-9 h-9 min-w-9 text-neutral-300 hover:text-white hover:bg-white/10 rounded-xl"
              >
                <Sparkles className="w-4 h-4" />
              </Button>
            </Tooltip>
          )}

          {/* Input Mapping */}
          <Tooltip content="Input Mapping" placement="top">
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={onOpenInputMap}
              className="w-9 h-9 min-w-9 text-neutral-300 hover:text-white hover:bg-white/10 rounded-xl"
            >
              <Gamepad2 className="w-4 h-4" />
            </Button>
          </Tooltip>


          {/* Netplay */}
          {!isPlay && !isPico8 && (
            <Tooltip content="Multiplayer Netplay" placement="top">
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={onOpenNetplay}
                className="w-9 h-9 min-w-9 text-neutral-300 hover:text-white hover:bg-white/10 rounded-xl"
              >
                <Globe className="w-4 h-4" />
              </Button>
            </Tooltip>
          )}

          {/* Separator */}
          <div className="w-[1px] h-5 bg-white/10 mx-0.5" />

          {/* Screenshot */}
          <Tooltip content="Take Screenshot" placement="top">
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => PlayerBridge.takeScreenshot(itemId, gameName)}
              className="w-9 h-9 min-w-9 text-neutral-300 hover:text-white hover:bg-white/10 rounded-xl"
            >
              <Camera className="w-4 h-4" />
            </Button>
          </Tooltip>

          {/* Settings */}
          <Tooltip content="Settings" placement="top">
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={onOpenSettings}
              className="w-9 h-9 min-w-9 text-neutral-300 hover:text-white hover:bg-white/10 rounded-xl"
            >
              <Settings className="w-4 h-4" />
            </Button>
          </Tooltip>

          {/* Core Options */}
          {!isPlay && !isPico8 && (
            <Tooltip content="Core Options" placement="top">
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={onOpenCoreOpts}
                className="w-9 h-9 min-w-9 text-neutral-300 hover:text-white hover:bg-white/10 rounded-xl"
              >
                <Sliders className="w-4 h-4" />
              </Button>
            </Tooltip>
          )}

          {/* Multi-Disc Selector (if m3u/j3u) */}
          {isM3u && (
            <>
              <div className="w-[1px] h-5 bg-white/10 mx-0.5" />
              <Tooltip content="Select Disc" placement="top">
                <Button
                  isIconOnly
                  size="sm"
                  variant="light"
                  onPress={onOpenDiscSelect}
                  className="w-9 h-9 min-w-9 text-neutral-300 hover:text-white hover:bg-white/10 rounded-xl"
                >
                  <Disc className="w-4 h-4" />
                </Button>
              </Tooltip>
            </>
          )}

          {/* Separator */}
          <div className="w-[1px] h-5 bg-white/10 mx-0.5" />

          {/* Fullscreen */}
          <Tooltip content={isFullscreen ? 'Exit Fullscreen' : 'Fullscreen'} placement="top">
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => PlayerBridge.toggleFullscreen()}
              className="w-9 h-9 min-w-9 text-neutral-300 hover:text-white hover:bg-white/10 rounded-xl"
            >
              {isFullscreen ? <Minimize2 className="w-4 h-4" /> : <Maximize2 className="w-4 h-4" />}
            </Button>
          </Tooltip>

          {/* Minimize Dock */}
          <Tooltip content="Minimize Controls" placement="top">
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={onToggleMinimize}
              className="w-9 h-9 min-w-9 text-neutral-400 hover:text-white hover:bg-white/10 rounded-xl"
            >
              <ChevronDown className="w-4 h-4" />
            </Button>
          </Tooltip>
        </div>
      </div>
    </>
  );
};
