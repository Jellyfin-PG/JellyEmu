import { injectionService } from './injectionService';
import { CardMeta } from '../../types/injection';

export interface ItemDetailPageInfo {
  page: HTMLElement;
  itemId: string;
  buttonContainer: HTMLElement | null;
  miscInfoContainer: HTMLElement | null;
}

export const KNOWN_REGIONS = new Set([
  'USA', 'Europe', 'Japan', 'World', 'Australia', 'Brazil', 'Canada', 'China',
  'France', 'Germany', 'Italy', 'Korea', 'Netherlands', 'Russia', 'Spain', 'Sweden',
  'Asia', 'Scandinavia', 'Unlicensed', 'Prototype', 'Demo', 'Sample'
]);

export const UNSUPPORTED_PLATFORMS = new Set([
  'Dreamcast', 'PlayStation 3', 'Xbox', 'Xbox 360', 'GameCube', 'Wii', 'Wii U',
  'Nintendo Switch', 'PlayStation Vita', 'Windows', 'Linux', 'MacOS', 'Android', 'Unsupported'
]);

export const ROM_EXTENSIONS = new Set([
  'nes', 'fds', 'unf', 'unif', 'smc', 'sfc', 'swc', 'fig', 'z64', 'n64', 'v64',
  'gb', 'gbc', 'gba', 'nds', 'vb', 'sms', 'gg', 'md', 'smd', 'gen', '68k', '32x',
  'pbp', 'cue', 'iso', 'chd', 'gdi', 'cdi', 'mdf', 'cso', 'a26', 'a78', 'lnx',
  'jag', 'j64', 'ws', 'wsc', 'pce', 'col', 'cv', 'ngp', 'ngc', 'zip', 'd64',
  't64', 'crt', 'tap', 'prg', 'adf', 'dms', 'ipf', 'adz', 'dsk', 'bin', '3ds', 'cci', 'cia'
]);

export function isPlayableGame(tags?: string[]): boolean {
  if (!tags || !tags.length) return false;
  if (!tags.includes('JellyEmu')) return false;
  for (const tag of tags) {
    if (tag === 'Unknown') return false;
    if (UNSUPPORTED_PLATFORMS.has(tag)) return false;
  }
  return true;
}

export function isDiscTag(tag: string): boolean {
  return /^Dis[ck]\s+[1-9IVX]/i.test(tag);
}

// Queue for batching card metadata
const _metaQueue: Array<{ cardId: string; resolve: (meta: CardMeta | null) => void }> = [];
let _batchActive = 0;
let _batchScheduled = false;

function queueGetItem(cardId: string, resolve: (meta: CardMeta | null) => void) {
  _metaQueue.push({ cardId, resolve });
  if (!_batchScheduled) {
    _batchScheduled = true;
    setTimeout(drainBatchQueue, 16);
  }
}

async function drainBatchQueue() {
  _batchScheduled = false;
  while (_batchActive < 2 && _metaQueue.length > 0) {
    const batch = _metaQueue.splice(0, 50);
    _batchActive++;

    const ids = batch.map(b => b.cardId);
    const metaMap = await injectionService.getCardMetaBatch(ids);

    batch.forEach(b => {
      b.resolve(metaMap[b.cardId] || null);
    });

    _batchActive--;
    if (_metaQueue.length > 0) drainBatchQueue();
  }

  if (_metaQueue.length > 0 && _batchActive >= 2 && !_batchScheduled) {
    _batchScheduled = true;
    setTimeout(drainBatchQueue, 16);
  }
}

const _cardIntersectionObserver = (typeof IntersectionObserver !== 'undefined')
  ? new IntersectionObserver((entries, observer) => {
      entries.forEach(entry => {
        if (entry.isIntersecting) {
          const card = entry.target as HTMLElement;
          observer.unobserve(card);
          enqueueCardForProcessing(card);
        }
      });
    }, { rootMargin: '300px 0px' })
  : null;

const _pendingCards = new Set<HTMLElement>();
let _cardFlushScheduled = false;

function enqueueCardForProcessing(card: HTMLElement) {
  _pendingCards.add(card);
  if (!_cardFlushScheduled) {
    _cardFlushScheduled = true;
    setTimeout(() => {
      _cardFlushScheduled = false;
      const batch = Array.from(_pendingCards);
      _pendingCards.clear();
      batch.forEach(processCard);
    }, 0);
  }
}

function scheduleCardProcess(card: HTMLElement | null) {
  if (!card || card.getAttribute('data-jellyemu-checked') === '1') return;
  if (_cardIntersectionObserver) {
    _cardIntersectionObserver.observe(card);
  } else {
    enqueueCardForProcessing(card);
  }
}

