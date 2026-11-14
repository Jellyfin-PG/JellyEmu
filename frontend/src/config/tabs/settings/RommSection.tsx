import React from 'react';
import { Card, CardBody, Input, Switch, Chip } from '@heroui/react';
import { Cloud, Lock, User, RefreshCw, Trophy, Camera, Library } from 'lucide-react';
import { PluginConfiguration } from '../../../types/config';

interface RommSectionProps {
  config: PluginConfiguration;
  onUpdate: <K extends keyof PluginConfiguration>(field: K, value: PluginConfiguration[K]) => void;
}

export const RommSection: React.FC<RommSectionProps> = ({ config, onUpdate }) => {
  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-lg font-semibold text-white flex items-center gap-2">
          <Cloud className="w-5 h-5 text-primary" />
          Romm Cloud Sync & Integration
        </h3>
        <p className="text-sm text-slate-400">
          Synchronize save states, playtime, collections, and in-game screenshots with Romm.
        </p>
      </div>

      <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
        <CardBody className="p-5 space-y-5">
          <div className="flex items-center justify-between">
            <h4 className="font-semibold text-white text-sm flex items-center gap-2">
              <Cloud className="w-4 h-4 text-primary" />
              Romm Connection & Credentials
            </h4>
            <Chip
              size="sm"
              color={config.RommEnabled ? "success" : "default"}
              variant="flat"
            >
              {config.RommEnabled ? "Enabled" : "Disabled"}
            </Chip>
          </div>

          <div className="flex items-center justify-between p-3.5 rounded-xl bg-white/[0.03] border border-white/5">
            <div>
              <span className="font-semibold text-xs text-white">Enable Romm Integration</span>
              <p className="text-[11px] text-slate-400">Connect to your self-hosted Romm instance for bidirectional cloud sync.</p>
            </div>
            <Switch
              size="sm"
              isSelected={!!config.RommEnabled}
              onValueChange={(val) => onUpdate('RommEnabled', val)}
              color="primary"
            />
          </div>

          {config.RommEnabled && (
            <div className="space-y-4 animate-in fade-in duration-200">
              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-slate-300">Romm Instance URL</label>
                <Input
                  size="sm"
                  placeholder="https://romm.example.com"
                  value={config.RommInstanceUrl || ''}
                  onValueChange={(val) => onUpdate('RommInstanceUrl', val)}
                  startContent={<Cloud className="w-4 h-4 text-slate-400" />}
                  variant="bordered"
                />
                <p className="text-[11px] text-slate-400">Base URL of your Romm server.</p>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-slate-300">Romm Username</label>
                  <Input
                    size="sm"
                    placeholder="Username"
                    value={config.RommUsername || ''}
                    onValueChange={(val) => onUpdate('RommUsername', val)}
                    startContent={<User className="w-4 h-4 text-slate-400" />}
                    variant="bordered"
                  />
                </div>
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-slate-300">Romm Password</label>
                  <Input
                    type="password"
                    size="sm"
                    placeholder="••••••••"
                    value={config.RommPassword || ''}
                    onValueChange={(val) => onUpdate('RommPassword', val)}
                    startContent={<Lock className="w-4 h-4 text-slate-400" />}
                    variant="bordered"
                  />
                </div>
              </div>

              <div className="pt-2 border-t border-white/10 space-y-3">
                <h5 className="text-xs font-semibold uppercase tracking-wider text-slate-400">Sync Behaviors</h5>
                
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  <div className="flex items-center justify-between p-3 rounded-xl bg-white/[0.03] border border-white/5">
                    <div className="flex items-center gap-2">
                      <RefreshCw className="w-4 h-4 text-primary" />
                      <span className="text-xs font-medium text-white">Save State Sync</span>
                    </div>
                    <Switch
                      size="sm"
                      isSelected={config.RommSaveSyncEnabled !== false}
                      onValueChange={(val) => onUpdate('RommSaveSyncEnabled', val)}
                    />
                  </div>

                  <div className="flex items-center justify-between p-3 rounded-xl bg-white/[0.03] border border-white/5">
                    <div className="flex items-center gap-2">
                      <Trophy className="w-4 h-4 text-warning" />
                      <span className="text-xs font-medium text-white">Playtime Reporting</span>
                    </div>
                    <Switch
                      size="sm"
                      isSelected={config.RommPlaytimeReportEnabled !== false}
                      onValueChange={(val) => onUpdate('RommPlaytimeReportEnabled', val)}
                    />
                  </div>

                  <div className="flex items-center justify-between p-3 rounded-xl bg-white/[0.03] border border-white/5">
                    <div className="flex items-center gap-2">
                      <Library className="w-4 h-4 text-secondary" />
                      <span className="text-xs font-medium text-white">Collection Sync</span>
                    </div>
                    <Switch
                      size="sm"
                      isSelected={config.RommCollectionSyncEnabled !== false}
                      onValueChange={(val) => onUpdate('RommCollectionSyncEnabled', val)}
                    />
                  </div>

                  <div className="flex items-center justify-between p-3 rounded-xl bg-white/[0.03] border border-white/5">
                    <div className="flex items-center gap-2">
                      <Camera className="w-4 h-4 text-success" />
                      <span className="text-xs font-medium text-white">Screenshot Upload</span>
                    </div>
                    <Switch
                      size="sm"
                      isSelected={config.RommScreenshotPushEnabled !== false}
                      onValueChange={(val) => onUpdate('RommScreenshotPushEnabled', val)}
                    />
                  </div>
                </div>
              </div>
            </div>
          )}
        </CardBody>
      </Card>
    </div>
  );
};
