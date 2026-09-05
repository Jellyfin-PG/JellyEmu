using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using JellyEmu.Providers;
using JellyEmu.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Xunit;

namespace JellyEmu.Tests
{
    public class ScreenScraperProviderTests
    {
        [Theory]
        [InlineData("/games/snes/Super Mario World [ss-136].sfc", "136")]
        [InlineData("C:\\Roms\\GBA\\Pokemon Emerald [screenscraper-4952].gba", "4952")]
        [InlineData("Akumajou Dracula [SS-1002].bin", "1002")]
        [InlineData("Sonic [SCREENSCRAPER-789].bin", "789")]
        [InlineData("Chrono Trigger [rawg-123].sfc", null)]
        [InlineData("Super Mario 64 [tgdb-456].z64", null)]
        [InlineData("Super Mario 64.z64", null)]
        public void TryExtractEmbeddedScreenScraperId_ShouldExtractValidIds(string path, string? expectedId)
        {
            var result = BaseScreenScraperProvider.TryExtractEmbeddedScreenScraperId(path);
            Assert.Equal(expectedId, result);
        }

        [Theory]
        [InlineData("NES", 3)]
        [InlineData("Famicom", 3)]
        [InlineData("SNES", 4)]
        [InlineData("Super Famicom", 4)]
        [InlineData("N64", 5)]
        [InlineData("Nintendo 64", 5)]
        [InlineData("GameCube", 13)]
        [InlineData("Wii", 14)]
        [InlineData("Wii U", 18)]
        [InlineData("Nintendo Switch", 225)]
        [InlineData("Game Boy", 9)]
        [InlineData("Game Boy Color", 10)]
        [InlineData("Game Boy Advance", 12)]
        [InlineData("Nintendo DS", 15)]
        [InlineData("Nintendo 3DS", 17)]
        [InlineData("PlayStation", 57)]
        [InlineData("PS1", 57)]
        [InlineData("PlayStation 2", 58)]
        [InlineData("PS2", 58)]
        [InlineData("PlayStation 3", 59)]
        [InlineData("PSP", 61)]
        [InlineData("PlayStation Vita", 62)]
        [InlineData("Sega Genesis", 1)]
        [InlineData("Mega Drive", 1)]
        [InlineData("Master System", 2)]
        [InlineData("Sega Saturn", 22)]
        [InlineData("Dreamcast", 23)]
        [InlineData("Atari 2600", 26)]
        [InlineData("TurboGrafx-16", 31)]
        [InlineData("PC Engine", 31)]
        [InlineData("Neo Geo", 142)]
        [InlineData("NonExistentConsole", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void ScreenScraperSystemMap_ShouldMapKnownPlatforms(string? platform, int? expectedId)
        {
            var result = ScreenScraperSystemMap.GetSystemId(platform);
            Assert.Equal(expectedId, result);
        }

        [Theory]
        [InlineData("Japan", "jp")]
        [InlineData("JPN", "jp")]
        [InlineData("jp", "jp")]
        [InlineData("Europe", "eu")]
        [InlineData("EUR", "eu")]
        [InlineData("eu", "eu")]
        [InlineData("USA", "us")]
        [InlineData("US", "us")]
        [InlineData("World", "wor")]
        [InlineData("wor", "wor")]
        [InlineData("France", "fr")]
        [InlineData("Germany", "de")]
        [InlineData("Spain", "es")]
        [InlineData("Italy", "it")]
        [InlineData("Brazil", "br")]
        [InlineData("Korea", "kr")]
        [InlineData("China", "cn")]
        [InlineData("Australia", "au")]
        [InlineData("Unknown", "us")]
        [InlineData("", "us")]
        [InlineData(null, "us")]
        public void MapRegionToCode_ShouldMapCorrectly(string? region, string expectedCode)
        {
            var result = BaseScreenScraperProvider.MapRegionToCode(region);
            Assert.Equal(expectedCode, result);
        }

        [Fact]
        public void ResolveEffectiveRegion_ShouldAutoDetectFromFilenameTags()
        {
            var jpPath = "/roms/ps1/Akumajou Dracula X - Gekka no Yasoukyoku (Japan).cue";
            var euPath = "/roms/snes/Terranigma (Europe).sfc";
            var usPath = "/roms/snes/Super Mario World (USA).sfc";
            var noTagPath = "/roms/snes/Super Mario World.sfc";

            Assert.Equal("jp", BaseScreenScraperProvider.ResolveEffectiveRegion(jpPath, "auto"));
            Assert.Equal("eu", BaseScreenScraperProvider.ResolveEffectiveRegion(euPath, "auto"));
            Assert.Equal("us", BaseScreenScraperProvider.ResolveEffectiveRegion(usPath, "auto"));
            Assert.Equal("us", BaseScreenScraperProvider.ResolveEffectiveRegion(noTagPath, "auto"));
        }

        [Fact]
        public void ResolveEffectiveRegion_ShouldRespectExplicitPreference()
        {
            var jpPath = "/roms/ps1/Akumajou Dracula X - Gekka no Yasoukyoku (Japan).cue";

            Assert.Equal("eu", BaseScreenScraperProvider.ResolveEffectiveRegion(jpPath, "eu"));
            Assert.Equal("us", BaseScreenScraperProvider.ResolveEffectiveRegion(jpPath, "us"));
            Assert.Equal("jp", BaseScreenScraperProvider.ResolveEffectiveRegion(jpPath, "jp"));
            Assert.Equal("wor", BaseScreenScraperProvider.ResolveEffectiveRegion(jpPath, "wor"));
        }

        [Fact]
        public void ExtractLocalizedTitle_ShouldPrioritizeTargetRegion()
        {
            var json = """
            {
              "nom": "Castlevania: Symphony of the Night",
              "noms": [
                { "region": "wor", "text": "Castlevania - Symphony of the Night" },
                { "region": "us", "text": "Castlevania: Symphony of the Night" },
                { "region": "jp", "text": "Akumajou Dracula X: Gekka no Yasoukyoku" },
                { "region": "eu", "text": "Castlevania: Symphony of the Night (Europe)" }
              ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            var titleJp = BaseScreenScraperProvider.ExtractLocalizedTitle(doc.RootElement, "jp");
            var titleEu = BaseScreenScraperProvider.ExtractLocalizedTitle(doc.RootElement, "eu");
            var titleUs = BaseScreenScraperProvider.ExtractLocalizedTitle(doc.RootElement, "us");

            Assert.Equal("Akumajou Dracula X: Gekka no Yasoukyoku", titleJp);
            Assert.Equal("Castlevania: Symphony of the Night (Europe)", titleEu);
            Assert.Equal("Castlevania: Symphony of the Night", titleUs);
        }

        [Fact]
        public void ExtractLocalizedTitle_ShouldFallbackWhenTargetMissing()
        {
            var json = """
            {
              "nom": "Default Name",
              "noms": [
                { "region": "us", "text": "Biohazard" }
              ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            // Requesting jp, fallback order for jp is: jp -> us -> wor -> eu
            var title = BaseScreenScraperProvider.ExtractLocalizedTitle(doc.RootElement, "jp");
            Assert.Equal("Biohazard", title);
        }

        [Fact]
        public void ExtractLocalizedTitle_ShouldFallbackToDirectNom_WhenNoNomsArray()
        {
            var json = """
            {
              "nom": "Chrono Trigger Direct"
            }
            """;

            using var doc = JsonDocument.Parse(json);
            var title = BaseScreenScraperProvider.ExtractLocalizedTitle(doc.RootElement, "jp");
            Assert.Equal("Chrono Trigger Direct", title);
        }

        [Fact]
        public void ExtractSynopsis_ShouldMatchLanguagePreference()
        {
            var json = """
            {
              "synopsis": [
                { "langue": "en", "text": "English synopsis of the game." },
                { "langue": "fr", "text": "Synopsis en francais du jeu." },
                { "langue": "ja", "text": "日本のあらすじ。" }
              ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            Assert.Equal("Synopsis en francais du jeu.", BaseScreenScraperProvider.ExtractSynopsis(doc.RootElement, "fr"));
            Assert.Equal("日本のあらすじ。", BaseScreenScraperProvider.ExtractSynopsis(doc.RootElement, "ja"));
            Assert.Equal("English synopsis of the game.", BaseScreenScraperProvider.ExtractSynopsis(doc.RootElement, "en"));
            // Missing language falls back to "en"
            Assert.Equal("English synopsis of the game.", BaseScreenScraperProvider.ExtractSynopsis(doc.RootElement, "de"));
        }

        [Fact]
        public void ExtractReleaseDate_ShouldExtractTargetRegionDate()
        {
            var json = """
            {
              "dates": [
                { "region": "jp", "text": "1997-03-20" },
                { "region": "us", "text": "1997-10-02" },
                { "region": "eu", "text": "1997-11-01" }
              ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            var dateJp = BaseScreenScraperProvider.ExtractReleaseDate(doc.RootElement, "jp");
            var dateUs = BaseScreenScraperProvider.ExtractReleaseDate(doc.RootElement, "us");

            Assert.NotNull(dateJp);
            Assert.Equal(new DateTime(1997, 3, 20), dateJp!.Value);

            Assert.NotNull(dateUs);
            Assert.Equal(new DateTime(1997, 10, 2), dateUs!.Value);
        }

        [Fact]
        public void ExtractMediaUrl_ShouldExtractCorrectTypeAndRegion()
        {
            var json = """
            {
              "medias": [
                { "type": "box-2d", "region": "us", "url": "https://media.screenscraper.fr/box2d_us.png" },
                { "type": "box-2d", "region": "jp", "url": "https://media.screenscraper.fr/box2d_jp.png" },
                { "type": "box-3d", "region": "us", "url": "https://media.screenscraper.fr/box3d_us.png" },
                { "type": "wheel", "region": "wor", "url": "https://media.screenscraper.fr/wheel_wor.png" },
                { "type": "fanart", "region": "wor", "url": "https://media.screenscraper.fr/fanart.jpg" }
              ]
            }
            """;

            using var doc = JsonDocument.Parse(json);

            var boxJp = BaseScreenScraperProvider.ExtractMediaUrl(doc.RootElement, "box-2d", "jp");
            var boxUs = BaseScreenScraperProvider.ExtractMediaUrl(doc.RootElement, "box-2d", "us");
            var wheel = BaseScreenScraperProvider.ExtractMediaUrl(doc.RootElement, "wheel", "us");
            var missing = BaseScreenScraperProvider.ExtractMediaUrl(doc.RootElement, "video", "us");

            Assert.Equal("https://media.screenscraper.fr/box2d_jp.png", boxJp);
            Assert.Equal("https://media.screenscraper.fr/box2d_us.png", boxUs);
            Assert.Equal("https://media.screenscraper.fr/wheel_wor.png", wheel);
            Assert.Null(missing);
        }

        [Fact]
        public void ComputeFastChecksums_ShouldComputeAccurateMd5AndCrc32()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                // Write known test string "123456789"
                // CRC32 of "123456789" (ASCII) = 0xcbf43926
                // MD5 of "123456789" = 25f9e794323b453885f5181f1b624d0b
                File.WriteAllBytes(tempFile, System.Text.Encoding.ASCII.GetBytes("123456789"));

                var (md5, crc, size) = BaseScreenScraperProvider.ComputeFastChecksums(tempFile);

                Assert.Equal("25f9e794323b453885f5181f1b624d0b", md5);
                Assert.Equal("cbf43926", crc);
                Assert.Equal(9, size);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void ScreenScraperImageProvider_SupportedImages_ShouldIncludeExpectedTypes()
        {
            var provider = new ScreenScraperImageProvider(null!, null!);
            var supported = provider.GetSupportedImages(new Book()).ToList();

            Assert.Contains(ImageType.Primary, supported);
            Assert.Contains(ImageType.Backdrop, supported);
            Assert.Contains(ImageType.Menu, supported);
        }

        [Fact]
        public void ScreenScraperExternalId_Properties_ShouldMatchConventions()
        {
            var externalId = new ScreenScraperExternalId();

            Assert.Equal("ScreenScraper", externalId.ProviderName);
            Assert.Equal("ScreenScraper", externalId.Key);
            Assert.Equal("https://www.screenscraper.fr/gameinfos.php?gameid={0}", externalId.UrlFormatString);
            Assert.Null(externalId.Type);
        }

        [Fact]
        public void ScreenScraperExternalUrlProvider_ShouldGenerateUrl_WhenProviderIdPresent()
        {
            var urlProvider = new ScreenScraperExternalUrlProvider();
            var book = new Book { Path = "/games/ps1/Castlevania.bin" };
            book.SetProviderId("ScreenScraper", "12345");

            var urls = urlProvider.GetExternalUrls(book).ToList();

            Assert.Single(urls);
            Assert.Equal("https://www.screenscraper.fr/gameinfos.php?gameid=12345", urls[0]);
        }

        [Fact]
        public void ScreenScraperExternalUrlProvider_ShouldSkipWindowsRoms()
        {
            var urlProvider = new ScreenScraperExternalUrlProvider();
            var book = new Book { Path = "C:\\Games\\Doom\\Doom.exe" };
            book.SetProviderId("ScreenScraper", "12345");

            var urls = urlProvider.GetExternalUrls(book).ToList();

            Assert.Empty(urls);
        }

        [Fact]
        public void ExtractMediaUrl_ManuelAndMap_ShouldExtractCorrectUrls()
        {
            var json = """
            {
                "medias": [
                    { "type": "box-2d", "region": "us", "url": "https://screenscraper.fr/box2d.png" },
                    { "type": "manuel(pdf)", "region": "us", "url": "https://screenscraper.fr/manual_us.pdf" },
                    { "type": "manuel-pdf", "region": "wor", "url": "https://screenscraper.fr/manual_wor.pdf" },
                    { "type": "map", "region": "us", "url": "https://screenscraper.fr/map_us.png" },
                    { "type": "video-normalized", "region": "wor", "url": "https://screenscraper.fr/video.mp4" }
                ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var manualUrl = BaseScreenScraperProvider.ExtractMediaUrl(root, "manuel", "us");
            var mapUrl = BaseScreenScraperProvider.ExtractMediaUrl(root, "map", "us");
            var videoUrl = BaseScreenScraperProvider.ExtractMediaUrl(root, "video-normalized", "us");

            Assert.Equal("https://screenscraper.fr/manual_us.pdf", manualUrl);
            Assert.Equal("https://screenscraper.fr/map_us.png", mapUrl);
            Assert.Equal("https://screenscraper.fr/video.mp4", videoUrl);
        }

        [Fact]
        public void ScreenScraperService_ParseGuideDetails_ShouldPopulateAllFields()
        {
            var json = """
            {
                "id": "14210",
                "noms": [
                    { "region": "us", "text": "Spyro: Year of the Dragon" }
                ],
                "synopsis": [
                    { "langue": "en", "text": "Spyro travels to the Forgotten Realms to rescue dragon eggs." }
                ],
                "developpeur": { "text": "Insomniac Games" },
                "editeur": { "text": "Sony Computer Entertainment" },
                "dates": [
                    { "region": "us", "text": "2000-10-24" }
                ],
                "note": { "text": "18.0" },
                "genres": [
                    {
                        "noms": [
                            { "langue": "en", "text": "Platformer" }
                        ]
                    }
                ],
                "medias": [
                    { "type": "manuel(pdf)", "region": "us", "url": "https://screenscraper.fr/spyro_manual.pdf" },
                    { "type": "map", "region": "us", "url": "https://screenscraper.fr/spyro_map.png" },
                    { "type": "video-normalized", "region": "wor", "url": "https://screenscraper.fr/spyro.mp4" },
                    { "type": "box-2d", "region": "us", "url": "https://screenscraper.fr/spyro_box.png" },
                    { "type": "wheel", "region": "us", "url": "https://screenscraper.fr/spyro_wheel.png" }
                ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            var jeu = doc.RootElement;

            var details = Services.ScreenScraperService.ParseGuideDetails(jeu, "14210", "us", "en");

            Assert.NotNull(details);
            Assert.Equal("14210", details!.GameId);
            Assert.Equal("Spyro: Year of the Dragon", details.Title);
            Assert.Equal("https://www.screenscraper.fr/gameinfos.php?gameid=14210&action=onglet&zone=gameinfostips", details.GuideUrl);
            Assert.Equal("https://screenscraper.fr/spyro_manual.pdf", details.ManualUrl);
            Assert.Equal("https://screenscraper.fr/spyro_map.png", details.MapUrl);
            Assert.Equal("https://screenscraper.fr/spyro.mp4", details.VideoUrl);
            Assert.Equal("https://screenscraper.fr/spyro_box.png", details.BoxArtUrl);
            Assert.Equal("https://screenscraper.fr/spyro_wheel.png", details.WheelUrl);
            Assert.Contains("rescue dragon eggs", details.Overview);
            Assert.Equal("Insomniac Games", details.Developer);
            Assert.Equal("Sony Computer Entertainment", details.Publisher);
            Assert.Equal(2000, details.ReleaseDate?.Year);
            Assert.Equal(9.0f, details.Rating);
            Assert.Contains("Platformer", details.Genres);
        }

        [Fact]
        public void TryGetLocalManualPath_ReturnsSameNamePdf_WhenExists()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var romPath = Path.Combine(tempDir, "Super Mario World.sfc");
                var pdfPath = Path.Combine(tempDir, "Super Mario World.pdf");
                File.WriteAllText(romPath, "dummy rom");
                File.WriteAllText(pdfPath, "dummy pdf");

                var result = Controllers.JellyEmuMetaController.TryGetLocalManualPath(romPath);
                Assert.Equal(pdfPath, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void TryGetLocalManualPath_ReturnsSameNameUpperPdf_WhenExists()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var romPath = Path.Combine(tempDir, "Pokemon Yellow.gbc");
                var pdfPath = Path.Combine(tempDir, "Pokemon Yellow.PDF");
                File.WriteAllText(romPath, "dummy rom");
                File.WriteAllText(pdfPath, "dummy pdf");

                var result = Controllers.JellyEmuMetaController.TryGetLocalManualPath(romPath);
                Assert.NotNull(result);
                Assert.Equal(pdfPath, result, ignoreCase: true);
                Assert.True(File.Exists(result));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void TryGetLocalManualPath_ReturnsGenericManualPdf_WhenSameNameMissing()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var romPath = Path.Combine(tempDir, "Zelda.nes");
                var manualPdf = Path.Combine(tempDir, "manual.pdf");
                File.WriteAllText(romPath, "dummy rom");
                File.WriteAllText(manualPdf, "dummy manual");

                var result = Controllers.JellyEmuMetaController.TryGetLocalManualPath(romPath);
                Assert.Equal(manualPdf, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void TryGetLocalManualPath_ReturnsNull_WhenNoManualExists()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var romPath = Path.Combine(tempDir, "Sonic.bin");
                File.WriteAllText(romPath, "dummy rom");

                var result = Controllers.JellyEmuMetaController.TryGetLocalManualPath(romPath);
                Assert.Null(result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void TryGetLocalManualPath_ReturnsNull_ForNullOrEmpty()
        {
            Assert.Null(Controllers.JellyEmuMetaController.TryGetLocalManualPath(null));
            Assert.Null(Controllers.JellyEmuMetaController.TryGetLocalManualPath(""));
            Assert.Null(Controllers.JellyEmuMetaController.TryGetLocalManualPath("   "));
            Assert.Null(Services.JellyEmuFileService.TryGetLocalManualPath(null));
        }

        [Fact]
        public void JellyEmuFileService_TryGetLocalManualPath_ResolvesVariants()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var romPath = Path.Combine(tempDir, "Crash Bandicoot.iso");
                var dashManual = Path.Combine(tempDir, "Crash Bandicoot-manual.pdf");
                File.WriteAllText(romPath, "dummy rom");
                File.WriteAllText(dashManual, "dummy dash manual");

                var result = Services.JellyEmuFileService.TryGetLocalManualPath(romPath);
                Assert.Equal(dashManual, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