function applyGameCardTreatment(card: HTMLElement) {
  card.setAttribute('data-jellyemu-game', '1');

  requestAnimationFrame(() => {
    // Hide Jellyfin native play/resume buttons
    card.querySelectorAll('button[data-action="resume"], button[data-action="play"]').forEach(b => {
      (b as HTMLElement).style.setProperty('display', 'none', 'important');
    });

    if (!card.querySelector('.jellyemu-card-badge-wrap')) {
      const cardId = card.getAttribute('data-id');
      if (cardId) {
        queueGetItem(cardId, (meta) => {
          const tags = meta?.tags || [];
          if (!tags.includes('JellyEmu')) {
            card.removeAttribute('data-jellyemu-game');
            card.querySelectorAll('button[data-action="resume"], button[data-action="play"]').forEach(b => {
              (b as HTMLElement).style.removeProperty('display');
            });
            return;
          }

          // Update gamepad icon on card
          const iconSpan = card.querySelector('.cardImageIcon');
          if (iconSpan) iconSpan.textContent = 'sports_esports';

          const imgCtr = (card.querySelector('.cardImageContainer') || card.querySelector('.cardScalable') || card.querySelector('.cardBox')) as HTMLElement;
          if (!imgCtr) return;

          card.setAttribute('data-jellyemu-tags', tags.join(','));

          // System / region / disc / status badges
          const badgeWrap = document.createElement('div');
          badgeWrap.className = 'jellyemu-card-badge-wrap';
          badgeWrap.style.cssText = 'position:absolute;bottom:4px;left:4px;display:flex;gap:3px;flex-wrap:wrap;z-index:2;pointer-events:none;';

          tags.filter(t => t !== 'JellyEmu' && t !== 'Game' && t !== 'Unsupported').forEach(tag => {
            const badge = document.createElement('span');
            const isRegion = KNOWN_REGIONS.has(tag);
            const isDisc = isDiscTag(tag);
            const isUnknown = tag === 'Unknown';
            const isUnsupported = UNSUPPORTED_PLATFORMS.has(tag);

            badge.style.cssText = 'font-size:9px;font-weight:700;letter-spacing:.03em;padding:1px 5px;border-radius:3px;opacity:.88;' +
              (isRegion
                ? 'background:rgba(0,164,220,.85);color:#fff;'
                : isDisc
                  ? 'background:rgba(220,140,0,.85);color:#fff;'
                  : 'background:rgba(0,0,0,.72);color:#e0e0e0;border:1px solid rgba(255,255,255,.18);');
            badge.textContent = tag;
            badgeWrap.appendChild(badge);

            if (isUnsupported || isUnknown) {
              const statusBadge = document.createElement('span');
              statusBadge.style.cssText = 'font-size:9px;font-weight:700;letter-spacing:.03em;padding:1px 5px;border-radius:3px;opacity:.88;' +
                'background:rgba(200,120,0,.75);color:#fff;border:1px solid rgba(255,180,0,.3);';
              statusBadge.textContent = isUnknown ? 'Unknown' : 'Unsupported';
              badgeWrap.appendChild(statusBadge);
            }
          });

          if (badgeWrap.children.length > 0) {
            imgCtr.appendChild(badgeWrap);
          }

          // Top-right community rating badge
          const rating = meta?.communityRating;
          const pids = meta?.providerIds || {};
          if (typeof rating === 'number' && rating > 0 && (pids.IGDB || pids.Romm || pids.RAWG || pids.ScreenScraper || pids.screenscraper)) {
            const ratingBadge = document.createElement('div');
            ratingBadge.className = 'jellyemu-card-rating-badge';
            const src = pids.IGDB ? 'IGDB' : pids.Romm ? 'RoMM' : pids.RAWG ? 'RAWG' : pids.ScreenScraper ? 'ScreenScraper' : 'Community';
            ratingBadge.title = `${src} rating: ${rating.toFixed(1)} / 10`;
            ratingBadge.style.cssText = 'position:absolute;top:4px;right:4px;z-index:2;pointer-events:none;' +
              'display:inline-flex;align-items:center;gap:2px;' +
              'background:rgba(0,0,0,.72);border:1px solid rgba(255,255,255,.18);' +
              'border-radius:3px;padding:1px 5px;font-size:9px;font-weight:700;color:#e0e0e0;opacity:.92;';
            ratingBadge.innerHTML = `<span class="material-icons" aria-hidden="true" style="font-size:10px;line-height:1;color:#f59e0b;margin-right:2px;">star</span>${rating.toFixed(1)}`;
            imgCtr.appendChild(ratingBadge);
          }

          // Center hover play button
          if (isPlayableGame(tags)) {
            const playParent = (card.querySelector('.cardScalable') || card.querySelector('.cardBox') || imgCtr) as HTMLElement;
            if (playParent && !card.querySelector('.jellyemu-card-play')) {
              const sterileBtn = document.createElement('button');
              sterileBtn.type = 'button';
              sterileBtn.className = 'jellyemu-card-play';
              sterileBtn.title = 'Play Game';
              sterileBtn.innerHTML = '<span class="material-icons" aria-hidden="true">sports_esports</span>';
              sterileBtn.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                e.stopImmediatePropagation();
                injectionService.launchEmulator(cardId);
              });
              playParent.appendChild(sterileBtn);
            }
          }
        });
      }
    }
  });
}

