(function () {
    var pluginId = "9bab105e-9af0-4e25-a87d-876713b60962";
    var activeCategory = "";
    var currentDiscussions = [];
    var activeDiscussionItem = null;
    var currentInstanceId = null;
    var workerUrl = "https://jellyemu-community.grimmdev.workers.dev";
    var activeEditTarget = null; // { type: 'issue'|'reply', id: string, image_url: string }

    // Instance ID Generator / Retriever
    function getInstanceId() {
        if (currentInstanceId) return currentInstanceId;
        var cached = localStorage.getItem('je_instance_id');
        if (cached) {
            currentInstanceId = cached;
            return cached;
        }
        var newId = 'inst_' + Math.random().toString(36).substring(2, 12) + Date.now().toString(36);
        localStorage.setItem('je_instance_id', newId);
        currentInstanceId = newId;
        return newId;
    }

    function getUsername() {
        var name = localStorage.getItem('je_username');
        if (name && name.trim()) return name.trim();
        var inst = getInstanceId();
        return 'Gamer-' + inst.substring(inst.length - 6);
    }

    function setUsername(name) {
        if (!name || !name.trim()) return;
        localStorage.setItem('je_username', name.trim());
    }

    // Worker API Config
    function fetchWorkerConfig(cb) {
        if (typeof ApiClient !== 'undefined' && ApiClient.getPluginConfiguration) {
            ApiClient.getPluginConfiguration(pluginId).then(function (cfg) {
                if (cfg && cfg.CommunityWorkerUrl && cfg.CommunityWorkerUrl.trim()) {
                    workerUrl = cfg.CommunityWorkerUrl.trim().replace(/\/+$/, '');
                }
                if (cb) cb();
            }).catch(function () {
                if (cb) cb();
            });
        } else {
            if (cb) cb();
        }
    }

    function cwFetch(endpoint, options) {
        options = options || {};
        options.headers = options.headers || {};
        if (!options.headers['Content-Type'] && !(options.body instanceof FormData)) {
            options.headers['Content-Type'] = 'application/json';
        }

        var url = workerUrl + endpoint;
        return fetch(url, options).then(function (r) {
            if (!r.ok) {
                return r.text().then(function (txt) {
                    throw new Error('Community API request failed (' + r.status + '): ' + txt);
                });
            }
            var ct = r.headers.get('content-type') || '';
            if (ct.indexOf('application/json') !== -1) {
                return r.json();
            }
            return r.text();
        });
    }

    function compressImageFile(file) {
        if (!file || !file.type || file.type.indexOf('image/') !== 0) return Promise.resolve(file);
        // Skip compression if file is already under 1MB
        if (file.size <= 1024 * 1024) return Promise.resolve(file);

        return new Promise(function (resolve) {
            var reader = new FileReader();
            reader.onload = function (e) {
                var img = new Image();
                img.onload = function () {
                    var maxW = 1920;
                    var maxH = 1080;
                    var w = img.width;
                    var h = img.height;

                    if (w > maxW || h > maxH) {
                        if (w / h > maxW / maxH) {
                            h = Math.round((h * maxW) / w);
                            w = maxW;
                        } else {
                            w = Math.round((w * maxH) / h);
                            h = maxH;
                        }
                    }

                    var canvas = document.createElement('canvas');
                    canvas.width = w;
                    canvas.height = h;
                    var ctx = canvas.getContext('2d');
                    ctx.drawImage(img, 0, 0, w, h);

                    canvas.toBlob(function (blob) {
                        if (!blob) {
                            resolve(file);
                            return;
                        }
                        var compFile = new File([blob], (file.name || 'screenshot').replace(/\.[^/.]+$/, "") + ".jpg", {
                            type: 'image/jpeg',
                            lastModified: Date.now()
                        });
                        resolve(compFile);
                    }, 'image/jpeg', 0.85);
                };
                img.onerror = function () { resolve(file); };
                img.src = e.target.result;
            };
            reader.onerror = function () { resolve(file); };
            reader.readAsDataURL(file);
        });
    }

    // Image Upload Helper (Catbox.moe via Worker Proxy)
    function uploadAttachment(file) {
        if (!file) return Promise.resolve(null);

        // Max limit check: 10MB
        if (file.size > 10 * 1024 * 1024) {
            alert('Image file size exceeds 10MB. Please select a smaller screenshot.');
            return Promise.reject(new Error('File exceeds 10MB size limit.'));
        }

        return compressImageFile(file).then(function (compFile) {
            var formData = new FormData();
            formData.append('file', compFile, compFile.name || 'screenshot.jpg');

            return cwFetch('/api/upload', {
                method: 'POST',
                body: formData
            }).then(function (res) {
                return (res && res.url) ? res.url : null;
            }).catch(function (err) {
                console.error('[JellyEmu] Image upload failed:', err);
                throw new Error('Image upload failed. Please check your image or try publishing without an attachment.');
            });
        });
    }

    // Init Tab
    window.jeInitCommunityTab = function (page) {
        if (!page) page = document.querySelector('#JellyEmuConfigPage');
        if (!page) return;

        // Try fetching Jellyfin ServerId for Instance ID
        if (typeof ApiClient !== 'undefined' && ApiClient.getPublicSystemInfo) {
            ApiClient.getPublicSystemInfo().then(function (info) {
                if (info && info.Id) {
                    currentInstanceId = 'srv_' + info.Id;
                    localStorage.setItem('je_instance_id', currentInstanceId);
                }
                updateIdentityUI(page);
            }).catch(function () {
                getInstanceId();
                updateIdentityUI(page);
            });
        } else {
            getInstanceId();
            updateIdentityUI(page);
        }

        fetchWorkerConfig(function () {
            bindEventHandlers(page);
            loadDiscussions(page);
        });
    };

    function updateIdentityUI(page) {
        var userTag = page.querySelector('#jeDisplayUserTag');
        var instTag = page.querySelector('#jeInstanceIdTag');
        var inst = getInstanceId();
        var username = getUsername();

        if (userTag) userTag.textContent = 'Posting as: ' + username;
        if (instTag) instTag.textContent = 'Instance ID: ' + inst;
    }

    function bindEventHandlers(page) {
        // Username modal
        var btnEditUser = page.querySelector('#btnEditUsername');
        if (btnEditUser) {
            btnEditUser.onclick = function () {
                var modal = page.querySelector('#jeUsernameModal');
                var input = page.querySelector('#jeUsernameInput');
                if (input) input.value = getUsername();
                if (modal) modal.style.display = 'flex';
            };
        }

        var btnCloseUser = page.querySelector('#btnCloseUsernameModal');
        if (btnCloseUser) {
            btnCloseUser.onclick = function () {
                var modal = page.querySelector('#jeUsernameModal');
                if (modal) modal.style.display = 'none';
            };
        }

        var btnSaveUser = page.querySelector('#btnSaveUsername');
        if (btnSaveUser) {
            btnSaveUser.onclick = function () {
                var input = page.querySelector('#jeUsernameInput');
                if (input && input.value.trim()) {
                    setUsername(input.value.trim());
                    updateIdentityUI(page);
                    var modal = page.querySelector('#jeUsernameModal');
                    if (modal) modal.style.display = 'none';
                }
            };
        }

        // Category Pills
        var pillContainer = page.querySelector('#jeCategoryPillsContainer');
        if (pillContainer) {
            pillContainer.onclick = function (ev) {
                var target = ev.target;
                if (target && target.classList.contains('je-community-pill')) {
                    var pills = pillContainer.querySelectorAll('.je-community-pill');
                    pills.forEach(function (p) { p.classList.remove('active'); });
                    target.classList.add('active');
                    activeCategory = target.getAttribute('data-cat') || '';
                    renderDiscussionsGrid(page);
                }
            };
        }

        // Refresh Button
        var btnRefresh = page.querySelector('#btnRefreshDiscussions');
        if (btnRefresh) {
            btnRefresh.onclick = function () { loadDiscussions(page); };
        }

        // New Post Modal
        var btnNew = page.querySelector('#btnNewDiscussion');
        if (btnNew) {
            btnNew.onclick = function () {
                var modal = page.querySelector('#jeNewDiscussionModal');
                if (modal) modal.style.display = 'flex';
            };
        }

        var btnCloseNew = page.querySelector('#btnCloseNewDiscussionModal');
        if (btnCloseNew) {
            btnCloseNew.onclick = function () {
                var modal = page.querySelector('#jeNewDiscussionModal');
                if (modal) modal.style.display = 'none';
            };
        }

        // Dynamic Poll Options in New Post Modal
        var catSelect = page.querySelector('#jeNewDiscCategory');
        var pollContainer = page.querySelector('#jePollOptionsContainer');
        if (catSelect && pollContainer) {
            catSelect.onchange = function () {
                pollContainer.style.display = (catSelect.value === 'Polls') ? 'flex' : 'none';
            };
        }

        var btnAddPollOpt = page.querySelector('#btnAddPollOptionInput');
        if (btnAddPollOpt && pollContainer) {
            btnAddPollOpt.onclick = function () {
                var count = pollContainer.querySelectorAll('.je-poll-opt-input').length + 1;
                var input = document.createElement('input');
                input.type = 'text';
                input.className = 'emby-input je-poll-opt-input';
                input.placeholder = 'Option ' + count + '...';
                input.autocomplete = 'off';
                input.style.cssText = 'width:100%; background: rgba(0,0,0,0.3); border: 1px solid rgba(255,255,255,0.1); padding: 8px; color: #fff; border-radius: 4px;';
                pollContainer.insertBefore(input, btnAddPollOpt);
            };
        }

        // Submit New Post / Poll
        var btnSubmitNew = page.querySelector('#btnSubmitNewDiscussion');
        if (btnSubmitNew) {
            btnSubmitNew.onclick = function () { submitNewDiscussion(page); };
        }

        // Detail Modal Close
        var btnCloseDetail = page.querySelector('#btnCloseDetailModal');
        if (btnCloseDetail) {
            btnCloseDetail.onclick = function () {
                var modal = page.querySelector('#jeDiscussionDetailModal');
                if (modal) modal.style.display = 'none';
            };
        }

        // Submit Reply
        var btnSubmitReply = page.querySelector('#btnSubmitReply');
        if (btnSubmitReply) {
            btnSubmitReply.onclick = function () { submitReply(page); };
        }

        // Edit Modal Close
        var btnCloseEdit = page.querySelector('#btnCloseEditModal');
        if (btnCloseEdit) {
            btnCloseEdit.onclick = function () {
                var modal = page.querySelector('#jeEditModal');
                if (modal) modal.style.display = 'none';
            };
        }

        // Save Edit Submit
        var btnSaveEdit = page.querySelector('#btnSaveEditSubmit');
        if (btnSaveEdit) {
            btnSaveEdit.onclick = function () { saveEditSubmit(page); };
        }

        // Markdown Formatting Toolbars
        var mdToolbars = page.querySelectorAll('.je-md-toolbar');
        mdToolbars.forEach(function (toolbar) {
            var targetId = toolbar.getAttribute('data-target');
            var textarea = page.querySelector('#' + targetId);
            if (!textarea) return;

            toolbar.onclick = function (ev) {
                var btn = ev.target.closest('.je-md-btn');
                if (!btn) return;
                ev.preventDefault();
                var fmt = btn.getAttribute('data-fmt');
                applyMarkdownFormat(textarea, fmt);
            };
        });
    }

    function applyMarkdownFormat(textarea, fmt) {
        if (!textarea) return;
        var start = textarea.selectionStart || 0;
        var end = textarea.selectionEnd || 0;
        var val = textarea.value || '';
        var selected = val.substring(start, end);
        var before = val.substring(0, start);
        var after = val.substring(end);

        var insert = '';
        var cursorOffset = 0;

        switch (fmt) {
            case 'bold':
                insert = '**' + (selected || 'bold text') + '**';
                cursorOffset = selected ? insert.length : 2;
                break;
            case 'italic':
                insert = '*' + (selected || 'italic text') + '*';
                cursorOffset = selected ? insert.length : 1;
                break;
            case 'h3':
                insert = '### ' + (selected || 'Heading');
                cursorOffset = insert.length;
                break;
            case 'quote':
                insert = '> ' + (selected || 'Quote');
                cursorOffset = insert.length;
                break;
            case 'code':
                insert = '`' + (selected || 'code') + '`';
                cursorOffset = selected ? insert.length : 1;
                break;
            case 'codeblock':
                insert = '\n```\n' + (selected || 'code block') + '\n```\n';
                cursorOffset = selected ? insert.length : 5;
                break;
            case 'link':
                insert = '[' + (selected || 'link text') + '](https://example.com)';
                cursorOffset = selected ? insert.length : 1;
                break;
            case 'list':
                insert = '- ' + (selected || 'item');
                cursorOffset = insert.length;
                break;
            default:
                return;
        }

        textarea.value = before + insert + after;
        textarea.focus();
        var newPos = start + cursorOffset;
        textarea.setSelectionRange(newPos, newPos);
    }

    // Fetch Discussions from Cloudflare Worker
    function loadDiscussions(page) {
        var statusEl = page.querySelector('#jeDiscussionsStatus');
        var gridEl = page.querySelector('#jeDiscussionsContainer');
        if (!statusEl || !gridEl) return;

        statusEl.style.display = 'block';
        statusEl.textContent = 'Loading posts from community backend…';
        gridEl.innerHTML = '';

        cwFetch('/api/issues')
            .then(function (data) {
                statusEl.style.display = 'none';
                currentDiscussions = data || [];
                renderDiscussionsGrid(page);
            })
            .catch(function (err) {
                console.error('[JellyEmu] Failed to load discussions:', err);
                statusEl.style.display = 'block';
                statusEl.innerHTML = '<span style="color:#FF4444;">Failed to connect to Cloudflare Worker community backend.</span>';
            });
    }

    function isIssueUpvotedByMe(item) {
        var inst = getInstanceId();
        if (!item.issue_votes) item.issue_votes = [];
        return item.issue_votes.some(function (v) { return v.instance_id === inst; });
    }

    // Render Grid
    function renderDiscussionsGrid(page) {
        var gridEl = page.querySelector('#jeDiscussionsContainer');
        var statusEl = page.querySelector('#jeDiscussionsStatus');
        if (!gridEl) return;

        gridEl.innerHTML = '';

        var items = currentDiscussions;
        if (activeCategory) {
            items = items.filter(function (it) {
                return (it.category || '').toLowerCase() === activeCategory.toLowerCase();
            });
        }

        if (items.length === 0) {
            if (statusEl) {
                statusEl.style.display = 'block';
                statusEl.textContent = activeCategory ? ('No posts found in category "' + activeCategory + '".') : 'No community posts yet. Start the conversation!';
            }
            return;
        }

        if (statusEl) statusEl.style.display = 'none';

        var myInst = getInstanceId();

        items.forEach(function (item) {
            var card = document.createElement('div');
            card.className = 'verticalSection';
            card.style.cssText = 'background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06); border-radius: 10px; padding: 16px; display: flex; flex-direction: column; justify-content: space-between; transition: border-color 0.2s, background 0.2s; cursor: pointer;';

            card.onmouseenter = function () { card.style.borderColor = 'rgba(0,164,220,0.3)'; card.style.background = 'rgba(255,255,255,0.03)'; };
            card.onmouseleave = function () { card.style.borderColor = 'rgba(255,255,255,0.06)'; card.style.background = 'rgba(255,255,255,0.02)'; };

            var hasUpvoted = isIssueUpvotedByMe(item);
            var replyCount = item.replies_count || 0;
            var timeAgo = formatTimeAgo(item.created_at);
            var isOwner = item.instance_id === myInst;

            var catColor = getCategoryColor(item.category);

            var topRow = '<div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px;">' +
                '<div style="display: flex; align-items: center; gap: 6px;">' +
                '<span style="background: ' + catColor.bg + '; color: ' + catColor.fg + '; border: 1px solid ' + catColor.border + '; font-size: 0.72em; padding: 2px 7px; border-radius: 4px; font-weight: 600; text-transform: uppercase;">' + escapeHtml(item.category || 'General') + '</span>' +
                (item.is_poll && (item.category || '').toLowerCase() !== 'polls' ? '<span style="background: rgba(240,192,64,0.15); color: #f0c040; border: 1px solid rgba(240,192,64,0.3); font-size: 0.72em; padding: 2px 7px; border-radius: 4px; font-weight: 600;">POLL</span>' : '') +
                (item.image_url ? '<span class="material-icons" style="font-size: 16px; color: #888;" title="Has screenshot attachment">image</span>' : '') +
                '</div>' +
                '<span style="font-size: 0.75em; color: #777;">' + timeAgo + '</span>' +
                '</div>';

            var titleHtml = '<h4 style="margin: 0 0 8px 0; color: #fff; font-size: 1.05em; font-weight: 600; line-height: 1.3; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;">' + escapeHtml(item.title) + '</h4>';

            var bodySnippet = '<p style="margin: 0 0 14px 0; color: #aaa; font-size: 0.88em; line-height: 1.4; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;">' + escapeHtml(item.description) + '</p>';

            var footerRow = '<div style="display: flex; justify-content: space-between; align-items: center; margin-top: auto; padding-top: 10px; border-top: 1px solid rgba(255,255,255,0.05);">' +
                '<div style="display: flex; align-items: center; gap: 10px;">' +
                '<button type="button" class="je-btn-upvote" data-id="' + item.id + '" style="background: ' + (hasUpvoted ? 'rgba(0,164,220,0.25)' : 'rgba(255,255,255,0.05)') + '; border: 1px solid ' + (hasUpvoted ? '#00a4dc' : 'rgba(255,255,255,0.1)') + '; color: ' + (hasUpvoted ? '#00a4dc' : '#ccc') + '; padding: 3px 10px; border-radius: 6px; font-size: 0.8em; font-weight: 600; cursor: pointer; display: flex; align-items: center; gap: 4px;">' +
                '<span class="material-icons" style="font-size: 14px;">thumb_up</span>' +
                '<span>' + (item.upvotes || 0) + '</span>' +
                '</button>' +
                '<span style="font-size: 0.8em; color: #888; display: inline-flex; align-items: center; gap: 3px;"><span class="material-icons" style="font-size: 14px;">chat_bubble_outline</span> ' + replyCount + '</span>' +
                '</div>' +
                '<div style="font-size: 0.78em; color: #777;">by <strong style="color: #bbb;">' + escapeHtml(item.username) + '</strong></div>' +
                '</div>';

            card.innerHTML = topRow + titleHtml + bodySnippet + footerRow;

            // Click card to view details
            card.onclick = function (ev) {
                if (ev.target.closest('.je-btn-upvote')) return;
                openDiscussionDetail(item, page);
            };

            // Upvote button handler
            var upvoteBtn = card.querySelector('.je-btn-upvote');
            if (upvoteBtn) {
                upvoteBtn.onclick = function (ev) {
                    ev.stopPropagation();
                    toggleIssueUpvote(item, page);
                };
            }

            gridEl.appendChild(card);
        });
    }

    // Toggle Issue Upvote
    function toggleIssueUpvote(item, page) {
        var inst = getInstanceId();
        cwFetch('/api/issues/' + item.id + '/upvote', {
            method: 'POST',
            body: JSON.stringify({ instance_id: inst })
        }).then(function () {
            loadDiscussions(page);
        }).catch(function (err) { console.error('[JellyEmu] Upvote failed:', err); });
    }

    // Submit New Discussion / Poll
    function submitNewDiscussion(page) {
        var catSelect = page.querySelector('#jeNewDiscCategory');
        var titleInput = page.querySelector('#jeNewDiscTitle');
        var bodyInput = page.querySelector('#jeNewDiscBody');
        var imgFileInput = page.querySelector('#jeNewDiscImageFile');
        var btnSubmit = page.querySelector('#btnSubmitNewDiscussion');

        if (!titleInput || !bodyInput) return;
        var title = titleInput.value.trim();
        var body = bodyInput.value.trim();
        var category = catSelect ? catSelect.value : 'General';
        var isPoll = category === 'Polls';

        if (!title || !body) {
            alert('Please enter both a title and message.');
            return;
        }

        if (btnSubmit) { btnSubmit.disabled = true; btnSubmit.textContent = 'Publishing…'; }

        var file = (imgFileInput && imgFileInput.files && imgFileInput.files[0]) ? imgFileInput.files[0] : null;

        uploadAttachment(file).then(function (imageUrl) {
            var pollOpts = [];
            if (isPoll) {
                var pollContainer = page.querySelector('#jePollOptionsContainer');
                var optionInputs = pollContainer ? pollContainer.querySelectorAll('.je-poll-opt-input') : [];
                optionInputs.forEach(function (inp) {
                    var val = inp.value.trim();
                    if (val) pollOpts.push(val);
                });
            }

            var payload = {
                instance_id: getInstanceId(),
                username: getUsername(),
                category: category,
                title: title,
                description: body,
                image_url: imageUrl,
                is_poll: isPoll,
                poll_options: pollOpts
            };

            return cwFetch('/api/issues', {
                method: 'POST',
                body: JSON.stringify(payload)
            });
        }).then(function () {
            if (btnSubmit) { btnSubmit.disabled = false; btnSubmit.textContent = 'Publish Post'; }
            titleInput.value = '';
            bodyInput.value = '';
            if (imgFileInput) imgFileInput.value = '';
            var modal = page.querySelector('#jeNewDiscussionModal');
            if (modal) modal.style.display = 'none';
            loadDiscussions(page);
        }).catch(function (err) {
            if (btnSubmit) { btnSubmit.disabled = false; btnSubmit.textContent = 'Publish Post'; }
            console.error('[JellyEmu] Failed to submit post:', err);
            alert('Failed to publish post: ' + err.message);
        });
    }

    // Open Discussion Detail & Replies
    function openDiscussionDetail(item, page) {
        activeDiscussionItem = item;
        var modal = page.querySelector('#jeDiscussionDetailModal');
        if (!modal) return;

        var catBadge = modal.querySelector('#jeDetailCategoryBadge');
        var titleEl = modal.querySelector('#jeDetailTitle');
        var authorEl = modal.querySelector('#jeDetailAuthor');
        var dateEl = modal.querySelector('#jeDetailDate');
        var bodyEl = modal.querySelector('#jeDetailBody');
        var imgWrap = modal.querySelector('#jeDetailImageView');
        var imgEl = modal.querySelector('#jeDetailImg');
        var imgLink = modal.querySelector('#jeDetailImageLink');
        var ownerActionsEl = modal.querySelector('#jeDetailOwnerActions');
        var pollContainer = modal.querySelector('#jeDetailPollContainer');
        var upvoteBtn = modal.querySelector('#btnDetailUpvote');
        var upvotesCountEl = modal.querySelector('#jeDetailUpvotesCount');

        if (catBadge) catBadge.textContent = item.category || 'General';
        if (titleEl) titleEl.textContent = item.title;
        if (authorEl) authorEl.textContent = item.username;
        if (dateEl) dateEl.textContent = formatTimeAgo(item.created_at);
        if (bodyEl) bodyEl.innerHTML = renderMarkdown(item.description);

        var myInst = getInstanceId();
        var isOwner = item.instance_id === myInst;

        // Owner action buttons (Edit & Delete)
        if (ownerActionsEl) {
            ownerActionsEl.innerHTML = '';
            if (isOwner) {
                var editBtn = document.createElement('button');
                editBtn.type = 'button';
                editBtn.style.cssText = 'background: rgba(255,255,255,0.06); border: 1px solid rgba(255,255,255,0.1); color: #ccc; padding: 4px 10px; border-radius: 6px; font-size: 0.78em; cursor: pointer; display: inline-flex; align-items: center; gap: 3px;';
                editBtn.innerHTML = '<span class="material-icons" style="font-size: 14px;">edit</span> Edit';
                editBtn.onclick = function () { openEditModal('issue', item.id, item.title, item.description, page); };

                var delBtn = document.createElement('button');
                delBtn.type = 'button';
                delBtn.style.cssText = 'background: rgba(255,68,68,0.15); border: 1px solid rgba(255,68,68,0.3); color: #ff4444; padding: 4px 10px; border-radius: 6px; font-size: 0.78em; cursor: pointer; display: inline-flex; align-items: center; gap: 3px;';
                delBtn.innerHTML = '<span class="material-icons" style="font-size: 14px;">delete</span> Delete';
                delBtn.onclick = function () { deletePostOrReply('issue', item.id, item.image_url, page); };

                ownerActionsEl.appendChild(editBtn);
                ownerActionsEl.appendChild(delBtn);
            }
        }

        // Image Attachment
        if (item.image_url && imgWrap && imgEl) {
            imgWrap.style.display = 'block';
            imgEl.src = item.image_url;
            if (imgLink) imgLink.href = item.image_url;
        } else if (imgWrap) {
            imgWrap.style.display = 'none';
        }

        // Upvotes button
        var hasUpvoted = isIssueUpvotedByMe(item);
        if (upvotesCountEl) upvotesCountEl.textContent = item.upvotes || 0;
        if (upvoteBtn) {
            upvoteBtn.style.background = hasUpvoted ? 'rgba(0,164,220,0.25)' : 'rgba(255,255,255,0.06)';
            upvoteBtn.style.borderColor = hasUpvoted ? '#00a4dc' : 'rgba(255,255,255,0.1)';
            upvoteBtn.style.color = hasUpvoted ? '#00a4dc' : '#ccc';
            upvoteBtn.onclick = function () {
                toggleIssueUpvote(item, page);
            };
        }

        // Poll View
        if (item.is_poll && pollContainer) {
            pollContainer.style.display = 'block';
            renderPollView(item, pollContainer, page);
        } else if (pollContainer) {
            pollContainer.style.display = 'none';
        }

        modal.style.display = 'flex';
        loadReplies(item.id, page);
    }

    // Render Interactive Poll View
    function renderPollView(item, pollContainer, page) {
        pollContainer.innerHTML = '<div style="color: #aaa; font-size: 0.85em;">Loading poll options…</div>';

        cwFetch('/api/issues/' + item.id + '/poll_vote')
            .then(function (res) {
                var options = (res && res.poll_options) ? res.poll_options : [];
                var votes = (res && res.poll_votes) ? res.poll_votes : [];
                var myInst = getInstanceId();

                var myVote = votes.find(function (v) { return v.instance_id === myInst; });
                var totalVotes = votes.length;

                var html = '<div style="font-weight: 600; color: #fff; font-size: 0.95em; margin-bottom: 10px;">Poll Question &amp; Options (' + totalVotes + ' total votes)</div>';
                html += '<div style="display: flex; flex-direction: column; gap: 8px;">';

                options.forEach(function (opt) {
                    var optVotes = votes.filter(function (v) { return v.option_id === opt.id; }).length;
                    var pct = totalVotes > 0 ? Math.round((optVotes / totalVotes) * 100) : 0;
                    var isSelected = myVote && myVote.option_id === opt.id;

                    html += '<div class="je-poll-option-row" data-option-id="' + opt.id + '" style="background: ' + (isSelected ? 'rgba(0,164,220,0.15)' : 'rgba(255,255,255,0.03)') + '; border: 1px solid ' + (isSelected ? '#00a4dc' : 'rgba(255,255,255,0.08)') + '; border-radius: 6px; padding: 10px; cursor: pointer; position: relative; overflow: hidden; transition: border-color 0.2s;">' +
                        '<div style="position: absolute; top:0; left:0; bottom:0; width: ' + pct + '%; background: rgba(0,164,220,0.18); z-index: 1;"></div>' +
                        '<div style="position: relative; z-index: 2; display: flex; justify-content: space-between; align-items: center; font-size: 0.88em;">' +
                        '<span style="font-weight: 600; color: ' + (isSelected ? '#00a4dc' : '#fff') + ';">' + (isSelected ? '✓ ' : '') + escapeHtml(opt.option_text) + '</span>' +
                        '<span style="color: #aaa; font-size: 0.82em;">' + optVotes + ' votes (' + pct + '%)</span>' +
                        '</div>' +
                        '</div>';
                });

                html += '</div>';
                pollContainer.innerHTML = html;

                // Handle voting click
                var rows = pollContainer.querySelectorAll('.je-poll-option-row');
                rows.forEach(function (row) {
                    row.onclick = function () {
                        var optionId = this.getAttribute('data-option-id');
                        castPollVote(item.id, optionId, myVote, page);
                    };
                });
            }).catch(function (err) {
                console.error('[JellyEmu] Failed to load poll options:', err);
                pollContainer.innerHTML = '<div style="color:#FF4444; font-size: 0.85em;">Failed to load poll options.</div>';
            });
    }

    function castPollVote(issueId, optionId, existingVote, page) {
        var myInst = getInstanceId();
        if (existingVote && existingVote.option_id === optionId) return;

        cwFetch('/api/issues/' + issueId + '/poll_vote', {
            method: 'POST',
            body: JSON.stringify({ option_id: optionId, instance_id: myInst })
        }).then(function () {
            if (activeDiscussionItem) openDiscussionDetail(activeDiscussionItem, page);
        });
    }

    // Load Replies
    function loadReplies(issueId, page) {
        var container = page.querySelector('#jeRepliesContainer');
        var badge = page.querySelector('#jeRepliesCountBadge');
        if (!container) return;

        container.innerHTML = '<div style="color: #888; font-size: 0.85em;">Loading replies…</div>';

        cwFetch('/api/issues/' + issueId + '/replies')
            .then(function (replies) {
                replies = replies || [];
                if (badge) badge.textContent = replies.length;

                if (replies.length === 0) {
                    container.innerHTML = '<div style="color: #777; font-size: 0.85em; font-style: italic;">No replies yet. Be the first to answer!</div>';
                    return;
                }

                container.innerHTML = '';
                var myInst = getInstanceId();

                replies.forEach(function (rep) {
                    var repEl = document.createElement('div');
                    repEl.style.cssText = 'background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.05); border-radius: 8px; padding: 14px; display: flex; flex-direction: column; gap: 8px;';

                    var isOwner = rep.instance_id === myInst;
                    var votes = rep.reply_votes || [];
                    var hasUpvoted = votes.some(function (v) { return v.instance_id === myInst; });

                    var header = '<div style="display: flex; justify-content: space-between; align-items: center;">' +
                        '<div style="display: flex; align-items: center; gap: 8px;">' +
                        '<span class="material-icons" style="color: #aaa; font-size: 18px;">account_circle</span>' +
                        '<span style="font-weight: 600; color: #fff; font-size: 0.88em;">' + escapeHtml(rep.username) + '</span>' +
                        '<span style="font-size: 0.75em; color: #777;">' + formatTimeAgo(rep.created_at) + '</span>' +
                        '</div>' +
                        '<div class="je-reply-owner-actions" style="display: flex; gap: 6px;"></div>' +
                        '</div>';

                    var msg = '<div style="color: #ddd; font-size: 0.9em; line-height: 1.4; word-break: break-word;">' + renderMarkdown(rep.message) + '</div>';

                    var imgHtml = rep.image_url ? '<div style="margin-top: 6px;"><a href="' + rep.image_url + '" target="_blank"><img src="' + rep.image_url + '" style="max-width: 100%; max-height: 240px; border-radius: 6px; border: 1px solid rgba(255,255,255,0.1);" /></a></div>' : '';

                    var footer = '<div style="display: flex; align-items: center; gap: 10px; margin-top: 4px;">' +
                        '<button type="button" class="je-btn-reply-upvote" style="background: ' + (hasUpvoted ? 'rgba(0,164,220,0.25)' : 'rgba(255,255,255,0.04)') + '; border: 1px solid ' + (hasUpvoted ? '#00a4dc' : 'rgba(255,255,255,0.08)') + '; color: ' + (hasUpvoted ? '#00a4dc' : '#aaa') + '; padding: 2px 8px; border-radius: 4px; font-size: 0.78em; font-weight: 600; cursor: pointer; display: flex; align-items: center; gap: 4px;">' +
                        '<span class="material-icons" style="font-size: 12px;">thumb_up</span>' +
                        '<span>' + (rep.upvotes || 0) + '</span>' +
                        '</button>' +
                        '</div>';

                    repEl.innerHTML = header + msg + imgHtml + footer;

                    // Owner action buttons on reply
                    var ownerActions = repEl.querySelector('.je-reply-owner-actions');
                    if (ownerActions && isOwner) {
                        var editBtn = document.createElement('button');
                        editBtn.type = 'button';
                        editBtn.style.cssText = 'background: none; border: none; color: #aaa; cursor: pointer; font-size: 0.78em; display: inline-flex; align-items: center; gap: 2px;';
                        editBtn.innerHTML = '<span class="material-icons" style="font-size: 14px;">edit</span> Edit';
                        editBtn.onclick = function () { openEditModal('reply', rep.id, '', rep.message, page); };

                        var delBtn = document.createElement('button');
                        delBtn.type = 'button';
                        delBtn.style.cssText = 'background: none; border: none; color: #ff4444; cursor: pointer; font-size: 0.78em; display: inline-flex; align-items: center; gap: 2px;';
                        delBtn.innerHTML = '<span class="material-icons" style="font-size: 14px;">delete</span> Delete';
                        delBtn.onclick = function () { deletePostOrReply('reply', rep.id, rep.image_url, page); };

                        ownerActions.appendChild(editBtn);
                        ownerActions.appendChild(delBtn);
                    }

                    // Upvote Reply Button
                    var upvoteBtn = repEl.querySelector('.je-btn-reply-upvote');
                    if (upvoteBtn) {
                        upvoteBtn.onclick = function () {
                            toggleReplyUpvote(rep, issueId, page);
                        };
                    }

                    container.appendChild(repEl);
                });
            })
            .catch(function (err) {
                console.error('[JellyEmu] Failed to load replies:', err);
                container.innerHTML = '<div style="color: #FF4444; font-size: 0.85em;">Failed to load replies.</div>';
            });
    }

    // Toggle Reply Upvote
    function toggleReplyUpvote(rep, issueId, page) {
        var inst = getInstanceId();
        cwFetch('/api/replies/' + rep.id + '/upvote', {
            method: 'POST',
            body: JSON.stringify({ instance_id: inst })
        }).then(function () {
            loadReplies(issueId, page);
        });
    }

    // Submit Reply
    function submitReply(page) {
        if (!activeDiscussionItem) return;
        var input = page.querySelector('#jeReplyInput');
        var imgFileInput = page.querySelector('#jeReplyImageFile');
        var btnSubmit = page.querySelector('#btnSubmitReply');

        if (!input) return;
        var text = input.value.trim();
        if (!text) {
            alert('Please write a reply message.');
            return;
        }

        if (btnSubmit) { btnSubmit.disabled = true; btnSubmit.textContent = 'Posting…'; }

        var file = (imgFileInput && imgFileInput.files && imgFileInput.files[0]) ? imgFileInput.files[0] : null;

        uploadAttachment(file).then(function (imageUrl) {
            var payload = {
                instance_id: getInstanceId(),
                username: getUsername(),
                message: text,
                image_url: imageUrl
            };

            return cwFetch('/api/issues/' + activeDiscussionItem.id + '/replies', {
                method: 'POST',
                body: JSON.stringify(payload)
            });
        }).then(function () {
            if (btnSubmit) { btnSubmit.disabled = false; btnSubmit.textContent = 'Post Reply'; }
            input.value = '';
            if (imgFileInput) imgFileInput.value = '';
            loadReplies(activeDiscussionItem.id, page);
            loadDiscussions(page);
        }).catch(function (err) {
            if (btnSubmit) { btnSubmit.disabled = false; btnSubmit.textContent = 'Post Reply'; }
            console.error('[JellyEmu] Reply submission failed:', err);
            alert('Failed to post reply: ' + err.message);
        });
    }

    // Open Edit Modal
    function openEditModal(type, id, currentTitle, currentBody, page) {
        activeEditTarget = { type: type, id: id };
        var modal = page.querySelector('#jeEditModal');
        var modalTitle = page.querySelector('#jeEditModalTitle');
        var titleWrap = page.querySelector('#jeEditTitleWrap');
        var titleInput = page.querySelector('#jeEditTitleInput');
        var bodyInput = page.querySelector('#jeEditBodyInput');

        if (!modal) return;

        if (modalTitle) modalTitle.textContent = (type === 'issue') ? 'Edit Post' : 'Edit Reply';
        if (titleWrap) titleWrap.style.display = (type === 'issue') ? 'block' : 'none';
        if (titleInput) titleInput.value = currentTitle || '';
        if (bodyInput) bodyInput.value = currentBody || '';

        modal.style.display = 'flex';
    }

    function saveEditSubmit(page) {
        if (!activeEditTarget) return;
        var titleInput = page.querySelector('#jeEditTitleInput');
        var bodyInput = page.querySelector('#jeEditBodyInput');
        var btnSave = page.querySelector('#btnSaveEditSubmit');

        var body = bodyInput ? bodyInput.value.trim() : '';
        if (!body) {
            alert('Content cannot be empty.');
            return;
        }

        if (btnSave) { btnSave.disabled = true; btnSave.textContent = 'Saving…'; }

        var endpoint = (activeEditTarget.type === 'issue') ? ('/api/issues/' + activeEditTarget.id) : ('/api/replies/' + activeEditTarget.id);
        var payload = (activeEditTarget.type === 'issue') ? { title: titleInput.value.trim(), description: body, instance_id: getInstanceId() } : { message: body, instance_id: getInstanceId() };

        cwFetch(endpoint, {
            method: 'PATCH',
            body: JSON.stringify(payload)
        }).then(function () {
            if (btnSave) { btnSave.disabled = false; btnSave.textContent = 'Save Changes'; }
            var modal = page.querySelector('#jeEditModal');
            if (modal) modal.style.display = 'none';

            if (activeEditTarget.type === 'issue' && activeDiscussionItem) {
                activeDiscussionItem.title = payload.title;
                activeDiscussionItem.description = payload.description;
                openDiscussionDetail(activeDiscussionItem, page);
            } else if (activeDiscussionItem) {
                loadReplies(activeDiscussionItem.id, page);
            }
            loadDiscussions(page);
        }).catch(function (err) {
            if (btnSave) { btnSave.disabled = false; btnSave.textContent = 'Save Changes'; }
            console.error('[JellyEmu] Edit failed:', err);
            alert('Failed to save changes: ' + err.message);
        });
    }

    // Delete Post or Reply
    function deletePostOrReply(type, id, imageUrl, page) {
        var label = (type === 'issue') ? 'post and all its replies' : 'reply';
        if (!confirm('Are you sure you want to delete this ' + label + '? This cannot be undone.')) return;

        var myInst = getInstanceId();
        var endpoint = (type === 'issue') ? ('/api/issues/' + id + '?instance_id=' + myInst) : ('/api/replies/' + id + '?instance_id=' + myInst);

        cwFetch(endpoint, { method: 'DELETE' })
            .then(function () {
                if (type === 'issue') {
                    var detailModal = page.querySelector('#jeDiscussionDetailModal');
                    if (detailModal) detailModal.style.display = 'none';
                    loadDiscussions(page);
                } else if (activeDiscussionItem) {
                    loadReplies(activeDiscussionItem.id, page);
                    loadDiscussions(page);
                }
            })
            .catch(function (err) {
                console.error('[JellyEmu] Deletion failed:', err);
                alert('Failed to delete: ' + err.message);
            });
    }

    // Utilities
    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    function formatTimeAgo(isoString) {
        if (!isoString) return '';
        var date = new Date(isoString);
        var now = new Date();
        var diffSec = Math.floor((now - date) / 1000);

        if (diffSec < 60) return 'just now';
        var diffMin = Math.floor(diffSec / 60);
        if (diffMin < 60) return diffMin + 'm ago';
        var diffHr = Math.floor(diffMin / 60);
        if (diffHr < 24) return diffHr + 'h ago';
        var diffDay = Math.floor(diffHr / 24);
        if (diffDay < 30) return diffDay + 'd ago';
        return date.toLocaleDateString();
    }

    function getCategoryColor(cat) {
        cat = (cat || '').toLowerCase();
        if (cat === 'bugs') return { bg: 'rgba(255,68,68,0.15)', fg: '#ff4444', border: 'rgba(255,68,68,0.3)' };
        if (cat === 'ideas') return { bg: 'rgba(82,181,75,0.15)', fg: '#52B54B', border: 'rgba(82,181,75,0.3)' };
        if (cat === 'polls') return { bg: 'rgba(240,192,64,0.15)', fg: '#f0c040', border: 'rgba(240,192,64,0.3)' };
        if (cat === 'announcements') return { bg: 'rgba(140,82,255,0.15)', fg: '#8c52ff', border: 'rgba(140,82,255,0.3)' };
        if (cat === 'q&a') return { bg: 'rgba(255,126,0,0.15)', fg: '#ff7e00', border: 'rgba(255,126,0,0.3)' };
        if (cat === 'show and tell') return { bg: 'rgba(0,210,255,0.15)', fg: '#00d2ff', border: 'rgba(0,210,255,0.3)' };
        return { bg: 'rgba(0,164,220,0.15)', fg: '#00a4dc', border: 'rgba(0,164,220,0.3)' };
    }

    function renderMarkdown(text) {
        if (!text) return '';
        var html = escapeHtml(text);

        // Code blocks (```code```)
        html = html.replace(/```([\s\S]*?)```/g, function (match, p1) {
            return '<pre style="background: rgba(0,0,0,0.4); border: 1px solid rgba(255,255,255,0.1); border-radius: 6px; padding: 10px; font-family: monospace; font-size: 0.88em; overflow-x: auto; margin: 8px 0; color: #7df;"><code>' + p1.trim() + '</code></pre>';
        });

        // Inline code (`code`)
        html = html.replace(/`([^`]+)`/g, '<code style="background: rgba(255,255,255,0.1); padding: 2px 6px; border-radius: 4px; font-family: monospace; font-size: 0.9em; color: #7df;">$1</code>');

        // Headings (# H1, ## H2, ### H3)
        html = html.replace(/^### (.*$)/gim, '<h5 style="margin: 10px 0 6px 0; color: #fff; font-size: 1.05em; font-weight: 600;">$1</h5>');
        html = html.replace(/^## (.*$)/gim, '<h4 style="margin: 12px 0 6px 0; color: #fff; font-size: 1.15em; font-weight: 600;">$1</h4>');
        html = html.replace(/^# (.*$)/gim, '<h3 style="margin: 14px 0 8px 0; color: #fff; font-size: 1.25em; font-weight: 700;">$1</h3>');

        // Bold & Italic (**bold**, *italic*, __bold__, _italic_)
        html = html.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
        html = html.replace(/__([^_]+)__/g, '<strong>$1</strong>');
        html = html.replace(/\*([^*]+)\*/g, '<em>$1</em>');
        html = html.replace(/_([^_]+)_/g, '<em>$1</em>');

        // Blockquotes (> quote)
        html = html.replace(/^\&gt;\s?(.*$)/gim, '<blockquote style="border-left: 3px solid #00a4dc; margin: 8px 0; padding-left: 10px; color: #aaa; font-style: italic;">$1</blockquote>');

        // Clickable Links ([text](url))
        html = html.replace(/\[([^\]]+)\]\((https?:\/\/[^\s\)]+)\)/g, '<a href="$2" target="_blank" style="color: #00a4dc; text-decoration: underline;">$1</a>');

        // Unordered Lists (- item or * item)
        html = html.replace(/^[\s]*[-\*]\s+(.*$)/gim, '<li style="margin-left: 20px; list-style-type: disc;">$1</li>');

        // Line breaks
        html = html.replace(/\n/g, '<br>');

        return html;
    }
})();
