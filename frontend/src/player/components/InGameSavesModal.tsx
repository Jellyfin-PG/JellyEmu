import React, { useState, useEffect, useRef } from 'react';
import {
  Modal,
  ModalContent,
  ModalHeader,
  ModalBody,
  Tabs,
  Tab,
  Button
} from '@heroui/react';
import { Save, Download, Upload, Image as ImageIcon, Check } from 'lucide-react';
import { PlayerBridge } from '../services/playerBridge';
import { SaveSlotInfo } from '../../types/player';

interface InGameSavesModalProps {
  itemId: string;
  userId?: string;
  gameName: string;
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
}

export const InGameSavesModal: React.FC<InGameSavesModalProps> = ({
  itemId,
  userId = '',
  gameName,
  isOpen,
  onOpenChange
}) => {
  const [activeTab, setActiveTab] = useState<string>('states');
  const [slots, setSlots] = useState<SaveSlotInfo[]>([
    { slot: 1, hasSave: false },
    { slot: 2, hasSave: false },
    { slot: 3, hasSave: false },
    { slot: 4, hasSave: false },
    { slot: 5, hasSave: false }
  ]);
  const [savingSlot, setSavingSlot] = useState<number | null>(null);
  const [loadingSlot, setLoadingSlot] = useState<number | null>(null);
  const [statusMessage, setStatusMessage] = useState<string>('');

  const stateFileInputRef = useRef<HTMLInputElement>(null);
  const sramFileInputRef = useRef<HTMLInputElement>(null);

  // Fetch slot status and screenshots on open
  useEffect(() => {
    if (!isOpen || !itemId || !userId) return;

    const fetchSlotData = async () => {
      const updated = await Promise.all(
        [1, 2, 3, 4, 5].map(async (slot) => {
          const hasSave = await PlayerBridge.checkSlotSave(itemId, userId, slot);
          let screenshotUrl: string | undefined;
          if (hasSave) {
            screenshotUrl = (await PlayerBridge.getSlotScreenshot(itemId, userId, slot)) || undefined;
          }
          return { slot, hasSave, screenshotUrl };
        })
      );
      setSlots(updated);
    };

    fetchSlotData();
  }, [isOpen, itemId, userId]);

  const handleSaveState = async (slot: number) => {
    setSavingSlot(slot);
    setStatusMessage('');
    const success = await PlayerBridge.saveState(itemId, userId, slot);
    if (success) {
      setStatusMessage(`Slot ${slot} saved successfully`);
      // Update screenshot and status
      const screenshotUrl = (await PlayerBridge.getSlotScreenshot(itemId, userId, slot)) || undefined;
      setSlots((prev) =>
        prev.map((s) => (s.slot === slot ? { ...s, hasSave: true, screenshotUrl } : s))
      );
    } else {
      setStatusMessage(`Failed to save Slot ${slot}`);
    }
    setSavingSlot(null);
  };

  const handleLoadState = async (slot: number) => {
    setLoadingSlot(slot);
    setStatusMessage('');
    const success = await PlayerBridge.loadState(itemId, userId, slot);
    if (success) {
      onOpenChange(false);
    } else {
      setStatusMessage(`Failed to load Slot ${slot}`);
    }
    setLoadingSlot(null);
  };

  const handleExportState = async () => {
    await PlayerBridge.exportState(gameName);
  };

  const handleStateFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const success = await PlayerBridge.importState(file);
    if (success) {
      onOpenChange(false);
    } else {
      setStatusMessage('Failed to import state file');
    }
  };

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
                <Save className="w-5 h-5 text-white" />
                <h2 className="text-base font-semibold tracking-wide">Saves &amp; States</h2>
              </div>
            </ModalHeader>

            <ModalBody className="flex flex-col gap-5">
              {/* Tab Navigation */}
              <Tabs
                selectedKey={activeTab}
                onSelectionChange={(key) => setActiveTab(key as string)}
                variant="solid"
                classNames={{
                  tabList: 'bg-black/50 p-1 rounded-xl border border-white/10 w-full',
                  cursor: 'bg-white text-black rounded-lg shadow-sm',
                  tab: 'h-8 text-xs font-medium text-neutral-400 data-[selected=true]:text-black transition-colors',
                  tabContent: 'group-data-[selected=true]:text-black'
                }}
              >
                <Tab key="states" title="Save States" />
                <Tab key="sram" title="SRAM (Battery)" />
              </Tabs>

              {statusMessage && (
                <div className="flex items-center gap-2 px-3 py-2 rounded-lg text-xs bg-emerald-500/10 border border-emerald-500/20 text-emerald-300">
                  <Check className="w-4 h-4 text-emerald-400" />
                  <span>{statusMessage}</span>
                </div>
              )}

              {/* Save States */}
              {activeTab === 'states' && (
                <div className="flex flex-col gap-4">
                  <div className="text-xs font-semibold text-neutral-400 uppercase tracking-wider">
                    Cloud Save States
                  </div>

                  {/* Slot Cards */}
                  <div className="flex flex-col gap-2.5">
                    {slots.map(({ slot, hasSave, screenshotUrl }) => (
                      <div
                        key={slot}
                        className="flex items-center justify-between p-3.5 rounded-xl bg-[#16161a] border border-white/10 hover:border-white/20 transition-all"
                      >
                        <div className="flex items-center gap-3">
                          {/* Screenshot Thumbnail */}
                          <div className="w-14 h-10 rounded-lg bg-[#121216] border border-white/10 overflow-hidden flex items-center justify-center flex-shrink-0">
                            {screenshotUrl ? (
                              <img
                                src={screenshotUrl}
                                alt={`Slot ${slot}`}
                                className="w-full h-full object-cover"
                              />
                            ) : (
                              <ImageIcon className="w-4 h-4 text-neutral-600" />
                            )}
                          </div>

                          <div className="flex flex-col">
                            <span className="text-xs font-semibold text-white">
                              Slot {slot}
                            </span>
                            <span className="text-[11px] text-neutral-400">
                              {hasSave ? 'Saved State Available' : 'Empty Slot'}
                            </span>
                          </div>
                        </div>

                        <div className="flex items-center gap-2">
                          <Button
                            size="sm"
                            variant="flat"
                            isLoading={savingSlot === slot}
                            onPress={() => handleSaveState(slot)}
                            className="h-7 px-3 text-xs bg-white/10 hover:bg-white/20 text-white border border-white/10 rounded-lg"
                          >
                            Save
                          </Button>
                          <Button
                            size="sm"
                            variant="solid"
                            isDisabled={!hasSave}
                            isLoading={loadingSlot === slot}
                            onPress={() => handleLoadState(slot)}
                            className="h-7 px-3 text-xs bg-white text-black hover:bg-neutral-200 font-medium rounded-lg disabled:opacity-30"
                          >
                            Load
                          </Button>
                        </div>
                      </div>
                    ))}
                  </div>

                  {/* Local Save State Section */}
                  <div className="text-xs font-semibold text-neutral-400 uppercase tracking-wider mt-2">
                    Local Save State
                  </div>

                  <div className="flex gap-2">
                    <Button
                      size="sm"
                      variant="flat"
                      onPress={handleExportState}
                      className="flex-1 h-8 text-xs bg-white/10 hover:bg-white/15 text-white border border-white/10 rounded-xl"
                      startContent={<Download className="w-3.5 h-3.5" />}
                    >
                      Export State (.state)
                    </Button>
                  </div>

                  {/* Dropzone */}
                  <div
                    onClick={() => stateFileInputRef.current?.click()}
                    className="border-2 border-dashed border-white/15 hover:border-white/30 rounded-xl p-5 text-center cursor-pointer transition-colors bg-[#16161a]/60 hover:bg-[#16161a]"
                  >
                    <Upload className="w-6 h-6 text-neutral-500 mx-auto mb-2" />
                    <div className="text-xs font-semibold text-neutral-200">
                      Click or Drop .state File Here
                    </div>
                    <div className="text-[11px] text-neutral-500 mt-0.5">
                      Loads state instantly into the active game
                    </div>
                    <input
                      type="file"
                      ref={stateFileInputRef}
                      onChange={handleStateFileChange}
                      accept=".state"
                      className="hidden"
                    />
                  </div>
                </div>
              )}

              {/* SRAM (Battery) */}
              {activeTab === 'sram' && (
                <div className="flex flex-col gap-4">
                  <div className="text-xs font-semibold text-neutral-400 uppercase tracking-wider">
                    Cloud SRAM Backups
                  </div>

                  <div className="p-4 rounded-xl bg-[#16161a] border border-white/10 text-xs text-neutral-300 leading-relaxed">
                    Battery saves (in-game save files) are synchronized automatically to your cloud storage on save-points and upon session exit.
                  </div>

                  <div className="text-xs font-semibold text-neutral-400 uppercase tracking-wider mt-2">
                    Local SRAM File
                  </div>

                  <Button
                    size="sm"
                    variant="flat"
                    className="h-8 text-xs bg-white/10 hover:bg-white/15 text-white border border-white/10 rounded-xl"
                    startContent={<Download className="w-3.5 h-3.5" />}
                  >
                    Export SRAM (.sav)
                  </Button>

                  {/* Dropzone */}
                  <div
                    onClick={() => sramFileInputRef.current?.click()}
                    className="border-2 border-dashed border-white/15 hover:border-white/30 rounded-xl p-5 text-center cursor-pointer transition-colors bg-[#16161a]/60 hover:bg-[#16161a]"
                  >
                    <Upload className="w-6 h-6 text-neutral-500 mx-auto mb-2" />
                    <div className="text-xs font-semibold text-neutral-200">
                      Click or Drop .sav / .srm File Here
                    </div>
                    <div className="text-[11px] text-neutral-500 mt-0.5">
                      Restores battery save and restarts game
                    </div>
                    <input
                      type="file"
                      ref={sramFileInputRef}
                      accept=".sav,.srm"
                      className="hidden"
                    />
                  </div>
                </div>
              )}
            </ModalBody>
          </>
        )}
      </ModalContent>
    </Modal>
  );
};