function processCard(card: HTMLElement) {
  if (card.getAttribute('data-jellyemu-checked') === '1') return;

  const cardType = card.getAttribute('data-type');
  const cardText = card.querySelector('.cardText')?.textContent || '';

  if (cardType === 'CollectionFolder') {
    card.setAttribute('data-jellyemu-checked', '1');
    if (cardText.includes('Games') || cardText.includes('Emulators')) {
      const iconSpan = card.querySelector('.cardImageIcon');
      if (iconSpan) iconSpan.textContent = 'sports_esports';
    }
    return;
  }

  const path = card.getAttribute('data-path');
  let isGameCard = card.getAttribute('data-jellyemu-game') === '1';

  if (path) {
    const extMatch = path.match(/\.([a-zA-Z0-9]+)$/);
    if (extMatch && ROM_EXTENSIONS.has(extMatch[1].toLowerCase())) {
      isGameCard = true;
    }
  }

  if (isGameCard) {
    card.setAttribute('data-jellyemu-checked', '1');
    applyGameCardTreatment(card);
  } else if (cardType === 'Book' || !cardType) {
    card.setAttribute('data-jellyemu-checked', '1');
    const cardId = card.getAttribute('data-id');
    if (cardId) {
      queueGetItem(cardId, (meta) => {
        if (meta?.tags?.includes('JellyEmu')) {
          applyGameCardTreatment(card);
        }
      });
    }
  }
}

