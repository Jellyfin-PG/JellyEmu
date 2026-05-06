using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Services
{
    /// <summary>
    /// Manages a local copy of the Three.js r128 runtime (three.min.js).
    ///
    /// On first startup the service fetches three.min.js from the cdnjs CDN
    /// and caches it at {DataPath}/jellyemu-threejs/three.min.js, then writes
    /// a .version stamp so subsequent startups skip the download.
    ///
    /// While the file is absent (first boot or download in-progress),
    /// <see cref="IsReady"/> is false and the controller falls back to
    /// referencing the CDN URL directly on each request.
    /// </summary>
    public class JellyEmuThreeJsManager
    {
        /// <summary>The canonical source URL for Three.js r128.</summary>
        public const string RuntimeUrl = "https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js";

        /// <summary>Filename used locally.</summary>
        public const string RuntimeFilename = "three.min.js";

        private readonly IApplicationPaths _appPaths;
        private readonly ILogger<JellyEmuThreeJsManager> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        private string ThreeJsRoot => Path.Combine(_appPaths.DataPath, "jellyemu-threejs");
        private string RuntimePath => Path.Combine(ThreeJsRoot, RuntimeFilename);
        private string StampFile => Path.Combine(ThreeJsRoot, ".version");

        private volatile bool _isReady;
        public bool IsReady => _isReady;
        public string LocalRoot => ThreeJsRoot;

        public JellyEmuThreeJsManager(
            IApplicationPaths appPaths,
            ILogger<JellyEmuThreeJsManager> logger,
            IHttpClientFactory httpClientFactory)
        {
            _appPaths = appPaths;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Called at startup alongside other EnsureXxxAsync calls.
        /// If the runtime is already cached, marks ready synchronously.
        /// Otherwise fires a background download so startup is never blocked.
        /// </summary>
        public void EnsureRuntimeAsync()
        {
            if (LocalRuntimeValid())
            {
                _isReady = true;
                _logger.LogInformation("[JellyEmu] Three.js runtime present at {Path}", RuntimePath);
                return;
            }

            _logger.LogInformation(
                "[JellyEmu] Three.js runtime missing — downloading from cdnjs in background...");

            _ = Task.Run(DownloadRuntimeAsync);
        }

        private bool LocalRuntimeValid()
        {
            if (!Directory.Exists(ThreeJsRoot)) return false;
            if (!File.Exists(StampFile)) return false;
            if (File.ReadAllText(StampFile).Trim() != "cdnjs-threejs-r128") return false;
            if (!File.Exists(RuntimePath)) return false;

            var info = new FileInfo(RuntimePath);
            if (info.Length < 500_000) return false;

            return true;
        }

        private async Task DownloadRuntimeAsync()
        {
            try
            {
                Directory.CreateDirectory(ThreeJsRoot);

                var client = _httpClientFactory.CreateClient("JellyEmuThreeJs");
                client.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (compatible; JellyEmu/1.0)");

                _logger.LogInformation("[JellyEmu] Fetching Three.js runtime from {Url}", RuntimeUrl);

                using var response = await client.GetAsync(RuntimeUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var tmpPath = RuntimePath + ".tmp";
                await using (var fs = File.Create(tmpPath))
                {
                    await response.Content.CopyToAsync(fs);
                    await fs.FlushAsync();
                }

                if (File.Exists(RuntimePath))
                    File.Delete(RuntimePath);
                File.Move(tmpPath, RuntimePath);

                await File.WriteAllTextAsync(StampFile, "cdnjs-threejs-r128");

                _isReady = true;
                _logger.LogInformation(
                    "[JellyEmu] Three.js runtime downloaded successfully ({Bytes:N0} bytes)",
                    new FileInfo(RuntimePath).Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[JellyEmu] Failed to download Three.js runtime — will fall back to CDN URL.");
            }
        }
    }
}