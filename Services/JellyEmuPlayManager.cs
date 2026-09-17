using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Services
{
    /// <summary>
    /// Manages a local copy of the Play! WebAssembly PS2 core runtime (Play.js, Play.wasm)
    /// from the upstream alvaro-zamorano/ps2web repository build artifacts.
    ///
    /// On startup, checks if the local runtime is present for instant offline play.
    /// In the background, checks the upstream GitHub Actions artifacts API for newer builds,
    /// comparing timestamps/artifact IDs against the local .version stamp and updating seamlessly.
    /// If network is offline, existing local assets are preserved.
    /// </summary>
    public class JellyEmuPlayManager
    {
        public const string DefaultRepoOwner = "alvaro-zamorano";
        public const string DefaultRepoName = "ps2web";
        public const string DefaultBranch = "main";
        public const string DefaultWorkflow = "build";

        public const string NightlyLinkSiteZip = "https://nightly.link/alvaro-zamorano/ps2web/workflows/build/main/ps2web-site.zip";
        public const string NightlyLinkPlayWasmZip = "https://nightly.link/alvaro-zamorano/ps2web/workflows/build/main/play-wasm.zip";

        public const string PlayJsFilename = "Play.js";
        public const string PlayWasmFilename = "Play.wasm";

        private readonly IApplicationPaths _appPaths;
        private readonly ILogger<JellyEmuPlayManager> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        private string PlayRoot => Path.Combine(_appPaths.DataPath, "jellyemu-play");
        private string PlayJsPath => Path.Combine(PlayRoot, PlayJsFilename);
        private string PlayWasmPath => Path.Combine(PlayRoot, PlayWasmFilename);
        private string StampFile => Path.Combine(PlayRoot, ".version");

        private volatile bool _isReady;
        public bool IsReady => _isReady;
        public string LocalRoot => PlayRoot;

        public JellyEmuPlayManager(
            IApplicationPaths appPaths,
            ILogger<JellyEmuPlayManager> logger,
            IHttpClientFactory httpClientFactory)
        {
            _appPaths = appPaths;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Called at startup by JellyEmuInjectorService.
        /// Immediately enables offline readiness if files exist, then checks for newer builds in background.
        /// </summary>
        public void EnsureRuntimeAsync()
        {
            if (LocalRuntimeValid())
            {
                _isReady = true;
                _logger.LogInformation("[JellyEmu] Play! PS2 runtime present at {Path}", PlayRoot);
                PatchPlayJsFile(PlayJsPath);
            }
            else
            {
                _logger.LogInformation("[JellyEmu] Play! PS2 runtime missing — syncing in background...");
            }

            _ = Task.Run(SyncRuntimeAsync);
        }

        public bool LocalRuntimeValid()
        {
            if (!Directory.Exists(PlayRoot)) return false;
            if (!File.Exists(PlayJsPath) || !File.Exists(PlayWasmPath)) return false;

            var jsInfo = new FileInfo(PlayJsPath);
            var wasmInfo = new FileInfo(PlayWasmPath);

            // Play.js is ~200KB, Play.wasm is ~2.2MB
            return jsInfo.Length > 10_000 && wasmInfo.Length > 500_000;
        }

        public string? GetRuntimeFilePath(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return null;

            var safeFilename = Path.GetFileName(filename);
            var filePath = Path.Combine(PlayRoot, safeFilename);

            if (File.Exists(filePath))
            {
                if (string.Equals(safeFilename, PlayJsFilename, StringComparison.OrdinalIgnoreCase))
                {
                    PatchPlayJsFile(filePath);
                }
                return filePath;
            }

            return null;
        }

        private void PatchPlayJsFile(string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                var content = File.ReadAllText(filePath);
                bool modified = false;

                var diagPrefix = "if(typeof self!=='undefined'&&typeof window==='undefined'){self.addEventListener('error',e=>console.error('[Play Worker Error Details]',e.message,e.filename,e.lineno,e.error));self.addEventListener('unhandledrejection',e=>console.error('[Play Worker Rejection Details]',e.reason));}\n";
                if (!content.StartsWith("if(typeof self!=='undefined'"))
                {
                    content = diagPrefix + content;
                    modified = true;
                }

                if (!content.Contains("var startWorker;"))
                {
                    content = content.Replace("var wasmModuleReceived;", "var wasmModuleReceived;var startWorker;");
                    modified = true;
                }

                if (!content.Contains("self.startWorker||startWorker"))
                {
                    content = content.Replace("if(ENVIRONMENT_IS_PTHREAD)return startWorker(Module);", "if(ENVIRONMENT_IS_PTHREAD)return (self.startWorker||startWorker)(Module);");
                    modified = true;
                }

                if (!content.Contains("startWorker=self.startWorker="))
                {
                    content = content.Replace("self.startWorker=instance=>{", "startWorker=self.startWorker=instance=>{");
                    modified = true;
                }

                if (modified)
                {
                    var tmpPath = filePath + ".tmp." + Guid.NewGuid().ToString("N");
                    File.WriteAllText(tmpPath, content);
                    File.Move(tmpPath, filePath, true);
                    _logger.LogInformation("[JellyEmu] Successfully applied pthread initialization patch to {File}", filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Failed to patch Play.js file at {Path}", filePath);
            }
        }

        private async Task SyncRuntimeAsync()
        {
            try
            {
                Directory.CreateDirectory(PlayRoot);

                var client = _httpClientFactory.CreateClient("JellyEmuPlay");
                if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    client.DefaultRequestHeaders.Add("User-Agent", JellyEmuVersion.BrowserUserAgent);
                }

                string? currentStamp = File.Exists(StampFile) ? (await File.ReadAllTextAsync(StampFile)).Trim() : null;

                // Check GitHub Actions Artifacts API for latest build
                var apiUrl = $"https://api.github.com/repos/{DefaultRepoOwner}/{DefaultRepoName}/actions/artifacts";
                _logger.LogDebug("[JellyEmu] Checking Play! build artifacts status from {Url}", apiUrl);

                string? remoteStamp = null;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                    using var response = await client.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("artifacts", out var artifactsElem) && artifactsElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var artifact in artifactsElem.EnumerateArray())
                            {
                                var name = artifact.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
                                if (string.Equals(name, "ps2web-site", StringComparison.OrdinalIgnoreCase))
                                {
                                    var artifactId = artifact.TryGetProperty("id", out var idElem) ? idElem.GetInt64().ToString() : "0";
                                    var updatedAt = artifact.TryGetProperty("updated_at", out var uElem) ? uElem.GetString() : string.Empty;
                                    var headSha = "";
                                    if (artifact.TryGetProperty("workflow_run", out var runElem) && runElem.TryGetProperty("head_sha", out var shaElem))
                                    {
                                        headSha = shaElem.GetString();
                                    }

                                    remoteStamp = $"artifact_{artifactId}_{headSha}_{updatedAt}";
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception apiEx)
                {
                    _logger.LogDebug(apiEx, "[JellyEmu] Could not query GitHub Actions artifacts API directly.");
                }

                if (!string.IsNullOrEmpty(remoteStamp) && LocalRuntimeValid() && !string.IsNullOrEmpty(currentStamp) && currentStamp == remoteStamp)
                {
                    _logger.LogInformation("[JellyEmu] Play! PS2 runtime is up-to-date ({Version})", remoteStamp);
                    _isReady = true;
                    return;
                }

                _logger.LogInformation("[JellyEmu] Newer or missing Play! PS2 runtime detected ({RemoteStamp} vs {LocalStamp}). Updating from build artifact archive...", remoteStamp ?? "latest", currentStamp ?? "none");

                // Download artifact zip via nightly.link (direct unauthenticated archive proxy)
                var downloaded = await TryDownloadAndExtractArtifactZipAsync(client, NightlyLinkSiteZip);

                if (!downloaded)
                {
                    _logger.LogInformation("[JellyEmu] Trying fallback artifact URL {Url}...", NightlyLinkPlayWasmZip);
                    downloaded = await TryDownloadAndExtractArtifactZipAsync(client, NightlyLinkPlayWasmZip);
                }

                if (downloaded)
                {
                    var stampToSave = remoteStamp ?? $"nightly_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                    await File.WriteAllTextAsync(StampFile, stampToSave);
                    _isReady = true;
                    _logger.LogInformation("[JellyEmu] Play! PS2 runtime updated successfully ({Stamp}).", stampToSave);
                }
                else if (LocalRuntimeValid())
                {
                    _isReady = true;
                    _logger.LogWarning("[JellyEmu] Could not download latest Play! build artifact — using existing local files.");
                }
                else
                {
                    _logger.LogError("[JellyEmu] Failed to download Play! PS2 runtime archive. PS2 emulation may be unavailable until connected.");
                }
            }
            catch (Exception ex)
            {
                if (LocalRuntimeValid())
                {
                    _isReady = true;
                    _logger.LogWarning(ex, "[JellyEmu] Offline or network error during Play! update check — using cached local runtime.");
                }
                else
                {
                    _logger.LogError(ex, "[JellyEmu] Failed to download Play! PS2 runtime. PS2 emulation may be unavailable until connected.");
                }
            }
        }

        private async Task<bool> TryDownloadAndExtractArtifactZipAsync(HttpClient client, string zipUrl)
        {
            var tmpZip = Path.Combine(PlayRoot, $"runtime_bundle_{Guid.NewGuid():N}.tmp.zip");
            try
            {
                using (var response = await client.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("[JellyEmu] Failed downloading Play! artifact from {Url} with status {Status}", zipUrl, response.StatusCode);
                        return false;
                    }

                    await using (var fs = File.Create(tmpZip))
                    {
                        await response.Content.CopyToAsync(fs);
                        await fs.FlushAsync();
                    }
                }

                using (var archive = ZipFile.OpenRead(tmpZip))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.Equals(entry.Name, PlayJsFilename, StringComparison.OrdinalIgnoreCase))
                        {
                            var target = PlayJsPath;
                            var tmpTarget = target + ".tmp";
                            if (File.Exists(tmpTarget)) File.Delete(tmpTarget);
                            entry.ExtractToFile(tmpTarget);

                            PatchPlayJsFile(tmpTarget);

                            if (File.Exists(target)) File.Delete(target);
                            File.Move(tmpTarget, target);
                        }
                        else if (string.Equals(entry.Name, PlayWasmFilename, StringComparison.OrdinalIgnoreCase))
                        {
                            var target = PlayWasmPath;
                            var tmpTarget = target + ".tmp";
                            if (File.Exists(tmpTarget)) File.Delete(tmpTarget);
                            entry.ExtractToFile(tmpTarget);
                            if (File.Exists(target)) File.Delete(target);
                            File.Move(tmpTarget, target);
                        }
                        else if (entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !entry.FullName.Contains('/'))
                        {
                            var target = Path.Combine(PlayRoot, entry.Name);
                            var tmpTarget = target + ".tmp";
                            if (File.Exists(tmpTarget)) File.Delete(tmpTarget);
                            entry.ExtractToFile(tmpTarget);
                            if (File.Exists(target)) File.Delete(target);
                            File.Move(tmpTarget, target);
                        }
                    }
                }

                return LocalRuntimeValid();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Error extracting Play! runtime archive from {Url}", zipUrl);
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(tmpZip)) File.Delete(tmpZip);
                }
                catch
                {
                    // Ignore temp cleanup errors
                }
            }
        }
    }
}
