using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Controller.Providers;

namespace JellyEmu
{
    /// <summary>
    /// This is the main entry point for your plugin.
    /// Jellyfin scans the DLL for a class inheriting from BasePlugin.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override string Name => "JellyEmu";

        public override Guid Id => Guid.Parse("a1b2c3d4-e5f6-4a5b-8c9d-0e1f2a3b4c5d");

        public static Plugin? Instance { get; private set; }

        public IEnumerable<PluginPageInfo> GetPages() => new[]
        {
            new PluginPageInfo
            {
                Name                 = "JellyEmuConfigPage",
                DisplayName          = "Emulator Library",
                EnableInMainMenu     = true,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            }
        };
    }

    /// <summary>
    /// Stores the plugin's configuration settings.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// IGDB / Twitch Client ID. Get yours at https://dev.twitch.tv/console/apps
        /// </summary>
        public string IgdbClientId { get; set; } = string.Empty;

        /// <summary>
        /// IGDB / Twitch Client Secret.
        /// </summary>
        public string IgdbClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// RAWG API Key. Get yours at https://rawg.io/apidocs
        /// </summary>
        public string RawgApiKey { get; set; } = string.Empty;
    }
}