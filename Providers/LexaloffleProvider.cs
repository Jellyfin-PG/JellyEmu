using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Providers
{
    public abstract class BaseLexaloffleProvider
    {
        public const string ProviderName  = "Lexaloffle";
        public const string ProviderId    = "LexaloffleLoId";
        protected const string BbsBase      = "https://www.lexaloffle.com/bbs/";
        protected const string ListerBase   = "https://www.lexaloffle.com/bbs/lister.php";
        protected const string CartImageUrl = "https://www.lexaloffle.com/bbs/cposts/{0}/{1}.p8.png";
        protected const int    CacheDays    = 7;

        // pdat array column indices
        // ['pid', tid, `title`, "/bbs/thumbs/x.png", w, h, "date", uid, "author",
        //  "last_reply_date", reply_uid, "reply_user", stars, comments, parent_pid,
        //  cat, sub, 'flags', [tags], cc_flags, ?, ?, `display_title`, `desc`]
        protected const int PdatPid          = 0;
        protected const int PdatTitle        = 2;
        protected const int PdatThumb        = 3;
        protected const int PdatDate         = 6;
        protected const int PdatAuthor       = 8;
        protected const int PdatStars        = 12;
        protected const int PdatDisplayTitle = 22;
        protected const int PdatDesc         = 23;

        protected readonly IApplicationPaths _appPaths;
        protected readonly IHttpClientFactory _httpClientFactory;
        protected readonly ILogger _logger;

        protected string CacheDir => Path.Combine(_appPaths.DataPath, "jellyemu-pico8", "metacache");

        protected BaseLexaloffleProvider(
            IApplicationPaths appPaths,
            IHttpClientFactory httpClientFactory,
            ILogger logger)
        {
            _appPaths          = appPaths;
            _httpClientFactory = httpClientFactory;
            _logger            = logger;
        }

        protected static string? TryExtractEmbeddedLexalId(string? input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            var match = Regex.Match(input, @"\[loid-(\d+)\]", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        protected async Task<ScrapedCart?> FetchAndCacheAsync(
            string loid, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(CacheDir);
            var cacheFile = Path.Combine(CacheDir, $"{loid}.json");

            if (File.Exists(cacheFile))
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile);
                if (age.TotalDays < CacheDays)
                {
                    try
                    {
                        var cachedJson   = await File.ReadAllTextAsync(cacheFile, cancellationToken).ConfigureAwait(false);
                        var cachedResult = JsonSerializer.Deserialize<ScrapedCart>(cachedJson);

                        if (cachedResult != null
                            && !string.IsNullOrWhiteSpace(cachedResult.Title)
                            && !cachedResult.Title.Contains("+dat[", StringComparison.Ordinal)
                            && !cachedResult.Title.Contains("`+dat", StringComparison.Ordinal))
                        {
                            return cachedResult;
                        }

                        _logger.LogInformation("[JellyEmu] Cache invalid for {LoId}, re-fetching", loid);
                        File.Delete(cacheFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[JellyEmu] Cache read failed for {LoId}, re-fetching", loid);
                        File.Delete(cacheFile);
                    }
                }
            }

            _logger.LogInformation("[JellyEmu] Fetching Lexaloffle BBS cart loid {LoId}", loid);

            var client   = _httpClientFactory.CreateClient("JellyEmuPico8");
            var encoded  = Uri.EscapeDataString(loid);

            var url  = $"{BbsBase}?pid={encoded}";
            var html = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

            var carts = ParsePdatEntries(html);

            var cart = carts.FirstOrDefault(c => c.ParentPid == loid)
                    ?? carts.FirstOrDefault(c => c.Pid == loid)
                    ?? (carts.Count == 1 ? carts[0] : null);

            if (cart != null)
            {
                var json = JsonSerializer.Serialize(cart);
                await File.WriteAllTextAsync(cacheFile, json, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("[JellyEmu] No pdat entry found for pid {LoId}", loid);
            }

            return cart;
        }

        protected List<RemoteSearchResult> ParsePdatArray(string html)
        {
            return ParsePdatEntries(html)
                .Select(e =>
                {
                    var r = new RemoteSearchResult
                    {
                        Name               = e.Title,
                        Overview           = string.IsNullOrEmpty(e.Description) ? $"by {e.Author}" : e.Description,
                        ProductionYear     = e.Year,
                        SearchProviderName = ProviderName,
                        ImageUrl           = e.ThumbUrl ?? BuildCartImageUrl(e.Pid),
                    };
                    r.SetProviderId(ProviderId, e.Pid);
                    return r;
                })
                .ToList();
        }

        protected List<ScrapedCart> ParsePdatEntries(string html)
        {
            var results = new List<ScrapedCart>();

            var pdatStart = Regex.Match(html, @"pdat\s*=\s*\[", RegexOptions.IgnoreCase);
            if (!pdatStart.Success)
            {
                _logger.LogWarning("[JellyEmu] pdat array not found in response");
                return results;
            }

            var bodyStart = pdatStart.Index + pdatStart.Length;
            var depth     = 1;
            var pos       = bodyStart;
            while (pos < html.Length && depth > 0)
            {
                if      (html[pos] == '[') depth++;
                else if (html[pos] == ']') depth--;
                pos++;
            }

            var pdatBody   = html.Substring(bodyStart, pos - bodyStart - 1);

            var rowMatches = new List<string>();
            for (var i = 0; i < pdatBody.Length; i++)
            {
                if (pdatBody[i] != '[') continue;

                var rStart  = i + 1;
                var rDepth  = 1;
                var inBack2 = false;
                var j       = rStart;
                while (j < pdatBody.Length && rDepth > 0)
                {
                    var ch = pdatBody[j];
                    if (inBack2) { if (ch == '`') inBack2 = false; }
                    else if (ch == '`')  inBack2 = true;
                    else if (ch == '[')  rDepth++;
                    else if (ch == ']')  rDepth--;
                    j++;
                }
                rowMatches.Add(pdatBody.Substring(rStart, j - rStart - 1));
                i = j - 1; // advance past this row
            }

            _logger.LogInformation("[JellyEmu] pdat rows found: {Count}", rowMatches.Count);

            foreach (var rowContent in rowMatches)
            {
                try
                {
                    var cols = SplitPdatRow(rowContent);
                    if (cols.Count < 9) continue;

                    var pid          = cols[PdatPid].Trim('\'', '"', ' ');
                    var title        = cols.Count > PdatTitle        ? cols[PdatTitle].Trim('`', ' ')         : string.Empty;
                    var thumbPath    = cols.Count > PdatThumb        ? cols[PdatThumb].Trim('"', '\'', ' ')   : string.Empty;
                    var dateStr      = cols.Count > PdatDate         ? cols[PdatDate].Trim('"', '\'', ' ')    : string.Empty;
                    var author       = cols.Count > PdatAuthor       ? cols[PdatAuthor].Trim('"', '\'', ' ')  : string.Empty;
                    if (author.StartsWith("+dat[", StringComparison.Ordinal)) author = string.Empty;
                    var starsStr     = cols.Count > PdatStars        ? cols[PdatStars].Trim()                 : string.Empty;
                    var parentPid    = cols.Count > 14               ? cols[14].Trim('\'', '"', ' ')          : string.Empty;
                    var displayTitle = cols.Count > PdatDisplayTitle ? cols[PdatDisplayTitle].Trim('`', ' ')  : string.Empty;
                    if (displayTitle.StartsWith("+dat[", StringComparison.Ordinal)) displayTitle = string.Empty;
                    var desc         = cols.Count > PdatDesc
                        ? cols[PdatDesc].Trim('`', ' ')
                        : string.Empty;

                    var bestTitle = !string.IsNullOrWhiteSpace(displayTitle) ? displayTitle
                                  : !string.IsNullOrWhiteSpace(title)        ? title
                                  : $"PICO-8 Cart {pid}";

                    int? year = null;
                    if (DateTime.TryParse(dateStr, out var dt))
                        year = dt.Year;

                    float stars = float.TryParse(starsStr, out var sv) ? sv : 0;

                    var thumbUrl = string.IsNullOrEmpty(thumbPath)
                        ? null
                        : thumbPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? thumbPath
                            : $"https://www.lexaloffle.com{thumbPath}";

                    results.Add(new ScrapedCart
                    {
                        Pid         = pid,
                        ParentPid   = parentPid,
                        Title       = bestTitle,
                        Author      = author,
                        Description = string.IsNullOrWhiteSpace(desc) ? null : desc,
                        Year        = year,
                        Stars       = stars,
                        ThumbUrl    = thumbUrl,
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[JellyEmu] Failed to parse pdat row: {Row}", rowContent);
                }
            }

            return results;
        }

        /// <summary>
        /// Splits a pdat row into columns, respecting backtick strings, quoted strings,
        /// and nested arrays (tags).
        /// </summary>
        protected static List<string> SplitPdatRow(string row)
        {
            var cols    = new List<string>();
            var current = new System.Text.StringBuilder();
            var depth   = 0;
            var inBack  = false;
            var inDq    = false;
            var inSq    = false;

            for (var i = 0; i < row.Length; i++)
            {
                var c = row[i];

                if (inBack)
                {
                    if (c == '`') inBack = false;
                    current.Append(c);
                    continue;
                }
                if (inDq)
                {
                    if (c == '"' && (i == 0 || row[i - 1] != '\\')) inDq = false;
                    current.Append(c);
                    continue;
                }
                if (inSq)
                {
                    if (c == '\'' && (i == 0 || row[i - 1] != '\\')) inSq = false;
                    current.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '`': inBack = true; current.Append(c); break;
                    case '"': inDq   = true; current.Append(c); break;
                    case '\'': inSq  = true; current.Append(c); break;
                    case '[': depth++; current.Append(c); break;
                    case ']': depth--; current.Append(c); break;
                    case ',' when depth == 0:
                        cols.Add(current.ToString().Trim());
                        current.Clear();
                        break;
                    default:
                        current.Append(c);
                        break;
                }
            }

            if (current.Length > 0)
                cols.Add(current.ToString().Trim());

            return cols;
        }

        protected static ScrapedCart? ParseCartPage(string html, string loid)
        {
            var title = RegexFirst(html,
                    @"<a[^>]*href=""[^""]*\?pid=" + loid + @"[^""]*""[^>]*>\s*<font[^>]*color:#eee[^>]*>([^<]+)</font>")
                ?? RegexFirst(html, @"<font[^>]*color:#eee[^>]*font-size:10pt[^>]*>([^<]+)</font>")
                ?? $"PICO-8 Cart {loid}";

            var author = RegexFirst(html,
                @"<font[^>]*color:#bbb[^>]*font-size:8pt[^>]*>\s*by\s*([^<]+?)\s*</font>")?.Trim();

            var rawDesc = RegexFirst(html,
                @"<div[^>]*class=""[^""]*post_body[^""]*""[^>]*>([\s\S]*?)</div>");
            var description = rawDesc != null
                ? Regex.Replace(rawDesc, "<[^>]+>", " ").Trim()
                : null;

            var dateStr = RegexFirst(html, @"(\d{4})-\d{2}-\d{2}\s*\d{2}:\d{2}:\d{2}");
            int? year   = int.TryParse(dateStr, out var y) ? y : null;

            var starsStr = RegexFirst(html, @"class=""[^""]*bbs_star[^""]*""[^>]*>\s*(\d+)");
            float stars  = float.TryParse(starsStr, out var s) ? s : 0;

            var thumbPath = RegexFirst(html, @"background:url\('(/bbs/thumbs/[^']+\.png)'\)");
            var thumbUrl  = thumbPath != null ? $"https://www.lexaloffle.com{thumbPath}" : null;

            return new ScrapedCart
            {
                Pid         = loid,
                Title       = title.Trim(),
                Author      = author,
                Description = description,
                Year        = year,
                Stars       = stars,
                ThumbUrl    = thumbUrl,
            };
        }

        protected static string? RegexFirst(string input, string pattern)
        {
            var m = Regex.Match(input, pattern,
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value : null;
        }

        protected static string BuildCartImageUrl(string loid)
        {
            var suffix = loid.Length >= 2
                ? loid.Substring(loid.Length - 2)
                : loid.PadLeft(2, '0');
            return string.Format(CartImageUrl, suffix, loid);
        }

        protected sealed class ScrapedCart
        {
            public string  Pid         { get; set; } = string.Empty;
            public string  ParentPid   { get; set; } = string.Empty;
            public string  Title       { get; set; } = string.Empty;
            public string? Author      { get; set; }
            public string? Description { get; set; }
            public int?    Year        { get; set; }
            public float   Stars       { get; set; }
            public string? ThumbUrl    { get; set; }
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
            return _httpClientFactory.CreateClient("JellyEmuPico8").GetAsync(url, cancellationToken);
        }
    }

    public class LexaloffleMetadataProvider : BaseLexaloffleProvider,
        IRemoteMetadataProvider<Book, BookInfo>,
        IExternalId,
        IHasOrder
    {
        public string Name  => ProviderName + " Metadata Provider";
        public int    Order => 0;

        string IExternalId.ProviderName      => ProviderName;
        string IExternalId.Key               => ProviderId;
        ExternalIdMediaType? IExternalId.Type => null;
        bool IExternalId.Supports(IHasProviderIds item)
            => item is Book b && RomExtensions.IsPico8Path(b.Path);

        public LexaloffleMetadataProvider(
            IApplicationPaths appPaths,
            IHttpClientFactory httpClientFactory,
            ILogger<LexaloffleMetadataProvider> logger)
            : base(appPaths, httpClientFactory, logger) { }

        // Identify
        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
            BookInfo searchInfo, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(searchInfo.Path) && !RomExtensions.IsPico8Path(searchInfo.Path))
                return Enumerable.Empty<RemoteSearchResult>();

            var loid = searchInfo.GetProviderId(ProviderId);
            if (string.IsNullOrEmpty(loid) && !string.IsNullOrEmpty(searchInfo.Name))
                loid = TryExtractEmbeddedLexalId(searchInfo.Name);

            if (!string.IsNullOrEmpty(loid))
            {
                try
                {
                    var scraped = await FetchAndCacheAsync(loid, cancellationToken).ConfigureAwait(false);
                    if (scraped == null) return Enumerable.Empty<RemoteSearchResult>();

                    var single = new RemoteSearchResult
                    {
                        Name               = scraped.Title,
                        Overview           = scraped.Description,
                        ProductionYear     = scraped.Year,
                        SearchProviderName = ProviderName,
                        ImageUrl           = scraped.ThumbUrl ?? BuildCartImageUrl(loid),
                    };
                    single.SetProviderId(ProviderId, loid);
                    return new[] { single };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[JellyEmu] Lexaloffle direct lookup failed for loid {LoId}", loid);
                    return Enumerable.Empty<RemoteSearchResult>();
                }
            }

            var searchName = RomExtensions.CleanName(searchInfo.Name);
            if (string.IsNullOrEmpty(searchName)) return Enumerable.Empty<RemoteSearchResult>();

            try
            {
                var client    = _httpClientFactory.CreateClient("JellyEmuPico8");
                var encoded   = Uri.EscapeDataString(searchName);
                var searchUrl = $"{ListerBase}?use_hurl=1&cat=7&sub=2&page=1&mode=carts&orderby=ts&search={encoded}";

                _logger.LogInformation("[JellyEmu] Searching Lexaloffle BBS: {Url}", searchUrl);

                var html = await client.GetStringAsync(searchUrl, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("[JellyEmu] Search response length: {Length}", html.Length);
                var results = ParsePdatArray(html);
                _logger.LogInformation("[JellyEmu] Parsed {Count} carts from pdat array", results.Count);

                _logger.LogInformation("[JellyEmu] Returning {Count} results", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Lexaloffle name search failed for '{Name}'", searchName);
                return Enumerable.Empty<RemoteSearchResult>();
            }
        }
        
        public async Task<MetadataResult<Book>> GetMetadata(
            BookInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book>();

            if (!string.IsNullOrEmpty(info.Path) && !RomExtensions.IsPico8Path(info.Path))
                return result;

            var loid = info.GetProviderId(ProviderId);
            if (string.IsNullOrEmpty(loid) && !string.IsNullOrEmpty(info.Path))
                loid = TryExtractEmbeddedLexalId(info.Path);
            if (string.IsNullOrEmpty(loid) && !string.IsNullOrEmpty(info.Name))
                loid = TryExtractEmbeddedLexalId(info.Name);

            if (string.IsNullOrEmpty(loid))
                return result;

            var scraped = await FetchAndCacheAsync(loid, cancellationToken).ConfigureAwait(false);
            if (scraped == null)
                return result;

            var item = new Book
            {
                Name            = scraped.Title,
                Overview        = scraped.Description,
                ProductionYear  = scraped.Year,
                CommunityRating = scraped.Stars > 0 ? (float?)scraped.Stars : null,
                Tags            = new[] { "PICO-8", "JellyEmu" },
            };

            item.SetProviderId(ProviderId, loid);

            if (!string.IsNullOrEmpty(scraped.Author))
            {
                result.AddPerson(new PersonInfo
                {
                    Name = scraped.Author,
                    Type = PersonKind.Director,
                });
            }

            result.Item        = item;
            result.HasMetadata = true;
            return result;
        }
    }

    public class LexaloffleImageProvider : BaseLexaloffleProvider,
        IRemoteImageProvider,
        IHasOrder
    {
        public string Name  => ProviderName + " Image Provider";
        public int    Order => 0;

        public LexaloffleImageProvider(
            IApplicationPaths appPaths,
            IHttpClientFactory httpClientFactory,
            ILogger<LexaloffleImageProvider> logger)
            : base(appPaths, httpClientFactory, logger) { }

        public bool Supports(BaseItem item)
            => item is Book;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
            => new[] { ImageType.Primary };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(
            BaseItem item, CancellationToken cancellationToken)
        {
            if (!RomExtensions.IsPico8Path(item.Path))
                return Enumerable.Empty<RemoteImageInfo>();

            var loid = item.GetProviderId(ProviderId);
            if (string.IsNullOrEmpty(loid))
                return Enumerable.Empty<RemoteImageInfo>();

            var cacheFile = Path.Combine(CacheDir, $"{loid}.json");
            string? thumbUrl = null;
            if (File.Exists(cacheFile))
            {
                try
                {
                    var json   = await File.ReadAllTextAsync(cacheFile, cancellationToken).ConfigureAwait(false);
                    var cached = JsonSerializer.Deserialize<ScrapedCart>(json);
                    thumbUrl   = cached?.ThumbUrl;
                }
                catch { }
            }

            return new[]
            {
                new RemoteImageInfo
                {
                    ProviderName = ProviderName,
                    Type         = ImageType.Primary,
                    Url          = thumbUrl ?? BuildCartImageUrl(loid),
                }
            };
        }
    }

    public class LexaloffleExternalId : IExternalId
    {
        public string ProviderName      => BaseLexaloffleProvider.ProviderName;
        public string Key               => BaseLexaloffleProvider.ProviderId;
        public ExternalIdMediaType? Type => null;
        public string UrlFormatString   => "https://www.lexaloffle.com/bbs/?pid={0}";
        public bool Supports(IHasProviderIds item)
            => item is Book b && RomExtensions.IsPico8Path(b.Path);
    }

    public class LexaloffleExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => BaseLexaloffleProvider.ProviderName;

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (item is Book && item.TryGetProviderId(BaseLexaloffleProvider.ProviderId, out var loid))
                yield return $"https://www.lexaloffle.com/bbs/?pid={loid}";
        }
    }
}