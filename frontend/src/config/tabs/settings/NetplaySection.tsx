import React from 'react';
import { Card, CardBody, Textarea, Chip } from '@heroui/react';
import { Radio, Server } from 'lucide-react';
import { PluginConfiguration } from '../../../types/config';

interface NetplaySectionProps {
  config: PluginConfiguration;
  onUpdate: <K extends keyof PluginConfiguration>(field: K, value: PluginConfiguration[K]) => void;
}

export const NetplaySection: React.FC<NetplaySectionProps> = ({ config, onUpdate }) => {
  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-lg font-semibold text-white flex items-center gap-2">
          <Radio className="w-5 h-5 text-primary" />
          WebRTC Netplay & Multiplayer
        </h3>
        <p className="text-sm text-slate-400">
          Configure peer-to-peer ICE/STUN/TURN signaling relays for online multiplayer games.
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
          <CardBody className="p-5 space-y-5">
            <div className="flex items-center justify-between">
              <h4 className="font-semibold text-white text-sm flex items-center gap-2">
                <Server className="w-4 h-4 text-primary" />
                ICE / STUN / TURN Relays
              </h4>
              <Chip size="sm" color="primary" variant="flat">WebRTC</Chip>
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-semibold text-slate-300">STUN & TURN Server Endpoints</label>
              <Textarea
                placeholder="stun:stun.l.google.com:19302&#10;stun:stun1.l.google.com:19302"
                value={config.NetplayIceServers || ''}
                onValueChange={(val) => onUpdate('NetplayIceServers', val)}
                minRows={4}
                variant="bordered"
              />
              <p className="text-[11px] text-slate-400">
                Custom STUN and TURN server URLs (one per line) used to establish peer-to-peer WebRTC connections across NATs and firewalls.
              </p>
            </div>
          </CardBody>
        </Card>

        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
          <CardBody className="p-5 space-y-5">
            <div className="flex items-center justify-between">
              <h4 className="font-semibold text-white text-sm flex items-center gap-2">
                <Radio className="w-4 h-4 text-secondary" />
                Multiplayer Information
              </h4>
              <Chip size="sm" color="secondary" variant="flat">Active</Chip>
            </div>

            <div className="space-y-3">
              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-slate-300">Default Public STUN Relay</label>
                <div className="p-2.5 rounded-xl bg-white/[0.03] border border-white/5 font-mono text-xs text-slate-300">
                  stun:stun.l.google.com:19302
                </div>
                <p className="text-[11px] text-slate-400">Fallback relay used when no custom servers are configured.</p>
              </div>

              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-slate-300">Input Synchronization</label>
                <div className="p-2.5 rounded-xl bg-white/[0.03] border border-white/5 text-xs text-slate-300">
                  Per-frame controller simulation supporting up to 8 simultaneous players via Rollback Netcode.
                </div>
                <p className="text-[11px] text-slate-400">Syncs inputs in real-time with zero server emulation overhead.</p>
              </div>
            </div>
          </CardBody>
        </Card>
      </div>
    </div>
  );
};
