using JellyEmu.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Xunit;

namespace JellyEmu.Tests
{
    public class WikipediaProviderTests
    {
        [Fact]
        public void WikipediaGameExternalId_Properties_ShouldMatchConventions()
        {
            var externalId = new WikipediaGameExternalId();

            Assert.Equal("Wikipedia", externalId.ProviderName);
            Assert.Equal("Wikipedia", externalId.Key);
            Assert.Equal("https://en.wikipedia.org/?curid={0}", externalId.UrlFormatString);
            Assert.Null(externalId.Type);
        }

        [Fact]
        public void WikipediaGameExternalId_Supports_ShouldSupportBookAndBookInfo()
        {
            var externalId = new WikipediaGameExternalId();

            Assert.True(externalId.Supports(new Book()));
            Assert.True(externalId.Supports(new BookInfo()));
        }

        [Fact]
        public void WikipediaExternalUrlProvider_ShouldGenerateUrl_WhenProviderIdPresent()
        {
            var urlProvider = new WikipediaExternalUrlProvider();
            var book = new Book { Path = "/games/nds/New Super Mario Bros. (USA).nds" };
            book.SetProviderId("Wikipedia", "1838125");

            var urls = urlProvider.GetExternalUrls(book).ToList();

            Assert.Single(urls);
            Assert.Equal("https://en.wikipedia.org/?curid=1838125", urls[0]);
        }

        [Fact]
        public void WikipediaImageProvider_Properties_ShouldBeValid()
        {
            var imageProvider = new WikipediaImageProvider(null!, null!);

            Assert.Equal("Wikipedia Image Provider", imageProvider.Name);
            Assert.Equal(3, imageProvider.Order);
            Assert.Contains(ImageType.Primary, imageProvider.GetSupportedImages(new Book()));
        }
    }
}
