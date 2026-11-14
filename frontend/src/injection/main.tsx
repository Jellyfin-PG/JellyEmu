import { createRoot } from 'react-dom/client';
import { HeroUIProvider } from '@heroui/react';
import { domObserver } from './services/domObserver';
import { GameDetailActionRow } from './components/GameDetailActionRow';
import { GameDetailMiscInfo } from './components/GameDetailMiscInfo';
import { ClientSettingsPage } from './components/ClientSettingsPage';
import { GlobalSaveBrowserPage } from './components/GlobalSaveBrowserPage';
import { injectionService } from './services/injectionService';
import { playbackInterceptor } from './services/playbackInterceptor';
import '../index.css';

console.log('[JellyEmu] Modern React + HeroUI Web Injection Active');

export function initInjection() {
  // Initialize global playback interceptor
  playbackInterceptor.init();

  // Preferences menu navigation integration
  domObserver.observePreferencesMenu();

  // Full page navigation integration for #/jellyemu-settings and #/jellyemu-saves
  const handlePageRoutes = () => {
    const hash = window.location.hash || '';

    const settingsPage = document.getElementById('jellyemu-settings-page');
    const savesPage = document.getElementById('jellyemu-saves-page');

    // JellyEmu settings page (#/jellyemu-settings)
    if (hash.startsWith('#/jellyemu-settings')) {
      if (savesPage) {
        savesPage.classList.add('hide');
        savesPage.style.display = 'none';
      }

      const parentContainer = document.querySelector('.view-manager') ||
                              document.querySelector('.mainAnimatedPages') ||
                              document.querySelector('.skinBody') ||
                              document.body;

      let page = settingsPage as HTMLElement | null;
      if (!page) {
        page = document.createElement('div');
        page.id = 'jellyemu-settings-page';
        page.className = 'page libraryPage noSecondaryNavPage mainAnimatedPage';
        page.setAttribute('data-title', 'JellyEmu Settings');
        page.setAttribute('data-backbutton', 'true');
        page.innerHTML = '<div id="je-settings-page-mount"></div>';
        parentContainer.appendChild(page);

        const mountPoint = page.querySelector('#je-settings-page-mount');
        if (mountPoint) {
          createRoot(mountPoint).render(
            <HeroUIProvider>
              <div className="dark text-foreground min-h-screen">
                <ClientSettingsPage />
              </div>
            </HeroUIProvider>
          );
        }
      }

      page.classList.remove('hide');
      page.style.display = 'block';

      // Gracefully hide other Jellyfin pages without destroying their DOM
      document.querySelectorAll('.page:not(#jellyemu-settings-page)').forEach(p => {
        (p as HTMLElement).classList.add('hide');
      });

      document.title = 'JellyEmu Settings';
      const headerTitle = document.querySelector('.skinHeader .pageTitle');
      if (headerTitle) headerTitle.textContent = 'JellyEmu Settings';

      return;
    }

    // B. Save State Browser Page (#/jellyemu-saves)
    if (hash.startsWith('#/jellyemu-saves')) {
      if (settingsPage) {
        settingsPage.classList.add('hide');
        settingsPage.style.display = 'none';
      }

      const parentContainer = document.querySelector('.view-manager') ||
                              document.querySelector('.mainAnimatedPages') ||
                              document.querySelector('.skinBody') ||
                              document.body;

      let page = savesPage as HTMLElement | null;
      if (!page) {
        page = document.createElement('div');
        page.id = 'jellyemu-saves-page';
        page.className = 'page libraryPage noSecondaryNavPage mainAnimatedPage';
        page.setAttribute('data-title', 'Save State Browser');
        page.setAttribute('data-backbutton', 'true');
        page.innerHTML = '<div id="je-saves-page-mount"></div>';
        parentContainer.appendChild(page);

        const mountPoint = page.querySelector('#je-saves-page-mount');
        if (mountPoint) {
          createRoot(mountPoint).render(
            <HeroUIProvider>
              <div className="dark text-foreground min-h-screen">
                <GlobalSaveBrowserPage />
              </div>
            </HeroUIProvider>
          );
        }
      }

      page.classList.remove('hide');
      page.style.display = 'block';

      // Gracefully hide other Jellyfin pages without destroying their DOM
      document.querySelectorAll('.page:not(#jellyemu-saves-page)').forEach(p => {
        (p as HTMLElement).classList.add('hide');
      });

      document.title = 'Save State Browser';
      const headerTitle = document.querySelector('.skinHeader .pageTitle');
      if (headerTitle) headerTitle.textContent = 'Save State Browser';

      return;
    }

    // C. Non-JellyEmu routes: Hide our custom pages
    if (settingsPage) {
      settingsPage.classList.add('hide');
      settingsPage.style.display = 'none';
    }
    if (savesPage) {
      savesPage.classList.add('hide');
      savesPage.style.display = 'none';
    }
  };

  handlePageRoutes();
  window.addEventListener('hashchange', handlePageRoutes);
  window.addEventListener('popstate', handlePageRoutes);
  document.addEventListener('viewshow', handlePageRoutes);
  document.addEventListener('pageshow', handlePageRoutes);

  // Observe detail pages (action button + misc info pills)
  domObserver.observeDetailPages(({ page, itemId, buttonContainer, miscInfoContainer }) => {
    const userId = injectionService.getCurrentUserId();
    if (userId) {
      injectionService.getItem(userId, itemId).then((item: any) => {
        const tags: string[] = item?.Tags || [];
        const isGame = tags.includes('JellyEmu');
        if (!isGame) return;

        page.classList.add('jellyemu-game-page');

        // Hide native Jellyfin video/audio playback buttons
        page.querySelectorAll('button[data-action="resume"], button[data-action="play"], .btnPlay, .btnPlaySimple, .playButton, .btnPlayTrailer').forEach(el => {
          (el as HTMLElement).style.setProperty('display', 'none', 'important');
        });

        // Mount Play Icon Button
        if (buttonContainer && !page.querySelector('.je-action-row-mounted')) {
          const mountPoint = document.createElement('div');
          mountPoint.className = 'je-action-row-mounted je-react-mounted inline-flex items-center';
          buttonContainer.insertBefore(mountPoint, buttonContainer.firstChild);

          createRoot(mountPoint).render(
            <HeroUIProvider>
              <div className="dark text-foreground inline-flex items-center">
                <GameDetailActionRow itemId={itemId} />
              </div>
            </HeroUIProvider>
          );
        }

        // Mount Media Info Pills
        if (miscInfoContainer && !page.querySelector('.je-misc-info-mounted')) {
          const miscMountPoint = document.createElement('div');
          miscMountPoint.className = 'je-misc-info-mounted je-react-mounted';
          miscMountPoint.style.display = 'contents';
          miscInfoContainer.appendChild(miscMountPoint);

          createRoot(miscMountPoint).render(
            <HeroUIProvider>
              <div className="dark text-foreground">
                <GameDetailMiscInfo
                  itemId={itemId}
                  tags={tags}
                  providerIds={item?.ProviderIds || {}}
                />
              </div>
            </HeroUIProvider>
          );
        }
      }).catch(() => {});
    }
  });

  // Observe game cards in library grids
  domObserver.observeCards();

  // Patch context menus (action sheets)
  domObserver.observeActionSheets();

  // Window message & BroadcastChannel lifecycle handling
  const cleanupOverlays = () => {
    document.querySelectorAll('.dialogBackdrop, .dialogContainer, .focuscontainer-fullscreen, #jellyemu-iframe').forEach(el => el.remove());
    document.body.style.overflow = '';
    document.documentElement.style.overflow = '';
  };

  window.addEventListener('pageshow', cleanupOverlays);
  window.addEventListener('popstate', cleanupOverlays);
  document.addEventListener('viewshow', cleanupOverlays);

  try {
    const exitChannel = new BroadcastChannel('jellyemu-exit');
    exitChannel.onmessage = (e) => {
      if (e.data === 'close-jellyemu') {
        cleanupOverlays();
      }
    };
  } catch {}

  window.addEventListener('message', (e: MessageEvent) => {
    if (e.data === 'close-jellyemu') {
      cleanupOverlays();
    }
    if (e.data?.type === 'jellyemu-save-written') {
      injectionService.toast('🎮 Game state saved successfully');
    }
    if (e.data?.type === 'jellyemu-session-end') {
      const seconds = e.data.seconds || 0;
      if (seconds > 0) {
        const mins = Math.max(1, Math.round(seconds / 60));
        injectionService.toast(`⏱️ Session ended: ${mins} minute(s) played`);
      }
    }
    if (e.data?.type === 'jellyemu-screenshot') {
      injectionService.toast('📸 Screenshot captured');
    }
  });
}

if (typeof window !== 'undefined') {
  initInjection();
}
