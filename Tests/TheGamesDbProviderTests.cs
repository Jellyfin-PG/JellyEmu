using JellyEmu.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Xunit;

namespace JellyEmu.Tests
{
    public class TheGamesDbProviderTests
    {
        [Theory]
        [InlineData("/games/snes/Super Mario World [tgdb-136].sfc", "136")]
        [InlineData("C:\\Roms\\GBA\\Pokemon Emerald [thegamesdb-4952].gba", "4952")]
        [InlineData("Zelda [TGDB-1002].nes", "1002")]
        [InlineData("Sonic [THEGAMESDB-789].bin", "789")]
        [InlineData("Chrono Trigger [rawg-123].sfc", null)]
        [InlineData("Super Mario 64.z64", null)]
        public void TryExtractEmbeddedTheGamesDbId_ShouldExtractValidIds(string path, string? expectedId)
        {
            var result = BaseTheGamesDbProvider.TryExtractEmbeddedTheGamesDbId(path);
            Assert.Equal(expectedId, result);
        }

        [Theory]
        [InlineData("NES", 7)]
        [InlineData("SNES", 6)]
        [InlineData("N64", 3)]
        [InlineData("Game Boy", 4)]
        [InlineData("Game Boy Color", 41)]
        [InlineData("Game Boy Advance", 5)]
        [InlineData("Nintendo DS", 8)]
        [InlineData("Virtual Boy", 4918)]
        [InlineData("Master System", 35)]
        [InlineData("Game Gear", 20)]
        [InlineData("Sega Genesis", 18)]
        [InlineData("Sega CD", 21)]
        [InlineData("Sega 32X", 22)]
        [InlineData("Sega Saturn", 17)]
        [InlineData("PlayStation", 10)]
        [InlineData("PSP", 13)]
        [InlineData("Atari 2600", 23)]
        [InlineData("Atari 7800", 25)]
        [InlineData("TurboGrafx-16", 33)]
        [InlineData("ColecoVision", 31)]
        [InlineData("NonExistentConsole", null)]
        public void ResolvePlatformId_ShouldMapKnownPlatforms(string platform, int? expectedId)
        {
            var result = BaseTheGamesDbProvider.ResolvePlatformId(platform);
            Assert.Equal(expectedId, result);
        }

        [Fact]
        public void ImageProvider_SupportedImages_ShouldIncludeCoreTypes()
        {
            var imageProvider = new TheGamesDbImageProvider(null!, null!);
            var supported = imageProvider.GetSupportedImages(new Book()).ToList();

            Assert.Contains(ImageType.Primary, supported);
            Assert.Contains(ImageType.Backdrop, supported);
            Assert.Contains(ImageType.BoxRear, supported);
            Assert.Contains(ImageType.Banner, supported);
            Assert.Contains(ImageType.Logo, supported);
        }

        [Fact]
        public void TheGamesDbGameExternalId_Properties_ShouldMatchConventions()
        {
            var externalId = new TheGamesDbGameExternalId();

            Assert.Equal("TheGamesDB", externalId.ProviderName);
            Assert.Equal("TheGamesDB", externalId.Key);
            Assert.Equal("https://thegamesdb.net/game.php?id={0}", externalId.UrlFormatString);
            Assert.Null(externalId.Type);
        }

        [Fact]
        public void TheGamesDbExternalUrlProvider_ShouldGenerateUrl_WhenProviderIdPresent()
        {
            var urlProvider = new TheGamesDbExternalUrlProvider();
            var book = new Book { Path = "/games/snes/Super Mario World.sfc" };
            book.SetProviderId("TheGamesDB", "136");

            var urls = urlProvider.GetExternalUrls(book).ToList();

            Assert.Single(urls);
            Assert.Equal("https://thegamesdb.net/game.php?id=136", urls[0]);
        }

        [Fact]
        public void ExtractBoxartMap_ShouldExtractFrontBoxart_OriginalAndThumb()
        {
            var json = """
            {
              "include": {
                "boxart": {
                  "base_url": {
                    "original": "https://cdn.thegamesdb.net/images/original/",
                    "thumb": "https://cdn.thegamesdb.net/images/thumb/"
                  },
                  "data": {
                    "136": [
                      { "id": 1, "type": "boxart", "side": "back", "filename": "boxart/back/136-1.jpg" },
                      { "id": 2, "type": "boxart", "side": "front", "filename": "boxart/front/136-1.jpg" }
                    ]
                  }
                }
              }
            }
            """;

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var thumbMap = TheGamesDbMetadataProvider.ExtractBoxartMap(doc.RootElement, preferOriginal: false);
            var origMap = TheGamesDbMetadataProvider.ExtractBoxartMap(doc.RootElement, preferOriginal: true);

            Assert.Equal("https://cdn.thegamesdb.net/images/thumb/boxart/front/136-1.jpg", thumbMap["136"]);
            Assert.Equal("https://cdn.thegamesdb.net/images/original/boxart/front/136-1.jpg", origMap["136"]);
        }

        [Fact]
        public void ExtractBoxartUrls_ShouldExtractBothFrontAndBack()
        {
            var json = """
            {
              "include": {
                "boxart": {
                  "base_url": {
                    "original": "https://cdn.thegamesdb.net/images/original/",
                    "thumb": "https://cdn.thegamesdb.net/images/thumb/"
                  },
                  "data": {
                    "6859": [
                      { "id": 82968, "type": "boxart", "side": "back", "filename": "boxart/back/6859-1.jpg" },
                      { "id": 83190, "type": "boxart", "side": "front", "filename": "boxart/front/6859-1.jpg" }
                    ]
                  }
                }
              }
            }
            """;

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var (front, back) = TheGamesDbMetadataProvider.ExtractBoxartUrls(doc.RootElement, "6859", preferOriginal: true);

            Assert.Equal("https://cdn.thegamesdb.net/images/original/boxart/front/6859-1.jpg", front);
            Assert.Equal("https://cdn.thegamesdb.net/images/original/boxart/back/6859-1.jpg", back);
        }

        [Fact]
        public void Providers_ShouldHaveOrderOne()
        {
            var meta = new TheGamesDbMetadataProvider(null!, null!, null!);
            var img = new TheGamesDbImageProvider(null!, null!);

            Assert.Equal(1, meta.Order);
            Assert.Equal(1, img.Order);
        }
    }
}
