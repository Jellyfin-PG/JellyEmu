import React, { useState, useEffect } from 'react';
import { Card, CardBody, Input, Switch, Button, Tooltip } from '@heroui/react';
import { Folder, Camera, MonitorPlay, Layers, Cpu, Edit3, ListFilter, RefreshCw } from 'lucide-react';
import { PluginConfiguration } from '../../../types/config';
import { jellyfinApi } from '../../../shared/apiClient';
import { CustomSelect } from '../../../shared/CustomSelect';

interface SystemSectionProps {
  config: PluginConfiguration;
  onUpdate: <K extends keyof PluginConfiguration>(field: K, value: PluginConfiguration[K]) => void;
}

interface LibraryFolder {
  name: string;
  path: string;
  type?: string;
}

export const SystemSection: React.FC<SystemSectionProps> = ({ config, onUpdate }) => {
  const [libraryFolders, setLibraryFolders] = useState<LibraryFolder[]>([]);
  const [loadingFolders, setLoadingFolders] = useState<boolean>(true);
  const [customGamesPathMode, setCustomGamesPathMode] = useState<boolean>(false);
  const [customScreenshotsMode, setCustomScreenshotsMode] = useState<boolean>(false);

  const [redownloadingEjs, setRedownloadingEjs] = useState<boolean>(false);
  const [redownloadMessage, setRedownloadMessage] = useState<string | null>(null);
  const [redownloadError, setRedownloadError] = useState<boolean>(false);

  const handleRedownloadEjs = async () => {
    try {
      setRedownloadingEjs(true);
      setRedownloadMessage(null);
      setRedownloadError(false);

      const url = jellyfinApi.getApiUrl('jellyemu/ejs/redownload');
      const res = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...jellyfinApi.getAuthHeaders()
        }
      });
      const data = await res.json();
      if (res.ok) {
        setRedownloadMessage(data?.message || 'Started re-downloading EmulatorJS assets in background!');
        setRedownloadError(false);
      } else {
        setRedownloadMessage(data?.message || `Failed with status ${res.status}`);
        setRedownloadError(true);
      }
    } catch (err: any) {
      setRedownloadMessage(err?.message || 'Network error triggering re-download');
      setRedownloadError(true);
    } finally {
      setRedownloadingEjs(false);
    }
  };

  const fetchFolders = async () => {
    setLoadingFolders(true);
    const list: LibraryFolder[] = [];

    // Primary: ApiClient.getVirtualFolders()
    try {
      if ((window as any).ApiClient?.getVirtualFolders) {
        const folders = await (window as any).ApiClient.getVirtualFolders();
        (folders || []).forEach((f: any) => {
          (f.Locations || []).forEach((loc: string) => {
            list.push({
              name: f.Name,
              path: loc,
              type: f.CollectionType
            });
          });
        });
      }
    } catch (err) {
      console.warn('[JellyEmu] ApiClient.getVirtualFolders failed:', err);
    }

    // Fallback: /jellyemu/marketplace/library-folders
    if (list.length === 0) {
      try {
        const url = jellyfinApi.getApiUrl('jellyemu/marketplace/library-folders');
        const res = await fetch(url, {
          headers: {
            'Content-Type': 'application/json',
            ...jellyfinApi.getAuthHeaders()
          }
        });
        if (res.ok) {
          const folders = await res.json();
          (folders || []).forEach((f: any) => {
            (f.paths || f.Locations || []).forEach((loc: string) => {
              list.push({
                name: f.name || f.Name,
                path: loc,
                type: f.type || f.CollectionType
              });
            });
          });
        }
      } catch (err) {
        console.warn('[JellyEmu] Marketplace library-folders failed:', err);
      }
    }

    setLibraryFolders(list);
    setLoadingFolders(false);
  };

  useEffect(() => {
    fetchFolders();
  }, []);

  const currentGamesPath = config.GamesLibraryPath || '';
  const gamesPathMatchesFolder = libraryFolders.some((f) => f.path === currentGamesPath);

  // Compute screenshot options
  const screenshotOptions = libraryFolders.map((f) => {
    const sep = f.path.includes('\\') ? '\\' : '/';
    const mappedPath = `${f.path}${sep}JellyEmu`;
    return {
      name: f.name,
      path: mappedPath,
      display: `${f.name} (${mappedPath})`
    };
  });

  const currentScreenshotsPath = config.ScreenshotsFolder || '';
  const screenshotsPathMatches =
    currentScreenshotsPath === '' || screenshotOptions.some((s) => s.path === currentScreenshotsPath);

  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-lg font-semibold text-white flex items-center gap-2">
          <Cpu className="w-5 h-5 text-primary" />
          System, Storage & Core Runtime
        </h3>
        <p className="text-sm text-slate-400">
          Configure library paths, screenshot destination, UI injectors, and EmulatorJS channels.
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
        {/* Storage Paths from Jellyfin Virtual Folders */}
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
          <CardBody className="p-5 space-y-5">
            <div className="flex items-center justify-between">
              <h4 className="font-semibold text-white text-sm flex items-center gap-2">
                <Folder className="w-4 h-4 text-primary" />
                Library Storage Locations
              </h4>
              <Tooltip content="Refresh Jellyfin library folders">
                <Button
                  size="sm"
                  variant="light"
                  isIconOnly
                  isLoading={loadingFolders}
                  onPress={fetchFolders}
                  aria-label="Refresh library folders"
                >
                  <RefreshCw className="w-3.5 h-3.5 text-slate-400" />
                </Button>
              </Tooltip>
            </div>

            {/* Games Library Path Dropdown / Input */}
            <div className="space-y-1.5">
              <div className="flex items-center justify-between">
                <label className="text-xs font-semibold text-slate-300">Games Library Destination</label>
                <button
                  type="button"
                  onClick={() => setCustomGamesPathMode(!customGamesPathMode)}
                  className="text-[11px] text-primary hover:underline flex items-center gap-1 font-medium"
                >
                  {customGamesPathMode ? (
                    <>
                      <ListFilter className="w-3 h-3" /> Select from libraries
                    </>
                  ) : (
                    <>
                      <Edit3 className="w-3 h-3" /> Custom text path
                    </>
                  )}
                </button>
              </div>

              {!customGamesPathMode && libraryFolders.length > 0 ? (
                <CustomSelect
                  value={currentGamesPath}
                  onChange={(val) => onUpdate('GamesLibraryPath', val)}
                  placeholder="-- Select a Jellyfin Library Folder --"
                  options={[
                    { value: '', label: '-- Select a Jellyfin Library Folder --' },
                    ...libraryFolders.map((folder) => ({
                      value: folder.path,
                      label: `${folder.name} (${folder.path})`
                    })),
                    ...(currentGamesPath && !gamesPathMatchesFolder
                      ? [{ value: currentGamesPath, label: `Current: ${currentGamesPath}` }]
                      : [])
                  ]}
                  size="sm"
                />
              ) : (
                <Input
                  size="sm"
                  placeholder="e.g. D:\Games or /media/games"
                  value={config.GamesLibraryPath || ''}
                  onValueChange={(val) => onUpdate('GamesLibraryPath', val)}
                  description="Target directory for downloaded ROM files."
                  variant="bordered"
                />
              )}
              {!customGamesPathMode && (
                <p className="text-[11px] text-slate-400">
                  Target library folder where downloaded ROM files are stored.
                </p>
              )}
            </div>

            {/* Screenshots Folder Dropdown / Input */}
            <div className="space-y-1.5">
              <div className="flex items-center justify-between">
                <label className="text-xs font-semibold text-slate-300">Screenshots Destination</label>
                <button
                  type="button"
                  onClick={() => setCustomScreenshotsMode(!customScreenshotsMode)}
                  className="text-[11px] text-primary hover:underline flex items-center gap-1 font-medium"
                >
                  {customScreenshotsMode ? (
                    <>
                      <ListFilter className="w-3 h-3" /> Select from libraries
                    </>
                  ) : (
                    <>
                      <Edit3 className="w-3 h-3" /> Custom text path
                    </>
                  )}
                </button>
              </div>

              {!customScreenshotsMode && (screenshotOptions.length > 0 || currentScreenshotsPath) ? (
                <CustomSelect
                  value={currentScreenshotsPath}
                  onChange={(val) => onUpdate('ScreenshotsFolder', val)}
                  placeholder="-- Download in browser (Default) --"
                  options={[
                    { value: '', label: '-- Download in browser (Default) --' },
                    ...screenshotOptions.map((opt) => ({
                      value: opt.path,
                      label: opt.display
                    })),
                    ...(currentScreenshotsPath && !screenshotsPathMatches
                      ? [{ value: currentScreenshotsPath, label: `Current: ${currentScreenshotsPath}` }]
                      : [])
                  ]}
                  size="sm"
                />
              ) : (
                <Input
                  size="sm"
                  placeholder="e.g. D:\Jellyfin\Photos\Screenshots"
                  value={config.ScreenshotsFolder || ''}
                  onValueChange={(val) => onUpdate('ScreenshotsFolder', val)}
                  description="When empty, in-game screenshots download directly in browser."
                  startContent={<Camera className="w-4 h-4 text-slate-400" />}
                  variant="bordered"
                />
              )}
              {!customScreenshotsMode && (
                <p className="text-[11px] text-slate-400">
                  When empty or default, screenshots are downloaded through browser.
                </p>
              )}
            </div>
          </CardBody>
        </Card>

        {/* Runtime & UI Integration */}
        <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
          <CardBody className="p-5 space-y-4">
            <h4 className="font-semibold text-white text-sm flex items-center gap-2">
              <Layers className="w-4 h-4 text-primary" />
              Runtime & UI Options
            </h4>

            {/* EmulatorJS Core Channel Dropdown */}
            <div className="space-y-2">
              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-slate-300 block">EmulatorJS Core Channel</label>
                <CustomSelect
                  value={config.EjsChannel || 'stable'}
                  onChange={(val) => onUpdate('EjsChannel', val as any)}
                  options={[
                    { value: 'stable', label: 'Stable (Recommended)' },
                    { value: 'latest', label: 'Latest' },
                    { value: 'nightly', label: 'Nightly (Experimental)' }
                  ]}
                  size="sm"
                />
                <p className="text-[11px] text-slate-400">Select release channel for WASM emulation cores.</p>
              </div>

              <div className="pt-1 flex flex-col sm:flex-row sm:items-center gap-2.5">
                <Button
                  size="sm"
                  variant="flat"
                  color="primary"
                  startContent={<RefreshCw className={`w-3.5 h-3.5 ${redownloadingEjs ? 'animate-spin' : ''}`} />}
                  isLoading={redownloadingEjs}
                  onPress={handleRedownloadEjs}
                  className="font-semibold text-xs"
                >
                  Force Re-download EmulatorJS Assets
                </Button>
                {redownloadMessage && (
                  <span
                    className={`text-[11px] font-medium animate-in fade-in duration-200 ${
                      redownloadError ? 'text-danger-400' : 'text-success-400'
                    }`}
                  >
                    {redownloadMessage}
                  </span>
                )}
              </div>
            </div>

            <div className="space-y-3 pt-2">
              <div className="flex items-center justify-between p-3 rounded-xl bg-white/[0.03] border border-white/5">
                <div className="flex items-center gap-2">
                  <MonitorPlay className="w-4 h-4 text-primary" />
                  <div>
                    <span className="text-xs font-semibold text-white">Open in Vantage Button</span>
                    <p className="text-[11px] text-slate-400">Display the Vantage client launcher on game cards.</p>
                  </div>
                </div>
                <Switch
                  size="sm"
                  color="primary"
                  isSelected={config.VantageEnabled !== false}
                  onValueChange={(val) => onUpdate('VantageEnabled', val)}
                  aria-label="Toggle Vantage button"
                />
              </div>

              <div className="flex items-center justify-between p-3 rounded-xl bg-white/[0.03] border border-white/5">
                <div className="flex items-center gap-2">
                  <Layers className="w-4 h-4 text-secondary" />
                  <div>
                    <span className="text-xs font-semibold text-white">Loom Injector Mode</span>
                    <p className="text-[11px] text-slate-400">
                      Use Loom plugin for dynamic memory injection instead of static script patch.
                    </p>
                  </div>
                </div>
                <Switch
                  size="sm"
                  color="secondary"
                  isSelected={!!config.UseLoomInjector}
                  onValueChange={(val) => onUpdate('UseLoomInjector', val)}
                  aria-label="Toggle Loom injector mode"
                />
              </div>
            </div>
          </CardBody>
        </Card>
      </div>
    </div>
  );
};
