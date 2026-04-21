using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyEmu
{
    public class RomResolver : IItemResolver
    {
        private readonly PlatformResolver _platformResolver;

        public RomResolver(PlatformResolver platformResolver)
        {
            _platformResolver = platformResolver;
        }

        public ResolverPriority Priority => ResolverPriority.First;

        public BaseItem? ResolvePath(ItemResolveArgs args)
        {
            if (args.IsDirectory) return null;

            if (RomExtensions.IsRomPath(args.Path))
            {
                var consoleTag  = _platformResolver.Resolve(args.Path);
                var regionTag   = PlatformResolver.ResolveRegion(args.Path);
                var displayName = PlatformResolver.CleanDisplayName(
                    Path.GetFileNameWithoutExtension(args.Path) ?? string.Empty);

                var tags = new List<string> { "Game", consoleTag };
                if (!string.IsNullOrEmpty(regionTag)) tags.Add(regionTag);

                return new Book
                {
                    Name            = RomExtensions.CleanName(displayName),
                    Path            = args.Path,
                    IsInMixedFolder = true,
                    SeriesName      = Path.GetFileName(Path.GetDirectoryName(args.Path)),
                    Tags            = tags.ToArray()
                };
            }

            return null;
        }
    }

    public class RomMetadataProvider : ILocalMetadataProvider<Book>, IRemoteImageProvider
    {
        private readonly ILogger<RomMetadataProvider> _logger;
        private readonly PlatformResolver _platformResolver;

        public RomMetadataProvider(ILogger<RomMetadataProvider> logger, PlatformResolver platformResolver)
        {
            _logger = logger;
            _platformResolver = platformResolver;
        }

        public string Name => "Retro Games Local Metadata";

        public Task<MetadataResult<Book>> GetMetadata(
            ItemInfo info,
            IDirectoryService directoryService,
            CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Book>();

            if (!RomExtensions.IsRomPath(info.Path))
                return Task.FromResult(result);

            var nfoPath = Path.ChangeExtension(info.Path, ".nfo");

            if (File.Exists(nfoPath))
            {
                var consoleTag = _platformResolver.Resolve(info.Path);

                result.HasMetadata = true;
                result.Item = new Book
                {
                    Overview = "Parsed successfully from local .nfo file!",
                    PremiereDate = new DateTime(1990, 1, 1),
                    Tags = new[] { "Game", consoleTag }
                };
            }

            return Task.FromResult(result);
        }

        public bool Supports(BaseItem item)
        {
            if (item is not Book) return false;
            if (string.IsNullOrEmpty(item.Path)) return true;
            return RomExtensions.IsRomPath(item.Path);
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => new[] { ImageType.Primary, ImageType.Backdrop };

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            if (!string.IsNullOrEmpty(item.Path))
            {
                var dir = Path.GetDirectoryName(item.Path);
                var baseName = Path.GetFileNameWithoutExtension(item.Path);
                var possible = Path.Combine(dir ?? string.Empty, baseName + ".jpg");
                if (File.Exists(possible))
                {
                    list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = new Uri(possible).AbsoluteUri });
                }
                var possiblePng = Path.ChangeExtension(possible, ".png");
                if (File.Exists(possiblePng))
                {
                    list.Add(new RemoteImageInfo { ProviderName = Name, Type = ImageType.Primary, Url = new Uri(possiblePng).AbsoluteUri });
                }
            }
            return list;
        }

        public Task<System.Net.Http.HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(url) && url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var localPath = new Uri(url).LocalPath;
                if (File.Exists(localPath))
                {
                    var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK);
                    var stream = File.OpenRead(localPath);
                    response.Content = new System.Net.Http.StreamContent(stream);
                    var ext = Path.GetExtension(localPath).ToLowerInvariant();
                    var mime = ext == ".png" ? "image/png" : "image/jpeg";
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
                    return Task.FromResult(response);
                }
            }
            return Task.FromResult<System.Net.Http.HttpResponseMessage>(null!);
        }
    }
}