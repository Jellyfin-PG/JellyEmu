(function () {
    var GITHUB_REPO_OWNER = "Jellyfin-PG";
    var GITHUB_REPO_NAME = "JellyEmu";
    var activeCategory = "";
    var currentDiscussions = [];
    var activeDiscussionItem = null;
    var cachedRepoId = null;
    var cachedCategoryMap = {};
    var currentUserLogin = null;

    window.jeInitCommunityTab = function (page) {
        if (!page) page = document.querySelector('#JellyEmuConfigPage');
        if (!page) return;

        updateAuthStatusUI(page);
        preloadRepoMetadata();

        var btnConnect = page.querySelector('#btnJeConnectGithub');
        if (btnConnect) {
            btnConnect.onclick = function () {
                var modal = page.querySelector('#jeGithubAuthModal');
                if (modal) modal.style.display = 'flex';
            };
        }

        var btnConnectReply = page.querySelector('#btnConnectFromReplyPrompt');
        if (btnConnectReply) {
            btnConnectReply.onclick = function () {
                var detailModal = page.querySelector('#jeDiscussionDetailModal');
                if (detailModal) detailModal.style.display = 'none';

                var modal = page.querySelector('#jeGithubAuthModal');
                if (modal) modal.style.display = 'flex';
            };
        }

        var btnCloseModal = page.querySelector('#btnCloseGithubAuthModal');
        if (btnCloseModal) {
            btnCloseModal.onclick = function () {
                var modal = page.querySelector('#jeGithubAuthModal');
                if (modal) modal.style.display = 'none';
            };
        }

        var btnSaveToken = page.querySelector('#btnSaveGithubToken');
        if (btnSaveToken) {
            btnSaveToken.onclick = function () {
                var input = page.querySelector('#jeGithubTokenInput');
                var token = input ? input.value.trim() : '';
                if (token) {
                    localStorage.setItem('je_github_token', token);
                    var modal = page.querySelector('#jeGithubAuthModal');
                    if (modal) modal.style.display = 'none';
                    updateAuthStatusUI(page);
                    preloadRepoMetadata();
                    loadDiscussions(page);
                }
            };
        }

        var btnDisconnect = page.querySelector('#btnJeDisconnectGithub');
        if (btnDisconnect) {
            btnDisconnect.onclick = function () {
                localStorage.removeItem('je_github_token');
                updateAuthStatusUI(page);
                loadDiscussions(page);
            };
        }

        var btnRefresh = page.querySelector('#btnRefreshDiscussions');
        if (btnRefresh) {
            btnRefresh.onclick = function () {
                loadDiscussions(page);
            };
        }

        var btnNewDiscussion = page.querySelector('#btnNewDiscussion');
        if (btnNewDiscussion) {
            btnNewDiscussion.onclick = function () {
                var token = localStorage.getItem('je_github_token');
                if (!token) {
                    var authModal = page.querySelector('#jeGithubAuthModal');
                    if (authModal) authModal.style.display = 'flex';
                    return;
                }
                var newModal = page.querySelector('#jeNewDiscussionModal');
                if (newModal) newModal.style.display = 'flex';
            };
        }

        var btnCloseNewDisc = page.querySelector('#btnCloseNewDiscussionModal');
        if (btnCloseNewDisc) {
            btnCloseNewDisc.onclick = function () {
                var newModal = page.querySelector('#jeNewDiscussionModal');
                if (newModal) newModal.style.display = 'none';
            };
        }

        var btnSubmitNewDisc = page.querySelector('#btnSubmitNewDiscussion');
        if (btnSubmitNewDisc) {
            btnSubmitNewDisc.onclick = function () {
                submitNewDiscussion(page);
            };
        }

        var btnCloseDetail = page.querySelector('#btnCloseDetailModal');
        if (btnCloseDetail) {
            btnCloseDetail.onclick = function () {
                var detailModal = page.querySelector('#jeDiscussionDetailModal');
                if (detailModal) detailModal.style.display = 'none';
            };
        }

        var btnSubmitReply = page.querySelector('#btnSubmitReply');
        if (btnSubmitReply) {
            btnSubmitReply.onclick = function () {
                submitReply(page);
            };
        }

        var catSelect = page.querySelector('#jeNewDiscCategory');
        var pollWrap = page.querySelector('#jePollOptionsContainer');
        if (catSelect && pollWrap) {
            catSelect.onchange = function() {
                pollWrap.style.display = (catSelect.value === 'Polls') ? 'flex' : 'none';
            };
        }

        var btnAddOpt = page.querySelector('#btnAddPollOptionInput');
        if (btnAddOpt && pollWrap) {
            btnAddOpt.onclick = function() {
                var count = pollWrap.querySelectorAll('.je-poll-opt-input').length + 1;
                var newInput = document.createElement('input');
                newInput.type = 'text';
                newInput.className = 'emby-input je-poll-opt-input';
                newInput.placeholder = 'Option ' + count + '...';
                newInput.autocomplete = 'off';
                newInput.style.cssText = 'width: 100%; box-sizing: border-box; background: rgba(0,0,0,0.3); border: 1px solid rgba(255,255,255,0.1); padding: 8px; color: #fff; border-radius: 4px;';
                pollWrap.insertBefore(newInput, btnAddOpt);
            };
        }

        loadDiscussions(page);
    };

    function preloadRepoMetadata() {
        var token = localStorage.getItem('je_github_token');
        if (!token) return;

        var query = JSON.stringify({
            query: 'query { repository(owner: "' + GITHUB_REPO_OWNER + '", name: "' + GITHUB_REPO_NAME + '") { id discussionCategories(first: 10) { nodes { id name } } } }'
        });

        fetch('https://api.github.com/graphql', {
            method: 'POST',
            headers: {
                'Authorization': 'bearer ' + token,
                'Content-Type': 'application/json'
            },
            body: query
        })
        .then(function(r) { return r.json(); })
        .then(function(res) {
            if (res.data && res.data.repository) {
                cachedRepoId = res.data.repository.id;
                var cats = (res.data.repository.discussionCategories && res.data.repository.discussionCategories.nodes) || [];
                cats.forEach(function(c) {
                    cachedCategoryMap[c.name.toLowerCase()] = c.id;
                });
            }
        })
        .catch(function(err) {
            console.error('[JellyEmu] Repo metadata fetch failed:', err);
        });
    }

    function updateAuthStatusUI(page) {
        var token = localStorage.getItem('je_github_token');
        var loggedOutBox = page.querySelector('#jeGithubLoggedOutBox');
        var loggedInBox = page.querySelector('#jeGithubLoggedInBox');
        var tokenInput = page.querySelector('#jeGithubTokenInput');
        var btnNewDiscussion = page.querySelector('#btnNewDiscussion');
        var replyForm = page.querySelector('#jeReplyForm');
        var replyAuthPrompt = page.querySelector('#jeReplyAuthPrompt');

        if (token) {
            if (loggedOutBox) loggedOutBox.style.display = 'none';
            if (loggedInBox) loggedInBox.style.display = 'flex';
            if (tokenInput) tokenInput.value = token;
            if (btnNewDiscussion) btnNewDiscussion.style.display = 'inline-flex';
            if (replyForm) replyForm.style.display = 'flex';
            if (replyAuthPrompt) replyAuthPrompt.style.display = 'none';

            fetch('https://api.github.com/user', {
                headers: { 'Authorization': 'token ' + token }
            })
            .then(function(r) { return r.json(); })
            .then(function(user) {
                if (user && user.login) {
                    currentUserLogin = user.login;
                }
                var userHandle = page.querySelector('#jeGithubUserHandle');
                var avatarImg = page.querySelector('#jeGithubUserAvatar');
                if (userHandle) userHandle.textContent = user.login ? ('@' + user.login) : 'Connected';
                if (avatarImg && user.avatar_url) avatarImg.src = user.avatar_url;
            })
            .catch(function() {
                var userHandle = page.querySelector('#jeGithubUserHandle');
                if (userHandle) userHandle.textContent = 'Token Active';
            });
        } else {
            if (loggedOutBox) loggedOutBox.style.display = 'flex';
            if (loggedInBox) loggedInBox.style.display = 'none';
            if (tokenInput) tokenInput.value = '';
            if (btnNewDiscussion) btnNewDiscussion.style.display = 'none';
            if (replyForm) replyForm.style.display = 'none';
            if (replyAuthPrompt) replyAuthPrompt.style.display = 'flex';
        }
    }

    window.jeFilterDiscussions = function (categoryName) {
        activeCategory = categoryName;
        var page = document.querySelector('#JellyEmuConfigPage');
        if (page) {
            var pills = page.querySelectorAll('.je-community-pill');
            pills.forEach(function (p) {
                p.classList.toggle('active', p.getAttribute('data-cat') === categoryName);
            });
            renderDiscussionCards(page, currentDiscussions);
        }
    };

    function loadDiscussions(page) {
        var container = page.querySelector('#jeDiscussionsContainer');
        var statusEl = page.querySelector('#jeDiscussionsStatus');
        if (!container) return;

        if (statusEl) {
            statusEl.style.display = 'block';
            statusEl.innerHTML = '<div style="display: flex; align-items: center; justify-content: center; gap: 10px;">' +
                '<span class="material-icons je-spinner" style="font-size: 24px; color: #00a4dc;">sync</span>' +
                '<span>Fetching Community Discussions...</span></div>';
        }
        container.innerHTML = '';

        fallbackPublicDiscussions(page);
    }

    function fallbackPublicDiscussions(page) {
        var statusEl = page.querySelector('#jeDiscussionsStatus');
        var authHeader = 'MediaBrowser Token="' + ApiClient.accessToken() + '"';
        fetch('/jellyemu/community/discussions?t=' + Date.now(), {
            headers: {
                'Authorization': authHeader,
                'Cache-Control': 'no-cache, no-store'
            }
        })
        .then(function(r) { return r.json(); })
        .then(function(items) {
            currentDiscussions = items || [];
            if (statusEl) statusEl.style.display = 'none';
            renderDiscussionCards(page, currentDiscussions);
        })
        .catch(function() {
            if (statusEl) {
                statusEl.innerHTML = '<div style="color: #FF4444;">Failed loading community discussions.</div>';
            }
        });
    }

    function getUpvotedDiscussions() {
        try {
            return JSON.parse(localStorage.getItem('je_upvoted_discussions') || '{}');
        } catch (e) {
            return {};
        }
    }

    function setUpvotedDiscussion(discKey, isUpvoted) {
        var map = getUpvotedDiscussions();
        if (isUpvoted) {
            map[discKey] = true;
        } else {
            delete map[discKey];
        }
        localStorage.setItem('je_upvoted_discussions', JSON.stringify(map));
    }

    function formatRelativeTime(dateStr) {
        if (!dateStr) return '';
        try {
            var date = new Date(dateStr);
            if (isNaN(date.getTime())) return '';
            var diffSec = Math.floor((new Date() - date) / 1000);
            if (diffSec < 45) return 'just now';
            if (diffSec < 3600) return Math.floor(diffSec / 60) + 'm ago';
            if (diffSec < 86400) return Math.floor(diffSec / 3600) + 'h ago';
            if (diffSec < 2592000) return Math.floor(diffSec / 86400) + 'd ago';
            return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
        } catch (e) {
            return '';
        }
    }

    function renderDiscussionCards(page, items) {
        var container = page.querySelector('#jeDiscussionsContainer');
        if (!container) return;
        container.innerHTML = '';

        var filtered = items.filter(function(item) {
            if (!activeCategory || activeCategory === '') return true;
            var catLower = activeCategory.toLowerCase();
            var itemCat = (item.category || '').toLowerCase();
            return itemCat.indexOf(catLower) !== -1 || catLower.indexOf(itemCat) !== -1;
        });

        if (filtered.length === 0) {
            container.innerHTML = '<div style="color: #aaa; padding: 2em; text-align: center; width: 100%;">No discussions found matching this category.</div>';
            return;
        }

        filtered.forEach(function(item) {
            var card = document.createElement('div');
            card.style.cssText = 'background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 10px; padding: 1.25em; display: flex; flex-direction: column; gap: 10px; transition: all 0.2s ease-in-out; position: relative; cursor: pointer;';

            var topRow = document.createElement('div');
            topRow.style.cssText = 'display: flex; align-items: center; justify-content: space-between; gap: 10px;';

            var authorInfo = document.createElement('div');
            authorInfo.style.cssText = 'display: flex; align-items: center; gap: 8px;';

            var avatar = document.createElement('img');
            avatar.style.cssText = 'width: 24px; height: 24px; border-radius: 50%; object-fit: cover;';
            avatar.src = item.avatar || 'https://github.githubassets.com/favicons/favicon.png';

            var timeStr = formatRelativeTime(item.created || item.updated);
            var authorName = document.createElement('span');
            authorName.style.cssText = 'font-size: 0.85em; color: #aaa; font-weight: 500;';
            authorName.textContent = '@' + (item.author || 'community') + (timeStr ? (' • ' + timeStr) : '');

            authorInfo.appendChild(avatar);
            authorInfo.appendChild(authorName);

            var rightBadgeGroup = document.createElement('div');
            rightBadgeGroup.style.cssText = 'display: flex; align-items: center; gap: 6px;';

            var categoryBadge = document.createElement('span');
            categoryBadge.style.cssText = 'font-size: 0.72em; font-weight: 700; padding: 2px 8px; border-radius: 4px; background: rgba(0, 164, 220, 0.15); color: #00a4dc; border: 1px solid rgba(0, 164, 220, 0.3); text-transform: uppercase;';
            categoryBadge.textContent = item.category || 'General';

            var ghLinkBtn = document.createElement('a');
            ghLinkBtn.href = item.url || ('https://github.com/' + GITHUB_REPO_OWNER + '/' + GITHUB_REPO_NAME + '/discussions');
            ghLinkBtn.target = '_blank';
            ghLinkBtn.title = 'Open Discussion on GitHub';
            ghLinkBtn.style.cssText = 'color: #aaa; display: inline-flex; align-items: center; justify-content: center; padding: 2px 6px; border-radius: 4px; background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.08); text-decoration: none; transition: all 0.15s;';
            ghLinkBtn.innerHTML = '<span class="material-icons" style="font-size: 14px;">open_in_new</span>';
            ghLinkBtn.onclick = function(e) { e.stopPropagation(); };

            rightBadgeGroup.appendChild(categoryBadge);
            rightBadgeGroup.appendChild(ghLinkBtn);

            topRow.appendChild(authorInfo);
            topRow.appendChild(rightBadgeGroup);

            var titleEl = document.createElement('h3');
            titleEl.style.cssText = 'margin: 0; font-size: 1.1em; font-weight: 600; color: #fff; line-height: 1.3;';
            titleEl.textContent = item.title || 'Untitled Discussion';

            var isPollCard = (item.category || '').toLowerCase() === 'polls' || item.poll;
            var middleContent;

            if (isPollCard) {
                var pollPreviewBox = document.createElement('div');
                pollPreviewBox.style.cssText = 'background: rgba(0, 164, 220, 0.05); border: 1px solid rgba(0, 164, 220, 0.15); border-radius: 6px; padding: 10px; display: flex; flex-direction: column; gap: 6px; font-size: 0.82em; color: #ccc; margin-top: 2px;';

                var opts = (item.poll && item.poll.options && item.poll.options.nodes) ? item.poll.options.nodes : [];
                var totalV = opts.reduce(function(a, b) { return a + (b.voteCount || 0); }, 0);

                if (opts.length > 0) {
                    opts.slice(0, 3).forEach(function(o) {
                        var vc = o.voteCount || 0;
                        var pct = totalV > 0 ? Math.round((vc / totalV) * 100) : 0;
                        var row = document.createElement('div');
                        row.style.cssText = 'display: flex; align-items: center; justify-content: space-between; font-weight: 500;';
                        row.innerHTML = '<span>' + (o.viewerHasVoted ? '✓ ' : '') + o.option + '</span><span style="color:#00a4dc; font-weight:600;">' + pct + '% (' + vc + ')</span>';
                        pollPreviewBox.appendChild(row);
                    });
                    if (opts.length > 3) {
                        var moreRow = document.createElement('div');
                        moreRow.style.cssText = 'font-size: 0.78em; color: #888; font-style: italic;';
                        moreRow.textContent = '+ ' + (opts.length - 3) + ' more options...';
                        pollPreviewBox.appendChild(moreRow);
                    }
                } else {
                    pollPreviewBox.innerHTML = '<span style="color: #00a4dc; font-weight: 600; display: flex; align-items: center; gap: 4px;"><span class="material-icons" style="font-size: 14px;">poll</span>Poll Topic</span><span style="color: #aaa;">Click to view options and vote.</span>';
                }
                middleContent = pollPreviewBox;
            } else {
                var bodyPreview = document.createElement('div');
                bodyPreview.style.cssText = 'margin: 0; color: #888; font-size: 0.88em; line-height: 1.4; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;';
                bodyPreview.innerHTML = renderFormattedContent(item.summary || 'Click to view discussion details and comments.');
                middleContent = bodyPreview;
            }

            var actionRow = document.createElement('div');
            actionRow.style.cssText = 'display: flex; align-items: center; justify-content: space-between; margin-top: auto; border-top: 1px solid rgba(255,255,255,0.04); padding-top: 8px;';

            var repliesCount = item.replies || 0;
            var replyInfo = document.createElement('div');
            replyInfo.style.cssText = 'display: flex; align-items: center; gap: 4px; color: #888; font-size: 0.82em;';
            replyInfo.innerHTML = '<span class="material-icons" style="font-size: 14px;">chat_bubble_outline</span><span>' + repliesCount + ' replies</span>';

            var viewLink = document.createElement('div');
            viewLink.style.cssText = 'color: #00a4dc; font-weight: 600; font-size: 0.82em;';
            viewLink.innerHTML = 'View &amp; Reply →';

            actionRow.appendChild(replyInfo);
            actionRow.appendChild(viewLink);

            card.appendChild(topRow);
            card.appendChild(titleEl);
            card.appendChild(middleContent);
            card.appendChild(actionRow);

            card.onclick = function() {
                openDiscussionDetail(page, item);
            };

            container.appendChild(card);
        });
    }

    window.jeApplyMarkdown = function(btn, type) {
        if (!btn) return;
        var parent = btn.closest('.inputContainer') || btn.closest('div[style*="flex-direction: column"]');
        var textarea = parent ? parent.querySelector('textarea') : null;
        if (!textarea) return;

        var start = textarea.selectionStart || 0;
        var end = textarea.selectionEnd || 0;
        var text = textarea.value;
        var selected = text.substring(start, end);
        var replacement = '';

        switch (type) {
            case 'bold':
                replacement = '**' + (selected || 'bold text') + '**';
                break;
            case 'italic':
                replacement = '*' + (selected || 'italic text') + '*';
                break;
            case 'code':
                if (selected.indexOf('\n') !== -1) {
                    replacement = '```\n' + selected + '\n```';
                } else {
                    replacement = '`' + (selected || 'code') + '`';
                }
                break;
            case 'link':
                replacement = '[' + (selected || 'link text') + '](https://)';
                break;
            case 'list':
                replacement = '- ' + (selected || 'list item');
                break;
            case 'quote':
                replacement = '> ' + (selected || 'quote text');
                break;
        }

        textarea.value = text.substring(0, start) + replacement + text.substring(end);
        textarea.focus();
        textarea.selectionStart = start + replacement.length;
        textarea.selectionEnd = start + replacement.length;
    };

    function renderFormattedContent(text) {
        if (!text) return '';
        var safe = document.createElement('div');
        safe.textContent = text;
        var html = safe.innerHTML;

        html = html.replace(/```([\s\S]*?)```/g, function(m, p1) {
            return '<pre style="background: rgba(0,0,0,0.4); border: 1px solid rgba(255,255,255,0.1); border-radius: 6px; padding: 10px; overflow-x: auto; font-family: monospace; font-size: 0.85em; color: #52B54B; margin: 8px 0;"><code>' + p1.trim() + '</code></pre>';
        });

        html = html.replace(/`([^`]+)`/g, function(m, p1) {
            return '<code style="background: rgba(0,0,0,0.3); border: 1px solid rgba(255,255,255,0.1); border-radius: 4px; padding: 2px 6px; font-family: monospace; font-size: 0.88em; color: #00a4dc;">' + p1 + '</code>';
        });

        html = html.replace(/!\[([^\]]*)\]\((https?:\/\/[^\s\)]+)\)/gi, function(m, alt, url) {
            return '<div style="margin: 10px 0;"><img src="' + url + '" alt="' + alt + '" style="max-width: 100%; max-height: 400px; object-fit: contain; border-radius: 8px; border: 1px solid rgba(255,255,255,0.1);" /></div>';
        });

        html = html.replace(/(^|\s)(https?:\/\/[^\s<]+\.(?:png|jpg|jpeg|gif|webp))(\s|$)/gi, function(m, p1, url, p3) {
            return p1 + '<div style="margin: 10px 0;"><img src="' + url + '" style="max-width: 100%; max-height: 400px; object-fit: contain; border-radius: 8px; border: 1px solid rgba(255,255,255,0.1);" /></div>' + p3;
        });

        html = html.replace(/\[([^\]]+)\]\((https?:\/\/[^\s\)]+)\)/gi, function(m, title, url) {
            return '<a href="' + url + '" target="_blank" style="color: #00a4dc; text-decoration: underline; font-weight: 500;">' + title + '</a>';
        });

        html = html.replace(/^###\s+(.*$)/gim, '<h4 style="margin: 8px 0 4px; color: #fff; font-size: 1.05em; font-weight: 600;">$1</h4>');
        html = html.replace(/^##\s+(.*$)/gim, '<h3 style="margin: 10px 0 6px; color: #fff; font-size: 1.2em; font-weight: 600;">$1</h3>');
        html = html.replace(/^#\s+(.*$)/gim, '<h2 style="margin: 12px 0 6px; color: #fff; font-size: 1.35em; font-weight: 700;">$1</h2>');

        html = html.replace(/(\*\*|__)(.*?)\1/g, '<strong style="color: #fff; font-weight: 700;">$2</strong>');
        html = html.replace(/(\*|_)(.*?)\1/g, '<em style="color: #ddd; font-style: italic;">$2</em>');

        html = html.replace(/^&gt;\s+(.*$)/gim, '<blockquote style="margin: 8px 0; padding: 6px 12px; border-left: 3px solid #00a4dc; background: rgba(0, 164, 220, 0.08); color: #ccc; font-style: italic;">$1</blockquote>');
        html = html.replace(/^[\-\*]\s+(.*$)/gim, '<li style="margin-left: 18px; color: #ddd;">$1</li>');

        return html;
    }

    function renderCommentsList(page, commentsList) {
        var container = page ? page.querySelector('#jeDetailCommentsContainer') : document.querySelector('#jeDetailCommentsContainer');
        if (!container) return;
        container.innerHTML = '';
        if (!commentsList || commentsList.length === 0) {
            container.innerHTML = '<div style="color: #888; font-size: 0.88em; font-style: italic;">No replies yet. Be the first to reply!</div>';
            return;
        }
        commentsList.forEach(function(c) {
            var cBox = document.createElement('div');
            cBox.style.cssText = 'background: rgba(255,255,255,0.03); border: 1px solid rgba(255,255,255,0.06); border-radius: 8px; padding: 12px; display: flex; flex-direction: column; gap: 6px;';

            var cHeader = document.createElement('div');
            cHeader.style.cssText = 'display: flex; align-items: center; justify-content: space-between; gap: 8px;';

            var cAuthorGroup = document.createElement('div');
            cAuthorGroup.style.cssText = 'display: flex; align-items: center; gap: 8px;';

            var cAvatar = document.createElement('img');
            cAvatar.style.cssText = 'width: 20px; height: 20px; border-radius: 50%;';
            cAvatar.src = (c.author && c.author.avatarUrl) ? c.author.avatarUrl : 'https://github.githubassets.com/favicons/favicon.png';

            var cTimeStr = formatRelativeTime(c.createdAt || c.created);
            var authorLogin = (c.author && c.author.login) ? c.author.login : ((c.user && c.user.login) ? c.user.login : 'user');
            var cHandle = document.createElement('span');
            cHandle.style.cssText = 'font-size: 0.82em; color: #aaa; font-weight: 500;';
            cHandle.textContent = '@' + authorLogin + (cTimeStr ? (' • ' + cTimeStr) : '');

            cAuthorGroup.appendChild(cAvatar);
            cAuthorGroup.appendChild(cHandle);
            cHeader.appendChild(cAuthorGroup);

            var isOwnComment = (c.isUser === true) || (currentUserLogin && authorLogin.toLowerCase() === currentUserLogin.toLowerCase());
            if (isOwnComment && c.id) {
                var delBtn = document.createElement('button');
                delBtn.type = 'button';
                delBtn.title = 'Delete Reply';
                delBtn.style.cssText = 'background: none; border: none; color: #888; cursor: pointer; display: flex; align-items: center; padding: 2px; transition: color 0.15s;';
                delBtn.innerHTML = '<span class="material-icons" style="font-size: 16px;">delete</span>';

                delBtn.onmouseover = function() { delBtn.style.color = '#FF4444'; };
                delBtn.onmouseout = function() { delBtn.style.color = '#888'; };

                delBtn.onclick = function() {
                    var token = localStorage.getItem('je_github_token');
                    if (!token) return;

                    if (confirm('Are you sure you want to delete your reply?')) {
                        delBtn.disabled = true;
                        fetch('https://api.github.com/repos/' + GITHUB_REPO_OWNER + '/' + GITHUB_REPO_NAME + '/issues/comments/' + c.id, {
                            method: 'DELETE',
                            headers: { 'Authorization': 'token ' + token }
                        })
                        .then(function(r) {
                            if (r.status === 204 || r.ok) {
                                cBox.remove();
                                if (activeDiscussionItem) {
                                    activeDiscussionItem.replies = Math.max(0, (activeDiscussionItem.replies || 1) - 1);
                                    var mainPage = page || document.querySelector('#JellyEmuConfigPage');
                                    renderDiscussionCards(mainPage, currentDiscussions);
                                }
                            } else {
                                alert('Failed deleting reply.');
                            }
                        })
                        .catch(function(err) {
                            alert('Error deleting reply: ' + err.message);
                        });
                    }
                };
                cHeader.appendChild(delBtn);
            }

            var cBody = document.createElement('div');
            cBody.style.cssText = 'font-size: 0.88em; color: #ddd; line-height: 1.4; white-space: pre-wrap;';
            cBody.innerHTML = renderFormattedContent(c.body || '');

            cBox.appendChild(cHeader);
            cBox.appendChild(cBody);
            container.appendChild(cBox);
        });
    }

    function openDiscussionDetail(page, item) {
        activeDiscussionItem = item;
        var modal = page.querySelector('#jeDiscussionDetailModal');
        if (!modal) return;

        var titleEl = page.querySelector('#jeDetailTitle');
        var badgeEl = page.querySelector('#jeDetailCategoryBadge');
        var avatarImg = page.querySelector('#jeDetailAuthorAvatar');
        var handleSpan = page.querySelector('#jeDetailAuthorHandle');
        var bodyEl = page.querySelector('#jeDetailBody');
        var pollContainer = page.querySelector('#jeDetailPollContainer');
        var commentsContainer = page.querySelector('#jeDetailCommentsContainer');
        var replyInput = page.querySelector('#jeReplyBody');
        var replyStatus = page.querySelector('#jeReplyStatus');

        var detailTimeStr = formatRelativeTime(item.created || item.updated);
        if (titleEl) titleEl.textContent = item.title || 'Discussion Details';
        if (badgeEl) badgeEl.textContent = item.category || 'General';
        if (avatarImg) avatarImg.src = item.avatar || 'https://github.githubassets.com/favicons/favicon.png';
        if (handleSpan) handleSpan.textContent = '@' + (item.author || 'community') + (detailTimeStr ? (' • ' + detailTimeStr) : '');

        if (bodyEl) {
            var bodyText = item.body || item.summary || '';
            var isPoll = (item.category || '').toLowerCase() === 'polls' || bodyText.indexOf('## Options') !== -1;
            if (isPoll) {
                bodyText = bodyText.replace(/##\s*Question[\s\S]*?##\s*Options/gi, '')
                                   .replace(/##\s*Options[\s\S]*/gi, '')
                                   .replace(/^[\-\*]\s*\[[ xX]\]\s*.*$/gm, '')
                                   .trim();
            }
            if (bodyText) {
                bodyEl.style.display = 'block';
                bodyEl.innerHTML = renderFormattedContent(bodyText);
            } else {
                bodyEl.style.display = 'none';
            }
        }

        if (replyInput) replyInput.value = '';
        if (replyStatus) replyStatus.textContent = '';

        renderPollContainer(page, item, pollContainer);

        renderCommentsList(page, item.comments || []);

        if (item.number) {
            var authHeader = 'MediaBrowser Token="' + ApiClient.accessToken() + '"';
            fetch('/jellyemu/community/discussions/' + item.number + '/comments?t=' + Date.now(), {
                headers: {
                    'Authorization': authHeader,
                    'Cache-Control': 'no-cache, no-store'
                }
            })
            .then(function(r) { return r.json(); })
            .then(function(fetchedComments) {
                if (fetchedComments && fetchedComments.length > 0) {
                    item.comments = fetchedComments;
                    renderCommentsList(page, fetchedComments);
                }
            })
            .catch(function(err) {
                console.error('[JellyEmu] Comments fetch failed:', err);
            });
        }

        modal.style.display = 'flex';
    }

    function renderPollContainer(page, item, container) {
        if (!container) return;
        container.innerHTML = '';

        var poll = item.poll;
        var catStr = (item.category || '').toLowerCase().trim();
        var isPollCategory = catStr.indexOf('poll') !== -1 || catStr === 'polls';

        if (!poll && !isPollCategory) {
            container.style.display = 'none';
            return;
        }

        container.style.display = 'block';

        var questionText = (poll && poll.question) ? poll.question : (item.title || 'Poll Options');
        var optionsNodes = (poll && poll.options && poll.options.nodes) ? poll.options.nodes : [];

        if (optionsNodes.length === 0 && (isPollCategory || poll)) {
            var textToParse = item.body || '';
            var lines = textToParse.split('\n');
            lines.forEach(function(line, idx) {
                var trimmed = line.trim();
                if (!trimmed || trimmed.indexOf('#') === 0) return;
                var isChecked = trimmed.indexOf('[x]') !== -1 || trimmed.indexOf('[X]') !== -1;
                var optText = trimmed.replace(/^[\-\*]\s*\[[ xX]\]\s*|^- |^\* |\d+[\.\)]|^option\s*\d+:?/i, '').trim();
                if (optText && optText.length > 0) {
                    optionsNodes.push({
                        id: 'opt_' + idx,
                        option: optText,
                        voteCount: isChecked ? 1 : 0,
                        viewerHasVoted: isChecked
                    });
                }
            });
            if (poll) {
                poll.options = { nodes: optionsNodes };
            } else {
                item.poll = { options: { nodes: optionsNodes } };
            }
        }

        if (optionsNodes.length === 0) {
            container.style.display = 'none';
            return;
        }

        var totalVotes = optionsNodes.reduce(function(a, b) { return a + (b.voteCount || 0); }, 0);
        if (poll && typeof poll.totalVoteCount === 'number' && poll.totalVoteCount > totalVotes) {
            totalVotes = poll.totalVoteCount;
        }

        var header = document.createElement('div');
        header.style.cssText = 'display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px;';
        header.innerHTML = '<h4 style="margin: 0; color: #fff; font-size: 1.05em; display: flex; align-items: center; gap: 6px;">' +
            '<span class="material-icons" style="color: #00a4dc;">poll</span>' +
            '<span>' + questionText + '</span></h4>' +
            '<span style="font-size: 0.82em; color: #aaa;">' + totalVotes + ' votes</span>';
        container.appendChild(header);

        var optionsWrap = document.createElement('div');
        optionsWrap.style.cssText = 'display: flex; flex-direction: column; gap: 8px;';

        optionsNodes.forEach(function(opt) {
            var vCount = opt.voteCount || 0;
            var pct = totalVotes > 0 ? Math.round((vCount / totalVotes) * 100) : 0;
            var hasVoted = opt.viewerHasVoted === true;

            var optCard = document.createElement('div');
            optCard.style.cssText = 'background: rgba(255,255,255,0.02); border: 1px solid ' + (hasVoted ? 'rgba(82, 181, 75, 0.4)' : 'rgba(255,255,255,0.06)') + '; border-radius: 8px; padding: 10px 14px; position: relative; overflow: hidden; display: flex; align-items: center; justify-content: space-between; gap: 10px; transition: all 0.15s;';

            var fillBar = document.createElement('div');
            fillBar.style.cssText = 'position: absolute; left: 0; top: 0; bottom: 0; width: ' + pct + '%; background: ' + (hasVoted ? 'rgba(82, 181, 75, 0.15)' : 'rgba(0, 164, 220, 0.12)') + '; transition: width 0.3s ease-in-out; pointer-events: none;';
            optCard.appendChild(fillBar);

            var infoText = document.createElement('div');
            infoText.style.cssText = 'position: relative; z-index: 1; display: flex; align-items: center; gap: 8px; font-size: 0.9em; color: #fff; font-weight: 500;';
            infoText.textContent = opt.option;
            optCard.appendChild(infoText);

            var rightBox = document.createElement('div');
            rightBox.style.cssText = 'position: relative; z-index: 1; display: flex; align-items: center; gap: 10px;';

            var pctSpan = document.createElement('span');
            pctSpan.style.cssText = 'font-size: 0.82em; color: #aaa; font-weight: 600;';
            pctSpan.textContent = pct + '% (' + vCount + ')';
            rightBox.appendChild(pctSpan);

            var voteBtn = document.createElement('button');
            voteBtn.type = 'button';
            voteBtn.style.cssText = 'background: ' + (hasVoted ? 'rgba(82,181,75,0.25)' : '#00a4dc') + '; color: ' + (hasVoted ? '#52B54B' : '#fff') + '; border: ' + (hasVoted ? '1px solid rgba(82,181,75,0.5)' : 'none') + '; padding: 4px 12px; border-radius: 6px; font-size: 0.8em; font-weight: 600; cursor: pointer; transition: all 0.15s;';
            voteBtn.textContent = hasVoted ? 'Voted ✓' : 'Vote';

            voteBtn.onclick = function(e) {
                e.stopPropagation();

                var willVote = !opt.viewerHasVoted;
                if (willVote) {
                    optionsNodes.forEach(function(o) {
                        if (o !== opt && o.viewerHasVoted) {
                            o.viewerHasVoted = false;
                            o.voteCount = Math.max(0, (o.voteCount || 1) - 1);
                        }
                    });
                    opt.viewerHasVoted = true;
                    opt.voteCount = (opt.voteCount || 0) + 1;
                } else {
                    opt.viewerHasVoted = false;
                    opt.voteCount = Math.max(0, (opt.voteCount || 1) - 1);
                }

                renderPollContainer(page, item, container);

                var token = localStorage.getItem('je_github_token');
                if (token && item && item.number) {
                    var optMarkdown = optionsNodes.map(function(o) {
                        return (o.viewerHasVoted ? '- [x] ' : '- [ ] ') + o.option;
                    }).join('\n');

                    var newBody = '## Question\n' + questionText + '\n\n## Options\n' + optMarkdown;

                    fetch('https://api.github.com/repos/' + GITHUB_REPO_OWNER + '/' + GITHUB_REPO_NAME + '/issues/' + item.number, {
                        method: 'PATCH',
                        headers: {
                            'Authorization': 'token ' + token,
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify({ body: newBody })
                    })
                    .catch(function(err) {
                        console.error('[JellyEmu] Persisting poll vote failed:', err);
                    });
                }
            };

            rightBox.appendChild(voteBtn);
            optCard.appendChild(rightBox);
            optionsWrap.appendChild(optCard);
        });

        container.appendChild(optionsWrap);
    }

    function submitReply(page) {
        var replyInput = page.querySelector('#jeReplyBody');
        var replyStatus = page.querySelector('#jeReplyStatus');
        var text = replyInput ? replyInput.value.trim() : '';

        if (!text) {
            if (replyStatus) {
                replyStatus.textContent = 'Please enter a reply message.';
                replyStatus.style.color = '#f0c040';
            }
            return;
        }

        var token = localStorage.getItem('je_github_token');
        if (!token) {
            var authModal = page.querySelector('#jeGithubAuthModal');
            if (authModal) authModal.style.display = 'flex';
            return;
        }

        if (replyStatus) {
            replyStatus.textContent = 'Posting reply...';
            replyStatus.style.color = '#aaa';
        }

        if (activeDiscussionItem && activeDiscussionItem.number) {
            fetch('https://api.github.com/repos/' + GITHUB_REPO_OWNER + '/' + GITHUB_REPO_NAME + '/issues/' + activeDiscussionItem.number + '/comments', {
                method: 'POST',
                headers: {
                    'Authorization': 'token ' + token,
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ body: text })
            })
            .then(function(r) { return r.json(); })
            .then(function(newC) {
                if (newC && newC.id) {
                    if (replyStatus) {
                        replyStatus.textContent = 'Reply posted!';
                        replyStatus.style.color = '#52B54B';
                    }
                    replyInput.value = '';

                    newC.isUser = true;
                    if (activeDiscussionItem) {
                        activeDiscussionItem.replies = (activeDiscussionItem.replies || 0) + 1;
                        if (!activeDiscussionItem.comments) activeDiscussionItem.comments = [];
                        activeDiscussionItem.comments.push(newC);
                        renderCommentsList(page, activeDiscussionItem.comments);
                        renderDiscussionCards(page, currentDiscussions);
                    }

                    setTimeout(function() {
                        if (replyStatus) replyStatus.textContent = '';
                    }, 3000);
                } else {
                    if (replyStatus) {
                        replyStatus.textContent = 'Posting reply failed.';
                        replyStatus.style.color = '#FF4444';
                    }
                }
            })
            .catch(function(err) {
                if (replyStatus) {
                    replyStatus.textContent = 'Error: ' + err.message;
                    replyStatus.style.color = '#FF4444';
                }
            });
        }
    }

    function submitNewDiscussion(page) {
        var token = localStorage.getItem('je_github_token');
        var catSelect = page.querySelector('#jeNewDiscCategory');
        var titleInput = page.querySelector('#jeNewDiscTitle');
        var bodyInput = page.querySelector('#jeNewDiscBody');
        var statusEl = page.querySelector('#jeNewDiscStatus');

        if (!token) {
            var authModal = page.querySelector('#jeGithubAuthModal');
            if (authModal) authModal.style.display = 'flex';
            return;
        }

        var catName = catSelect ? catSelect.value : 'General';
        var titleVal = titleInput ? titleInput.value.trim() : '';
        var bodyVal = bodyInput ? bodyInput.value.trim() : '';

        if (!titleVal) {
            if (statusEl) {
                statusEl.textContent = 'Title is required.';
                statusEl.style.color = '#f0c040';
            }
            return;
        }

        var labelKey = 'jellyemu:general';
        var catLower = catName.toLowerCase();
        if (catLower.indexOf('announcement') !== -1) labelKey = 'jellyemu:announcement';
        else if (catLower.indexOf('idea') !== -1) labelKey = 'jellyemu:idea';
        else if (catLower.indexOf('poll') !== -1) labelKey = 'jellyemu:poll';
        else if (catLower.indexOf('q&a') !== -1 || catLower.indexOf('qna') !== -1) labelKey = 'jellyemu:qna';
        else if (catLower.indexOf('show') !== -1) labelKey = 'jellyemu:showcase';

        var imgInput = page.querySelector('#jeNewDiscImageUrl');
        var imgUrlVal = imgInput ? imgInput.value.trim() : '';

        var formattedTitle = '[' + catName + '] ' + titleVal;
        var formattedBody = bodyVal;
        if (catName === 'Polls') {
            var optInputs = page.querySelectorAll('.je-poll-opt-input');
            var optList = [];
            optInputs.forEach(function(inp) {
                var v = inp.value.trim();
                if (v) optList.push('- [ ] ' + v);
            });
            if (optList.length > 0) {
                formattedBody = '## Question\n' + titleVal + '\n\n## Options\n' + optList.join('\n') + (bodyVal ? ('\n\n' + bodyVal) : '');
            }
        }

        if (imgUrlVal) {
            formattedBody = (formattedBody ? (formattedBody + '\n\n') : '') + '![Image](' + imgUrlVal + ')';
        }

        if (statusEl) {
            statusEl.textContent = 'Creating structured discussion...';
            statusEl.style.color = '#aaa';
        }

        fetch('https://api.github.com/repos/' + GITHUB_REPO_OWNER + '/' + GITHUB_REPO_NAME + '/issues', {
            method: 'POST',
            headers: {
                'Authorization': 'token ' + token,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                title: formattedTitle,
                body: formattedBody,
                labels: [labelKey, catName]
            })
        })
        .then(function(r) { return r.json(); })
        .then(function(res) {
            if (res && res.id) {
                if (statusEl) {
                    statusEl.textContent = 'Discussion created!';
                    statusEl.style.color = '#52B54B';
                }
                if (titleInput) titleInput.value = '';
                if (bodyInput) bodyInput.value = '';
                if (imgInput) imgInput.value = '';
                var newModal = page.querySelector('#jeNewDiscussionModal');
                if (newModal) newModal.style.display = 'none';

                var cleanTitle = (res.title || titleVal).replace(/^\[(Announcements|General|Ideas|Polls|Q&A|Show and Tell)\]\s*/i, '').trim();
                var pollOptionsNodes = [];
                if (catName === 'Polls' && optList.length > 0) {
                    pollOptionsNodes = optList.map(function(o, i) {
                        return {
                            id: 'opt_' + (i + 1),
                            option: o.replace(/^[\-\*]\s*\[[ xX]\]\s*/, ''),
                            voteCount: 0,
                            viewerHasVoted: false
                        };
                    });
                }

                var createdItem = {
                    id: res.html_url || ('#' + res.number),
                    number: res.number,
                    title: cleanTitle || titleVal,
                    url: res.html_url || '',
                    created: res.created_at || new Date().toISOString(),
                    updated: res.updated_at || new Date().toISOString(),
                    summary: (bodyVal || '').replace(/<[^>]*>?/gm, '').trim(),
                    body: formattedBody,
                    author: (res.user && res.user.login) ? res.user.login : 'you',
                    avatar: (res.user && res.user.avatar_url) ? res.user.avatar_url : 'https://github.githubassets.com/favicons/favicon.png',
                    category: catName,
                    upvotes: 1,
                    replies: 0,
                    comments: [],
                    poll: (catName === 'Polls' && pollOptionsNodes.length > 0) ? { question: cleanTitle || titleVal, options: { nodes: pollOptionsNodes } } : null
                };

                currentDiscussions.unshift(createdItem);
                renderDiscussionCards(page, currentDiscussions);

                setTimeout(function() {
                    loadDiscussions(page);
                }, 3000);
            } else {
                if (statusEl) {
                    statusEl.textContent = 'Creation failed.';
                    statusEl.style.color = '#FF4444';
                }
            }
        })
        .catch(function(err) {
            if (statusEl) {
                statusEl.textContent = 'Error: ' + err.message;
                statusEl.style.color = '#FF4444';
            }
        });
    }
})();
