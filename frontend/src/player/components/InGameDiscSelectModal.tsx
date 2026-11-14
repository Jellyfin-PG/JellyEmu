import React, { useState, useEffect } from 'react';
import {
  Modal,
  ModalContent,
  ModalHeader,
  ModalBody,
  Button
} from '@heroui/react';
import { Disc } from 'lucide-react';
import { PlayerBridge } from '../services/playerBridge';
import { DiscItem } from '../../types/player';

interface InGameDiscSelectModalProps {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
}

export const InGameDiscSelectModal: React.FC<InGameDiscSelectModalProps> = ({
  isOpen,
  onOpenChange
}) => {
  const [discs, setDiscs] = useState<DiscItem[]>([]);

  useEffect(() => {
    if (!isOpen) return;

    const gm = PlayerBridge.getGameManager();
    if (gm && gm.discs) {
      const list: DiscItem[] = gm.discs.map((d: any, idx: number) => ({
        index: idx,
        name: d.name || `Disc ${idx + 1}`,
        path: d.path || '',
        current: gm.currentDisc === idx
      }));
      setDiscs(list);
    } else {
      // Fallback disc list
      setDiscs([
        { index: 0, name: 'Disc 1', path: '', current: true },
        { index: 1, name: 'Disc 2', path: '', current: false }
      ]);
    }
  }, [isOpen]);

  const handleSelectDisc = (index: number) => {
    PlayerBridge.selectDisc(index);
    onOpenChange(false);
  };

  return (
    <Modal
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      size="sm"
      backdrop="blur"
      placement="center"
      scrollBehavior="inside"
      classNames={{
        base: 'bg-[#121216] border border-white/10 text-white shadow-2xl rounded-2xl max-w-sm',
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
                <Disc className="w-5 h-5 text-white" />
                <h2 className="text-base font-semibold tracking-wide">Select Disc</h2>
              </div>
            </ModalHeader>

            <ModalBody className="flex flex-col gap-2.5">
              {discs.map((d) => (
                <div
                  key={d.index}
                  className="flex items-center justify-between p-3.5 rounded-xl bg-[#16161a] border border-white/10 hover:border-white/20 transition-all"
                >
                  <div className="flex items-center gap-2.5">
                    <Disc className="w-4 h-4 text-neutral-400" />
                    <span className="text-xs font-semibold text-white">{d.name}</span>
                    {d.current && (
                      <span className="text-[10px] uppercase font-bold px-1.5 py-0.5 rounded bg-white/10 text-white">
                        Loaded
                      </span>
                    )}
                  </div>

                  <Button
                    size="sm"
                    variant={d.current ? 'flat' : 'solid'}
                    isDisabled={d.current}
                    onPress={() => handleSelectDisc(d.index)}
                    className="h-7 px-3 text-xs bg-white text-black hover:bg-neutral-200 font-medium rounded-lg disabled:opacity-40"
                  >
                    Insert
                  </Button>
                </div>
              ))}
            </ModalBody>
          </>
        )}
      </ModalContent>
    </Modal>
  );
};
