import React from 'react';
import { Card, CardBody, Input, Chip } from '@heroui/react';
import { Key, Globe, Image as ImageIcon, ShieldCheck, Sparkles } from 'lucide-react';
import { PluginConfiguration } from '../../../types/config';
import { CustomSelect } from '../../../shared/CustomSelect';

interface ScrapersSectionProps {
  config: PluginConfiguration;
  onUpdate: <K extends keyof PluginConfiguration>(field: K, value: PluginConfiguration[K]) => void;
}

export const ScrapersSection: React.FC<ScrapersSectionProps> = ({ config, onUpdate }) => {
  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-lg font-semibold text-white flex items-center gap-2">
          <Sparkles className="w-5 h-5 text-primary" />
          Metadata & Scraper Providers
        </h3>
        <p className="text-sm text-slate-400">
          Configure cover art, game descriptions, screenshots, and community ratings.
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
        {/* IGDB / Twitch */}
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
          <CardBody className="p-5 space-y-5">
            <div className="flex items-center justify-between">
              <h4 className="font-semibold text-white text-sm flex items-center gap-2">
                <Sparkles className="w-4 h-4 text-purple-400" />
                IGDB (via Twitch)
              </h4>
              <Chip size="sm" color="secondary" variant="flat">Primary</Chip>
            </div>

            <div className="space-y-4">
              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-slate-300">Twitch Client ID</label>
                <Input
                  size="sm"
                  placeholder="e.g. 7imyo6cfhtx5pzsoq3lnd7ochv6kle"
                  value={config.IgdbClientId || ''}
                  onValueChange={(val) => onUpdate('IgdbClientId', val)}
                  startContent={<Key className="w-4 h-4 text-slate-400" />}
                  variant="bordered"
                />
                <p className="text-[11px] text-slate-400">Twitch developer application Client ID for IGDB API.</p>
              </div>

              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-slate-300">Twitch Client Secret</label>
                <Input
                  type="password"
                  size="sm"
                  placeholder="••••••••••••••••••••••••"
                  value={config.IgdbClientSecret || ''}
                  onValueChange={(val) => onUpdate('IgdbClientSecret', val)}
                  startContent={<ShieldCheck className="w-4 h-4 text-slate-400" />}
                  variant="bordered"
                />
                <p className="text-[11px] text-slate-400">OAuth secret used for automated token renewal.</p>
              </div>
            </div>
          </CardBody>
        </Card>

        {/* ScreenScraper.fr */}
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
          <CardBody className="p-5 space-y-5">
            <div className="flex items-center justify-between">
              <h4 className="font-semibold text-white text-sm flex items-center gap-2">
                <Sparkles className="w-4 h-4 text-emerald-400" />
                ScreenScraper.fr
              </h4>
              <Chip size="sm" color="success" variant="flat">Retro Specialist</Chip>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-slate-300">SS Username</label>
                <Input
                  size="sm"
                  placeholder="Your username"
                  value={config.ScreenScraperUser || ''}
                  onValueChange={(val) => onUpdate('ScreenScraperUser', val)}
                  variant="bordered"
                />
              </div>
              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-slate-300">SS Password</label>
                <Input
                  type="password"
                  size="sm"
                  placeholder="••••••••"
                  value={config.ScreenScraperPassword || ''}
                  onValueChange={(val) => onUpdate('ScreenScraperPassword', val)}
                  variant="bordered"
                />
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-slate-300 block">Region Preference</label>
                <CustomSelect
                  value={config.ScreenScraperRegionPreference || 'auto'}
                  onChange={(val) => onUpdate('ScreenScraperRegionPreference', val as any)}
                  options={[
                    { value: 'auto', label: 'Auto / Worldwide' },
                    { value: 'us', label: 'North America (US)' },
                    { value: 'eu', label: 'Europe (EU)' },
                    { value: 'jp', label: 'Japan (JP)' }
                  ]}
                  size="sm"
                />
              </div>

              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-slate-300 block">Language Preference</label>
                <CustomSelect
                  value={config.ScreenScraperLanguagePreference || 'en'}
                  onChange={(val) => onUpdate('ScreenScraperLanguagePreference', val as any)}
                  options={[
                    { value: 'en', label: 'English (en)' },
                    { value: 'fr', label: 'French (fr)' },
                    { value: 'de', label: 'German (de)' },
                    { value: 'es', label: 'Spanish (es)' },
                    { value: 'ja', label: 'Japanese (ja)' }
                  ]}
                  size="sm"
                />
              </div>
            </div>

            <p className="text-[11px] text-slate-400">
              Retro wheels, 3D boxart, titles, and screenshots from ScreenScraper.
            </p>
          </CardBody>
        </Card>

        {/* SteamGridDB */}
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
          <CardBody className="p-5 space-y-5">
            <div className="flex items-center justify-between">
              <h4 className="font-semibold text-white text-sm flex items-center gap-2">
                <ImageIcon className="w-4 h-4 text-blue-400" />
                SteamGridDB
              </h4>
              <Chip size="sm" color="primary" variant="flat">Grids & Logos</Chip>
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-semibold text-slate-300">SteamGridDB API Key</label>
              <Input
                size="sm"
                placeholder="Paste your API key"
                value={config.SteamGridDbApiKey || ''}
                onValueChange={(val) => onUpdate('SteamGridDbApiKey', val)}
                startContent={<ImageIcon className="w-4 h-4 text-slate-400" />}
                variant="bordered"
              />
              <p className="text-[11px] text-slate-400">
                Used for high-resolution vertical grids, hero banners, and clear logos.
              </p>
            </div>
          </CardBody>
        </Card>

        {/* RAWG.io */}
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
          <CardBody className="p-5 space-y-5">
            <div className="flex items-center justify-between">
              <h4 className="font-semibold text-white text-sm flex items-center gap-2">
                <Globe className="w-4 h-4 text-orange-400" />
                RAWG.io
              </h4>
              <Chip size="sm" color="warning" variant="flat">Metadata & Ratings</Chip>
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-semibold text-slate-300">RAWG API Key</label>
              <Input
                size="sm"
                placeholder="Paste your RAWG API key"
                value={config.RawgApiKey || ''}
                onValueChange={(val) => onUpdate('RawgApiKey', val)}
                startContent={<Globe className="w-4 h-4 text-slate-400" />}
                variant="bordered"
              />
              <p className="text-[11px] text-slate-400">
                Community tags, genres, ratings, and game descriptions.
              </p>
            </div>
          </CardBody>
        </Card>

        {/* TheGamesDB */}
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
          <CardBody className="p-5 space-y-5">
            <div className="flex items-center justify-between">
              <h4 className="font-semibold text-white text-sm flex items-center gap-2">
                <Key className="w-4 h-4 text-cyan-400" />
                TheGamesDB
              </h4>
              <Chip size="sm" color="primary" variant="flat">Retro Scraper</Chip>
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-semibold text-slate-300">TheGamesDB API Key</label>
              <Input
                size="sm"
                placeholder="Paste your TheGamesDB API key"
                value={config.TheGamesDbApiKey || ''}
                onValueChange={(val) => onUpdate('TheGamesDbApiKey', val)}
                startContent={<Key className="w-4 h-4 text-slate-400" />}
                variant="bordered"
              />
              <p className="text-[11px] text-slate-400">
                Community retro database for box art and game information.
              </p>
            </div>
          </CardBody>
        </Card>
      </div>
    </div>
  );
};
