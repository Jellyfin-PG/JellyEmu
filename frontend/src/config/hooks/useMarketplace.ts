import { useState, useEffect, useCallback } from 'react';
import { marketplaceService } from '../services/marketplaceService';
import { FeedProvider, MarketplaceRom } from '../../types/marketplace';

export type DownloadStatus = 'idle' | 'downloading' | 'success' | 'error';

export function useMarketplace() {
  const [providers, setProviders] = useState<FeedProvider[]>([]);
  const [loadingProviders, setLoadingProviders] = useState<boolean>(true);
  const [feedUrl, setFeedUrl] = useState<string>('');
  const [savingFeedUrl, setSavingFeedUrl] = useState<boolean>(false);
  const [feedUrlSaved, setFeedUrlSaved] = useState<boolean>(false);
  
  const [roms, setRoms] = useState<MarketplaceRom[]>([]);
  const [searching, setSearching] = useState<boolean>(false);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [hasSearched, setHasSearched] = useState<boolean>(false);

  const [downloadStates, setDownloadStates] = useState<Record<string, { status: DownloadStatus; message?: string }>>({});

  const fetchProviders = useCallback(async () => {
    try {
      setLoadingProviders(true);
      const [data, currentFeedUrl] = await Promise.all([
        marketplaceService.getFeedProviders(),
        marketplaceService.getFeedUrl()
      ]);
      setProviders(data);
      setFeedUrl(currentFeedUrl);
    } catch (err) {
      console.warn('[JellyEmu] Marketplace providers load failed:', err);
    } finally {
      setLoadingProviders(false);
    }
  }, []);

  useEffect(() => {
    fetchProviders();
  }, [fetchProviders]);

  const updateFeedUrl = async (newUrl: string) => {
    setSavingFeedUrl(true);
    const ok = await marketplaceService.updateFeedUrl(newUrl);
    if (ok) {
      setFeedUrl(newUrl);
      setFeedUrlSaved(true);
      setTimeout(() => setFeedUrlSaved(false), 3000);
      await fetchProviders();
    }
    setSavingFeedUrl(false);
    return ok;
  };

  const addCustomProvider = async (providerUrl: string) => {
    const ok = await marketplaceService.addProvider(providerUrl);
    if (ok) {
      await fetchProviders();
    }
    return ok;
  };

  const toggleProvider = async (provider: FeedProvider) => {
    const nextState = !provider.isEnabled;
    // Optimistic update
    setProviders(prev =>
      prev.map(p => (p.url === provider.url ? { ...p, isEnabled: nextState } : p))
    );

    const success = nextState
      ? await marketplaceService.addProvider(provider.url)
      : await marketplaceService.removeProvider(provider.url);

    if (!success) {
      // Revert if failed
      setProviders(prev =>
        prev.map(p => (p.url === provider.url ? { ...p, isEnabled: !nextState } : p))
      );
    }
  };

  const executeSearch = async (query: string, system = '', letter = '') => {
    const trimmedQuery = query.trim();
    if (!trimmedQuery && !system && !letter) {
      setRoms([]);
      setHasSearched(false);
      setSearchError(null);
      return;
    }

    try {
      setSearching(true);
      setSearchError(null);
      setHasSearched(true);

      let results: MarketplaceRom[] = [];
      if (trimmedQuery) {
        results = await marketplaceService.search(trimmedQuery, system, letter);
      } else {
        // Find first active provider for browsing
        const activeProv = providers.find(p => p.isEnabled)?.domain || providers[0]?.domain || '';
        if (activeProv) {
          results = await marketplaceService.browse(activeProv, system || 'NES', letter || 'A');
        }
      }

      setRoms(results);
    } catch (err: any) {
      console.error('[JellyEmu] Search error:', err);
      setSearchError(err?.message || 'Search request failed. Check server logs.');
      setRoms([]);
    } finally {
      setSearching(false);
    }
  };

  const downloadRom = async (rom: MarketplaceRom) => {
    const key = rom.detailUrl;
    setDownloadStates(prev => ({
      ...prev,
      [key]: { status: 'downloading' }
    }));

    const res = await marketplaceService.downloadRom(rom.detailUrl, rom.system);

    setDownloadStates(prev => ({
      ...prev,
      [key]: {
        status: res.success ? 'success' : 'error',
        message: res.message
      }
    }));
  };

  return {
    providers,
    loadingProviders,
    feedUrl,
    savingFeedUrl,
    feedUrlSaved,
    updateFeedUrl,
    addCustomProvider,
    toggleProvider,
    reloadProviders: fetchProviders,
    roms,
    searching,
    searchError,
    hasSearched,
    downloadStates,
    executeSearch,
    downloadRom
  };
}
