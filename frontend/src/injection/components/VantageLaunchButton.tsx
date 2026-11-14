import React from 'react';
import { Button } from '@heroui/react';
import { MonitorPlay } from 'lucide-react';
import { injectionService } from '../services/injectionService';

interface VantageLaunchButtonProps {
  itemId: string;
}

export const VantageLaunchButton: React.FC<VantageLaunchButtonProps> = ({ itemId }) => {
  return (
    <Button
      size="md"
      variant="flat"
      color="secondary"
      startContent={<MonitorPlay className="w-4 h-4 text-purple-400" />}
      onPress={() => injectionService.launchVantage(itemId)}
      className="font-medium text-xs sm:text-sm bg-purple-500/15 border border-purple-500/30 text-purple-200 hover:bg-purple-500/25"
    >
      Open in Vantage
    </Button>
  );
};
