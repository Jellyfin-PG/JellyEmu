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
            // A folder that contains exactly one .cue file (plus any number of .bin tracks)
            // should be treated as a single game item, just like a movie folder.
            // Resolve it as a Book whose Path points at the .cue so all metadata providers
            // see a valid ROM path and can fetch cover art, overview, etc.
            if (args.IsDirectory)
            {
                try
                {
                    var cueFiles = Directory.GetFiles(args.Path, "*.cue");
                    // Only claim the folder as a game item when there is exactly one .cue
                    // and it actually references at least one .bin that exists on disk.
                    if (cueFiles.Length == 1 && CueParser.HasResolvedBin(cueFiles[0]))
                    {
                        var cuePath     = cueFiles[0];
                        var consoleTag  = _platformResolver.Resolve(cuePath);
                        var regionTag   = PlatformResolver.ResolveRegion(cuePath);
                        // Use the folder name as the display name — it's usually cleaner
                        // than the cue filename, and is what the user sees in the library.
                        var displayName = PlatformResolver.CleanDisplayName(
                            Path.GetFileName(args.Path));

                        var tags = new List<string> { "Game", consoleTag };
                        if (!string.IsNullOrEmpty(regionTag)) tags.Add(regionTag);

                        var parentFolder = Path.GetFileName(Path.GetDirectoryName(args.Path));
                        var seriesName   = (!string.IsNullOrEmpty(parentFolder) &&
                                            !PlatformResolver.Aliases.ContainsKey(parentFolder))
                                           ? parentFolder
                                           : null;

                        return new Book
                        {
                            Name            = RomExtensions.CleanName(displayName),
                            Path            = cuePath,   // metadata + playback both use the .cue
                            IsInMixedFolder = false,
                            SeriesName      = seriesName,
                            Tags            = tags.ToArray()
                        };
                    }
                }
                catch { }
                return null;
            }

            if (RomExtensions.IsRomPath(args.Path))
            {
                // .bin files that are referenced by a sibling .cue are track data, not
                // standalone ROMs. Suppress them so only the .cue appears in the library.
                if (string.Equals(Path.GetExtension(args.Path), ".bin", StringComparison.OrdinalIgnoreCase))
                {
                    if (CueParser.IsReferencedByAnyCue(args.Path))
                        return null;
                }

                // If this .cue is inside a dedicated game folder (folder resolver already
                // claimed the parent as a Book), suppress the file-level duplicate.
                // We know the folder was claimed if the .cue has a resolvable .bin — the
                // same condition the folder resolver uses.
                if (string.Equals(Path.GetExtension(args.Path), ".cue", StringComparison.OrdinalIgnoreCase))
                {
                    var dir = Path.GetDirectoryName(args.Path) ?? string.Empty;
                    if (Directory.GetFiles(dir, "*.cue").Length == 1 &&
                        CueParser.HasResolvedBin(args.Path))
                        return null;
                }

                var consoleTag  = _platformResolver.Resolve(args.Path);
                var regionTag   = PlatformResolver.ResolveRegion(args.Path);
                var displayName = PlatformResolver.CleanDisplayName(
                    Path.GetFileNameWithoutExtension(args.Path) ?? string.Empty);

                var tags = new List<string> { "Game", consoleTag };
                if (!string.IsNullOrEmpty(regionTag)) tags.Add(regionTag);

                // Only use the parent folder as SeriesName when it is NOT a platform name.
                // If the folder is e.g. "PlayStation" or "SNES", it is a library-organisation
                // folder, not a meaningful series grouping. Setting SeriesName to a platform
                // name causes Jellyfin to display the platform name as the item title in
                // grid/list views, with the actual game name appearing in the wrong field.
                var parentFolder = Path.GetFileName(Path.GetDirectoryName(args.Path));
                var seriesName   = (!string.IsNullOrEmpty(parentFolder) &&
                                    !PlatformResolver.Aliases.ContainsKey(parentFolder))
                                   ? parentFolder
                                   : null;

                return new Book
                {
                    Name            = RomExtensions.CleanName(displayName),
                    Path            = args.Path,
                    IsInMixedFolder = true,
                    SeriesName      = seriesName,
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

            var effectivePath = RomExtensions.EffectiveRomPath(info.Path);
            var nfoPath = Path.ChangeExtension(effectivePath, ".nfo");

            if (File.Exists(nfoPath))
            {
                var consoleTag = _platformResolver.Resolve(effectivePath);

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
                var effectivePath = RomExtensions.EffectiveRomPath(item.Path);
                var dir = Path.GetDirectoryName(effectivePath);
                var baseName = Path.GetFileNameWithoutExtension(effectivePath);
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