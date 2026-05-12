using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Resolvers;

namespace JellyEmu
{
    /// <summary>
    /// Tells Jellyfin how to identify ROM files and folders in the library scanner.
    /// </summary>
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
            if (args.IsDirectory)
            {
                try
                {
                    var cueFiles = Directory.GetFiles(args.Path, "*.cue");
                    if (cueFiles.Length == 1 && CueParser.HasResolvedBin(cueFiles[0]))
                    {
                        var cuePath     = cueFiles[0];
                        var binPath     = CueParser.GetFirstBinPath(cueFiles[0]);
                        var consoleTag  = _platformResolver.Resolve(cuePath);
                        var regionTag   = PlatformResolver.ResolveRegion(cuePath);
                        
                        var displayName = PlatformResolver.CleanDisplayName(
                            Path.GetFileName(args.Path));

                        var tags = new List<string> { "JellyEmu", consoleTag };
                        if (!string.IsNullOrEmpty(regionTag)) tags.Add(regionTag);

                        var parentFolder = Path.GetFileName(Path.GetDirectoryName(args.Path));
                        var seriesName   = (!string.IsNullOrEmpty(parentFolder) &&
                                            !PlatformResolver.Aliases.ContainsKey(parentFolder))
                                           ? parentFolder
                                           : null;

                        return new Book
                        {
                            Name            = RomExtensions.CleanName(displayName),
                            Path            = binPath,
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
                if (string.Equals(Path.GetExtension(args.Path), ".bin", StringComparison.OrdinalIgnoreCase))
                {
                    if (CueParser.IsReferencedByAnyCue(args.Path))
                        return null;
                }

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

                var tags = new List<string> { "JellyEmu", consoleTag };
                if (!string.IsNullOrEmpty(regionTag)) tags.Add(regionTag);

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

            if (RomExtensions.IsPico8Path(args.Path))
            {
                var displayName = args.Path.EndsWith(".p8.png", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(args.Path))
                    : Path.GetFileNameWithoutExtension(args.Path);

                displayName = PlatformResolver.CleanDisplayName(displayName ?? string.Empty);

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
                    Tags            = new[] { "JellyEmu", "PICO-8" }
                };
            }

            return null;
        }
    }
}