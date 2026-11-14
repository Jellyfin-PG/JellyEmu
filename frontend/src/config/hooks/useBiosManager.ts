import { useState, useEffect, useCallback } from 'react';
import { biosService, SystemBiosGroup } from '../services/biosService';

export function useBiosManager() {
  const [biosGroups, setBiosGroups] = useState<SystemBiosGroup[]>([]);
  const [loading, setLoading] = useState<boolean>(true);

  const scan = useCallback(async () => {
    try {
      setLoading(true);
      const data = await biosService.scanBiosFiles();
      setBiosGroups(data);
    } catch (err) {
      console.warn('[JellyEmu] Bios scan failed:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    scan();
  }, [scan]);

  return {
    biosGroups,
    loading,
    rescan: scan
  };
}
