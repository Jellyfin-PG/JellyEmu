import React, { useState } from 'react';
import { Modal, ModalContent, ModalHeader, ModalBody, ModalFooter, Button, Chip, Card, CardBody } from '@heroui/react';
import { Gamepad2, Radio, RotateCcw } from 'lucide-react';
import { useGamepadListener } from '../hooks/useGamepadListener';

interface GamepadConfigModalProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
}

interface ActionMapping {
  actionId: number;
  label: string;
  assignedBtn: number | null;
}

export const GamepadConfigModal: React.FC<GamepadConfigModalProps> = ({
  isOpen,
  onOpenChange
}) => {
  const [activeActionId, setActiveActionId] = useState<number | null>(null);
  const [mappings, setMappings] = useState<ActionMapping[]>([
    { actionId: 0, label: 'D-Pad Up', assignedBtn: 12 },
    { actionId: 1, label: 'D-Pad Down', assignedBtn: 13 },
    { actionId: 2, label: 'D-Pad Left', assignedBtn: 14 },
    { actionId: 3, label: 'D-Pad Right', assignedBtn: 15 },
    { actionId: 4, label: 'A / Cross Button', assignedBtn: 0 },
    { actionId: 5, label: 'B / Circle Button', assignedBtn: 1 },
    { actionId: 6, label: 'X / Square Button', assignedBtn: 2 },
    { actionId: 7, label: 'Y / Triangle Button', assignedBtn: 3 },
    { actionId: 8, label: 'Start Button', assignedBtn: 9 },
    { actionId: 9, label: 'Select Button', assignedBtn: 8 },
    { actionId: 10, label: 'L1 / Left Shoulder', assignedBtn: 4 },
    { actionId: 11, label: 'R1 / Right Shoulder', assignedBtn: 5 },
  ]);

  const { gamepads, hasGamepad } = useGamepadListener(
    activeActionId !== null,
    (btnIdx) => {
      if (activeActionId !== null) {
        setMappings(prev =>
          prev.map(m => m.actionId === activeActionId ? { ...m, assignedBtn: btnIdx } : m)
        );
        setActiveActionId(null);
      }
    }
  );

  return (
    <Modal isOpen={isOpen} onOpenChange={onOpenChange} size="xl" className="dark text-foreground">
      <ModalContent>
        {(onClose) => (
          <>
            <ModalHeader className="flex items-center gap-2.5 border-b border-divider/40">
              <div className="w-8 h-8 rounded-lg bg-primary/20 flex items-center justify-center text-primary">
                <Gamepad2 className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-white text-base font-semibold">Gamepad & Controller Configuration</h3>
                <p className="text-xs text-default-400 font-normal">
                  {hasGamepad ? `Detected: ${gamepads[0]}` : 'No gamepads detected. Press any button on your controller.'}
                </p>
              </div>
            </ModalHeader>

            <ModalBody className="py-5 max-h-[60vh] overflow-y-auto space-y-4">
              <div className="flex items-center justify-between p-3 rounded-xl bg-default-100/40 border border-divider/40">
                <div className="flex items-center gap-2">
                  <Radio className={`w-4 h-4 ${hasGamepad ? 'text-success animate-pulse' : 'text-default-400'}`} />
                  <span className="text-xs text-default-300 font-medium">
                    {hasGamepad ? 'Gamepad Connected & Ready' : 'Listening for Gamepad Input...'}
                  </span>
                </div>
                <Chip size="sm" color={hasGamepad ? "success" : "default"} variant="flat">
                  {gamepads.length} Device(s)
                </Chip>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                {mappings.map((mapping) => {
                  const isListening = activeActionId === mapping.actionId;

                  return (
                    <Card
                      key={mapping.actionId}
                      isPressable
                      onPress={() => setActiveActionId(isListening ? null : mapping.actionId)}
                      className={`border transition-all ${
                        isListening
                          ? 'border-primary bg-primary/10 shadow-lg shadow-primary/20 scale-[1.02]'
                          : 'border-divider/50 bg-default-50/70 hover:bg-default-100/50'
                      }`}
                    >
                      <CardBody className="p-3 flex flex-row items-center justify-between">
                        <span className="text-xs font-semibold text-white">{mapping.label}</span>
                        <Chip
                          size="sm"
                          color={isListening ? "primary" : mapping.assignedBtn !== null ? "secondary" : "default"}
                          variant={isListening ? "solid" : "flat"}
                          className="font-mono text-xs font-bold"
                        >
                          {isListening ? 'PRESS BUTTON...' : mapping.assignedBtn !== null ? `Button ${mapping.assignedBtn}` : 'UNBOUND'}
                        </Chip>
                      </CardBody>
                    </Card>
                  );
                })}
              </div>
            </ModalBody>

            <ModalFooter className="border-t border-divider/40 flex justify-between">
              <Button
                color="default"
                variant="flat"
                size="sm"
                startContent={<RotateCcw className="w-3.5 h-3.5" />}
                onPress={() => setActiveActionId(null)}
              >
                Reset Defaults
              </Button>
              <Button color="primary" onPress={onClose}>
                Save Controller Profile
              </Button>
            </ModalFooter>
          </>
        )}
      </ModalContent>
    </Modal>
  );
};
