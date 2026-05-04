using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Services
{
    /// <summary>
    /// Manages a local copy of the PICO-8 web runtime (pico8.js).
    ///
    /// On first startup the service fetches pico8.js directly from Lexaloffle's
    /// servers and caches it at {DataPath}/jellyemu-pico8/pico8.js, then writes
    /// a .version stamp so subsequent startups skip the download.
    ///
    /// The runtime is the same file Lexaloffle's BBS serves to every player —
    /// we are simply caching it locally rather than hitting their CDN on every
    /// cart load.
    ///
    /// While the file is absent (first boot or download in-progress),
    /// <see cref="IsReady"/> is false and the controller falls back to
    /// proxying the runtime directly from Lexaloffle on each request.
    /// </summary>
    public class JellyEmuPico8Manager
    {
        /// <summary>The canonical source URL for the PICO-8 web runtime.</summary>
        public const string RuntimeUrl = "https://www.lexaloffle.com/play/pico8_0207.js";

        /// <summary>Filename used locally.</summary>
        public const string RuntimeFilename = "pico8.js";

        private readonly IApplicationPaths _appPaths;
        private readonly ILogger<JellyEmuPico8Manager> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        private string Pico8Root => Path.Combine(_appPaths.DataPath, "jellyemu-pico8");
        private string RuntimePath => Path.Combine(Pico8Root, RuntimeFilename);
        private string StampFile => Path.Combine(Pico8Root, ".version");

        private volatile bool _isReady;
        public bool IsReady => _isReady;
        public string LocalRoot => Pico8Root;

        public JellyEmuPico8Manager(
            IApplicationPaths appPaths,
            ILogger<JellyEmuPico8Manager> logger,
            IHttpClientFactory httpClientFactory)
        {
            _appPaths = appPaths;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Called at startup (e.g. from JellyEmuInjectorService alongside EnsureAssetsAsync).
        /// If the runtime is already cached, marks ready synchronously.
        /// Otherwise fires a background download so startup is never blocked.
        /// </summary>
        public void EnsureRuntimeAsync()
        {
            if (LocalRuntimeValid())
            {
                _isReady = true;
                _logger.LogInformation("[JellyEmu] PICO-8 runtime present at {Path}", RuntimePath);
                return;
            }

            _logger.LogInformation(
                "[JellyEmu] PICO-8 runtime missing — downloading from Lexaloffle in background...");

            _ = Task.Run(DownloadRuntimeAsync);
        }

        private bool LocalRuntimeValid()
        {
            if (!Directory.Exists(Pico8Root)) return false;
            if (!File.Exists(StampFile)) return false;
            if (File.ReadAllText(StampFile).Trim() != "lexaloffle-0207") return false;
            if (!File.Exists(RuntimePath)) return false;

            // Sanity check: file should be several MB
            var info = new FileInfo(RuntimePath);
            if (info.Length < 1_000_000) return false;

            return true;
        }

        private async Task DownloadRuntimeAsync()
        {
            try
            {
                Directory.CreateDirectory(Pico8Root);

                var client = _httpClientFactory.CreateClient("JellyEmuPico8");
                client.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (compatible; JellyEmu/1.0)");

                _logger.LogInformation("[JellyEmu] Fetching PICO-8 runtime from {Url}", RuntimeUrl);

                using var response = await client.GetAsync(RuntimeUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var tmpPath = RuntimePath + ".tmp";
                await using (var fs = File.Create(tmpPath))
                {
                    await response.Content.CopyToAsync(fs);
                    await fs.FlushAsync();
                }

                // Atomic replace
                if (File.Exists(RuntimePath))
                    File.Delete(RuntimePath);
                File.Move(tmpPath, RuntimePath);

                await File.WriteAllTextAsync(StampFile, "lexaloffle-0207");

                _isReady = true;
                _logger.LogInformation(
                    "[JellyEmu] PICO-8 runtime downloaded successfully ({Bytes:N0} bytes)",
                    new FileInfo(RuntimePath).Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[JellyEmu] Failed to download PICO-8 runtime — will proxy live from Lexaloffle.");
            }
        }
    }
}