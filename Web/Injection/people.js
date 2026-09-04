(function() {
    window.JellyEmu = window.JellyEmu || {};
    const JE = window.JellyEmu;

    /**
     * Checks if a string contains any gaming or legacy scraper disambiguation suffixes.
     */
    function hasGamingSuffix(str) {
        if (!str || typeof str !== 'string') return false;
        return str.indexOf('(Gaming)') !== -1 ||
               str.indexOf('(gaming)') !== -1 ||
               str.indexOf('(RAWG)') !== -1 ||
               str.indexOf('(rawg)') !== -1;
    }

    /**
     * Replaces any gaming or legacy scraper disambiguation suffixes.
     */
    function cleanGamingSuffix(str) {
        if (!str || typeof str !== 'string') return str;
        return str.replace(/\s*\((?:Gaming|RAWG)\)/gi, '');
    }

    /**
     * Sanitizes a text node directly by stripping the gaming suffix.
     * Modifying nodeValue directly preserves all DOM elements, child nodes, and event listeners.
     */
    function sanitizeTextNode(node) {
        if (!node || node.nodeType !== 3) return; // 3 = Node.TEXT_NODE
        const val = node.nodeValue;
        if (hasGamingSuffix(val)) {
            node.nodeValue = cleanGamingSuffix(val);
        }
    }

    /**
     * Traverses all text nodes within a given root node and sanitizes them.
     */
    function sanitizeSubtree(root) {
        if (!root) return;

        if (root.nodeType === 3) {
            sanitizeTextNode(root);
            return;
        }

        if (root.nodeType === 1) { // 1 = Node.ELEMENT_NODE
            const walker = document.createTreeWalker(
                root,
                NodeFilter.SHOW_TEXT,
                null,
                false
            );

            let textNode;
            while ((textNode = walker.nextNode())) {
                sanitizeTextNode(textNode);
            }
        }

        // Clean document title if needed
        if (hasGamingSuffix(document.title)) {
            document.title = cleanGamingSuffix(document.title);
        }
    }

    /**
     * Cleans orphan leading, trailing, or duplicate commas from a container element.
     */
    function cleanOrphanCommas(container) {
        if (!container || !container.childNodes) return;

        const children = Array.prototype.slice.call(container.childNodes);
        for (let i = 0; i < children.length; i++) {
            const node = children[i];
            if (node.nodeType === 3) { // TEXT_NODE
                let text = node.nodeValue || '';
                // Collapse repeated commas
                text = text.replace(/,\s*,+/g, ',');

                // Check if there are preceding visible text or element siblings
                let hasPreceding = false;
                for (let prev = i - 1; prev >= 0; prev--) {
                    const sib = children[prev];
                    if (sib.nodeType === 1) { hasPreceding = true; break; }
                    if (sib.nodeType === 3 && (sib.nodeValue || '').trim().length > 0) { hasPreceding = true; break; }
                }
                if (!hasPreceding) {
                    text = text.replace(/^\s*,\s*/, '');
                }

                // Check if there are succeeding visible text or element siblings
                let hasSucceeding = false;
                for (let next = i + 1; next < children.length; next++) {
                    const sib = children[next];
                    if (sib.nodeType === 1) { hasSucceeding = true; break; }
                    if (sib.nodeType === 3 && (sib.nodeValue || '').trim().length > 0) { hasSucceeding = true; break; }
                }
                if (!hasSucceeding) {
                    text = text.replace(/,\s*$/, '');
                }

                // If the node became only whitespace or empty, clean it
                if (/^\s*,\s*$/.test(text)) {
                    text = '';
                }

                node.nodeValue = text;
            }
        }
    }

    /**
     * Removes dummy/broken TMDB external links (e.g. https://www.themoviedb.org/person/none)
     * along with any associated delimiter commas.
     */
    function removeBrokenTmdbLinks(root) {
        const scope = root && root.querySelectorAll ? root : document;
        const links = scope.querySelectorAll('a[href*="themoviedb.org/person/none"], a[href*="themoviedb.org/person/0"]');
        for (let i = 0; i < links.length; i++) {
            const link = links[i];
            const parent = link.parentElement;

            // Strip trailing comma from previous text node
            if (link.previousSibling && link.previousSibling.nodeType === 3) {
                link.previousSibling.nodeValue = (link.previousSibling.nodeValue || '').replace(/,\s*$/, '');
            }

            // Strip leading comma from next text node
            if (link.nextSibling && link.nextSibling.nodeType === 3) {
                link.nextSibling.nodeValue = (link.nextSibling.nodeValue || '').replace(/^\s*,\s*/, '');
            }

            const container = link.closest('.detailButton, .button-link, .externalIdItem') || link;
            container.remove();

            if (parent) {
                cleanOrphanCommas(parent);
                // Also clean parent's parent if parent was an inline wrapper
                if (parent.parentElement) {
                    cleanOrphanCommas(parent.parentElement);
                }
            }
        }
    }

    /**
     * Comprehensive scan of the current document or visible active view.
     */
    function scanAndSanitize() {
        sanitizeSubtree(document.body);
        removeBrokenTmdbLinks(document.body);
    }

    // Set up MutationObserver to handle dynamically rendered content (API responses, card loading, navigation)
    let observer = null;
    function initObserver() {
        if (observer) return;

        observer = new MutationObserver(function(mutations) {
            for (let i = 0; i < mutations.length; i++) {
                const m = mutations[i];
                if (m.type === 'childList') {
                    for (let j = 0; j < m.addedNodes.length; j++) {
                        sanitizeSubtree(m.addedNodes[j]);
                    }
                } else if (m.type === 'characterData') {
                    sanitizeTextNode(m.target);
                }
            }

            if (hasGamingSuffix(document.title)) {
                document.title = cleanGamingSuffix(document.title);
            }
        });

        observer.observe(document.documentElement || document.body, {
            childList: true,
            subtree: true,
            characterData: true
        });
    }

    // Trigger sanitation across all Jellyfin SPA navigation lifecycle events
    function onNavigationEvent() {
        scanAndSanitize();
        // Schedule subsequent sweeps to catch asynchronous API data binding (e.g. details page load)
        setTimeout(scanAndSanitize, 50);
        setTimeout(scanAndSanitize, 150);
        setTimeout(scanAndSanitize, 300);
        setTimeout(scanAndSanitize, 600);
        setTimeout(scanAndSanitize, 1200);
    }

    window.addEventListener('hashchange', onNavigationEvent);
    window.addEventListener('popstate', onNavigationEvent);
    document.addEventListener('viewshow', onNavigationEvent);
    document.addEventListener('pageshow', onNavigationEvent);

    // Initial setup
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() {
            initObserver();
            onNavigationEvent();
        });
    } else {
        initObserver();
        onNavigationEvent();
    }

    JE.sanitizePeople = scanAndSanitize;
    console.log('[JellyEmu] People UI sanitization module active.');
})();
