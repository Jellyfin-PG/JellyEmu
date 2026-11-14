import React, { useState, useEffect } from 'react';
import { Card, CardBody, Button, Chip, Input, Spinner, Switch, Tooltip } from '@heroui/react';
import { Search, Download, CheckCircle2, AlertCircle, Globe, RefreshCw, Gamepad2, Layers, BookOpen, Sparkles, Plus, Save, Check } from 'lucide-react';
import { useMarketplace } from '../../hooks/useMarketplace';

const SYSTEM_OPTIONS = [
  { label: 'All Systems', value: '' },
  { label: 'NES', value: 'NES' },
  { label: 'SNES', value: 'SNES' },
  { label: 'N64', value: 'N64' },
  { label: 'Genesis', value: 'Sega Genesis' },
  { label: 'GBA', value: 'Game Boy Advance' },
  { label: 'GBC', value: 'Game Boy Color' },
  { label: 'Nintendo DS', value: 'Nintendo DS' },
  { label: 'PlayStation', value: 'PlayStation' },
  { label: 'PICO-8', value: 'PICO-8' }
];

const ALPHABET = ['ALL', '#', 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z'];

export const MarketplaceTab: React.FC = () => {
  const {
    providers,
    loadingProviders,
    feedUrl,
    savingFeedUrl,
    feedUrlSaved,
    updateFeedUrl,
    addCustomProvider,
    toggleProvider,
    reloadProviders,
    roms,
    searching,
    searchError,
    hasSearched,
    downloadStates,
    executeSearch,
    downloadRom
  } = useMarketplace();

  const [inputFeedUrl, setInputFeedUrl] = useState<string>('');
  const [customProviderInput, setCustomProviderInput] = useState<string>('');
  const [addingCustomProvider, setAddingCustomProvider] = useState<boolean>(false);
  const [customProviderMsg, setCustomProviderMsg] = useState<string | null>(null);

  useEffect(() => {
    setInputFeedUrl(feedUrl || '');
  }, [feedUrl]);

  const [searchQuery, setSearchQuery] = useState<string>('');
  const [selectedSystem, setSelectedSystem] = useState<string>('');
  const [selectedLetter, setSelectedLetter] = useState<string>('');
  const [imageErrorMap, setImageErrorMap] = useState<Record<string, boolean>>({});

  const handleSaveFeedUrl = async () => {
    await updateFeedUrl(inputFeedUrl);
  };

  const handleAddCustomProvider = async () => {
    if (!customProviderInput.trim()) return;
    try {
      setAddingCustomProvider(true);
      setCustomProviderMsg(null);
      const ok = await addCustomProvider(customProviderInput.trim());
      if (ok) {
        setCustomProviderMsg('Provider added successfully!');
        setCustomProviderInput('');
        setTimeout(() => setCustomProviderMsg(null), 3000);
      } else {
        setCustomProviderMsg('Failed to add provider. Verify URL format.');
      }
    } finally {
      setAddingCustomProvider(false);
    }
  };

  const handleSearch = (sys = selectedSystem, letter = selectedLetter) => {
    executeSearch(searchQuery, sys, letter === 'ALL' ? '' : letter);
  };

  const handleSystemChange = (sys: string) => {
    setSelectedSystem(sys);
    executeSearch(searchQuery, sys, selectedLetter === 'ALL' ? '' : selectedLetter);
  };

  const handleLetterChange = (letter: string) => {
    const nextLetter = letter === 'ALL' ? '' : letter;
    setSelectedLetter(nextLetter);
    executeSearch(searchQuery, selectedSystem, nextLetter);
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') {
      handleSearch();
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h3 className="text-xl font-bold text-white flex items-center gap-2">
          <Sparkles className="w-5 h-5 text-primary" />
          ROM Marketplace & Explorer
        </h3>
        <p className="text-sm text-slate-300">
          Search, browse, and download retro ROMs directly into your configured Jellyfin games library.
        </p>
      </div>

      {/* Scraper Providers & Custom Feed Section */}
      <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
        <CardBody className="p-5 space-y-5">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Globe className="w-4 h-4 text-primary" />
              <h4 className="text-sm font-semibold text-white">ROM Scraper Sources & Custom Providers</h4>
            </div>
            <Tooltip content="Refresh providers feed">
              <Button
                size="sm"
                variant="light"
                isIconOnly
                onPress={reloadProviders}
                isLoading={loadingProviders}
                aria-label="Refresh providers"
              >
                <RefreshCw className="w-3.5 h-3.5 text-slate-400" />
              </Button>
            </Tooltip>
          </div>

          {/* Marketplace Scraper Feed URL configuration */}
          <div className="space-y-1.5">
            <label className="text-xs font-semibold text-slate-300">Marketplace Scraper Feed URL</label>
            <div className="flex flex-col sm:flex-row gap-2">
              <Input
                size="sm"
                placeholder="e.g. https://example.com/jellyemu-feed.json"
                value={inputFeedUrl}
                onValueChange={setInputFeedUrl}
                variant="bordered"
                startContent={<Globe className="w-4 h-4 text-slate-400" />}
                className="flex-1"
              />
              <Button
                size="sm"
                color={feedUrlSaved ? "success" : "primary"}
                variant={feedUrlSaved ? "flat" : "solid"}
                startContent={feedUrlSaved ? <Check className="w-3.5 h-3.5" /> : <Save className="w-3.5 h-3.5" />}
                isLoading={savingFeedUrl}
                onPress={handleSaveFeedUrl}
                className="font-semibold shrink-0 text-xs"
              >
                {feedUrlSaved ? 'Feed Saved' : 'Save Feed URL'}
              </Button>
            </div>
            <p className="text-[11px] text-slate-400">
              JSON endpoint defining ROM scraper providers, search regexes, and download parsers for the Marketplace.
            </p>
          </div>

          {/* Add Custom Provider URL */}
          <div className="pt-2 border-t border-white/10 space-y-1.5">
            <label className="text-xs font-semibold text-slate-300">Add Custom Provider URL</label>
            <div className="flex flex-col sm:flex-row gap-2">
              <Input
                size="sm"
                placeholder="e.g. https://custom-scraper.example.com"
                value={customProviderInput}
                onValueChange={setCustomProviderInput}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') handleAddCustomProvider();
                }}
                variant="bordered"
                startContent={<Plus className="w-4 h-4 text-slate-400" />}
                className="flex-1"
              />
              <Button
                size="sm"
                variant="flat"
                color="primary"
                startContent={<Plus className="w-3.5 h-3.5" />}
                isLoading={addingCustomProvider}
                onPress={handleAddCustomProvider}
                className="font-semibold shrink-0 text-xs"
              >
                Add Provider
              </Button>
            </div>
            {customProviderMsg && (
              <p className="text-[11px] font-medium text-emerald-400 animate-in fade-in duration-200">
                {customProviderMsg}
              </p>
            )}
          </div>

          {/* Active Providers List */}
          <div className="pt-2 border-t border-white/10 space-y-2">
            <span className="text-xs font-semibold text-slate-300 block">Configured Providers</span>
            <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-3">
              {providers.map((prov) => (
                <div
                  key={prov.domain}
                  className="flex items-center justify-between p-3 rounded-xl bg-white/[0.03] border border-white/5 hover:border-white/15 transition-all"
                >
                  <div className="space-y-0.5 min-w-0 pr-2">
                    <span className="font-semibold text-white text-xs block truncate">{prov.name}</span>
                    <span className="text-[11px] text-slate-400 font-mono block truncate">{prov.domain}</span>
                  </div>
                  <Switch
                    size="sm"
                    color="primary"
                    isSelected={prov.isEnabled}
                    onValueChange={() => toggleProvider(prov)}
                    aria-label={`Toggle ${prov.name}`}
                  />
                </div>
              ))}
              {providers.length === 0 && !loadingProviders && (
                <div className="text-xs text-slate-400 italic p-3 col-span-full rounded-xl bg-white/[0.02] border border-white/5 text-center">
                  No scraper providers configured. Enter a Marketplace Scraper Feed URL or add a custom provider above to get started.
                </div>
              )}
            </div>
          </div>
        </CardBody>
      </Card>

      {/* Explorer Controls: Search, System Filter, Alphabet Filter */}
      <Card className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm">
        <CardBody className="p-5 space-y-5">
          {/* Search Bar */}
          <div className="flex flex-col sm:flex-row gap-3">
            <Input
              size="md"
              placeholder="Search game title (e.g. Super Mario, Zelda, Sonic, Pokemon)..."
              value={searchQuery}
              onValueChange={setSearchQuery}
              onKeyDown={handleKeyDown}
              startContent={<Search className="w-4 h-4 text-slate-400" />}
              isClearable
              onClear={() => {
                setSearchQuery('');
                if (selectedSystem || selectedLetter) {
                  executeSearch('', selectedSystem, selectedLetter);
                }
              }}
              variant="bordered"
              className="flex-1"
            />
            <Button
              color="primary"
              variant="shadow"
              size="md"
              startContent={<Search className="w-4 h-4" />}
              isLoading={searching}
              onPress={() => handleSearch()}
              className="font-semibold px-6 shadow-lg shadow-primary/25"
            >
              Search ROMs
            </Button>
          </div>

          {/* System Filter Grid */}
          <div className="space-y-2">
            <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider flex items-center gap-1.5">
              <Layers className="w-3.5 h-3.5 text-primary" /> Filter by System
            </span>
            <div className="flex flex-wrap gap-2">
              {SYSTEM_OPTIONS.map((sys) => {
                const isSelected = selectedSystem === sys.value;
                return (
                  <Button
                    key={sys.label}
                    size="sm"
                    variant={isSelected ? 'solid' : 'bordered'}
                    color={isSelected ? 'primary' : 'default'}
                    className={`font-medium text-xs transition-all ${
                      isSelected
                        ? 'shadow-md shadow-primary/20'
                        : 'border-white/10 text-slate-300 hover:border-white/25 hover:bg-white/[0.04]'
                    }`}
                    onPress={() => handleSystemChange(sys.value)}
                  >
                    {sys.label}
                  </Button>
                );
              })}
            </div>
          </div>

          {/* Alphabet Filter Row */}
          <div className="space-y-2">
            <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">
              Filter by Starting Letter
            </span>
            <div className="flex items-center gap-1 overflow-x-auto pb-2 scrollbar-thin scrollbar-thumb-white/10">
              {ALPHABET.map((letter) => {
                const activeLetter = selectedLetter === '' ? 'ALL' : selectedLetter;
                const isSelected = activeLetter === letter;
                return (
                  <button
                    key={letter}
                    type="button"
                    onClick={() => handleLetterChange(letter)}
                    className={`min-w-8 h-8 px-2 rounded-lg text-xs font-semibold transition-all flex items-center justify-center shrink-0 ${
                      isSelected
                        ? 'bg-primary text-white shadow-sm shadow-primary/40'
                        : 'bg-white/[0.04] text-slate-400 hover:bg-white/10 hover:text-white border border-white/5'
                    }`}
                  >
                    {letter}
                  </button>
                );
              })}
            </div>
          </div>
        </CardBody>
      </Card>

      {/* Results Status or Loading */}
      {searching && (
        <div className="flex flex-col items-center justify-center p-12 space-y-3 bg-[#18181b]/50 border border-white/10 rounded-2xl">
          <Spinner size="lg" color="primary" />
          <p className="text-sm text-slate-300 font-medium">Searching ROM sources and repositories...</p>
        </div>
      )}

      {searchError && !searching && (
        <Card className="bg-danger-500/10 border border-danger-500/30">
          <CardBody className="p-4 flex items-center gap-3 text-danger-300 text-sm">
            <AlertCircle className="w-5 h-5 shrink-0 text-danger-400" />
            <div className="flex-1">
              <strong className="font-semibold block">Search Error</strong>
              <span>{searchError}</span>
            </div>
          </CardBody>
        </Card>
      )}

      {/* Empty State Before Search */}
      {!hasSearched && !searching && (
        <div className="flex flex-col items-center justify-center p-14 text-center space-y-3 bg-[#18181b]/40 border border-dashed border-white/15 rounded-2xl">
          <div className="w-14 h-14 rounded-2xl bg-white/5 border border-white/10 flex items-center justify-center text-slate-400">
            <Gamepad2 className="w-7 h-7" />
          </div>
          <div className="max-w-md space-y-1">
            <h4 className="text-base font-bold text-white">Find and Download Retro Games</h4>
            <p className="text-xs text-slate-400">
              Type a title in the search box above or pick a console system to discover and download ROMs directly into your library.
            </p>
          </div>
        </div>
      )}

      {/* No Results Found */}
      {hasSearched && !searching && roms.length === 0 && !searchError && (
        <div className="flex flex-col items-center justify-center p-14 text-center space-y-3 bg-[#18181b]/40 border border-white/10 rounded-2xl">
          <div className="w-12 h-12 rounded-xl bg-white/5 border border-white/10 flex items-center justify-center text-slate-400">
            <Search className="w-6 h-6" />
          </div>
          <div className="max-w-sm space-y-1">
            <h4 className="text-base font-bold text-white">No ROMs Found</h4>
            <p className="text-xs text-slate-400">
              No matching titles found for your search criteria. Try a different search term or ensure your scraper provider is enabled.
            </p>
          </div>
        </div>
      )}

      {/* Results Grid */}
      {!searching && roms.length > 0 && (
        <div className="space-y-3">
          <div className="flex items-center justify-between text-xs text-slate-400 px-1">
            <span>Found <strong className="text-white font-semibold">{roms.length}</strong> ROMs</span>
            {selectedSystem && <span>Filtered by: <strong className="text-primary">{selectedSystem}</strong></span>}
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
            {roms.map((rom, idx) => {
              const downloadInfo = downloadStates[rom.detailUrl];
              const dlStatus = downloadInfo?.status || 'idle';
              const hasImgErr = imageErrorMap[rom.thumbnailUrl];

              return (
                <Card
                  key={`${rom.detailUrl}-${idx}`}
                  className="bg-[#18181b]/90 border border-white/10 shadow-md backdrop-blur-sm hover:border-primary/40 transition-all flex flex-col justify-between"
                >
                  <CardBody className="p-4 space-y-3 flex flex-col justify-between">
                    <div className="space-y-3">
                      {/* Thumbnail / Boxart Container */}
                      <div className="w-full h-36 rounded-xl bg-black/40 border border-white/10 flex items-center justify-center overflow-hidden relative">
                        {rom.thumbnailUrl && !hasImgErr ? (
                          <img
                            src={rom.thumbnailUrl}
                            alt={rom.title}
                            className="w-full h-full object-contain p-2 hover:scale-105 transition-transform duration-300"
                            onError={() =>
                              setImageErrorMap((prev) => ({ ...prev, [rom.thumbnailUrl]: true }))
                            }
                            loading="lazy"
                          />
                        ) : (
                          <div className="flex flex-col items-center justify-center text-slate-500 gap-1.5">
                            <Gamepad2 className="w-8 h-8 opacity-40" />
                            <span className="text-[10px] uppercase font-mono tracking-wider">{rom.system || 'Retro Game'}</span>
                          </div>
                        )}

                        {rom.hasManual && (
                          <div className="absolute top-2 right-2">
                            <Tooltip content="Game manual included">
                              <Chip size="sm" variant="solid" color="secondary" className="h-5 text-[10px] font-bold px-1">
                                <BookOpen className="w-3 h-3 inline mr-1" /> MANUAL
                              </Chip>
                            </Tooltip>
                          </div>
                        )}
                      </div>

                      {/* Title & Metadata */}
                      <div className="space-y-1.5">
                        <Tooltip content={rom.title}>
                          <h4 className="font-bold text-white text-sm line-clamp-2 leading-snug">
                            {rom.title}
                          </h4>
                        </Tooltip>

                        {/* Badges */}
                        <div className="flex flex-wrap gap-1.5 pt-1">
                          {rom.system && (
                            <Chip
                              size="sm"
                              variant="flat"
                              color="primary"
                              className="text-[10px] h-5 font-semibold"
                            >
                              {rom.system}
                            </Chip>
                          )}
                          {rom.region && (
                            <Chip
                              size="sm"
                              variant="flat"
                              color="default"
                              className="text-[10px] h-5 border border-white/10 text-slate-300"
                            >
                              {rom.region}
                            </Chip>
                          )}
                          {rom.version && (
                            <Chip
                              size="sm"
                              variant="flat"
                              color="default"
                              className="text-[10px] h-5 border border-white/10 text-slate-400 font-mono"
                            >
                              v{rom.version}
                            </Chip>
                          )}
                          {rom.providerName && (
                            <Chip
                              size="sm"
                              variant="bordered"
                              className="text-[10px] h-5 border-white/15 text-slate-400"
                            >
                              {rom.providerName}
                            </Chip>
                          )}
                        </div>
                      </div>
                    </div>

                    {/* Download Action Footer */}
                    <div className="pt-3 border-t border-white/10 space-y-1.5">
                      {dlStatus === 'idle' && (
                        <Button
                          size="sm"
                          color="primary"
                          variant="solid"
                          startContent={<Download className="w-4 h-4" />}
                          onPress={() => downloadRom(rom)}
                          className="w-full font-semibold shadow-sm shadow-primary/20"
                        >
                          Download to Library
                        </Button>
                      )}

                      {dlStatus === 'downloading' && (
                        <Button
                          size="sm"
                          color="primary"
                          variant="flat"
                          isLoading
                          className="w-full font-semibold"
                        >
                          Downloading ROM...
                        </Button>
                      )}

                      {dlStatus === 'success' && (
                        <Button
                          size="sm"
                          color="success"
                          variant="flat"
                          startContent={<CheckCircle2 className="w-4 h-4" />}
                          className="w-full font-semibold cursor-default"
                        >
                          Downloaded
                        </Button>
                      )}

                      {dlStatus === 'error' && (
                        <div className="space-y-1">
                          <Button
                            size="sm"
                            color="danger"
                            variant="flat"
                            startContent={<AlertCircle className="w-4 h-4" />}
                            onPress={() => downloadRom(rom)}
                            className="w-full font-semibold"
                          >
                            Retry Download
                          </Button>
                          {downloadInfo?.message && (
                            <p className="text-[11px] text-danger-400 leading-tight">
                              {downloadInfo.message}
                            </p>
                          )}
                        </div>
                      )}
                    </div>
                  </CardBody>
                </Card>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
};
