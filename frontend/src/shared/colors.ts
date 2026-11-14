export const CONSOLE_COLORS: Record<string, string> = {
  'NES': '#e74c3c',
  'SNES': '#9b59b6',
  'N64': '#2ecc71',
  'Game Boy': '#1abc9c',
  'Game Boy Color': '#16a085',
  'Game Boy Advance': '#3498db',
  'Nintendo DS': '#2980b9',
  'Nintendo 3DS': '#d35400',
  'Sega Genesis': '#e67e22',
  'Master System': '#d35400',
  'Game Gear': '#f39c12',
  'Sega Saturn': '#34495e',
  'Dreamcast': '#ff5a00',
  'PlayStation': '#3f51b5',
  'PlayStation 2': '#2e6db4',
  'PlayStation Portable': '#00bcd4',
  'PICO-8': '#e91e63',
  'Atari 2600': '#795548',
  'Atari 7800': '#8d6e63',
  'TurboGrafx-16': '#ff5722',
  'NeoGeo Pocket': '#00bcd4',
  'Virtual Boy': '#c0392b',
  'Sega CD': '#8e44ad',
  'Sega 32X': '#d35400',
  'WonderSwan': '#1abc9c',
  'ColecoVision': '#7f8c8d'
};

export function getConsoleColor(platformName: string): string {
  if (CONSOLE_COLORS[platformName]) return CONSOLE_COLORS[platformName];
  const c = platformName.toLowerCase();
  if (c.includes('nes') || c.includes('nintendo entertainment system') || c.includes('famicom')) return '#e60012';
  if (c.includes('snes') || c.includes('super nintendo') || c.includes('super famicom')) return '#8c52ff';
  if (c.includes('n64') || c.includes('nintendo 64')) return '#007cff';
  if (c.includes('game boy') || c.includes('gb') || c.includes('gba') || c.includes('gbc')) return '#52B54B';
  if (c.includes('nds') || c.includes('ds') || c.includes('3ds')) return '#2980b9';
  if (c.includes('playstation') || c.includes('psx') || c.includes('ps1') || c.includes('ps2') || c.includes('ps3') || c.includes('psp') || c.includes('vita')) return '#2e6db4';
  if (c.includes('pico')) return '#ff7e00';
  if (c.includes('dreamcast')) return '#ff5a00';
  if (c.includes('xbox')) return '#107c10';
  if (c.includes('wii') || c.includes('switch')) return '#00d2ff';
  if (c.includes('sega') || c.includes('genesis') || c.includes('megadrive') || c.includes('saturn') || c.includes('master system') || c.includes('game gear')) return '#005ecc';
  if (c.includes('atari')) return '#e21b22';
  if (c.includes('turbografx') || c.includes('pc engine')) return '#ff5722';
  if (c.includes('neogeo') || c.includes('neo geo')) return '#00bcd4';

  let hash = 0;
  for (let i = 0; i < platformName.length; i++) {
    hash = platformName.charCodeAt(i) + ((hash << 5) - hash);
  }
  const h = Math.abs(hash % 360);
  return `hsl(${h}, 70%, 55%)`;
}

export interface RegionColorStyle {
  bg: string;
  border: string;
  text: string;
  icon: string;
}

export function getRegionColorClasses(region: string): RegionColorStyle {
  const r = region.toLowerCase();
  if (r.includes('usa') || r.includes('us') || r.includes('north america') || r.includes('canada')) {
    return {
      bg: 'bg-sky-500/15',
      border: 'border-sky-500/35',
      text: 'text-sky-300',
      icon: 'text-sky-400'
    };
  }
  if (r.includes('europe') || r.includes('eu') || r.includes('france') || r.includes('germany') || r.includes('italy') || r.includes('spain') || r.includes('sweden') || r.includes('netherlands')) {
    return {
      bg: 'bg-blue-500/15',
      border: 'border-blue-500/35',
      text: 'text-blue-300',
      icon: 'text-blue-400'
    };
  }
  if (r.includes('japan') || r.includes('jp') || r.includes('asia') || r.includes('korea') || r.includes('china')) {
    return {
      bg: 'bg-rose-500/15',
      border: 'border-rose-500/35',
      text: 'text-rose-300',
      icon: 'text-rose-400'
    };
  }
  if (r.includes('australia') || r.includes('brazil') || r.includes('world')) {
    return {
      bg: 'bg-teal-500/15',
      border: 'border-teal-500/35',
      text: 'text-teal-300',
      icon: 'text-teal-400'
    };
  }
  return {
    bg: 'bg-indigo-500/15',
    border: 'border-indigo-500/35',
    text: 'text-indigo-300',
    icon: 'text-indigo-400'
  };
}
