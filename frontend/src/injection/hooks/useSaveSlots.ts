import { useState, useEffect, useCallback } from 'react';
import { SaveStateEntry } from '../../types/injection';
import { injectionService } from '../services/injectionService';

export function useSaveSlots(itemId: string) {
  const [slots, setSlots] = useState<SaveStateEntry[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const userId = injectionService.getCurrentUserId();

  const fetchSlots = useCallback(async () => {
    if (!itemId || !userId) {
      setSlots([]);
      setLoading(false);
      return;
    }
    try {
      setLoading(true);
      const data = await injectionService.getSaveSlots(itemId, userId);
      setSlots(data);
    } catch {
      setSlots([]);
    } finally {
      setLoading(false);
    }
  }, [itemId, userId]);

  useEffect(() => {
    fetchSlots();
  }, [fetchSlots]);

  const deleteSlot = async (slot: number) => {
    if (!userId) return false;
    const ok = await injectionService.deleteSaveSlot(itemId, userId, slot);
    if (ok) {
      setSlots(prev => prev.filter(s => s.slot !== slot));
    }
    return ok;
  };

  return {
    slots,
    loading,
    reload: fetchSlots,
    deleteSlot
  };
}
