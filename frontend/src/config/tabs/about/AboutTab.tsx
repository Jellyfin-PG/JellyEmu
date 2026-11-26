import React from 'react';
import { Card, CardBody, Button } from '@heroui/react';
import { Info, Github, Heart, Users, ExternalLink } from 'lucide-react';

export const AboutTab: React.FC = () => {
  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-xl font-bold text-white flex items-center gap-2">
          <Info className="w-5 h-5 text-primary" />
          About JellyEmu
        </h3>
        <p className="text-sm text-slate-300">Seamless retro games emulation and library indexing for Jellyfin Media Server.</p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
          <CardBody className="p-6 space-y-4">
            <h4 className="font-bold text-white text-base">Project Information</h4>
            <div className="space-y-2 text-xs text-slate-200">
              <div className="flex justify-between py-1.5 border-b border-white/10">
                <span className="text-slate-400 font-medium">Plugin Version</span>
                <span className="font-mono font-semibold text-white">0.9.5</span>
              </div>
              <div className="flex justify-between py-1.5 border-b border-white/10">
                <span className="text-slate-400 font-medium">Core Runtime</span>
                <span className="text-white font-medium">RetroKit (Libretro + EmulatorJS + Play! + PICO-8)</span>
              </div>
              <div className="flex justify-between py-1.5 border-b border-white/10">
                <span className="text-slate-400 font-medium">Target Jellyfin Server</span>
                <span className="text-white font-medium">v10.11.x - v12.x.x</span>
              </div>
              <div className="flex justify-between py-1.5">
                <span className="text-slate-400 font-medium">License</span>
                <span className="text-white font-medium">MIT</span>
              </div>
            </div>

            <div className="pt-2 flex flex-wrap gap-2.5">
              <Button
                as="a"
                href="https://groups.google.com/u/1/g/jellyemu"
                target="_blank"
                rel="noopener noreferrer"
                size="sm"
                variant="flat"
                color="secondary"
                startContent={<Users className="w-4 h-4" />}
              >
                Google Groups Community
              </Button>
              <Button
                as="a"
                href="https://github.com/Jellyfin-PG/JellyEmu"
                target="_blank"
                rel="noopener noreferrer"
                size="sm"
                variant="flat"
                color="primary"
                startContent={<Github className="w-4 h-4" />}
              >
                GitHub Repository
              </Button>
              <Button
                as="a"
                href="https://github.com/Jellyfin-PG/JellyEmu/issues"
                target="_blank"
                rel="noopener noreferrer"
                size="sm"
                variant="flat"
                className="bg-white/10 text-slate-200 hover:bg-white/15"
                startContent={<ExternalLink className="w-4 h-4" />}
              >
                Report Issues
              </Button>
            </div>
          </CardBody>
        </Card>

        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
          <CardBody className="p-6 space-y-4">
            <h4 className="font-bold text-white text-base flex items-center gap-2">
              <Heart className="w-4 h-4 text-danger" />
              Credits & Acknowledgements
            </h4>
            <ul className="text-xs text-slate-200 space-y-2.5">
              <li><strong className="text-white">RetroKit:</strong> Unified WebAssembly emulation orchestrator.</li>
              <li><strong className="text-white">Libretro &amp; EmulatorJS:</strong> Cross-platform WebAssembly emulation cores.</li>
              <li><strong className="text-white">HeroUI &amp; Framer Motion:</strong> Modern UI component suite.</li>
              <li><strong className="text-white">Jellyfin Community:</strong> Open-source media server development.</li>
            </ul>
          </CardBody>
        </Card>
      </div>
    </div>
  );
};
