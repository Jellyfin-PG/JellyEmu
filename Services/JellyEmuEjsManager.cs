using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace JellyEmu.Services
{
    /// <summary>
    /// Manages a local copy of the EmulatorJS data bundle.
    ///
    /// On first startup the service calls the GitHub releases API to find the
    /// latest release, downloads the .7z asset, extracts the inner "data/"
    /// folder directly to {DataPath}/jellyemu-ejs/, and writes a .version stamp.
    ///
    /// The archive structure is:
    ///   data/
    ///     loader.js
    ///     emulator.min.js
    ///     emulator.min.css
    ///     emulator.css
    ///     version.json
    ///     compression/
    ///     cores/
    ///     localization/
    ///     src/
    ///
    /// All entries are extracted with the leading "data/" segment stripped so
    /// loader.js lands directly in EjsRoot.
    ///
    /// While assets are absent (first boot, download in-progress, or failed)
    /// <see cref="IsReady"/> is false and the controller proxies the CDN.
    /// </summary>
    public class JellyEmuEjsManager
    {
        public static string CurrentChannel => (Plugin.Instance?.Configuration.EjsChannel ?? "stable").ToLowerInvariant();

        /// <summary>CDN base used as fallback when local assets are not ready.</summary>
        public static string CdnBase => $"https://cdn.emulatorjs.org/{CurrentChannel}/data/";

        private readonly IApplicationPaths _appPaths;
        private readonly ILogger<JellyEmuEjsManager> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        private string EjsRoot => Path.Combine(_appPaths.DataPath, "jellyemu-ejs");
        private string StampFile => Path.Combine(EjsRoot, ".version");

        private volatile bool _isReady;
        public bool IsReady => _isReady;
        public string LocalRoot => EjsRoot;

        public JellyEmuEjsManager(
            IApplicationPaths appPaths,
            ILogger<JellyEmuEjsManager> logger,
            IHttpClientFactory httpClientFactory)
        {
            _appPaths = appPaths;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Called by <see cref="JellyEmuInjectorService"/> at startup.
        /// If assets are current, marks ready synchronously.
        /// Otherwise fires a background download so startup is never blocked.
        /// </summary>
        public void EnsureAssetsAsync()
        {
            if (LocalAssetsValid())
            {
                _isReady = true;
                PatchEjsAssets(EjsRoot);
                _logger.LogInformation("[JellyEmu] EmulatorJS assets present at {Root} (channel: {Channel})", EjsRoot, CurrentChannel);
                return;
            }

            _logger.LogInformation(
                "[JellyEmu] EmulatorJS assets missing or outdated — downloading channel {Channel} in background...", CurrentChannel);

            _ = Task.Run(DownloadFromCdnAsync);
        }

        public async Task RedownloadAsync()
        {
            _isReady = false;
            _logger.LogInformation("[JellyEmu] Force re-downloading EmulatorJS assets for channel {Channel}...", CurrentChannel);
            await DownloadFromCdnAsync();
        }

        private bool LocalAssetsValid()
        {
            if (!Directory.Exists(EjsRoot)) return false;
            if (!File.Exists(StampFile)) return false;
            var stamp = File.ReadAllText(StampFile).Trim();
            if (stamp != $"cdn-{CurrentChannel}") return false;
            if (!File.Exists(Path.Combine(EjsRoot, "loader.js"))) return false;
            return true;
        }

        private async Task DownloadFromCdnAsync()
        {
            try
            {
                var client = _httpClientFactory.CreateClient("JellyEmuEjs");
                var downloadQueue = new List<(string Url, string LocalPath)>();

                _logger.LogInformation("[JellyEmu] Scraping EmulatorJS CDN directory listing for channel {Channel}...", CurrentChannel);

                if (Directory.Exists(EjsRoot))
                    Directory.Delete(EjsRoot, recursive: true);
                Directory.CreateDirectory(EjsRoot);

                await ScrapeDirectoryRecursiveAsync(client, CdnBase, EjsRoot, downloadQueue);

                _logger.LogInformation("[JellyEmu] Found {Count} files on CDN. Starting concurrent downloads...", downloadQueue.Count);

                using var throttler = new SemaphoreSlim(15, 15);

                var tasks = downloadQueue.Select(async item =>
                {
                    await throttler.WaitAsync();
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(item.LocalPath)!);

                        using var response = await client.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        using var fs = File.Create(item.LocalPath);
                        await response.Content.CopyToAsync(fs);
                        await fs.FlushAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("[JellyEmu] Failed to download {Url}: {Message}", item.Url, ex.Message);
                    }
                    finally
                    {
                        throttler.Release();
                    }
                });

                await Task.WhenAll(tasks);

                PatchEjsAssets(EjsRoot);

                await File.WriteAllTextAsync(StampFile, $"cdn-{CurrentChannel}");

                _isReady = true;
                _logger.LogInformation("[JellyEmu] CDN download complete! Assets are ready at {Root} (stamp: cdn-{Channel})", EjsRoot, CurrentChannel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Fatal error downloading from CDN — falling back to live proxy.");
            }
        }

        public void PatchEjsAssets(string root)
        {
            try
            {
                var minJs = Path.Combine(root, "emulator.min.js");
                if (File.Exists(minJs))
                {
                    var content = File.ReadAllText(minJs);
                    const string target1 = "this.gamepadSelection[e]=this.gamepad.gamepads[t.gamepadIndex].id+\"_\"+this.gamepad.gamepads[t.gamepadIndex].index";
                    const string repl1 = "const _gp=this.gamepad.gamepads.find(g=>g&&g.index==t.gamepadIndex)||this.gamepad.gamepads[t.gamepadIndex]||this.gamepad.gamepads[0]||{id:\"Gamepad\",index:t.gamepadIndex};this.gamepadSelection[e]=_gp.id+\"_\"+_gp.index";

                    const string target2 = "const e=this.gamepadSelection.indexOf(this.gamepad.gamepads[t.gamepadIndex].id+\"_\"+this.gamepad.gamepads[t.gamepadIndex].index)";
                    const string repl2 = "const _evGp=this.gamepad.gamepads.find(g=>g&&g.index==t.gamepadIndex)||this.gamepad.gamepads[t.gamepadIndex]||this.gamepad.gamepads[0]||{id:\"Gamepad\",index:t.gamepadIndex};const e=this.gamepadSelection.indexOf(_evGp.id+\"_\"+_evGp.index)";

                    bool modified = false;
                    if (content.Contains(target1))
                    {
                        content = content.Replace(target1, repl1);
                        modified = true;
                    }
                    if (content.Contains(target2))
                    {
                        content = content.Replace(target2, repl2);
                        modified = true;
                    }
                    if (modified)
                    {
                        File.WriteAllText(minJs, content);
                        _logger.LogInformation("[JellyEmu] Successfully patched gamepad index bug in emulator.min.js");
                    }
                }

                var srcJs = Path.Combine(root, "src", "emulator.js");
                if (File.Exists(srcJs))
                {
                    var content = File.ReadAllText(srcJs);
                    const string srcTarget1 = "this.gamepadSelection[i] = this.gamepad.gamepads[e.gamepadIndex].id + \"_\" + this.gamepad.gamepads[e.gamepadIndex].index;";
                    const string srcRepl1 = "const _gp = this.gamepad.gamepads.find(g => g && g.index == e.gamepadIndex) || this.gamepad.gamepads[e.gamepadIndex] || this.gamepad.gamepads[0] || { id: \"Gamepad\", index: e.gamepadIndex };\n                    this.gamepadSelection[i] = _gp.id + \"_\" + _gp.index;";

                    const string srcTarget2 = "const gamepadIndex = this.gamepadSelection.indexOf(this.gamepad.gamepads[e.gamepadIndex].id + \"_\" + this.gamepad.gamepads[e.gamepadIndex].index);";
                    const string srcRepl2 = "const _evGp = this.gamepad.gamepads.find(g => g && g.index == e.gamepadIndex) || this.gamepad.gamepads[e.gamepadIndex] || this.gamepad.gamepads[0] || { id: \"Gamepad\", index: e.gamepadIndex };\n        const gamepadIndex = this.gamepadSelection.indexOf(_evGp.id + \"_\" + _evGp.index);";

                    bool modified = false;
                    if (content.Contains(srcTarget1))
                    {
                        content = content.Replace(srcTarget1, srcRepl1);
                        modified = true;
                    }
                    if (content.Contains(srcTarget2))
                    {
                        content = content.Replace(srcTarget2, srcRepl2);
                        modified = true;
                    }
                    if (modified)
                    {
                        File.WriteAllText(srcJs, content);
                        _logger.LogInformation("[JellyEmu] Successfully patched gamepad index bug in src/emulator.js");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Failed to patch gamepad bug in EJS assets");
            }
        }

        /// <summary>
        /// Reads the HTML of an open directory listing, extracts the links, and recursively traverses folders.
        /// </summary>
        private async Task ScrapeDirectoryRecursiveAsync(HttpClient client, string requestUrl, string localDir, List<(string Url, string LocalPath)> queue)
        {
            var html = await client.GetStringAsync(requestUrl);

            var matches = Regex.Matches(html, @"href=""([^""]+)""");

            foreach (Match match in matches)
            {
                var href = match.Groups[1].Value;

                if (href.StartsWith("../") || href.StartsWith("?") || href.StartsWith("/"))
                    continue;

                var decodedName = Uri.UnescapeDataString(href);
                var fullUrl = requestUrl.TrimEnd('/') + "/" + href;
                var targetLocalPath = Path.Combine(localDir, decodedName);

                if (href.EndsWith("/"))
                {
                    Directory.CreateDirectory(targetLocalPath);
                    await ScrapeDirectoryRecursiveAsync(client, fullUrl, targetLocalPath, queue);
                }
                else
                {
                    string finalUrl = requestUrl.TrimEnd('/') + "/" + href;
                    string finalLocalPath = Path.Combine(localDir, decodedName);

                    queue.Add((Url: finalUrl, LocalPath: finalLocalPath));
                }
            }
        }
    }
}