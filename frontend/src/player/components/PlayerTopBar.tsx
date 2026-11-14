import React from 'react';
import { Button, Chip } from '@heroui/react';
import { ArrowLeft, Glasses, Box, Radio } from 'lucide-react';
import { PlayerBridge } from '../services/playerBridge';

interface PlayerTopBarProps {
  title: string;
  systemName?: string;
  visible: boolean;
  isPlay?: boolean;
  isPico8?: boolean;
  isNetplayActive?: boolean;
  onOpenNetplay?: () => void;
  isVrSupported?: boolean;
  isArSupported?: boolean;
}

export const PlayerTopBar: React.FC<PlayerTopBarProps> = ({
  title,
  systemName,
  visible,
  isPlay = false,
  isPico8 = false,
  isNetplayActive = false,
  onOpenNetplay,
  isVrSupported = false,
  isArSupported = false
}) => {
  return (
    <header
      className={`fixed top-0 left-0 right-0 z-40 transition-all duration-300 ease-in-out px-4 py-2 flex items-center justify-between pointer-events-auto bg-black/90 backdrop-blur-md border-b border-white/10 text-white ${
        visible ? 'translate-y-0 opacity-100' : '-translate-y-full opacity-0 pointer-events-none'
      }`}
    >
      {/* Title & Platform Tag */}
      <div className="flex items-center gap-2.5 sm:gap-3">
        <h1 className="text-sm font-semibold text-white tracking-wide truncate max-w-[200px] sm:max-w-md">
          {title}
        </h1>
        {systemName && (
          <Chip
            size="sm"
            variant="flat"
            className="bg-white/10 text-neutral-300 text-[10px] font-medium tracking-wider uppercase border border-white/10"
          >
            {systemName}
          </Chip>
        )}
      </div>

      {/* Action Buttons */}
      <div className="flex items-center gap-2">
        {/* Netplay Live Pill */}
        {!isPlay && !isPico8 && isNetplayActive && (
          <Button
            size="sm"
            variant="flat"
            onPress={onOpenNetplay}
            className="h-8 px-3 text-xs bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 hover:bg-emerald-500/30 transition-all"
            startContent={<Radio className="w-3.5 h-3.5 animate-pulse" />}
          >
            Live Netplay
          </Button>
        )}

        {/* VR Button */}
        {!isPlay && !isPico8 && (
          <Button
            isIconOnly
            size="sm"
            variant="flat"
            isDisabled={!isVrSupported}
            title={isVrSupported ? 'Enter VR Mode' : 'VR Not Supported / No Device Detected'}
            onPress={() => PlayerBridge.enterVR()}
            className={`w-8 h-8 min-w-8 border transition-all ${
              isVrSupported
                ? 'bg-white/5 hover:bg-white/15 text-neutral-300 hover:text-white border-white/10'
                : 'bg-white/[0.02] text-neutral-600 border-white/5 opacity-40 cursor-not-allowed'
            }`}
          >
            <Glasses className="w-4 h-4" />
          </Button>
        )}

        {/* AR Button */}
        {!isPlay && !isPico8 && (
          <Button
            isIconOnly
            size="sm"
            variant="flat"
            isDisabled={!isArSupported}
            title={isArSupported ? 'Enter AR Mode' : 'AR Not Supported / No Device Detected'}
            onPress={() => PlayerBridge.enterAR()}
            className={`w-8 h-8 min-w-8 border transition-all ${
              isArSupported
                ? 'bg-white/5 hover:bg-white/15 text-neutral-300 hover:text-white border-white/10'
                : 'bg-white/[0.02] text-neutral-600 border-white/5 opacity-40 cursor-not-allowed'
            }`}
          >
            <Box className="w-4 h-4" />
          </Button>
        )}

        {/* Exit Button */}
        <Button
          size="sm"
          variant="flat"
          title="Exit Emulation"
          onPress={() => PlayerBridge.exit()}
          className="h-8 px-3 text-xs bg-white/10 hover:bg-white/20 text-white border border-white/15 transition-colors font-medium"
          startContent={<ArrowLeft className="w-3.5 h-3.5" />}
        >
          Exit
        </Button>
      </div>
    </header>
  );
};
