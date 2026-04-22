using JellyEmu.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
                client.Timeout = TimeSpan.FromMinutes(10);
            });

            serviceCollection.AddSingleton<PlatformResolver>();

            serviceCollection.AddSingleton<JellyEmuEjsManager>();

            serviceCollection.AddSingleton<JellyEmuSessionService>();

            serviceCollection.AddHostedService<JellyEmuInjectorService>();
        }

        /// <summary>
        /// Injects COOP/COEP headers on every response so that SharedArrayBuffer
        /// is available both on the Jellyfin parent page and inside the JellyEmu
        /// iframe — without needing to open a popup.
        /// </summary>
        public void Configure(IApplicationBuilder app)
        {
            app.Use(async (context, next) =>
            {
                context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
                context.Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";
                await next();
            });
        }
    }
}