export const domObserver = {
  lastCardId: null as string | null,

  observeDetailPages: (callback: (info: ItemDetailPageInfo) => void): void => {
    let lastItemId: string | null = null;

    const checkPage = () => {
      const page = document.querySelector('.itemDetailPage:not(.hide)') as HTMLElement;
      if (!page) return;

      const urlParams = new URLSearchParams(window.location.search || window.location.hash.split('?')[1]);
      const itemId = urlParams.get('id') || page.getAttribute('data-itemid');
      if (!itemId) return;

      if (itemId !== lastItemId || !page.querySelector('.je-action-row-mounted')) {
        lastItemId = itemId;
        const buttonContainer = (page.querySelector('.mainDetailButtons') || page.querySelector('.detailButtons')) as HTMLElement;
        const miscInfoContainer = page.querySelector('.itemMiscInfo-primary') as HTMLElement;

        callback({
          page,
          itemId,
          buttonContainer,
          miscInfoContainer
        });
      }
    };

    checkPage();
    document.addEventListener('viewshow', checkPage);
    document.addEventListener('pageshow', checkPage);
    window.addEventListener('popstate', checkPage);
    window.addEventListener('hashchange', checkPage);

    const observer = new MutationObserver(() => checkPage());
    observer.observe(document.body, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ['class', 'style']
    });
  },

  observeCards: (): void => {
    const scanCards = () => {
      document.querySelectorAll('.card').forEach(c => scheduleCardProcess(c as HTMLElement));
    };

    scanCards();
    document.addEventListener('viewshow', scanCards);
    document.addEventListener('pageshow', scanCards);

    const cardObserver = new MutationObserver(mutations => {
      mutations.forEach(mutation => {
        mutation.addedNodes.forEach(node => {
          if (node.nodeType !== 1) return;
          const el = node as HTMLElement;
          if (el.getAttribute?.('data-jellyemu-mods')) return;

          if (el.tagName === 'BUTTON' && el.classList?.contains('headerButton')) {
            const titleStr = el.getAttribute('title') || '';
            if (titleStr.includes('Games')) {
              const iconSpan = el.querySelector('.material-icons');
              if (iconSpan) iconSpan.textContent = 'sports_esports';
            }
            return;
          }

          if (el.classList?.contains('card')) {
            scheduleCardProcess(el);
          } else if (
            el.classList?.contains('itemsContainer') ||
            el.classList?.contains('cardScroller') ||
            el.classList?.contains('section') ||
            el.tagName === 'SECTION'
          ) {
            el.querySelectorAll('.card').forEach(c => scheduleCardProcess(c as HTMLElement));
          } else if (!el.classList?.contains('jellyemu-card-badge-wrap') && !el.classList?.contains('jellyemu-card-rating-badge')) {
            const parentCard = el.closest?.('.card') as HTMLElement;
            if (parentCard) scheduleCardProcess(parentCard);
          }
        });
      });
    });

    const viewContainer = document.querySelector('.view-manager') || document.body;
    cardObserver.observe(viewContainer, { childList: true, subtree: true });
  },

  observeActionSheets: (): void => {
    document.body.addEventListener('click', (e: MouseEvent) => {
      const target = e.target as HTMLElement;
      const menuBtn = target.closest('button[data-action="menu"]');
      if (!menuBtn) return;
      const card = menuBtn.closest('.card[data-jellyemu-game="1"]');
      if (card) {
        domObserver.lastCardId = card.getAttribute('data-id');
      }
    }, true);

    const sheetObserver = new MutationObserver(mutations => {
      mutations.forEach(m => {
        m.addedNodes.forEach(node => {
          if (node.nodeType !== 1) return;
          const el = node as HTMLElement;
          const sheet = el.classList?.contains('actionSheet') ? el : el.querySelector?.('.actionSheet');
          if (sheet && domObserver.lastCardId) {
            const itemId = domObserver.lastCardId;
            const playBtn = sheet.querySelector('button[data-id="resume"]') as HTMLButtonElement;
            if (playBtn && !playBtn.hasAttribute('data-je-patched')) {
              playBtn.setAttribute('data-je-patched', '1');
              const label = playBtn.querySelector('.actionSheetItemText');
              if (label) label.textContent = 'Play Game';

              playBtn.addEventListener('click', (ev) => {
                ev.preventDefault();
                ev.stopPropagation();
                ev.stopImmediatePropagation();
                const container = document.querySelector('.dialogContainer') || document.querySelector('.dialogBackdrop');
                if (container) container.remove();
                injectionService.launchEmulator(itemId);
              }, true);
            }

            const playAllBtn = sheet.querySelector('button[data-id="playallfromhere"]') as HTMLElement;
            if (playAllBtn) playAllBtn.style.display = 'none';
          }
        });
      });
    });

    sheetObserver.observe(document.body, { childList: true });
  },

  observePreferencesMenu: (): void => {
    const injectEntries = () => {
      const page = document.getElementById('myPreferencesMenuPage');
      if (!page || page.querySelector('.jellyemu-prefs-entry')) return;

      const targetSection =
        page.querySelector('.verticalSection:not(.adminSection):not(.userSection)') ||
        page.querySelector('.verticalSection') ||
        page.querySelector('.readOnlyContent') ||
        page;

      if (!targetSection) return;

      const userId = injectionService.getCurrentUserId() || '';

      // Save State Browser Entry
      const savesEntry = document.createElement('a');
      savesEntry.className = 'emby-button jellyemu-prefs-entry listItem-border';
      savesEntry.href = '#/jellyemu-saves' + (userId ? '?userId=' + userId : '');
      savesEntry.style.cssText = 'display:block; margin:0; padding:0; cursor:pointer;';
      savesEntry.innerHTML = `
        <div class="listItem">
          <span class="material-icons listItemIcon listItemIcon-transparent save" aria-hidden="true"></span>
          <div class="listItemBody">
            <div class="listItemBodyText">Save State Browser</div>
          </div>
        </div>
      `;
      savesEntry.addEventListener('click', (e) => {
        e.preventDefault();
        window.location.hash = '#/jellyemu-saves' + (userId ? '?userId=' + userId : '');
      });

      // Settings Entry
      const settingsEntry = document.createElement('a');
      settingsEntry.className = 'emby-button jellyemu-prefs-entry listItem-border';
      settingsEntry.href = '#/jellyemu-settings';
      settingsEntry.style.cssText = 'display:block; margin:0; padding:0; cursor:pointer;';
      settingsEntry.innerHTML = `
        <div class="listItem">
          <span class="material-icons listItemIcon listItemIcon-transparent settings" aria-hidden="true"></span>
          <div class="listItemBody">
            <div class="listItemBodyText">JellyEmu Settings</div>
          </div>
        </div>
      `;
      settingsEntry.addEventListener('click', (e) => {
        e.preventDefault();
        window.location.hash = '#/jellyemu-settings';
      });

      targetSection.appendChild(settingsEntry);
      targetSection.appendChild(savesEntry);
    };

    injectEntries();
    document.addEventListener('viewshow', injectEntries);
    window.addEventListener('popstate', injectEntries);
  }
};
