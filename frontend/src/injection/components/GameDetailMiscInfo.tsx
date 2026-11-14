import React, { useEffect, useState } from 'react';
import {
  Clock,
  Save,
  Trophy,
  BookOpen,
  Map as MapIcon,
  Compass,
  AlertTriangle,
  Globe
} from 'lucide-react';
import { motion } from 'framer-motion';
import { injectionService } from '../services/injectionService';
import {
  SaveStateEntry,
  RetroAchievementsProgress,
  GameGuideData
} from '../../types/injection';
import { getConsoleColor, getRegionColorClasses } from '../../shared/colors';

interface GameDetailMiscInfoProps {
  itemId: string;
  tags: string[];
  providerIds: Record<string, string>;
  onOpenSettings?: (tab?: string) => void;
}

const KNOWN_REGIONS = new Set([
  'USA', 'Europe', 'Japan', 'World', 'Australia', 'Brazil', 'Canada', 'China',
  'France', 'Germany', 'Italy', 'Korea', 'Netherlands', 'Russia', 'Spain', 'Sweden',
  'Asia', 'Scandinavia', 'Unlicensed', 'Prototype', 'Demo', 'Sample'
]);

const UNSUPPORTED_PLATFORMS = new Set([
  'Dreamcast', 'PlayStation 3', 'Xbox', 'Xbox 360', 'GameCube', 'Wii', 'Wii U',
  'Nintendo Switch', 'PlayStation Vita', 'Windows', 'Linux', 'MacOS', 'Android', 'Unsupported'
]);

const containerVariants = {
  hidden: { opacity: 0 },
  show: {
    opacity: 1,
    transition: {
      staggerChildren: 0.04
    }
  }
};

const pillVariants = {
  hidden: { opacity: 0, scale: 0.85, y: 5 },
  show: {
    opacity: 1,
    scale: 1,
    y: 0,
    transition: { type: 'spring', stiffness: 450, damping: 22 }
  }
};

