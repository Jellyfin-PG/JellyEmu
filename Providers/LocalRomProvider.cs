using System.Xml.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Providers
{
    public class RomLocalProvider : ILocalMetadataProvider<Book>, IRemoteImageProvider
    {
        private readonly ILogger<RomLocalProvider> _logger;
        private readonly PlatformResolver _platformResolver;

        public RomLocalProvider(ILogger<RomLocalProvider> logger, PlatformResolver platformResolver)
        {
            _logger = logger;
            _platformResolver = platformResolver;
        }

        public string Name => "Local Rom Assets";

        public Task<MetadataResult<Book>> GetMetadata(ItemInfo info, IDirectoryService directoryService, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book>();

            if (!string.IsNullOrEmpty(info.Path) && !RomExtensions.IsRomPath(info.Path))
                return Task.FromResult(result);

            if (!string.IsNullOrEmpty(info.Path))
            {
                var nfoPath = Path.ChangeExtension(info.Path, ".nfo");
                if (File.Exists(nfoPath))
                {
                    var nfoTags = new List<string> { "JellyEmu", "Game", _platformResolver.Resolve(RomExtensions.EffectiveRomPath(info.Path)) };
                    var nfoRegion = PlatformResolver.ResolveRegion(RomExtensions.EffectiveRomPath(info.Path));
                    var nfoDisc = PlatformResolver.ResolveDisc(RomExtensions.EffectiveRomPath(info.Path));
                    if (!string.IsNullOrEmpty(nfoRegion)) nfoTags.Add(nfoRegion);
                    if (!string.IsNullOrEmpty(nfoDisc)) nfoTags.Add(nfoDisc);

                    var item = new Book { Tags = nfoTags.ToArray() };

                    try
                    {
                        var doc = XDocument.Load(nfoPath);
                        var root = doc.Root;
                        if (root != null)
                        {
                            item.Name = root.Element("title")?.Value ?? item.Name;
                            item.Overview = root.Element("plot")?.Value ?? item.Overview;

                            if (DateTime.TryParse(root.Element("premiered")?.Value ?? root.Element("releasedate")?.Value, out var date))
                            {
                                item.PremiereDate = date;
                                item.ProductionYear = date.Year;
                            }

                            if (float.TryParse(root.Element("rating")?.Value, out var rating))
                                item.CommunityRating = rating;

                            if (float.TryParse(root.Element("criticrating")?.Value, out var criticRating))
                                item.CriticRating = criticRating;

                            item.OfficialRating = root.Element("esrb")?.Value ?? root.Element("mpaa")?.Value ?? item.OfficialRating;
                            item.SeriesName = root.Element("set")?.Value ?? root.Element("series")?.Value ?? item.SeriesName;

                            foreach (var genre in root.Elements("genre"))
                                if (!string.IsNullOrWhiteSpace(genre.Value)) item.AddGenre(genre.Value);

                            foreach (var dev in root.Elements("developer"))
                                if (!string.IsNullOrWhiteSpace(dev.Value)) item.AddStudio(dev.Value);

                            foreach (var pub in root.Elements("publisher"))
                                if (!string.IsNullOrWhiteSpace(pub.Value)) item.AddStudio(pub.Value);

                            Action<XElement, string> parsePerson = (node, defaultRole) =>
                            {
                                var name = node.Element("name")?.Value ?? node.Value;
                                if (string.IsNullOrWhiteSpace(name)) return;
                                var role = node.Element("role")?.Value ?? defaultRole;
                                var p = new PersonInfo { Name = name.Trim(), Type = PersonKind.Author, Role = role };
                                var thumb = node.Element("thumb")?.Value;
                                if (!string.IsNullOrWhiteSpace(thumb)) p.ImageUrl = thumb;
                                result.AddPerson(p);
                            };

                            foreach (var actor in root.Elements("actor")) parsePerson(actor, "Actor");
                            foreach (var director in root.Elements("director")) parsePerson(director, "Director");
                            foreach (var credits in root.Elements("credits")) parsePerson(credits, "Writer/Credits");
                        }
                    }
                    catch
                    {
                        item.Overview = "Failed to parse .nfo XML. Check logs.";
                    }

                    result.Item = item;
                    result.HasMetadata = true;
                }
            }

            return Task.FromResult(result);
        }

        public bool Supports(BaseItem item) => item is Book && RomExtensions.IsRomPath(item.Path);

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path) && RomExtensions.IsRomPath(item.Path))
            {
                var effectivePath = RomExtensions.EffectiveRomPath(item.Path);
                var dir = Path.GetDirectoryName(effectivePath);
                var baseName = Path.GetFileNameWithoutExtension(effectivePath);
                var possible = Path.Combine(dir ?? string.Empty, baseName + ".jpg");
                if (File.Exists(possible))
                    list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = new Uri(possible).AbsoluteUri });

                var possiblePng = Path.ChangeExtension(possible, ".png");
                if (File.Exists(possiblePng))
                    list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = new Uri(possiblePng).AbsoluteUri });
            }
            return list;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(url) && url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var localPath = new Uri(url).LocalPath;
                if (File.Exists(localPath))
                {
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                    var stream = File.OpenRead(localPath);
                    response.Content = new StreamContent(stream);
                    var ext = Path.GetExtension(localPath).ToLowerInvariant();
                    var mime = ext == ".png" ? "image/png" : "image/jpeg";
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
                    return Task.FromResult(response);
                }
            }
            return Task.FromResult<HttpResponseMessage>(null!);
        }
    }
}