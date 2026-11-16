/**
 * Safe WakeLock wrapper to prevent uncaught NotAllowedError exceptions
 * (e.g. "The requesting page is not visible at PlatformEmscriptenDoSetWakeLock")
 * from crashing Emscripten / WebAssembly runtimes or freezing the player UI.
 */
export function initSafeWakeLock(): void {
  try {
    if (typeof navigator !== 'undefined' && 'wakeLock' in navigator && navigator.wakeLock && typeof navigator.wakeLock.request === 'function') {
      const origWakeLockRequest = navigator.wakeLock.request.bind(navigator.wakeLock);

      const createDummySentinel = (type?: string): any => {
        const target = typeof EventTarget !== 'undefined' ? new EventTarget() : {};
        const sentinel = Object.assign(target, {
          released: false,
          type: type || 'screen',
          onrelease: null,
          release: () => {
            sentinel.released = true;
            if (typeof (sentinel as any).dispatchEvent === 'function') {
              try { (sentinel as any).dispatchEvent(new Event('release')); } catch { }
            }
            if (typeof (sentinel as any).onrelease === 'function') {
              try { (sentinel as any).onrelease(new Event('release')); } catch { }
            }
            return Promise.resolve();
          }
        });
        return sentinel;
      };

      navigator.wakeLock.request = async function (type?: string) {
        if (typeof document !== 'undefined' && document.visibilityState !== 'visible') {
          return createDummySentinel(type);
        }
        try {
          return await origWakeLockRequest(type as any);
        } catch (err: any) {
          console.warn('[JellyEmu] Screen WakeLock request suppressed:', err?.message || err);
          return createDummySentinel(type);
        }
      };
    }

    if (typeof window !== 'undefined' && typeof window.addEventListener === 'function') {
      window.addEventListener('unhandledrejection', (ev: PromiseRejectionEvent) => {
        if (ev?.reason && (
          ev.reason.name === 'NotAllowedError' ||
          (typeof ev.reason.message === 'string' && ev.reason.message.toLowerCase().includes('wakelock'))
        )) {
          ev.preventDefault();
          console.warn('[JellyEmu] Caught unhandled WakeLock rejection:', ev.reason);
        }
      });
    }
  } catch {
    // Ignore initialization errors
  }
}
