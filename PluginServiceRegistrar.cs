using JellyEmu.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace JellyEmu
{
    public class PluginServiceRegistrar : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddHttpClient("JellyEmuEjs", client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"{JellyEmuVersion.UserAgent} (Jellyfin plugin; +https://github.com/Jellyfin-PG/JellyEmu)");
                client.Timeout = TimeSpan.FromMinutes(10);
            });

            serviceCollection.AddHttpClient("JellyEmuPico8", client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    JellyEmuVersion.BrowserUserAgent);
                client.Timeout = TimeSpan.FromMinutes(5);
            });

            serviceCollection.AddSingleton<PlatformResolver>();

            serviceCollection.AddSingleton<JellyEmuEjsManager>();

            serviceCollection.AddSingleton<JellyEmuPico8Manager>();

            serviceCollection.AddSingleton<JellyEmuThreeJsManager>();

            serviceCollection.AddSingleton<JellyEmuSessionService>();
            serviceCollection.AddSingleton<IgdbClientService>();
            serviceCollection.AddSingleton<MarketplaceService>();
            serviceCollection.AddSingleton<JellyEmuBiosService>();
            serviceCollection.AddSingleton<JellyEmuPreferenceService>();
            serviceCollection.AddSingleton<JellyEmuInputService>();
            serviceCollection.AddMemoryCache();
            serviceCollection.AddSingleton<JellyEmuCacheService>();
            serviceCollection.AddSingleton<JellyEmuFileService>();
            serviceCollection.AddSingleton<ScreenScraperService>();

            serviceCollection.AddHostedService<JellyEmuInjectorService>();
        }
    }
}