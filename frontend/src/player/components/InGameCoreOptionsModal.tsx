import React, { useState, useEffect, useCallback } from 'react';
import {
  Modal,
  ModalContent,
  ModalHeader,
  ModalBody,
  Button,
  Input
} from '@heroui/react';
import { Sliders, RotateCcw, Search, Cpu, Check } from 'lucide-react';
import { PlayerBridge } from '../services/playerBridge';
import { CoreOptionItem } from '../../types/player';
import { CustomSelect } from '../../shared/CustomSelect';

interface InGameCoreOptionsModalProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
}

export const InGameCoreOptionsModal: React.FC<InGameCoreOptionsModalProps> = ({
  isOpen,
  onOpenChange
}) => {
  const [options, setOptions] = useState<CoreOptionItem[]>([]);
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [statusMsg, setStatusMsg] = useState<string>('');
  const coreName = PlayerBridge.getCoreName();

  const loadOptions = useCallback(() => {
    const list = PlayerBridge.getCoreOptionsList();
    if (list && list.length > 0) {
      setOptions(list);
    } else {
      // Fallback: check emulator.coreOptions if emulator provides it
      const e = PlayerBridge.getEmulator();
      if (e && e.coreOptions) {
        const parsed: CoreOptionItem[] = Object.keys(e.coreOptions).map((key) => {
          const item = e.coreOptions[key];
          return {
            key,
            name: item.name || key,
            value: item.value || '',
            options: (item.options || []).map((opt: any) => ({
              value: typeof opt === 'object' ? opt.value : String(opt),
              label: typeof opt === 'object' ? opt.label : String(opt)
            })),
            description: item.description
          };
        });
        setOptions(parsed);
      } else {
        setOptions([]);
      }
    }
  }, []);

  useEffect(() => {
    if (!isOpen) return;
    loadOptions();
    // In case core options take 200ms to mount
    const timer = setTimeout(loadOptions, 250);
    return () => clearTimeout(timer);
  }, [isOpen, loadOptions]);

  const handleChange = (key: string, val: string) => {
    PlayerBridge.setCoreOption(key, val);
    setOptions((prev) =>
      prev.map((opt) => (opt.key === key ? { ...opt, value: val } : opt))
    );
    setStatusMsg(`Saved ${key}`);
    setTimeout(() => setStatusMsg(''), 2000);
  };

  const handleResetDefaults = () => {
    PlayerBridge.clearSavedCoreOptions();
    const gm = PlayerBridge.getGameManager();
    if (gm && typeof gm.getCoreOptions === 'function') {
      const optsRaw = gm.getCoreOptions();
      if (optsRaw) {
        const lines = optsRaw.split('\n');
        lines.forEach((line: string) => {
          if (!line.trim()) return;
          const parts = line.split(/;\s*/);
          if (parts.length >= 2) {
            const keyVal = parts[0].split('|');
            const k = keyVal[0]?.trim();
            const defVal = keyVal[1]?.trim();
            if (k && defVal && typeof gm.setVariable === 'function') {
              try { gm.setVariable(k, defVal); } catch (_) {}
            }
          }
        });
      }
    }
    loadOptions();
    setStatusMsg('Core options reset to defaults');
    setTimeout(() => setStatusMsg(''), 2500);
  };

  const filteredOptions = options.filter(
    (opt) =>
      opt.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
      opt.key.toLowerCase().includes(searchQuery.toLowerCase())
  );

  return (
    <Modal
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      size="lg"
      backdrop="blur"
      placement="center"
      scrollBehavior="inside"
      classNames={{
        base: 'bg-[#121216] border border-white/10 text-white shadow-2xl rounded-2xl max-w-lg',
        header: 'border-b border-white/10 px-6 py-4 flex items-center justify-between',
        body: 'p-6',
        closeButton: 'text-neutral-400 hover:text-white hover:bg-white/10 p-1.5 rounded-lg'
      }}
    >
      <ModalContent>
        {() => (
          <>
            <ModalHeader>
              <div className="flex items-center gap-2">
                <Sliders className="w-5 h-5 text-white" />
                <div className="flex items-center gap-2">
                  <h2 className="text-base font-semibold tracking-wide">Core Options</h2>
                  <span className="flex items-center gap-1 text-[11px] px-2 py-0.5 rounded-md bg-white/10 border border-white/15 text-neutral-300 font-mono">
                    <Cpu className="w-3 h-3 text-neutral-400" />
                    {coreName}
                  </span>
                </div>
              </div>
            </ModalHeader>

            <ModalBody className="flex flex-col gap-4 max-h-[28rem] overflow-y-auto pr-1">
              <div className="flex items-center justify-between gap-2">
                <Input
                  size="sm"
                  placeholder="Filter core options..."
                  value={searchQuery}
                  onValueChange={setSearchQuery}
                  startContent={<Search className="w-3.5 h-3.5 text-neutral-400" />}
                  classNames={{
                    base: 'flex-1',
                    inputWrapper: 'bg-black/50 border border-white/15 h-8 text-xs'
                  }}
                />
                <Button
                  size="sm"
                  variant="flat"
                  onPress={handleResetDefaults}
                  className="h-8 text-xs bg-white/5 hover:bg-white/10 text-neutral-300 border border-white/10 px-3 flex-shrink-0 cursor-pointer"
                  startContent={<RotateCcw className="w-3.5 h-3.5" />}
                >
                  Reset Defaults
                </Button>
              </div>

              {statusMsg && (
                <div className="flex items-center gap-2 px-3 py-1.5 rounded-lg text-xs bg-white/5 border border-white/10 text-neutral-300">
                  <Check className="w-3.5 h-3.5 text-emerald-400" />
                  <span>{statusMsg}</span>
                </div>
              )}

              {options.length === 0 ? (
                <div className="text-xs text-neutral-400 py-10 text-center flex flex-col items-center gap-2">
                  <Cpu className="w-8 h-8 text-neutral-600" />
                  <p>No configurable Libretro options available for {coreName}.</p>
                  <p className="text-[11px] text-neutral-500">Core options are populated dynamically once the emulation core initializes.</p>
                </div>
              ) : filteredOptions.length === 0 ? (
                <div className="text-xs text-neutral-400 py-6 text-center">
                  No options matching &ldquo;{searchQuery}&rdquo;
                </div>
              ) : (
                filteredOptions.map((opt) => (
                  <div key={opt.key} className="flex flex-col gap-1.5 p-3.5 rounded-xl bg-black/40 border border-white/10">
                    <div className="flex items-center justify-between">
                      <span className="text-xs font-semibold text-white">{opt.name}</span>
                      <span className="text-[10px] font-mono text-neutral-500">{opt.key}</span>
                    </div>
                    {opt.description && (
                      <span className="text-[11px] text-neutral-400">{opt.description}</span>
                    )}
                    <CustomSelect
                      value={opt.value}
                      onChange={(val) => handleChange(opt.key, val)}
                      options={opt.options.map((o) => ({
                        value: o.value,
                        label: o.label || o.value
                      }))}
                      size="sm"
                    />
                  </div>
                ))
              )}
            </ModalBody>
          </>
        )}
      </ModalContent>
    </Modal>
  );
};

