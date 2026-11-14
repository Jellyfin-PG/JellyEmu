import React, { useState, useEffect } from 'react';
import {
  Modal,
  ModalContent,
  ModalHeader,
  ModalBody,
  Button,
  Switch
} from '@heroui/react';
import { Sparkles, Plus, Trash2 } from 'lucide-react';
import { PlayerBridge } from '../services/playerBridge';
import { CheatItem } from '../../types/player';

interface InGameCheatsModalProps {
  itemId: string;
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
}

export const InGameCheatsModal: React.FC<InGameCheatsModalProps> = ({
  itemId,
  isOpen,
  onOpenChange
}) => {
  const [cheats, setCheats] = useState<CheatItem[]>([]);
  const [loading, setLoading] = useState<boolean>(false);
  const [cheatName, setCheatName] = useState<string>('');
  const [cheatCode, setCheatCode] = useState<string>('');

  useEffect(() => {
    if (!isOpen || !itemId) return;

    const loadCheats = async () => {
      setLoading(true);
      const dbCheats = await PlayerBridge.fetchCheats(itemId);
      const e = PlayerBridge.getEmulator();
      const liveCheats = e?.cheats || [];

      // Merge server db cheats with live cheats
      const merged: CheatItem[] = dbCheats.map((ch) => {
        const live = liveCheats.find((c: any) => c.code === ch.code);
        return { ...ch, checked: live ? !!live.checked : false };
      });

      // Add any live cheats not in db
      liveCheats.forEach((c: any) => {
        if (!merged.find((m) => m.code === c.code)) {
          merged.push({
            desc: c.desc,
            code: c.code,
            checked: !!c.checked,
            fromDb: false
          });
        }
      });

      setCheats(merged);
      setLoading(false);
    };

    loadCheats();
  }, [isOpen, itemId]);

  const handleToggle = (cheat: CheatItem, enabled: boolean) => {
    PlayerBridge.toggleCheat(cheat, enabled);
    setCheats((prev) =>
      prev.map((c) => (c.code === cheat.code ? { ...c, checked: enabled } : c))
    );
  };

  const handleAddCheat = () => {
    if (!cheatName.trim() || !cheatCode.trim()) return;
    PlayerBridge.addCustomCheat(cheatName.trim(), cheatCode.trim());
    setCheats((prev) => [
      ...prev,
      {
        desc: cheatName.trim(),
        code: cheatCode.trim(),
        checked: false,
        fromDb: false
      }
    ]);
    setCheatName('');
    setCheatCode('');
  };

  const handleDeleteCheat = (code: string) => {
    PlayerBridge.removeCustomCheat(code);
    setCheats((prev) => prev.filter((c) => c.code !== code));
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
                <Sparkles className="w-5 h-5 text-white" />
                <h2 className="text-base font-semibold tracking-wide">Cheats</h2>
              </div>
            </ModalHeader>

            <ModalBody className="flex flex-col gap-5">
              {/* Cheats List */}
              <div className="flex flex-col gap-2.5 max-h-60 overflow-y-auto pr-1">
                {loading ? (
                  <div className="text-xs text-neutral-500 py-4 text-center">
                    Loading cheats…
                  </div>
                ) : cheats.length === 0 ? (
                  <div className="text-xs text-neutral-500 py-4 text-center">
                    No cheats found for this game
                  </div>
                ) : (
                  cheats.map((ch, idx) => (
                    <div
                      key={idx}
                      className="flex items-center justify-between p-3.5 rounded-xl bg-[#16161a] border border-white/10 hover:border-white/20 transition-all"
                    >
                      <div className="flex items-center gap-3 pr-2">
                        <Switch
                          size="sm"
                          isSelected={ch.checked}
                          onValueChange={(val) => handleToggle(ch, val)}
                          classNames={{
                            wrapper: 'bg-white/20 group-data-[selected=true]:bg-white',
                            thumb: 'bg-black'
                          }}
                        />
                        <span className="text-xs text-white font-medium break-words">
                          {ch.desc}
                        </span>
                      </div>

                      {!ch.fromDb && (
                        <Button
                          isIconOnly
                          size="sm"
                          variant="light"
                          onPress={() => handleDeleteCheat(ch.code)}
                          className="w-7 h-7 min-w-7 text-neutral-400 hover:text-red-400 hover:bg-red-500/10 rounded-lg"
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </Button>
                      )}
                    </div>
                  ))
                )}
              </div>

              {/* Add Custom Cheat Section */}
              <div className="flex flex-col gap-3 pt-3 border-t border-white/10">
                <div className="text-xs font-semibold text-neutral-400 uppercase tracking-wider">
                  Add Cheat
                </div>

                <input
                  type="text"
                  placeholder="Cheat name"
                  value={cheatName}
                  onChange={(e) => setCheatName(e.target.value)}
                  className="h-9 px-3 text-xs bg-[#16161a] border border-white/15 rounded-xl text-white outline-none focus:border-white/40"
                />

                <textarea
                  rows={2}
                  placeholder="Cheat code"
                  value={cheatCode}
                  onChange={(e) => setCheatCode(e.target.value)}
                  className="p-3 text-xs bg-[#16161a] border border-white/15 rounded-xl text-white outline-none focus:border-white/40 resize-none"
                />

                <Button
                  size="sm"
                  variant="solid"
                  onPress={handleAddCheat}
                  isDisabled={!cheatName.trim() || !cheatCode.trim()}
                  className="h-8 text-xs bg-white text-black hover:bg-neutral-200 font-semibold rounded-xl"
                  startContent={<Plus className="w-3.5 h-3.5" />}
                >
                  Add Cheat
                </Button>
              </div>
            </ModalBody>
          </>
        )}
      </ModalContent>
    </Modal>
  );
};
