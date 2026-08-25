(function() {
    window.JellyEmu = window.JellyEmu || {};
    const JE = window.JellyEmu;

    JE.hijackJellyEmuSettings = function() {
        const activePage = document.querySelector('.page:not(.hide):not(#myPreferencesMenuPage)');
        if (!activePage) return;

        if (activePage.hasAttribute('data-jellyemu-settings-hijacked')) {
            const headerTitle = document.querySelector('.skinHeader .pageTitle');
            if (headerTitle && headerTitle.textContent !== 'JellyEmu Settings') {
                headerTitle.textContent = 'JellyEmu Settings';
            }
            return;
        }

        activePage.setAttribute('data-jellyemu-settings-hijacked', '1');
        activePage.className = 'page libraryPage noSecondaryNavPage mainAnimatedPage';
        activePage.setAttribute('data-title', 'JellyEmu Settings');
        activePage.setAttribute('data-backbutton', 'true');

        document.title = 'JellyEmu Settings';
        const headerTitle = document.querySelector('.skinHeader .pageTitle');
        if (headerTitle) headerTitle.textContent = 'JellyEmu Settings';

        const userId = window.ApiClient ? window.ApiClient.getCurrentUserId() : null;
        const token  = window.ApiClient ? window.ApiClient.accessToken() : '';

        activePage.innerHTML = `
            <div class="je-settings-container">
                <div class="je-settings-section">
                    <div class="je-settings-title">
                        <span class="material-icons" style="color:#f0c040">emoji_events</span>
                        RetroAchievements
                    </div>
                    <div class="je-settings-field">
                        <label class="je-settings-label">Username</label>
                        <input type="text" id="je-ra-user" class="je-settings-input" placeholder="Enter RA Username">
                    </div>
                    <div class="je-settings-field">
                        <label class="je-settings-label">Web API Key</label>
                        <input type="password" id="je-ra-key" class="je-settings-input" placeholder="Enter RA API Key">
                        <div class="je-settings-footer">Get your key from <a href="https://retroachievements.org/settings" target="_blank" style="color:#00a4dc">retroachievements.org/settings</a></div>
                    </div>
                    <button id="je-settings-save" class="je-settings-btn-save">Save Credentials</button>
                </div>
            </div>`;

        if (!userId) {
            activePage.querySelector('.je-settings-container').innerHTML = '<div style="text-align:center;padding:40px;color:#aaa;">Please sign in to manage settings.</div>';
            return;
        }

        const userInp = activePage.querySelector('#je-ra-user');
        const keyInp  = activePage.querySelector('#je-ra-key');
        const saveBtn = activePage.querySelector('#je-settings-save');

        fetch('/jellyemu/retroachievements/' + userId, {
            headers: { 'Authorization': 'MediaBrowser Token="' + token + '"' }
        })
        .then(r => r.ok ? r.json() : null)
        .then(data => {
            if (data) {
                userInp.value = data.raUsername || '';
                keyInp.value  = data.raApiKey   || '';
            }
        });

        saveBtn.addEventListener('click', function() {
            saveBtn.disabled = true;
            saveBtn.textContent = 'Saving...';

            fetch('/jellyemu/retroachievements/' + userId, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'MediaBrowser Token="' + token + '"'
                },
                body: JSON.stringify({
                    raUsername: userInp.value,
                    raApiKey: keyInp.value
                })
            })
            .then(r => r.ok ? r.json() : Promise.reject())
            .then(() => {
                saveBtn.textContent = 'Saved!';
                setTimeout(() => {
                    saveBtn.disabled = false;
                    saveBtn.textContent = 'Save Credentials';
                }, 2000);
            })
            .catch(() => {
                alert('Failed to save credentials.');
                saveBtn.disabled = false;
                saveBtn.textContent = 'Save Credentials';
            });
        });
    };
})();
