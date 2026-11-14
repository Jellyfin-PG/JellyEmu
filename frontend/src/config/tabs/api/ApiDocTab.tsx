import React from 'react';
import { Card, CardBody, Chip } from '@heroui/react';
import { Code, Gamepad2, HardDrive, Save } from 'lucide-react';

interface ApiEndpoint {
  methods: string[];
  path: string;
  desc: string;
  note?: string;
}

export const ApiDocTab: React.FC = () => {
  const retroArchEndpoints: ApiEndpoint[] = [
    {
      methods: ['GET'],
      path: '/jellyemu/retroarch/playlist',
      desc: 'Downloads JellyEmu.lpl, the full library playlist. Copy it to your RetroArch playlists/ folder to stream ROMs directly over HTTP.'
    },
    {
      methods: ['GET'],
      path: '/jellyemu/retroarch/playlist/{system}',
      desc: 'Downloads a per-system playlist JellyEmu-{system}.lpl. Replace {system} with a platform tag like NES or SNES.'
    },
    {
      methods: ['GET'],
      path: '/jellyemu/retroarch/launch/{itemId}',
      desc: 'Deep-link launch handler. Opening this endpoint on a system with RetroArch installed initiates the game directly.'
    },
    {
      methods: ['GET'],
      path: '/jellyemu/retroarch/info',
      desc: 'Returns a JSON summary of all detected systems, total game counts, and playlist configuration details.'
    },
    {
      methods: ['GET'],
      path: '/jellyemu/retroarch/cores/.index-dirs',
      desc: 'Lists the available systems, one per line, in the libretro buildbot index format.'
    },
    {
      methods: ['GET'],
      path: '/jellyemu/retroarch/cores/{system}/.index',
      desc: 'Lists downloadable files for a system. .index-extended adds modified date and hash per line.'
    },
    {
      methods: ['GET'],
      path: '/jellyemu/retroarch/cores/{system}/{filename}',
      desc: 'Downloads a ROM named in the .index.'
    },
    {
      methods: ['GET'],
      path: '/jellyemu/retroarch/system/{path}',
      desc: 'Browses the JellyEmu BIOS folder in buildbot format (.index-dirs, .index, .index-extended), or downloads the BIOS file at any subpath.'
    },
    {
      methods: ['GET'],
      path: '/jellyemu/retroarch/frontend/{file}',
      desc: 'Redirects known frontend asset names to the official libretro buildbot so other Online Updater features continue working.'
    }
  ];

  const coreEndpoints: ApiEndpoint[] = [
    {
      methods: ['GET'],
      path: '/jellyemu/rom/{itemId}',
      desc: 'Streams the raw game ROM binary file. Used for RetroArch playback and download routing.'
    },
    {
      methods: ['GET'],
      path: '/jellyemu/rom/download-zip/{itemId}',
      desc: 'Builds and streams a ZIP file packaging all multi-part installer files (designed for Windows/GOG installer packages).'
    }
  ];

  const saveEndpoints: ApiEndpoint[] = [
    {
      methods: ['GET'],
      path: '/jellyemu/save-slots/{itemId}/{userId}',
      desc: 'Retrieves a list of saved state slots, file sizes, timestamps, and screenshots for the given game and user.'
    },
    {
      methods: ['HEAD', 'GET', 'POST', 'DELETE'],
      path: '/jellyemu/save/{itemId}/{userId}?slot={slot}',
      desc: 'Checks existence (HEAD), downloads (GET), uploads (POST), or deletes (DELETE) the save state file for the specified slot (also supports path /jellyemu/save/{itemId}/{userId}/{slot}).'
    },
    {
      methods: ['HEAD', 'GET', 'POST', 'DELETE'],
      path: '/jellyemu/sram/{itemId}/{userId}?slot={slot}',
      desc: 'Checks existence (HEAD), downloads (GET), uploads (POST), or deletes (DELETE) SRAM cartridge battery-backed save data for the specified slot (also supports path /jellyemu/sram/{itemId}/{userId}/{slot}).'
    },
    {
      methods: ['GET', 'POST'],
      path: '/jellyemu/prefs/{userId}',
      desc: 'Downloads (GET) or updates (POST) user emulator preferences and controller mappings.'
    }
  ];

  const getMethodColor = (m: string) => {
    switch (m.toUpperCase()) {
      case 'GET': return 'primary';
      case 'POST': return 'success';
      case 'DELETE': return 'danger';
      case 'HEAD': return 'warning';
      default: return 'default';
    }
  };

  const renderSection = (title: string, desc: string, icon: React.ReactNode, items: ApiEndpoint[]) => (
    <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
      <CardBody className="p-6 space-y-5">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-white/10 pb-4">
          <div className="flex items-center gap-2.5">
            {icon}
            <div>
              <h4 className="font-bold text-white text-base">{title}</h4>
              <p className="text-xs text-slate-400">{desc}</p>
            </div>
          </div>
          <Chip size="sm" color="primary" variant="flat" className="w-fit">
            {items.length} Endpoints
          </Chip>
        </div>

        <div className="space-y-3">
          {items.map((ep) => (
            <div
              key={ep.path}
              className="p-3.5 rounded-xl bg-white/[0.03] border border-white/5 space-y-1.5 hover:bg-white/[0.05] transition-colors"
            >
              <div className="flex items-center gap-2 flex-wrap">
                {ep.methods.map((m) => (
                  <Chip
                    key={m}
                    size="sm"
                    color={getMethodColor(m) as any}
                    variant="flat"
                    className="font-mono font-bold text-[11px] h-6"
                  >
                    {m}
                  </Chip>
                ))}
                <span className="font-mono text-xs font-semibold text-white">{ep.path}</span>
              </div>
              <p className="text-xs text-slate-300 leading-relaxed">{ep.desc}</p>
            </div>
          ))}
        </div>
      </CardBody>
    </Card>
  );

  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-xl font-bold text-white flex items-center gap-2">
          <Code className="w-5 h-5 text-primary" />
          JellyEmu REST API Reference
        </h3>
        <p className="text-sm text-slate-300">
          Complete public API reference for RetroArch integration, buildbot assets, direct ROM delivery, and cloud saves.
        </p>
      </div>

      {renderSection(
        'RetroArch Integration',
        'Endpoints exposed for RetroArch system streaming, playlist auto-generation, and buildbot updater.',
        <Gamepad2 className="w-5 h-5 text-primary" />,
        retroArchEndpoints
      )}

      {renderSection(
        'Core File Delivery',
        'Endpoints managing direct game ROM delivery, multi-part packaging, and download streams.',
        <HardDrive className="w-5 h-5 text-secondary" />,
        coreEndpoints
      )}

      {renderSection(
        'Saves & Preferences',
        'Endpoints managing cloud saves, SRAM battery backups, state slots, and user controller mappings.',
        <Save className="w-5 h-5 text-success" />,
        saveEndpoints
      )}
    </div>
  );
};