export const GameDetailMiscInfo: React.FC<GameDetailMiscInfoProps> = ({
  itemId,
  tags,
  providerIds,
  onOpenSettings
}) => {
  const [saveSlots, setSaveSlots] = useState<SaveStateEntry[]>([]);
  const [playtimeSeconds, setPlaytimeSeconds] = useState<number | null>(null);
  const [raProgress, setRaProgress] = useState<RetroAchievementsProgress | null>(null);
  const [guides, setGuides] = useState<GameGuideData | null>(null);

  useEffect(() => {
    const userId = injectionService.getCurrentUserId();
    if (!userId || !itemId) return;

    // Fetch save slots
    injectionService.getSaveSlots(itemId, userId).then(setSaveSlots);

    // Fetch playtime
    injectionService.getPlaytime(itemId, userId).then(res => {
      if (res && typeof res.seconds === 'number') {
        setPlaytimeSeconds(res.seconds);
      }
    });

    // Fetch RetroAchievements
    injectionService.getRetroAchievementsProgress(itemId, userId).then(setRaProgress);

    // Fetch ScreenScraper guides
    const ssId = providerIds.ScreenScraper || providerIds.screenscraper || '';
    injectionService.getGuides(itemId, ssId).then(setGuides);
  }, [itemId, providerIds]);

  // Tag categorization
  const systemTags = tags.filter(
    t => t !== 'Game' && t !== 'JellyEmu' && t !== 'Unsupported' && !KNOWN_REGIONS.has(t) && !/^Dis[ck]\s+[1-9IVX]/i.test(t)
  );
  const regionTags = tags.filter(t => KNOWN_REGIONS.has(t));
  const discTags = tags.filter(t => /^Dis[ck]\s+[1-9IVX]/i.test(t));

  // Time to Beat parsing
  const ttbRaw = providerIds.IgdbTTB;
  const ttbPills: { label: string; text: string }[] = [];
  if (ttbRaw) {
    const parts = ttbRaw.split(',');
    const map: Record<string, string> = {};
    parts.forEach(p => {
      if (p.length > 1) map[p[0]] = p.substring(1);
    });
    if (map.M) ttbPills.push({ label: 'Main Story', text: `${map.M}h` });
    if (map.H) ttbPills.push({ label: 'Main + Extra', text: `${map.H}h` });
    if (map.C) ttbPills.push({ label: 'Completionist', text: `${map.C}h` });
  }

  // Playtime formatting
  const formatPlaytime = (sec: number) => {
    if (sec === 0) return '0m played';
    const h = Math.floor(sec / 3600);
    const m = Math.floor((sec % 3600) / 60);
    if (h > 0) return `${h}h ${m}m played`;
    if (m > 0) return `${m}m played`;
    return '<1m played';
  };

  const isUnknown = tags.includes('Unknown');
  const isUnsupported = !isUnknown && tags.some(t => UNSUPPORTED_PLATFORMS.has(t));

  return (
    <motion.div
      variants={containerVariants}
      initial="hidden"
      animate="show"
      className="je-misc-info-wrapper inline-flex flex-wrap items-center gap-1.5 text-xs text-default-400 py-1"
    >
      {/* System Tags */}
      {systemTags.map(tag => {
        const c = getConsoleColor(tag);
        return (
          <motion.span
            key={tag}
            variants={pillVariants}
            style={{
              backgroundColor: `${c}22`,
              borderColor: `${c}55`,
              color: c
            }}
            className="inline-flex items-center px-2 py-0.5 rounded-md border font-semibold text-[11px] shadow-sm backdrop-blur-sm"
          >
            {tag}
          </motion.span>
        );
      })}

      {/* Region Tags */}
      {regionTags.map(tag => {
        const rc = getRegionColorClasses(tag);
        return (
          <motion.span
            key={tag}
            variants={pillVariants}
            className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md ${rc.bg} border ${rc.border} ${rc.text} font-medium text-[11px] shadow-sm backdrop-blur-sm`}
          >
            <Globe className={`w-3 h-3 ${rc.icon}`} />
            {tag}
          </motion.span>
        );
      })}

      {/* Disc Tags */}
      {discTags.map(tag => (
        <motion.span
          key={tag}
          variants={pillVariants}
          className="inline-flex items-center px-2 py-0.5 rounded-md bg-amber-500/15 border border-amber-500/35 text-amber-300 text-[11px] font-semibold shadow-sm"
        >
          {tag}
        </motion.span>
      ))}

      {/* Time to Beat */}
      {ttbPills.length > 0 && (
        <motion.span
          variants={pillVariants}
          className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-md bg-indigo-500/15 border border-indigo-500/35 text-indigo-300 text-[11px] font-medium shadow-sm"
          title={ttbPills.map(p => `${p.label}: ${p.text}`).join(' | ')}
        >
          <Clock className="w-3 h-3 text-indigo-400" />
          TTB: {ttbPills.map(p => p.text).join(' • ')}
        </motion.span>
      )}

      {/* Playtime */}
      {playtimeSeconds !== null && (
        <motion.span
          variants={pillVariants}
          className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-md bg-emerald-500/15 border border-emerald-500/35 text-emerald-300 text-[11px] font-semibold shadow-sm backdrop-blur-sm"
          title={`${playtimeSeconds} seconds tracked`}
        >
          <Clock className="w-3 h-3 text-emerald-400" />
          {formatPlaytime(playtimeSeconds)}
        </motion.span>
      )}

      {/* Save Slots */}
      {saveSlots.map(s => (
        <motion.button
          key={s.slot}
          variants={pillVariants}
          whileHover={{ scale: 1.06, y: -1 }}
          whileTap={{ scale: 0.94 }}
          type="button"
          onClick={() => injectionService.launchEmulator(itemId, s.slot)}
          title={`Slot ${s.slot} (${s.lastModified ? new Date(s.lastModified).toLocaleDateString() : 'Ready'}) — Click to load`}
          className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-md bg-blue-500/15 hover:bg-blue-500/25 border border-blue-500/35 text-blue-300 text-[11px] font-semibold transition-colors cursor-pointer shadow-sm"
        >
          <Save className="w-3 h-3 text-blue-400" />
          Slot {s.slot}
        </motion.button>
      ))}

      {/* RetroAchievements */}
      {raProgress && (
        raProgress.error === 'unauthorized' ? (
          <motion.button
            variants={pillVariants}
            whileHover={{ scale: 1.05 }}
            whileTap={{ scale: 0.95 }}
            type="button"
            onClick={() => {
              if (onOpenSettings) {
                onOpenSettings('ra');
              } else {
                const userId = injectionService.getCurrentUserId();
                const userParam = userId ? `userId=${userId}&` : '';
                window.location.hash = `#/jellyemu-settings?${userParam}tab=ra`;
              }
            }}
            className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md bg-default-100/60 hover:bg-default-200/70 border border-divider/35 text-default-400 text-[11px] transition-colors cursor-pointer"
            title="Click to sign in to RetroAchievements"
          >
            <Trophy className="w-3 h-3 opacity-50" />
            RA: Sign In
          </motion.button>
        ) : (
          <motion.a
            variants={pillVariants}
            whileHover={{ scale: 1.05, y: -1 }}
            whileTap={{ scale: 0.95 }}
            href={raProgress.raGameUrl || 'https://retroachievements.org'}
            target="_blank"
            rel="noopener noreferrer"
            title={`RetroAchievements: ${raProgress.numUnlocked || 0}/${raProgress.numTotal || 0} Unlocked (${raProgress.progressPercent || 0}%)`}
            className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-md bg-amber-500/15 hover:bg-amber-500/25 border border-amber-500/35 text-amber-200 text-[11px] font-semibold transition-colors shadow-sm"
          >
            <Trophy className="w-3.5 h-3.5 text-amber-400" />
            <span>{raProgress.numUnlocked || 0}/{raProgress.numTotal || 0}</span>
            {typeof raProgress.progressPercent === 'number' && (
              <div className="w-8 h-1.5 rounded-full bg-black/40 overflow-hidden ml-0.5">
                <div
                  className="h-full bg-amber-400 transition-all duration-300"
                  style={{ width: `${Math.min(100, Math.max(0, raProgress.progressPercent))}%` }}
                />
              </div>
            )}
          </motion.a>
        )
      )}

      {/* ScreenScraper Manual */}
      {guides?.manualUrl && (
        <motion.a
          variants={pillVariants}
          whileHover={{ scale: 1.06, y: -1 }}
          whileTap={{ scale: 0.94 }}
          href={guides.manualUrl}
          target="_blank"
          rel="noopener noreferrer"
          title="View Official Game Manual (PDF)"
          className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-md bg-cyan-500/15 hover:bg-cyan-500/25 border border-cyan-500/35 text-cyan-300 text-[11px] font-medium transition-colors shadow-sm"
        >
          <BookOpen className="w-3 h-3 text-cyan-400" />
          Manual
        </motion.a>
      )}

      {/* ScreenScraper Map */}
      {guides?.mapUrl && (
        <motion.a
          variants={pillVariants}
          whileHover={{ scale: 1.06, y: -1 }}
          whileTap={{ scale: 0.94 }}
          href={guides.mapUrl}
          target="_blank"
          rel="noopener noreferrer"
          title="View Game Map"
          className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-md bg-emerald-500/15 hover:bg-emerald-500/25 border border-emerald-500/35 text-emerald-300 text-[11px] font-medium transition-colors shadow-sm"
        >
          <MapIcon className="w-3 h-3 text-emerald-400" />
          Map
        </motion.a>
      )}

      {/* ScreenScraper Guide */}
      {guides?.guideUrl && (
        <motion.a
          variants={pillVariants}
          whileHover={{ scale: 1.06, y: -1 }}
          whileTap={{ scale: 0.94 }}
          href={guides.guideUrl}
          target="_blank"
          rel="noopener noreferrer"
          title="View Game Guide & Info"
          className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-md bg-purple-500/15 hover:bg-purple-500/25 border border-purple-500/35 text-purple-300 text-[11px] font-medium transition-colors shadow-sm"
        >
          <Compass className="w-3 h-3 text-purple-400" />
          Guide
        </motion.a>
      )}

      {/* Unsupported Warning */}
      {(isUnknown || isUnsupported) && (
        <motion.span
          variants={pillVariants}
          className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-md bg-amber-500/15 border border-amber-500/35 text-amber-300 text-[11px] font-semibold shadow-sm"
          title={isUnknown ? 'Platform could not be detected' : 'This platform requires external emulation'}
        >
          <AlertTriangle className="w-3 h-3 text-amber-400" />
          {isUnknown ? 'Unknown Platform' : 'External Only'}
        </motion.span>
      )}
    </motion.div>
  );
};
