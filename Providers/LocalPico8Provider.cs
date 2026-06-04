using System.IO.Compression;
using System.Xml.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Providers
{
    /// <summary>
    /// Local metadata and image provider for PICO-8 carts.
    ///
    /// Supported formats:
    ///   .p8       — plain text cart
    ///   .p8.png   — cartridge image (used by itch.io exports and BBS)
    ///   .zip      — multi-cart bundle or itch.io HTML export; must be tagged "PICO-8"
    ///               to disambiguate from other zip-based ROMs
    ///
    /// Sidecar files (placed next to the cart file, same base name):
    ///   .nfo      — standard NFO XML (same schema as RomLocalProvider)
    ///   .jpg/.png — box art / cover image
    /// </summary>
    public class LocalPico8Provider : ILocalMetadataProvider<Book>, IRemoteImageProvider
    {
        private readonly ILogger<LocalPico8Provider> _logger;

        public LocalPico8Provider(ILogger<LocalPico8Provider> logger)
        {
            _logger = logger;
        }

        public string Name => "Local PICO-8 Assets";

        public bool Supports(BaseItem item) => item is Book && RomExtensions.IsPico8Path(item.Path);

        /// <summary>
        /// For a .zip bundle, finds the first .p8.png or .p8 entry and returns
        /// its entry name, or null if none found.
        /// </summary>
        public static string? FindCartInZip(string zipPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var cart = zip.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith(".p8.png", StringComparison.OrdinalIgnoreCase) ||
                    e.FullName.EndsWith(".p8",     StringComparison.OrdinalIgnoreCase));
                return cart?.FullName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public Task<MetadataResult<Book>> GetMetadata(
            ItemInfo info,
            IDirectoryService directoryService,
            CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book>();

            if (!RomExtensions.IsPico8Path(info.Path))
                return Task.FromResult(result);

            var basePath = RomExtensions.EffectivePicoPath(info.Path);
            var dir      = Path.GetDirectoryName(info.Path) ?? string.Empty;
            var baseName = Path.GetFileName(basePath);

            var tags = new List<string> { "JellyEmu", "PICO-8", "Game" };

            if (info.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var cartEntry = FindCartInZip(info.Path);
                if (cartEntry == null)
                    return Task.FromResult(result);
            }

            var item = new Book { Tags = tags.ToArray() };

            var nfoPath = basePath + ".nfo";

            if (!File.Exists(nfoPath) && info.Path.EndsWith(".p8.png", StringComparison.OrdinalIgnoreCase))
                nfoPath = info.Path[..^4] + ".nfo";

            if (File.Exists(nfoPath))
            {
                try
                {
                    var doc  = XDocument.Load(nfoPath);
                    var root = doc.Root;
                    if (root != null)
                    {
                        item.Name     = root.Element("title")?.Value    ?? item.Name;
                        item.Overview = root.Element("plot")?.Value     ?? item.Overview;

                        if (DateTime.TryParse(
                            root.Element("premiered")?.Value ??
                            root.Element("releasedate")?.Value, out var date))
                        {
                            item.PremiereDate   = date;
                            item.ProductionYear = date.Year;
                        }

                        if (float.TryParse(root.Element("rating")?.Value, out var rating))
                            item.CommunityRating = rating;

                        if (float.TryParse(root.Element("criticrating")?.Value, out var criticRating))
                            item.CriticRating = criticRating;

                        item.OfficialRating = root.Element("esrb")?.Value ??
                                              root.Element("mpaa")?.Value ??
                                              item.OfficialRating;

                        item.SeriesName = root.Element("set")?.Value ??
                                          root.Element("series")?.Value ??
                                          item.SeriesName;

                        foreach (var genre in root.Elements("genre"))
                            if (!string.IsNullOrWhiteSpace(genre.Value))
                                item.AddGenre(genre.Value);

                        foreach (var dev in root.Elements("developer"))
                            if (!string.IsNullOrWhiteSpace(dev.Value))
                                item.AddStudio(dev.Value);

                        foreach (var pub in root.Elements("publisher"))
                            if (!string.IsNullOrWhiteSpace(pub.Value))
                                item.AddStudio(pub.Value);

                        void ParsePerson(XElement node, string defaultRole)
                        {
                            var name = node.Element("name")?.Value ?? node.Value;
                            if (string.IsNullOrWhiteSpace(name)) return;
                            var role = node.Element("role")?.Value ?? defaultRole;
                            var p    = new PersonInfo { Name = name.Trim(), Type = PersonKind.Author, Role = role };
                            var thumb = node.Element("thumb")?.Value;
                            if (!string.IsNullOrWhiteSpace(thumb)) p.ImageUrl = thumb;
                            result.AddPerson(p);
                        }

                        foreach (var actor    in root.Elements("actor"))    ParsePerson(actor,    "Actor");
                        foreach (var director in root.Elements("director")) ParsePerson(director, "Director");
                        foreach (var credits  in root.Elements("credits"))  ParsePerson(credits,  "Writer/Credits");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[JellyEmu] Failed to parse PICO-8 NFO at {Path}", nfoPath);
                    item.Overview = "Failed to parse .nfo XML. Check logs.";
                }
            }

            // For .p8.png carts, try to extract the label image embedded in the PNG
            // as a fallback cover if no sidecar image exists
            if (info.Path.EndsWith(".p8.png", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("[JellyEmu] PICO-8 cartridge image detected: {Path}", info.Path);
                // The label is embedded in the PNG as pixel data — extraction would
                // require decoding the PNG and reading the top 128x128 label region.
                // Left as a future enhancement; sidecar .jpg/.png covers take priority.
            }

            result.Item        = item;
            result.HasMetadata = true;
            return Task.FromResult(result);
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) =>
            new[] { ImageType.Primary, ImageType.Backdrop };

        public Task<IEnumerable<RemoteImageInfo>> GetImages(
            BaseItem item,
            CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();

            if (string.IsNullOrEmpty(item.Path) || !RomExtensions.IsPico8Path(item.Path))
                return Task.FromResult<IEnumerable<RemoteImageInfo>>(list);

            var basePath = RomExtensions.EffectivePicoPath(item.Path);

            foreach (var ext in new[] { ".jpg", ".png" })
            {
                var candidate = basePath + ext;
                if (File.Exists(candidate))
                {
                    list.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Type         = ImageType.Primary,
                        Url          = new Uri(candidate).AbsoluteUri,
                    });
                    break;
                }
            }

            if (item.Path.EndsWith(".p8.png", StringComparison.OrdinalIgnoreCase))
            {
                var p8jpg = item.Path[..^4] + ".jpg";
                if (File.Exists(p8jpg) && list.Count == 0)
                    list.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Type         = ImageType.Primary,
                        Url          = new Uri(p8jpg).AbsoluteUri,
                    });
            }

            return Task.FromResult<IEnumerable<RemoteImageInfo>>(list);
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(url) &&
                url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var localPath = new Uri(url).LocalPath;
                if (File.Exists(localPath))
                {
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                    var stream   = File.OpenRead(localPath);
                    response.Content = new StreamContent(stream);
                    var mime = Path.GetExtension(localPath).ToLowerInvariant() == ".png"
                        ? "image/png" : "image/jpeg";
                    response.Content.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
                    return Task.FromResult(response);
                }
            }
            return Task.FromResult<HttpResponseMessage>(null!);
        }
    }
}