import React from 'react';
import { Modal, ModalContent, ModalHeader, ModalBody, ModalFooter, Button, Card, CardBody, Chip, Spinner } from '@heroui/react';
import { Save, Play, Trash2, Calendar, Image as ImageIcon } from 'lucide-react';
import { useSaveSlots } from '../hooks/useSaveSlots';
import { injectionService } from '../services/injectionService';

interface SaveManagerModalProps {
  itemId: string;
  itemName?: string;
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
}

export const SaveManagerModal: React.FC<SaveManagerModalProps> = ({
  itemId,
  itemName = 'Game',
  isOpen,
  onOpenChange
}) => {
  const { slots, loading, deleteSlot } = useSaveSlots(itemId);

  const formatBytes = (bytes?: number) => {
    if (!bytes) return '0 KB';
    return (bytes / 1024).toFixed(1) + ' KB';
  };

  return (
    <Modal isOpen={isOpen} onOpenChange={onOpenChange} size="2xl" className="dark text-foreground">
      <ModalContent>
        {(onClose) => (
          <>
            <ModalHeader className="flex items-center gap-2.5 border-b border-divider/40">
              <div className="w-8 h-8 rounded-lg bg-primary/20 flex items-center justify-center text-primary">
                <Save className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-white text-base font-semibold">Save State Manager</h3>
                <p className="text-xs text-default-400 font-normal">{itemName}</p>
              </div>
            </ModalHeader>

            <ModalBody className="py-5 max-h-[70vh] overflow-y-auto">
              {loading ? (
                <div className="flex items-center justify-center py-10">
                  <Spinner label="Loading save states..." color="primary" />
                </div>
              ) : slots.length === 0 ? (
                <div className="text-center py-10 space-y-2">
                  <div className="w-12 h-12 rounded-2xl bg-default-100 flex items-center justify-center text-default-400 mx-auto">
                    <Save className="w-6 h-6" />
                  </div>
                  <p className="text-sm text-default-300 font-medium">No Save States Found</p>
                  <p className="text-xs text-default-500">Save states created in-game will appear here automatically.</p>
                </div>
              ) : (
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                  {slots.map((slot) => (
                    <Card key={slot.slot} className="bg-default-50/80 border border-divider/60 shadow-sm">
                      <CardBody className="p-3.5 space-y-3">
                        <div className="flex items-center justify-between">
                          <Chip size="sm" color="primary" variant="flat" className="font-semibold text-xs">
                            Slot {slot.slot}
                          </Chip>
                          <span className="text-[11px] text-default-400 font-mono">
                            {formatBytes(slot.sizeBytes)}
                          </span>
                        </div>

                        {slot.screenshotUrl ? (
                          <div className="aspect-video w-full rounded-lg overflow-hidden bg-black/40 border border-divider/30">
                            <img
                              src={slot.screenshotUrl}
                              alt={`Slot ${slot.slot} Preview`}
                              className="w-full h-full object-cover"
                            />
                          </div>
                        ) : (
                          <div className="aspect-video w-full rounded-lg bg-default-100/50 border border-divider/30 flex items-center justify-center text-default-400">
                            <ImageIcon className="w-6 h-6 opacity-40" />
                          </div>
                        )}

                        <div className="space-y-1 text-xs text-default-400">
                          {slot.lastModified && (
                            <div className="flex items-center gap-1.5 text-[11px]">
                              <Calendar className="w-3.5 h-3.5 text-default-500" />
                              <span>{new Date(slot.lastModified).toLocaleString()}</span>
                            </div>
                          )}
                        </div>

                        <div className="flex items-center gap-2 pt-1 border-t border-divider/30">
                          <Button
                            size="sm"
                            color="primary"
                            variant="flat"
                            startContent={<Play className="w-3.5 h-3.5" />}
                            className="flex-1 font-medium"
                            onPress={() => {
                              onClose();
                              injectionService.launchEmulator(itemId, slot.slot);
                            }}
                          >
                            Load Slot {slot.slot}
                          </Button>
                          <Button
                            size="sm"
                            color="danger"
                            variant="light"
                            isIconOnly
                            onPress={() => deleteSlot(slot.slot)}
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </Button>
                        </div>
                      </CardBody>
                    </Card>
                  ))}
                </div>
              )}
            </ModalBody>

            <ModalFooter className="border-t border-divider/40">
              <Button color="default" variant="light" onPress={onClose}>
                Close
              </Button>
            </ModalFooter>
          </>
        )}
      </ModalContent>
    </Modal>
  );
};
