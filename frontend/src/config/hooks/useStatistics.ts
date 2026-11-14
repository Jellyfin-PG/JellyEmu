import { useState, useEffect, useCallback } from 'react';
import {
  statsService,
  PlaytimeSummaryResponse,
  LibraryGameSummary,
  UserPlaytimeDetails
} from '../services/statsService';

export function useStatistics() {
  const [playtimeStats, setPlaytimeStats] = useState<PlaytimeSummaryResponse>({ totalSeconds: 0, users: [], topGames: [] });
  const [libraryStats, setLibraryStats] = useState<LibraryGameSummary>({
    totalGames: 0,
    platforms: [],
    unsupportedCount: 0
  });
  const [userGames, setUserGames] = useState<UserPlaytimeDetails | null>(null);
  const [loading, setLoading] = useState<boolean>(true);

  const fetchStats = useCallback(async () => {
    try {
      setLoading(true);
      const [ptData, libData, userGameData] = await Promise.all([
        statsService.getPlaytimeSummary(),
        statsService.getLibraryGameStats(),
        statsService.getUserPlaytimeDetails()
      ]);

      setPlaytimeStats(ptData);
      setLibraryStats(libData);
      setUserGames(userGameData);
    } catch (err) {
      console.warn('[JellyEmu] Stats load failed:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchStats();
  }, [fetchStats]);

  return {
    playtimeStats,
    libraryStats,
    userGames,
    loading,
    reload: fetchStats,
    formatDuration: statsService.formatDuration
  };
}
