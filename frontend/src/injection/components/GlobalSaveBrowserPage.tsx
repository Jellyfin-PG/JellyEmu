import React, { useEffect, useState } from 'react';
import {
  Button,
  Spinner,
  Card,
  CardBody
} from '@heroui/react';
import {
  Save,
  Play,
  Trash2,
  Calendar,
  HardDrive,
  Gamepad2,
  Image as ImageIcon,
  Search,
  Download,
  Globe
} from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { injectionService } from '../services/injectionService';
import { jellyfinApi } from '../../shared/apiClient';
import { CustomSelect } from '../../shared/CustomSelect';
import { SaveStateEntry } from '../../types/injection';

const containerVariants = {
  hidden: { opacity: 0 },
  show: {
    opacity: 1,
    transition: {
      staggerChildren: 0.04
    }
  }
};

const cardVariants = {
  hidden: { opacity: 0, y: 15, scale: 0.95 },
  show: {
    opacity: 1,
    y: 0,
    scale: 1,
    transition: { type: 'spring', stiffness: 400, damping: 24 }
  },
  exit: {
    opacity: 0,
    scale: 0.8,
    y: -10,
    transition: { duration: 0.2 }
  }
};

export const GlobalSaveBrowserPage: React.FC = () => {
  const [saves, setSaves] = useState<SaveStateEntry[]>([]);
  const [screenshots, setScreenshots] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [filterQuery, setFilterQuery] = useState('');
  const [selectedSlot, setSelectedSlot] = useState<string>('');
  const [selectedPlatform, setSelectedPlatform] = useState<string>('');

  const loadSaves = async () => {
    setLoading(true);
    const userId = injectionService.getCurrentUserId();
    if (userId) {
      const data = await injectionService.getAllUserSaves(userId);
      setSaves(data);

      data.forEach(s => {
        if (s.hasScreenshot) {
          const key = `${s.itemId}-${s.slot}`;
          injectionService.getSaveScreenshot(s.itemId, userId, s.slot).then(dataUrl => {
            if (dataUrl) {
              setScreenshots(prev => ({ ...prev, [key]: dataUrl }));
            }
          });
        }
      });
    }
    setLoading(false);
  };

  useEffect(() => {
    loadSaves();
  }, []);

  const handleDelete = async (itemId: string, slot: number) => {
    if (!window.confirm(`Are you sure you want to delete save slot ${slot}?`)) return;
    const userId = injectionService.getCurrentUserId();
    if (!userId) return;

    const ok = await injectionService.deleteSaveSlot(itemId, userId, slot);
    if (ok) {
      setSaves(prev => prev.filter(s => !(s.itemId === itemId && s.slot === slot)));
      injectionService.toast(`Save slot ${slot} deleted`);
    } else {
      injectionService.toast('Failed to delete save slot');
    }
  };

  const formatSize = (bytes?: number) => {
    if (!bytes) return '0 KB';
    return (bytes / 1024).toFixed(1) + ' KB';
  };

  const genericNonPlatformTags = new Set(['game', 'jellyemu', 'unsupported', 'unknown']);
  const platforms = Array.from(
    new Set(
      saves
        .map(s => s.platform)
        .filter((p): p is string => Boolean(p && !genericNonPlatformTags.has(p.trim().toLowerCase())))
    )
  ).sort();

  const filtered = saves.filter(s => {
    const title = s.gameName || s.itemName || s.itemId;
    const matchesQuery =
      title.toLowerCase().includes(filterQuery.toLowerCase()) ||
      (s.platform || '').toLowerCase().includes(filterQuery.toLowerCase());
    const matchesSlot = selectedSlot ? String(s.slot) === selectedSlot : true;
    const matchesPlatform = selectedPlatform ? s.platform === selectedPlatform : true;
    return matchesQuery && matchesSlot && matchesPlatform;
  });

  if (loading) {
    return (
      <div className="flex flex-col items-center justify-center py-28 space-y-3">
        <Spinner size="lg" color="primary" />
        <p className="text-xs text-default-400">Loading user save states...</p>
      </div>
    );
  }

  const userId = injectionService.getCurrentUserId() || '';

  return (
    <motion.div
      initial={{ opacity: 0, y: 15 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.35, ease: 'easeOut' }}
      className="max-w-7xl mx-auto p-4 sm:p-6 lg:p-8 space-y-6"
    >
      {/* Header Bar */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 pb-5 border-b border-white/10">
        <div className="flex items-center gap-3">
          <motion.div
            initial={{ scale: 0.8, rotate: -10 }}
            animate={{ scale: 1, rotate: 0 }}
            transition={{ type: 'spring', stiffness: 400, damping: 20 }}
            className="w-10 h-10 rounded-xl bg-white/10 border border-white/15 flex items-center justify-center text-white shadow-md"
          >
            <Save className="w-5 h-5" />
          </motion.div>
          <div>
            <h1 className="text-xl sm:text-2xl font-bold text-white flex items-center gap-2">
              Save State Browser
            </h1>
            <p className="text-xs sm:text-sm text-default-400">
              Manage, download, and launch save states across your retro collection
            </p>
          </div>
        </div>

        {/* Filters & Search */}
        <div className="flex flex-wrap items-center gap-3">
          {/* Slot Filter */}
          <div className="w-36">
            <CustomSelect
              value={selectedSlot}
              onChange={val => setSelectedSlot(val)}
              options={[
                { value: '', label: 'All Slots' },
                { value: '1', label: 'Slot 1' },
                { value: '2', label: 'Slot 2' },
                { value: '3', label: 'Slot 3' },
                { value: '4', label: 'Slot 4' },
                { value: '5', label: 'Slot 5' }
              ]}
              size="sm"
            />
          </div>

          {/* Platform Filter */}
          {platforms.length > 0 && (
            <div className="w-44">
              <CustomSelect
                value={selectedPlatform}
                onChange={val => setSelectedPlatform(val)}
                options={[
                  { value: '', label: 'All Platforms' },
                  ...platforms.map(p => ({ value: p, label: p }))
                ]}
                size="sm"
              />
            </div>
          )}

          {/* Search Box */}
          <div className="relative">
            <Search className="w-3.5 h-3.5 text-neutral-400 absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" />
            <input
              type="text"
              placeholder="Search games..."
              value={filterQuery}
              onChange={e => setFilterQuery(e.target.value)}
              className="pl-8 pr-3 py-2 rounded-xl bg-[#16161a] border border-white/15 text-xs text-white placeholder-neutral-400 focus:outline-none focus:border-white/50 focus:ring-1 focus:ring-white/25 w-44 sm:w-56 transition-colors"
            />
          </div>

          <div className="px-2.5 py-1 rounded-full bg-white/10 border border-white/15 text-xs font-bold text-white">
            {filtered.length} {filtered.length === 1 ? 'save' : 'saves'}
          </div>
        </div>
      </div>

      {/* Save States Grid */}
      {filtered.length === 0 ? (
        <motion.div
          initial={{ opacity: 0, scale: 0.95 }}
          animate={{ opacity: 1, scale: 1 }}
          className="text-center py-24 space-y-3 bg-white/5 rounded-2xl border border-white/10"
        >
          <div className="w-12 h-12 rounded-2xl bg-white/10 flex items-center justify-center text-white mx-auto shadow-inner border border-white/10">
            <HardDrive className="w-6 h-6" />
          </div>
          <p className="text-sm font-semibold text-white">No Save States Found</p>
          <p className="text-xs text-neutral-400 max-w-sm mx-auto">
            Save states created in EmulatorJS during gameplay will automatically appear here.
          </p>
        </motion.div>
      ) : (
        <motion.div
          variants={containerVariants}
          initial="hidden"
          animate="show"
          className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4 sm:gap-5"
        >
          <AnimatePresence mode="popLayout">
            {filtered.map(s => {
              const key = `${s.itemId}-${s.slot}`;
              const title = s.gameName || s.itemName || s.itemId;
              const ssUrl = screenshots[key];
              const primaryArtUrl = s.hasArt
                ? jellyfinApi.getApiUrl(`Items/${s.itemId}/Images/Primary?maxHeight=420&quality=90`)
                : null;
              const downloadUrl = s.downloadUrl || jellyfinApi.getApiUrl(`jellyemu/save/${s.itemId}/${userId}?slot=${s.slot}`);

              return (
                <motion.div
                  key={key}
                  variants={cardVariants}
                  layout
                  whileHover={{ y: -4, scale: 1.01 }}
                  transition={{ type: 'spring', stiffness: 450, damping: 25 }}
                >
                  <Card className="bg-[#121216]/90 border border-white/10 shadow-md hover:border-white/30 hover:shadow-xl transition-all duration-200 group">
                    <CardBody className="p-4 space-y-3">
                      <div className="flex items-start justify-between gap-2">
                        <div className="min-w-0">
                          <h3 className="text-xs sm:text-sm font-semibold text-white truncate" title={title}>
                            {title}
                          </h3>
                          {s.platform && (
                            <p className="text-[11px] text-neutral-400 flex items-center gap-1 mt-0.5">
                              <Gamepad2 className="w-3 h-3 text-white shrink-0" />
                              <span className="truncate">{s.platform}</span>
                            </p>
                          )}
                        </div>
                        <span className="px-2 py-0.5 rounded-md bg-white/10 border border-white/15 text-[10px] font-bold text-white shrink-0">
                          Slot {s.slot}
                        </span>
                      </div>

                      {/* Screenshot / Cover Art Preview */}
                      <div className="aspect-video w-full rounded-xl overflow-hidden bg-black/60 border border-white/10 relative flex items-center justify-center">
                        {ssUrl ? (
                          <img
                            src={ssUrl}
                            alt={`Slot ${s.slot}`}
                            className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                          />
                        ) : primaryArtUrl ? (
                          <img
                            src={primaryArtUrl}
                            alt={title}
                            className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                          />
                        ) : (
                          <div className="flex items-center justify-center text-neutral-500">
                            <ImageIcon className="w-8 h-8 opacity-30" />
                          </div>
                        )}

                        {/* Badges Overlay */}
                        <div className="absolute bottom-2 left-2 flex flex-wrap gap-1 pointer-events-none">
                          {s.region && (
                            <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded bg-black/80 border border-white/20 text-white text-[9px] font-bold backdrop-blur-md shadow">
                              <Globe className="w-2.5 h-2.5" />
                              {s.region}
                            </span>
                          )}
                          {s.disc && (
                            <span className="inline-flex items-center px-1.5 py-0.5 rounded bg-black/80 border border-white/20 text-white text-[9px] font-bold backdrop-blur-md shadow">
                              {s.disc}
                            </span>
                          )}
                        </div>
                      </div>

                      {/* Metadata line */}
                      <div className="flex items-center justify-between text-xs text-neutral-400 pt-1">
                        {s.lastModified && (
                          <span className="flex items-center gap-1 text-[11px]">
                            <Calendar className="w-3 h-3 text-neutral-400" />
                            {new Date(s.lastModified).toLocaleDateString()}
                          </span>
                        )}
                        <span className="font-mono text-[10px] text-neutral-400">{formatSize(s.sizeBytes)}</span>
                      </div>

                      {/* Action Buttons */}
                      <div className="flex items-center gap-2 pt-2 border-t border-white/10">
                        <motion.div whileTap={{ scale: 0.94 }} className="flex-1">
                          <Button
                            size="sm"
                            variant="solid"
                            startContent={<Play className="w-3.5 h-3.5 fill-black text-black" />}
                            className="w-full font-semibold text-xs bg-white hover:bg-neutral-200 active:bg-neutral-300 text-black border border-white/40 shadow-sm cursor-pointer"
                            onPress={() => injectionService.launchEmulator(s.itemId, s.slot)}
                          >
                            Play Slot
                          </Button>
                        </motion.div>
                        <motion.a
                          whileHover={{ scale: 1.08 }}
                          whileTap={{ scale: 0.92 }}
                          href={downloadUrl}
                          download={`${title.replace(/[^a-zA-Z0-9 _-]/g, '_')}_slot${s.slot}.state`}
                          title="Download Save State"
                          className="p-2 rounded-lg bg-white/5 hover:bg-white/15 text-neutral-300 hover:text-white border border-white/10 transition-colors flex items-center justify-center shrink-0 cursor-pointer"
                        >
                          <Download className="w-3.5 h-3.5" />
                        </motion.a>
                        <motion.div whileHover={{ scale: 1.08 }} whileTap={{ scale: 0.92 }}>
                          <Button
                            size="sm"
                            variant="light"
                            isIconOnly
                            onPress={() => handleDelete(s.itemId, s.slot)}
                            title="Delete Save Slot"
                            className="text-neutral-400 hover:text-red-400 hover:bg-red-500/10 cursor-pointer"
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </Button>
                        </motion.div>
                      </div>
                    </CardBody>
                  </Card>
                </motion.div>
              );
            })}
          </AnimatePresence>
        </motion.div>
      )}
    </motion.div>
  );
};
