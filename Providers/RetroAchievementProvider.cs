using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace JellyEmu.Providers
{
    public class RetroAchievementGameExternalId : IExternalId
    {
        public string ProviderName => "RetroAchievements";
        public string Key => "RetroAchievements";
        public ExternalIdMediaType? Type => null;
        public string UrlFormatString => "https://retroachievements.org/game/{0}";
        public bool Supports(IHasProviderIds item) => item is Book && RomExtensions.IsRomPath((item as BaseItem)?.Path);
    }

    public class RetroAchievementExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "RetroAchievements";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (item.TryGetProviderId("RetroAchievements", out var id))
                yield return $"https://retroachievements.org/game/{id}";
        }
    }
}