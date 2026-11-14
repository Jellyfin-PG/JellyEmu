import React, { useState } from 'react';
import { Card, CardBody, Input, Button, Chip, Accordion, AccordionItem } from '@heroui/react';
import { FolderGit2, RefreshCw, CheckCircle2, XCircle, HardDrive, Search } from 'lucide-react';
import { PluginConfiguration } from '../../../types/config';
import { useBiosManager } from '../../hooks/useBiosManager';

interface BiosSectionProps {
  config: PluginConfiguration;
  onUpdate: <K extends keyof PluginConfiguration>(field: K, value: PluginConfiguration[K]) => void;
}

export const BiosSection: React.FC<BiosSectionProps> = ({ config, onUpdate }) => {
  const { biosGroups, loading, rescan } = useBiosManager();
  const [biosFilter, setBiosFilter] = useState<string>('');

  const formatBytes = (bytes?: number) => {
    if (!bytes) return '';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  };

  const filteredGroups = biosGroups.filter(g =>
    g.system.toLowerCase().includes(biosFilter.toLowerCase()) ||
    (g.rawSystem && g.rawSystem.toLowerCase().includes(biosFilter.toLowerCase())) ||
    g.files.some(f => f.filename.toLowerCase().includes(biosFilter.toLowerCase()))
  );

  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-lg font-semibold text-white flex items-center gap-2">
          <HardDrive className="w-5 h-5 text-primary" />
          Emulation & System BIOS Manager
        </h3>
        <p className="text-sm text-slate-400">
          System firmware validation matrix ({biosGroups.length} systems loaded from database).
        </p>
      </div>

      <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
        <CardBody className="p-5 space-y-5">
          <div className="flex items-center justify-between">
            <h4 className="font-semibold text-white text-sm flex items-center gap-2">
              <FolderGit2 className="w-4 h-4 text-primary" />
              BIOS Directory & System Firmware
            </h4>
            <Button
              size="sm"
              variant="flat"
              color="primary"
              startContent={<RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />}
              isLoading={loading}
              onPress={rescan}
              className="font-semibold text-xs"
            >
              Rescan Directory
            </Button>
          </div>

          <div className="space-y-1.5">
            <label className="text-xs font-semibold text-slate-300">Custom BIOS Folder Path</label>
            <Input
              size="sm"
              placeholder="e.g. C:\Jellyfin\BIOS or /var/lib/jellyfin/data/bios"
              value={config.BiosPath || ''}
              onValueChange={(val) => onUpdate('BiosPath', val)}
              startContent={<FolderGit2 className="w-4 h-4 text-slate-400" />}
              variant="bordered"
            />
            <p className="text-[11px] text-slate-400">
              Target directory for BIOS files. Defaults to {'{DataPath}'}/jellyemu-bios when left blank.
            </p>
          </div>

          <div className="pt-2 border-t border-white/10 space-y-3">
            <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
              <div>
                <span className="text-xs font-semibold text-white block">System Firmware Validation Matrix</span>
                <p className="text-[11px] text-slate-400">
                  {filteredGroups.length} platforms matching filter
                </p>
              </div>
              <div className="w-full sm:w-64">
                <Input
                  size="sm"
                  placeholder="Filter systems or BIOS files..."
                  value={biosFilter}
                  onValueChange={setBiosFilter}
                  startContent={<Search className="w-3.5 h-3.5 text-slate-400" />}
                  isClearable
                  variant="bordered"
                />
              </div>
            </div>

            {filteredGroups.length === 0 ? (
              <div className="text-center py-8 text-slate-400 text-xs bg-white/[0.02] border border-white/5 rounded-xl">
                {loading ? 'Scanning BIOS directory and database...' : 'No matching systems found.'}
              </div>
            ) : (
              <Accordion variant="splitted" className="px-0 gap-2.5">
                {filteredGroups.map((group, groupIdx) => {
                  const files = group.files || [];
                  const foundCount = files.filter(f => f.found).length;
                  const totalCount = files.length;
                  const isComplete = totalCount > 0 && foundCount === totalCount;
                  const itemKey = `${group.system}-${groupIdx}`;

                  return (
                    <AccordionItem
                      key={itemKey}
                      aria-label={group.system}
                      title={
                        <div className="flex items-center justify-between w-full pr-3">
                          <div className="flex items-center gap-2.5">
                            <span className="font-semibold text-sm text-white">{group.system}</span>
                            {group.rawSystem && group.rawSystem !== group.system && (
                              <span className="text-xs text-slate-400 font-normal">({group.rawSystem})</span>
                            )}
                          </div>
                          <Chip
                            size="sm"
                            color={isComplete ? "success" : foundCount > 0 ? "warning" : "danger"}
                            variant="flat"
                            startContent={isComplete ? <CheckCircle2 className="w-3.5 h-3.5" /> : <XCircle className="w-3.5 h-3.5" />}
                          >
                            {foundCount}/{totalCount} Found
                          </Chip>
                        </div>
                      }
                      className="bg-white/[0.03] border border-white/10 rounded-xl overflow-hidden shadow-none"
                    >
                      <div className="space-y-2 py-2">
                        {files.map((file, fileIdx) => (
                          <div
                            key={`${file.filename}-${fileIdx}`}
                            className="flex flex-col sm:flex-row sm:items-center justify-between p-3 rounded-xl bg-[#141418] border border-white/10 text-xs gap-2"
                          >
                            <div className="space-y-0.5">
                              <div className="flex items-center gap-2 flex-wrap">
                                <span className="font-mono font-semibold text-white">{file.filename}</span>
                                <span className="text-slate-400">({file.description})</span>
                                {file.size && (
                                  <span className="text-slate-500 font-mono text-[11px]">[{formatBytes(file.size)}]</span>
                                )}
                              </div>
                              {file.md5 && (
                                <p className="font-mono text-[10px] text-slate-500">MD5: {file.md5}</p>
                              )}
                            </div>
                            <div className="flex items-center gap-2">
                              <Chip
                                size="sm"
                                color={file.found ? "success" : "default"}
                                variant="dot"
                              >
                                {file.found ? (file.status || "Verified") : "Missing"}
                              </Chip>
                            </div>
                          </div>
                        ))}
                      </div>
                    </AccordionItem>
                  );
                })}
              </Accordion>
            )}
          </div>
        </CardBody>
      </Card>
    </div>
  );
};
