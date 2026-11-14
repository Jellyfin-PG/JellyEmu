import React from 'react';
import { Gamepad2, Star, AlertTriangle, Globe } from 'lucide-react';
import { injectionService } from '../services/injectionService';
import { CardMeta } from '../../types/injection';
import { getConsoleColor, getRegionColorClasses } from '../../shared/colors';

interface GameCardOverlayProps {
  itemId: string;
  meta: CardMeta | null;
  isPlayable: boolean;
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

export const GameCardOverlay: React.FC<GameCardOverlayProps> = ({
  itemId,
  meta,
  isPlayable
}) => {
  const tags = meta?.tags || [];
  const systemTags = tags.filter(
    t => t !== 'Game' && t !== 'JellyEmu' && t !== 'Unsupported' && !KNOWN_REGIONS.has(t) && !/^Dis[ck]\s+[1-9IVX]/i.test(t)
  );
  const regionTags = tags.filter(t => KNOWN_REGIONS.has(t));
  const discTags = tags.filter(t => /^Dis[ck]\s+[1-9IVX]/i.test(t));
  const isUnknown = tags.includes('Unknown');
  const isUnsupported = !isUnknown && tags.some(t => UNSUPPORTED_PLATFORMS.has(t));

  const rating = meta?.communityRating;
  const pids = meta?.providerIds || {};
  const hasRating = typeof rating === 'number' && rating > 0 && (pids.IGDB || pids.Romm || pids.RAWG || pids.ScreenScraper || pids.screenscraper);
  const ratingSource = pids.IGDB ? 'IGDB' : pids.Romm ? 'RoMM' : pids.RAWG ? 'RAWG' : pids.ScreenScraper ? 'ScreenScraper' : 'Rating';

  const handlePlayClick = (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    injectionService.launchEmulator(itemId);
  };

  return (
    <div className="je-card-overlay-root absolute inset-0 pointer-events-none z-10 select-none overflow-hidden rounded-[inherit]">
      {/* Top-Right Community / IGDB Rating Badge */}
      {hasRating && (
        <div className="absolute top-1.5 right-1.5 pointer-events-none">
          <span
            title={`${ratingSource} Rating: ${rating.toFixed(1)} / 10`}
            className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-black/80 border border-white/20 text-[10px] font-bold text-amber-300 backdrop-blur-md shadow-md"
          >
            <Star className="w-2.5 h-2.5 fill-amber-400 text-amber-400" />
            {rating.toFixed(1)}
          </span>
        </div>
      )}

      {/* Center Hover Play Button */}
      {isPlayable && (
        <button
          type="button"
          onClick={handlePlayClick}
          className="jellyemu-card-play pointer-events-auto absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-12 h-12 rounded-full bg-black hover:bg-white text-white hover:text-black flex items-center justify-center border-2 border-white opacity-0 group-hover:opacity-100 hover:scale-110 active:scale-95 transition-all duration-150 cursor-pointer"
          title="Play Game"
          aria-label="Play Game"
        >
          <Gamepad2 className="w-6 h-6 text-inherit fill-current" />
        </button>
      )}

      {/* Bottom-Left System / Region / Disc / Status Badges */}
      <div className="absolute bottom-1.5 left-1.5 flex flex-wrap gap-1 max-w-[85%] pointer-events-none">
        {systemTags.map(tag => {
          const c = getConsoleColor(tag);
          return (
            <span
              key={tag}
              style={{
                backgroundColor: `${c}33`,
                borderColor: `${c}66`,
                color: '#fff'
              }}
              className="inline-flex items-center px-1.5 py-0.5 rounded border text-[9px] font-bold backdrop-blur-md shadow"
            >
              {tag}
            </span>
          );
        })}

        {regionTags.map(tag => {
          const rc = getRegionColorClasses(tag);
          return (
            <span
              key={tag}
              className={`inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded ${rc.bg} border ${rc.border} ${rc.text} text-[9px] font-bold backdrop-blur-md shadow`}
            >
              <Globe className={`w-2.5 h-2.5 ${rc.icon}`} />
              {tag}
            </span>
          );
        })}

        {discTags.map(tag => (
          <span
            key={tag}
            className="inline-flex items-center px-1.5 py-0.5 rounded bg-amber-600/85 text-white text-[9px] font-bold backdrop-blur-md shadow"
          >
            {tag}
          </span>
        ))}

        {(isUnsupported || isUnknown) && (
          <span
            className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded bg-amber-600/90 border border-amber-400/40 text-white text-[9px] font-bold backdrop-blur-md shadow"
            title={isUnknown ? 'Unknown Platform' : 'External Emulation Only'}
          >
            <AlertTriangle className="w-2.5 h-2.5" />
            {isUnknown ? 'Unknown' : 'Unsupported'}
          </span>
        )}
      </div>
    </div>
  );
};
