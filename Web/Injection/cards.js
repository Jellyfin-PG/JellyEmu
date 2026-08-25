(function() {
    window.JellyEmu = window.JellyEmu || {};
    const JE = window.JellyEmu;

    const BATCH_SIZE        = 50;
    const BATCH_CONCURRENCY = 2;

    const _metaQueue    = [];
    let _batchActive    = 0;
    let _batchScheduled = false;

    JE.queueGetItem = function(cardId, resolve) {
        JE.perf.mark('getItem-queued:' + cardId);
        _metaQueue.push({ cardId, resolve });
        if (!_batchScheduled) {
            _batchScheduled = true;
            setTimeout(_drainBatchQueue, 16);
        }
    };

    function _drainBatchQueue() {
        _batchScheduled = false;
        while (_batchActive < BATCH_CONCURRENCY && _metaQueue.length > 0) {
            const batch = _metaQueue.splice(0, BATCH_SIZE);
            _batchActive++;

            const ids      = batch.map(b => b.cardId);
            const resolves = {};
            batch.forEach(b => {
                resolves[b.cardId] = b.resolve;
                JE.perf.mark('getItem-start:' + b.cardId);
            });

            JE.perf.mark('batch-fetch-start:' + ids[0]);
            fetch('/jellyemu/cardmeta?ids=' + ids.join(','))
                .then(r => r.ok ? r.json() : {})
                .catch(() => ({}))
                .then(function(data) {
                    JE.perf.mark('batch-fetch-end:' + ids[0]);
                    try { performance.measure('jellyemu:batch-fetch[' + ids.length + ']:' + ids[0], 'jellyemu:batch-fetch-start:' + ids[0], 'jellyemu:batch-fetch-end:' + ids[0]); } catch(_) {}

                    batch.forEach(function(b) {
                        const meta = data[b.cardId];
                        JE.perf.mark('getItem-end:' + b.cardId);
                        try { performance.measure('jellyemu:getItem-api:' + b.cardId, 'jellyemu:getItem-start:' + b.cardId, 'jellyemu:getItem-end:' + b.cardId); } catch(_) {}
                        b.resolve(meta ? {
                            Tags:            meta.tags            || [],
                            CommunityRating: meta.communityRating ?? null,
                            ProviderIds:     meta.providerIds     || {},
                        } : null);
                    });
                })
                .finally(function() {
                    _batchActive--;
                    if (_metaQueue.length > 0) _drainBatchQueue();
                });
        }

        if (_metaQueue.length > 0 && _batchActive >= BATCH_CONCURRENCY && !_batchScheduled) {
            _batchScheduled = true;
            setTimeout(_drainBatchQueue, 16);
        }
    }

    const _cardIntersectionObserver = (typeof IntersectionObserver !== 'undefined')
        ? new IntersectionObserver(function(entries, observer) {
            entries.forEach(function(entry) {
                if (entry.isIntersecting) {
                    const card = entry.target;
                    observer.unobserve(card);
                    _enqueueCardForProcessing(card);
                }
            });
        }, { rootMargin: '300px 0px' })
        : null;

    const _pendingCards = new Set();
    let _cardFlushScheduled = false;

    JE.scheduleCardProcess = function(card) {
        if (!card || card.getAttribute('data-jellyemu-checked') === '1') return;
        if (_cardIntersectionObserver) {
            _cardIntersectionObserver.observe(card);
        } else {
            _enqueueCardForProcessing(card);
        }
    };

    function _enqueueCardForProcessing(card) {
        _pendingCards.add(card);
        if (!_cardFlushScheduled) {
            _cardFlushScheduled = true;
            JE.perf.mark('card-flush-scheduled');
            setTimeout(function() {
                _cardFlushScheduled = false;
                const batch = Array.from(_pendingCards);
                _pendingCards.clear();
                JE.perf.mark('card-flush-start');
                batch.forEach(JE.processCard);
                JE.perf.mark('card-flush-end');
                try { performance.measure('jellyemu:card-flush[' + batch.length + ']', 'jellyemu:card-flush-start', 'jellyemu:card-flush-end'); } catch(_) {}
            }, 0);
        }
    }

    JE.applyGameCardTreatment = function(card) {
        card.setAttribute('data-jellyemu-game', '1');

        const cardId0 = card.getAttribute('data-id') || 'unknown';
        JE.perf.mark('card-rAF-scheduled:' + cardId0);
        requestAnimationFrame(function() {
            JE.perf.mark('card-rAF-start:' + cardId0);

            card.querySelectorAll('button[data-action="resume"], button[data-action="play"]').forEach(function(b) {
                b.style.display = 'none';
            });

            if (!card.querySelector('.jellyemu-card-badge-wrap')) {
                const cardId = card.getAttribute('data-id');
                if (cardId && window.ApiClient) {
                    JE.queueGetItem(cardId, function(item) {
                        if (!item || !item.Tags || !item.Tags.includes('JellyEmu')) {
                            card.removeAttribute('data-jellyemu-game');
                            card.querySelectorAll('button[data-action="resume"], button[data-action="play"]').forEach(function(b) {
                                b.style.display = '';
                            });
                            return;
                        }
                        const iconSpan = card.querySelector('.cardImageIcon');
                        if (iconSpan) iconSpan.innerHTML = 'sports_esports';

                        const imgCtr = card.querySelector('.cardImageContainer');
                        if (!imgCtr) return;

                        JE.perf.mark('badge-render-start:' + cardId);

                        card.setAttribute('data-jellyemu-tags', item.Tags.join(','));

                        const badgeWrap = document.createElement('div');
                        badgeWrap.className = 'jellyemu-card-badge-wrap';
                        badgeWrap.style.cssText = 'position:absolute;bottom:4px;left:4px;display:flex;gap:3px;flex-wrap:wrap;z-index:2;pointer-events:none;';
                        item.Tags.filter(t => t !== 'JellyEmu' && t !== 'Game' && t !== 'Unsupported').forEach(function(tag) {
                            const badge = document.createElement('span');
                            const isRegion      = JE.knownRegions.has(tag);
                            const isDisc        = JE.isDiscTag(tag);
                            const isUnknown     = tag === 'Unknown';
                            const isUnsupported = JE.ejsUnsupportedPlatforms.has(tag);
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
                        const cardOverlayFrag = document.createDocumentFragment();
                        if (badgeWrap.children.length > 0) cardOverlayFrag.appendChild(badgeWrap);

                        const rating = item.CommunityRating;
                        const pids = item.ProviderIds || {};
                        if (typeof rating === 'number' && (pids['IGDB'] || pids['Romm'])) {
                            const ratingBadge = document.createElement('div');
                            ratingBadge.className = 'jellyemu-card-rating-badge';
                            ratingBadge.title = (pids['IGDB'] ? 'IGDB' : 'RoMM') + ' rating: ' + rating.toFixed(1) + ' / 10';
                            ratingBadge.style.cssText = 'position:absolute;top:4px;right:4px;z-index:2;pointer-events:none;' +
                                'display:inline-flex;align-items:center;gap:2px;' +
                                'background:rgba(0,0,0,.72);border:1px solid rgba(255,255,255,.18);' +
                                'border-radius:3px;padding:1px 5px;font-size:9px;font-weight:700;color:#e0e0e0;opacity:.92;';
                            ratingBadge.innerHTML =
                                '<span class="material-icons starIcon star" aria-hidden="true" style="font-size:9px;line-height:1;"></span>' +
                                rating.toFixed(1);
                            cardOverlayFrag.appendChild(ratingBadge);
                        }

                        if (cardOverlayFrag.children.length > 0) imgCtr.appendChild(cardOverlayFrag);

                        if (JE.isPlayable(item.Tags)) {
                            card.querySelectorAll('button[data-action="resume"], button[data-action="play"]').forEach(function(playBtn) {
                                if (playBtn.parentNode && !playBtn.parentNode.querySelector('.jellyemu-card-play')) {
                                    const sterileBtn = document.createElement('button');
                                    sterileBtn.type = 'button';
                                    sterileBtn.className = 'cardOverlayButton cardOverlayButton-hover jellyemu-card-play';
                                    sterileBtn.title = 'Play Game';
                                    sterileBtn.innerHTML = '<span class="material-icons" aria-hidden="true">sports_esports</span>';
                                    sterileBtn.addEventListener('click', function(e) {
                                        e.preventDefault();
                                        e.stopPropagation();
                                        e.stopImmediatePropagation();
                                        JE.launchEmulator(cardId);
                                    });
                                    playBtn.parentNode.insertBefore(sterileBtn, playBtn);
                                }
                            });
                        }

                        JE.perf.mark('badge-render-end:' + cardId);
                        try { performance.measure('jellyemu:badge-render:' + cardId, 'jellyemu:badge-render-start:' + cardId, 'jellyemu:badge-render-end:' + cardId); } catch(_) {}
                    });
                }
            }

            JE.perf.mark('card-rAF-end:' + cardId0);
            try { performance.measure('jellyemu:card-rAF:' + cardId0, 'jellyemu:card-rAF-start:' + cardId0, 'jellyemu:card-rAF-end:' + cardId0); } catch(_) {}
        });
    };

    JE.processCard = function(card) {
        if (card.getAttribute('data-jellyemu-checked') === '1') return;

        const cardType = card.getAttribute('data-type');
        const cardText = card.querySelector('.cardText')?.textContent || '';

        if (cardType === 'CollectionFolder') {
            card.setAttribute('data-jellyemu-checked', '1');
            if (cardText.includes('Games') || cardText.includes('Emulators')) {
                const iconSpan = card.querySelector('.cardImageIcon');
                if (iconSpan) iconSpan.innerHTML = 'sports_esports';
            }
            return;
        }

        const path = card.getAttribute('data-path');
        let isGameCard = card.getAttribute('data-jellyemu-game') === '1';

        if (path) {
            const extMatch = path.match(/\.([a-zA-Z0-9]+)$/);
            if (extMatch && JE.romExtensions.has(extMatch[1].toLowerCase())) {
                isGameCard = true;
            }
        }

        if (isGameCard) {
            card.setAttribute('data-jellyemu-checked', '1');
            JE.applyGameCardTreatment(card);
        } else if (cardType === 'Book') {
            card.setAttribute('data-jellyemu-checked', '1');
            const cardId = card.getAttribute('data-id');
            if (cardId && window.ApiClient) {
                JE.queueGetItem(cardId, function(item) {
                    if (item && item.Tags && item.Tags.includes('JellyEmu')) {
                        JE.currentItemIsGame = true;
                        JE.cachedTags        = item.Tags;
                        JE.cachedProviderIds = item.ProviderIds || {};
                        JE.applyGameCardTreatment(card);
                    }
                });
            }
        }
    };

    const cardObserver = new MutationObserver((mutations) => {
        JE.perf.mark('observer-batch-start');

        mutations.forEach((mutation) => {
            mutation.addedNodes.forEach((node) => {
                if (node.nodeType !== 1) return;
                if (node.getAttribute?.('data-jellyemu-mods')) return;

                if (node.tagName === 'BUTTON' && node.classList?.contains('headerButton')) {
                    const titleStr = node.getAttribute('title') || '';
                    if (titleStr.includes('Games')) {
                        const iconSpan = node.querySelector('.material-icons');
                        if (iconSpan) iconSpan.innerHTML = 'sports_esports';
                    }
                    return;
                }

                if (node.classList?.contains('card')) {
                    JE.scheduleCardProcess(node);
                } else if (node.classList?.contains('itemsContainer') ||
                           node.classList?.contains('cardScroller') ||
                           node.classList?.contains('section') ||
                           node.tagName === 'SECTION') {
                    node.querySelectorAll('.card').forEach(JE.scheduleCardProcess);
                } else if (!node.classList?.contains('jellyemu-card-badge-wrap') &&
                           !node.classList?.contains('jellyemu-card-rating-badge')) {
                    const parentCard = node.closest?.('.card');
                    if (parentCard) JE.scheduleCardProcess(parentCard);
                }
            });
        });

        JE.perf.mark('observer-batch-end');
        JE.perf.measure('observer-batch', 'observer-batch-start', 'observer-batch-end');
    });

    const _viewContainer = document.querySelector('.view-manager') || document.body;
    cardObserver.observe(_viewContainer, { childList: true, subtree: true });

    const bodyObserver = new MutationObserver((mutations) => {
        mutations.forEach((mutation) => {
            mutation.addedNodes.forEach((node) => {
                if (node.nodeType !== 1) return;
                const sheetContent = node.classList?.contains('actionSheetContent')
                    ? node
                    : node.querySelector?.('.actionSheetContent');
                if (sheetContent && JE.patchActionSheet) JE.patchActionSheet(sheetContent);
            });
        });
    });
    bodyObserver.observe(document.body, { childList: true });

    document.querySelectorAll('.card').forEach(JE.scheduleCardProcess);
})();
