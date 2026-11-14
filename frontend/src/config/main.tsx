import React from 'react';
import { createRoot, Root } from 'react-dom/client';
import { ConfigApp } from './ConfigApp';
import '../index.css';

let configRoot: Root | null = null;

export function mountConfigApp() {
  const container = document.getElementById('jellyemu-config-root');
  if (container) {
    if (!configRoot) {
      configRoot = createRoot(container);
    }
    configRoot.render(
      <React.StrictMode>
        <ConfigApp />
      </React.StrictMode>
    );
  }
}

export function unmountConfigApp() {
  if (configRoot) {
    configRoot.unmount();
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

// Legacy pageshow / pagehide fallback
window.addEventListener('pageshow', () => {
  if (document.getElementById('jellyemu-config-root')) {
    mountConfigApp();
  }
});
