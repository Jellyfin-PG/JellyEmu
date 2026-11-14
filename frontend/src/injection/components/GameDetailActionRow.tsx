import React from 'react';
import { Button, Tooltip } from '@heroui/react';
import { Gamepad2 } from 'lucide-react';
import { motion } from 'framer-motion';
import { injectionService } from '../services/injectionService';

interface GameDetailActionRowProps {
  itemId: string;
}

export const GameDetailActionRow: React.FC<GameDetailActionRowProps> = ({ itemId }) => {
  return (
    <motion.div
      initial={{ scale: 0.5, opacity: 0 }}
      animate={{ scale: 1, opacity: 1 }}
      transition={{ type: 'spring', stiffness: 450, damping: 22 }}
      className="je-detail-action-container inline-flex items-center my-1 mr-2 relative"
    >
      {/* Ambient Pulse Glow */}
      <span className="absolute inset-0 rounded-full bg-white/20 animate-ping opacity-30 pointer-events-none" />

      <Tooltip
        content="Play Game"
        placement="top"
        delay={120}
        closeDelay={0}
        className="font-semibold text-xs bg-neutral-900 text-white border border-white/20 shadow-xl"
      >
        <motion.div
          whileHover={{ scale: 1.08 }}
          whileTap={{ scale: 0.92 }}
          transition={{ type: 'spring', stiffness: 500, damping: 18 }}
        >
          <Button
            isIconOnly
            radius="full"
            className="w-11 h-11 min-w-11 min-h-11 shadow-xl bg-white hover:bg-neutral-200 active:bg-neutral-300 border border-white/40 text-black cursor-pointer transition-all duration-150"
            aria-label="Play Game"
            onPress={() => injectionService.launchEmulator(itemId)}
          >
            <Gamepad2 className="w-5 h-5 text-black" />
          </Button>
        </motion.div>
      </Tooltip>
    </motion.div>
  );
};
