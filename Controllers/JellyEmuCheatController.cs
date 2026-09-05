using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Fetches, fuzzy-matches, caches, and serves libretro cheat codes.
    /// Routes: /jellyemu/cheats/*
    /// </summary>
    public class JellyEmuCheatController : JellyEmuBaseController
    {
        // Strips No-Intro parenthetical tokens, e.g. "Super Mario (USA) (Rev 1)" → "super mario"
        private static readonly System.Text.RegularExpressions.Regex ParenRegex =
            new(@"\s*\([^)]*\)", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string StripParens(string name) =>
            ParenRegex.Replace(name, "").Trim().ToLowerInvariant();

        public JellyEmuCheatController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger<JellyEmuCheatController> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
            : base(libraryManager, appPaths, logger, ejsManager, sessionService, httpClientFactory) { }

        /// <summary>
        /// Fetches and parses cheats for a ROM from the libretro cheat database on GitHub.
        /// Results are cached to disk for 7 days so subsequent launches are instant.
        /// Path: GET /jellyemu/cheats/{itemId}
        /// Returns: JSON array of [name, code, ""] triples, or empty array if none found.
        /// </summary>
        [HttpGet("/jellyemu/cheats/{itemId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCheats(string itemId,
            [FromServices] IHttpClientFactory httpClientFactory)
        {
            var cacheKey = JellyEmuCacheKeys.Cheats(itemId);
            if (CacheService.TryGetValue<string>(cacheKey, out var cachedJson) && cachedJson != null)
            {
                return Content(cachedJson, "application/json");
            }

            var item = LibraryManager.GetItemById(itemId);
            if (item == null) return Ok(Array.Empty<object>());

            var json = await GetCheatsJsonAsync(item, httpClientFactory);
            var payload = json ?? "[]";
            CacheService.Set(cacheKey, payload, slidingExpiration: TimeSpan.FromHours(24));
            return Content(payload, "application/json");
        }

        /// <summary>
        /// Resolves the EJS-ready cheats JSON for a given item (7-day disk cache).
        /// </summary>
        private async Task<string?> GetCheatsJsonAsync(
            MediaBrowser.Controller.Entities.BaseItem item,
            IHttpClientFactory httpClientFactory)
        {
            var consoleTags = (item.Tags ?? Array.Empty<string>())
                .Where(t => CheatDbFolderMap.ContainsKey(t))
                .ToList();
            if (consoleTags.Count == 0) return null;

            var dbFolder = CheatDbFolderMap[consoleTags[0]];
            var romName  = Path.GetFileNameWithoutExtension(item.Path ?? item.Name ?? "");

            var cacheDir  = Path.Combine(AppPaths.DataPath, "jellyemu-cheats");
            Directory.CreateDirectory(cacheDir);
            var cacheFile = Path.Combine(cacheDir, item.Id + ".json");

            if (System.IO.File.Exists(cacheFile) &&
                (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(cacheFile)).TotalDays < 7)
            {
                var cached = await System.IO.File.ReadAllTextAsync(cacheFile);
                return cached == "[]" ? null : cached;
            }

            try
            {
                var candidates = await GetSystemCheatListAsync(dbFolder, httpClientFactory);
                if (candidates == null || candidates.Count == 0)
                {
                    await System.IO.File.WriteAllTextAsync(cacheFile, "[]");
                    return null;
                }

                var matched = FuzzyMatchCht(romName, candidates);
                if (matched == null)
                {
                    Logger.LogDebug("[JellyEmu] No cheat match for '{Rom}' in {Folder}", romName, dbFolder);
                    await System.IO.File.WriteAllTextAsync(cacheFile, "[]");
                    return null;
                }

                Logger.LogInformation("[JellyEmu] Matched '{Rom}' → '{Matched}'", romName, matched);

                var encodedFolder = Uri.EscapeDataString(dbFolder);
                var encodedFile   = Uri.EscapeDataString(matched);
                var url = $"https://raw.githubusercontent.com/libretro/libretro-database/master/cht/{encodedFolder}/{encodedFile}";

                var client   = httpClientFactory.CreateClient("JellyEmuCheats");
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    await System.IO.File.WriteAllTextAsync(cacheFile, "[]");
                    return null;
                }

                var chtText = await response.Content.ReadAsStringAsync();
                var cheats  = ParseChtFile(chtText);
                var json    = System.Text.Json.JsonSerializer.Serialize(cheats);
                await System.IO.File.WriteAllTextAsync(cacheFile, json);
                Logger.LogInformation("[JellyEmu] Loaded {Count} cheats for '{Rom}'", cheats.Count, romName);
                return cheats.Count > 0 ? json : null;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Failed to fetch cheats for '{Rom}'", romName);
                return null;
            }
        }

        /// <summary>
        /// Fetches and caches (30 days) the list of .cht filenames for a system folder
        /// from the GitHub Contents API.
        /// </summary>
        private async Task<List<string>?> GetSystemCheatListAsync(
            string dbFolder, IHttpClientFactory httpClientFactory)
        {
            var cacheDir  = Path.Combine(AppPaths.DataPath, "jellyemu-cheats", "index");
            Directory.CreateDirectory(cacheDir);
            var safeName  = string.Concat(dbFolder.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            var cacheFile = Path.Combine(cacheDir, safeName + ".json");

            if (System.IO.File.Exists(cacheFile) &&
                (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(cacheFile)).TotalDays < 30)
            {
                try
                {
                    var cached = await System.IO.File.ReadAllTextAsync(cacheFile);
                    return System.Text.Json.JsonSerializer.Deserialize<List<string>>(cached);
                }
                catch { /* fall through to re-fetch */ }
            }

            try
            {
                var encoded = Uri.EscapeDataString(dbFolder);
                var url     = $"https://api.github.com/repos/libretro/libretro-database/contents/cht/{encoded}";
                var client  = httpClientFactory.CreateClient("JellyEmuCheats");
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "JellyEmu-Plugin");
                var response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogWarning("[JellyEmu] GitHub Contents API returned {Status} for {Folder}",
                        response.StatusCode, dbFolder);
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(body);

                var names = doc.RootElement.EnumerateArray()
                    .Where(e => e.TryGetProperty("name", out var n) &&
                                n.GetString()?.EndsWith(".cht", StringComparison.OrdinalIgnoreCase) == true)
                    .Select(e => e.GetProperty("name").GetString()!)
                    .ToList();

                var json = System.Text.Json.JsonSerializer.Serialize(names);
                await System.IO.File.WriteAllTextAsync(cacheFile, json);
                Logger.LogInformation("[JellyEmu] Cached {Count} cheat entries for {Folder}", names.Count, dbFolder);
                return names;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Failed to fetch cheat index for {Folder}", dbFolder);
                return null;
            }
        }

        /// <summary>
        /// Finds the best-matching .cht filename for the given ROM name using stripped-paren comparison.
        /// </summary>
        private static string? FuzzyMatchCht(string romName, List<string> candidates)
        {
            var stripped = StripParens(romName);
            if (string.IsNullOrWhiteSpace(stripped)) return null;

            // 1. Exact match after stripping parens from both sides
            foreach (var c in candidates)
                if (StripParens(Path.GetFileNameWithoutExtension(c)) == stripped)
                    return c;

            // 2. Candidate starts-with — DB entry has extra subtitle tokens
            var startsWith = candidates
                .Where(c => StripParens(Path.GetFileNameWithoutExtension(c))
                                .StartsWith(stripped, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Length)
                .FirstOrDefault();
            if (startsWith != null) return startsWith;

            // 3. ROM-name starts-with candidate — ROM has more info than DB entry
            return candidates
                .Where(c => stripped.StartsWith(
                    StripParens(Path.GetFileNameWithoutExtension(c)),
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Length)
                .FirstOrDefault();
        }

        /// <summary>
        /// Parses a libretro .cht file into EJS-compatible [description, code, ""] triples.
        /// All cheats start disabled ("") — the user enables them from the in-game cheat menu.
        /// </summary>
        private static List<string[]> ParseChtFile(string cht)
        {
            var result  = new List<string[]>();
            var entries = new Dictionary<int, (string? Name, string? Code)>();

            foreach (var rawLine in cht.Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.Contains('=')) continue;

                var eqIdx = line.IndexOf('=');
                var key   = line[..eqIdx].Trim();
                var value = line[(eqIdx + 1)..].Trim().Trim('"');

                if (!key.StartsWith("cheat", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = key.Split('_', 2);
                if (parts.Length < 2) continue;
                if (!int.TryParse(parts[0]["cheat".Length..], out var idx)) continue;

                var field = parts[1].ToLowerInvariant();
                if (!entries.ContainsKey(idx)) entries[idx] = (null, null);
                var entry = entries[idx];

                if (field == "desc") entries[idx] = (value, entry.Code);
                else if (field == "code") entries[idx] = (entry.Name, value);
            }

            foreach (var (_, (name, code)) in entries.OrderBy(e => e.Key))
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(code))
                    result.Add(new[] { name, code, "" }); // "" = disabled by default

            return result;
        }
    }
}
