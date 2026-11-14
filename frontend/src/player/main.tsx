import { createRoot } from 'react-dom/client';
import { PlayerApp } from './PlayerApp';
import { PlayerInitialData } from '../types/player';
import '../index.css';

console.log('[JellyEmu] Modern React + HeroUI Player Overlay Active');

export function initPlayer() {
  const container = document.getElementById('jellyemu-player-root');
  if (container) {
    const rawConfig = (window as any).JellyEmuConfig || {};
    const rawPlayerData = (window as any).__JELLYEMU_PLAYER_DATA__ || {};
    const isPlay =
      rawPlayerData.core === 'play' ||
      rawConfig.core === 'play' ||
      rawPlayerData.platformTag?.toLowerCase() === 'ps2' ||
      rawConfig.platformTag?.toLowerCase() === 'ps2' ||
      rawPlayerData.isPlay === true;

    const isPico8 =
      rawPlayerData.core?.toLowerCase() === 'pico8' ||
      rawConfig.core?.toLowerCase() === 'pico8' ||
      rawPlayerData.platformTag?.toLowerCase() === 'pico8' ||
      rawConfig.platformTag?.toLowerCase() === 'pico8' ||
      rawPlayerData.platformTag?.toLowerCase() === 'p8' ||
      rawConfig.platformTag?.toLowerCase() === 'p8' ||
      rawPlayerData.systemName?.toLowerCase().includes('pico') ||
      rawConfig.systemName?.toLowerCase().includes('pico') ||
      rawPlayerData.isPico8 === true;

    const data: PlayerInitialData = {
      itemId: rawPlayerData.itemId || rawConfig.itemId || '',
      userId: rawPlayerData.userId || rawConfig.userId || '',
      gameName: rawPlayerData.gameName || rawConfig.gameName || document.title || 'Retro Game',
      systemName: rawPlayerData.systemName || rawConfig.platformTag || 'Retro System',
      platformTag: rawConfig.platformTag || (window as any).EJS_platformTag || rawPlayerData.platformTag || '',
      core: rawPlayerData.core || (window as any).EJS_core || rawConfig.core || '',
      romUrl: rawPlayerData.romUrl || rawConfig.romUrl || '',
      saveGetUrl: rawPlayerData.saveGetUrl || rawConfig.saveGetUrl || '',
      savePostUrl: rawPlayerData.savePostUrl || rawConfig.savePostUrl || '',
      activeSlot: rawPlayerData.activeSlot ?? rawConfig.activeSlot ?? 1,
      hasNetplay: (isPlay || isPico8) ? false : (rawPlayerData.hasNetplay ?? rawConfig.hasNetplay ?? true),
      hasSaves: (isPlay || isPico8) ? false : (rawPlayerData.hasSaves ?? rawConfig.hasSaves ?? true),
      isPlay: isPlay,
      isPico8: isPico8,
      netplayServer: rawPlayerData.netplayServer || rawConfig.netplayServer || '',
      netplayIceServers: rawPlayerData.netplayIceServers || rawConfig.netplayIceServers || [],
      gameId: rawPlayerData.gameId ?? rawConfig.gameId,
      isM3u: rawPlayerData.isM3u ?? rawConfig.isM3u ?? false,
      activeShader: rawPlayerData.activeShader || rawConfig.activeShader,
      videoRotation: rawPlayerData.videoRotation ?? rawConfig.videoRotation ?? 0,
      vsync: rawPlayerData.vsync || rawConfig.vsync,
      ffrate: rawPlayerData.ffrate || rawConfig.ffrate,
      smrate: rawPlayerData.smrate || rawConfig.smrate,
      showFps: rawPlayerData.showFps || rawConfig.showFps,
      scale: rawPlayerData.scale || rawConfig.scale,
      volume: rawPlayerData.volume || rawConfig.volume,
      mute: rawPlayerData.mute || rawConfig.mute,
      inputScheme: rawPlayerData.inputScheme || rawConfig.inputScheme || (window as any)._activeBackendScheme || null,
      customBindings: rawPlayerData.customBindings || rawConfig.customBindings || (window as any)._jeBindings || null,
      availableCores: (window as any).EJS_availableCores || [],
      version: rawPlayerData.version || rawConfig.version
    };

    const root = createRoot(container);
    root.render(<PlayerApp initialData={data} />);
  }
}

if (typeof window !== 'undefined') {
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initPlayer);
  } else {
    initPlayer();
  }
}
