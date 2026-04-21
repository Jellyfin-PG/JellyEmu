using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyEmu
{
    /// <summary>
    /// Runs when the Jellyfin server starts up.
    ///
    /// IMPORTANT: We do NOT manually register providers here anymore.
    /// Jellyfin 10.9+ auto-discovers any public class in the plugin assembly
    /// that implements IRemoteMetadataProvider&lt;T,TLookup&gt;, IRemoteImageProvider,
    /// ILocalMetadataProvider&lt;T&gt;, IItemResolver, etc., and wires them up via DI.
    ///
    /// Calling _providerManager.AddParts(...) on top of that causes double
    /// registration — and worse, instances passed as IImageProvider[] don't
    /// always get surfaced in the library "Image fetchers" UI list because
    /// the UI enumerates DI-discovered IRemoteImageProvider instances
    /// specifically. Removing the manual registration is what makes the
    /// image providers show up under Manage Library → your library →
    /// Image fetchers.
    /// </summary>
    public class ServerEntryPoint : IHostedService
    {
        private readonly ILogger<ServerEntryPoint> _logger;

        public ServerEntryPoint(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ServerEntryPoint>();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "JellyEmu: Server entry point initialized. " +
                "Providers are auto-discovered by Jellyfin's DI container.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}