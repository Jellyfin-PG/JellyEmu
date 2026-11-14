import React from 'react';
import { Chip } from '@heroui/react';
import { JellyEmuLogo } from '../../shared/JellyEmuLogo';

interface ConfigHeaderProps {
  version?: string;
}

export const ConfigHeader: React.FC<ConfigHeaderProps> = ({
  version = '0.9.2.4'
}) => {
  return (
    <div className="flex items-center justify-between border-b border-white/10 pb-5 pt-2">
      <div className="flex items-center gap-4">
        <div className="w-12 h-12 rounded-2xl bg-gradient-to-tr from-primary/30 to-primary/10 border border-primary/40 flex items-center justify-center text-primary shadow-lg shadow-primary/15 backdrop-blur-md">
          <JellyEmuLogo className="w-7 h-7 text-primary" />
        </div>
        <div>
          <div className="flex items-center gap-2.5">
            <h1 className="text-2xl font-bold tracking-tight text-white">JellyEmu</h1>
            <Chip size="sm" color="primary" variant="flat" className="font-semibold text-xs border border-primary/20">
              v{version}
            </Chip>
          </div>
          <p className="text-sm text-slate-300 mt-0.5">Retro Games Library Integration & In-Browser Emulation</p>
        </div>
      </div>
    </div>
  );
};
