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

        public override Guid Id => Guid.Parse("9bab105e-9af0-4e25-a87d-876713b60962");

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

        public string NetplayServer { get; set; } = string.Empty;

        /// <summary>
        /// Whether the Romm metadata provider is enabled. Disabled by default.
        /// </summary>
        public bool RommEnabled { get; set; } = false;

        /// <summary>
        /// Base URL of the Romm instance, e.g. https://romm.example.com
        /// </summary>
        public string RommInstanceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Romm username for Basic authentication.
        /// </summary>
        public string RommUsername { get; set; } = string.Empty;

        /// <summary>
        /// Romm password for Basic authentication.
        /// </summary>
        public string RommPassword { get; set; } = string.Empty;

        /// <summary>
        /// Whether to sync save states with Romm. Pull if Romm is newer on load; push on save.
        /// </summary>
        public bool RommSaveSyncEnabled { get; set; } = true;

        /// <summary>
        /// Whether to report session playtime to Romm after each play session ends.
        /// </summary>
        public bool RommPlaytimeReportEnabled { get; set; } = true;

        /// <summary>
        /// Whether to synchronise Romm collections into Jellyfin playlists/collections.
        /// </summary>
        public bool RommCollectionSyncEnabled { get; set; } = true;

        /// <summary>
        /// Whether to push EmulatorJS screenshots to Romm.
        /// </summary>
        public bool RommScreenshotPushEnabled { get; set; } = true;
    }
}