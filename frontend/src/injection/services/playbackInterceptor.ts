import { injectionService } from './injectionService';

export const playbackInterceptor = {
  hooked: false,

  init: (): void => {
    if (playbackInterceptor.hooked) return;
    playbackInterceptor.hooked = true;

    // Optional safe wrap for window.playbackManager.play if present
    const tryHookPlaybackManager = () => {
      try {
        const pm = (window as any).playbackManager;
        if (pm && typeof pm.play === 'function' && !pm.__jellyemu_hooked) {
          pm.__jellyemu_hooked = true;
          const originalPlay = pm.play;
          pm.play = function (options: any) {
            try {
              const firstItem = options?.items?.[0] || options?.item;
              const itemId = firstItem?.Id || (Array.isArray(options?.ids) ? options.ids[0] : options?.id);

              if (itemId) {
                const tags: string[] = firstItem?.Tags || [];
                const card = document.querySelector(`.card[data-id="${itemId}"]`);
                const isGame = tags.includes('JellyEmu') || card?.getAttribute('data-jellyemu-game') === '1';

                if (isGame) {
                  injectionService.launchEmulator(itemId);
                  return Promise.resolve();
                }
              }
            } catch (err) {
              console.warn('[JellyEmu] playbackManager hook check error:', err);
            }

            return originalPlay.apply(this, arguments);
          };
        }
      } catch {}
    };

    tryHookPlaybackManager();
    document.addEventListener('viewshow', tryHookPlaybackManager);
  }
};
