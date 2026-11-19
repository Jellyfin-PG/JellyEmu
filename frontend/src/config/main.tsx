import React from 'react';
import { createRoot, Root } from 'react-dom/client';
import { ConfigApp } from './ConfigApp';
import '../index.css';

let configRoot: Root | null = null;
let containerObserver: MutationObserver | null = null;

export function mountConfigApp() {
  const container = document.getElementById('jellyemu-config-root');
  if (container) {
    if (configRoot) {
      try {
        configRoot.unmount();
      } catch (e) {
        console.warn('[JellyEmu] Error unmounting previous configRoot:', e);
      }
      configRoot = null;
    }

    if (containerObserver) {
      containerObserver.disconnect();
      containerObserver = null;
    }

    configRoot = createRoot(container);
    configRoot.render(
      <React.StrictMode>
        <ConfigApp />
      </React.StrictMode>
    );

    // Watch for DOM detachment to safely unmount
    if (container.parentElement) {
      containerObserver = new MutationObserver(() => {
        if (!container.isConnected) {
          unmountConfigApp();
        }
      });
      containerObserver.observe(container.parentElement, { childList: true });
    }
  }
}

export function unmountConfigApp() {
  if (containerObserver) {
    containerObserver.disconnect();
    containerObserver = null;
  }
  if (configRoot) {
    try {
      configRoot.unmount();
    } catch (e) {
      console.warn('[JellyEmu] Error unmounting configRoot:', e);
    }
    configRoot = null;
  }
}

declare global {
  interface Window {
    JellyEmuConfig?: {
      mount: () => void;
      unmount: () => void;
    };
  }
}

window.JellyEmuConfig = {
  mount: mountConfigApp,
  unmount: unmountConfigApp
};

// Auto mount if container already exists
if (document.getElementById('jellyemu-config-root')) {
  mountConfigApp();
}

// Listen for Jellyfin SPA navigation lifecycle events
document.addEventListener('viewshow', (e: any) => {
  if (e.detail?.options?.id === 'JellyEmuConfigPage' || document.getElementById('jellyemu-config-root')) {
    mountConfigApp();
  }
});

document.addEventListener('viewhide', (e: any) => {
  if (e.detail?.options?.id === 'JellyEmuConfigPage') {
    unmountConfigApp();
  }
});

document.addEventListener('viewdestroy', (e: any) => {
  if (e.detail?.options?.id === 'JellyEmuConfigPage' || !document.getElementById('jellyemu-config-root')) {
    unmountConfigApp();
  }
});

// Legacy pageshow / pagehide fallback
window.addEventListener('pageshow', () => {
  if (document.getElementById('jellyemu-config-root')) {
    mountConfigApp();
  }
});
