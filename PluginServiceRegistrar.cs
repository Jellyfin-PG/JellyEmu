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
                    "JellyEmu/1.0 (Jellyfin plugin; +https://github.com/Jellyfin-PG/JellyEmu)");
                client.Timeout = TimeSpan.FromMinutes(10); // data.zip can be large
            });

            serviceCollection.AddSingleton<PlatformResolver>();

            serviceCollection.AddSingleton<JellyEmuEjsManager>();

            serviceCollection.AddHostedService<JellyEmuInjectorService>();
        }
    }
}
