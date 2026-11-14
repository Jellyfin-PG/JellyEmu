import React, { useState } from 'react';
import { Input, Button, Tooltip } from '@heroui/react';
import { Search, Sparkles, HardDrive, Radio, Cpu, Layers, Save, Check } from 'lucide-react';
import { PluginConfiguration } from '../../../types/config';
import { ScrapersSection } from './ScrapersSection';
import { BiosSection } from './BiosSection';
import { NetplaySection } from './NetplaySection';
import { SystemSection } from './SystemSection';

interface SettingsTabProps {
  config: PluginConfiguration;
  onUpdate: <K extends keyof PluginConfiguration>(field: K, value: PluginConfiguration[K]) => void;
  onSave?: () => void;
  saving?: boolean;
  saveSuccess?: boolean;
}

type CategoryType = 'all' | 'metadata' | 'emulation' | 'netplay' | 'system';

export const SettingsTab: React.FC<SettingsTabProps> = ({
  config,
  onUpdate,
  onSave,
  saving = false,
  saveSuccess = false
}) => {
  const [search, setSearch] = useState<string>('');
  const [selectedCategory, setSelectedCategory] = useState<CategoryType>('all');

  const categories = [
    { id: 'all', label: 'All Settings', icon: Layers },
    { id: 'metadata', label: 'Metadata & Scrapers', icon: Sparkles },
    { id: 'netplay', label: 'Netplay', icon: Radio },
    { id: 'system', label: 'System & Storage', icon: Cpu },
    { id: 'emulation', label: 'Emulation & BIOS', icon: HardDrive },
  ];

  const shouldShow = (cat: CategoryType) => {
    if (selectedCategory !== 'all' && selectedCategory !== cat) return false;
    if (!search.trim()) return true;
    return true;
  };

  return (
    <div className="space-y-6">
      {/* Category Pills & Top Action Toolbar */}
      <div className="flex flex-col md:flex-row items-stretch md:items-center justify-between gap-4 p-4 rounded-xl bg-[#18181b]/80 border border-white/10 backdrop-blur-md">
        <div className="flex items-center gap-2 overflow-x-auto pb-1 md:pb-0 scrollbar-none">
          {categories.map((cat) => {
            const Icon = cat.icon;
            const isSelected = selectedCategory === cat.id;
            return (
              <Button
                key={cat.id}
                size="sm"
                variant={isSelected ? 'solid' : 'light'}
                color={isSelected ? 'primary' : 'default'}
                onPress={() => setSelectedCategory(cat.id as CategoryType)}
                startContent={<Icon className="w-3.5 h-3.5" />}
                className={`font-semibold whitespace-nowrap text-xs ${
                  isSelected ? 'shadow-md shadow-primary/20 text-white' : 'text-slate-300 hover:text-white'
                }`}
              >
                {cat.label}
              </Button>
            );
          })}
        </div>

        <div className="flex items-center gap-2.5">
          <div className="w-full sm:w-64">
            <Input
              size="sm"
              placeholder="Search settings..."
              value={search}
              onValueChange={setSearch}
              startContent={<Search className="w-4 h-4 text-slate-400" />}
              isClearable
              variant="bordered"
              className="text-white"
            />
          </div>

          {onSave && (
            <Tooltip content={saveSuccess ? "Settings Saved to Server!" : "Save Settings"}>
              <Button
                color={saveSuccess ? "success" : "primary"}
                variant={saveSuccess ? "flat" : "shadow"}
                size="sm"
                isIconOnly
                isLoading={saving}
                onPress={onSave}
                aria-label="Save Settings"
                className={`shrink-0 transition-all ${
                  saveSuccess ? 'bg-success/20 text-success' : 'shadow-md shadow-primary/25'
                }`}
              >
                {saveSuccess ? <Check className="w-4 h-4" /> : <Save className="w-4 h-4" />}
              </Button>
            </Tooltip>
          )}
        </div>
      </div>

      {/* Sections */}
      <div className="space-y-8">
        {shouldShow('metadata') && (
          <ScrapersSection config={config} onUpdate={onUpdate} />
        )}

        {shouldShow('netplay') && (
          <NetplaySection config={config} onUpdate={onUpdate} />
        )}

        {shouldShow('system') && (
          <SystemSection config={config} onUpdate={onUpdate} />
        )}

        {/* BIOS Section placed at the bottom */}
        {shouldShow('emulation') && (
          <BiosSection config={config} onUpdate={onUpdate} />
        )}
      </div>
    </div>
  );
};
