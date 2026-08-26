using System;
using System.Collections.Generic;
using System.IO;
using JellyEmu.Controllers;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyEmu.Tests
{
    [NonController]
    public class CoreResolutionTestController : JellyEmuBaseController
    {
        public CoreResolutionTestController(IApplicationPaths appPaths)
            : base(
                null!,
                appPaths,
                NullLogger<CoreResolutionTestController>.Instance,
                null!,
                null!,
                null!)
        {
        }

        [NonAction]
        public string TestResolveCore(BaseItem item, string? userId = null, string? queryCoreOverride = null)
        {
            return ResolveCore(item, userId, queryCoreOverride);
        }

        [NonAction]
        public CoreInfo TestResolveCoreInfo(BaseItem item, string? userId = null, string? queryCoreOverride = null)
        {
            return ResolveCoreInfo(item, userId, queryCoreOverride);
        }

        [NonAction]
        public List<CoreOption> TestGetAvailableCores(BaseItem item)
        {
            return GetAvailableCoresForItem(item);
        }

        [NonAction]
        public void TestSaveUserPrefs(string userId, UserFullPrefs prefs)
        {
            PreferenceService.SetPreferencesAsync(userId, "global", "", new Dictionary<string, string?>
            {
                ["scale"] = prefs.Scale,
                ["mute"] = prefs.Mute,
                ["controller"] = prefs.Controller,
                ["haptics"] = prefs.Haptics,
                ["autosave"] = prefs.Autosave,
                ["shader"] = prefs.Shader,
                ["videoRotation"] = prefs.VideoRotation.ToString(),
                ["controls"] = prefs.Controls,
                ["controllerControls"] = prefs.ControllerControls,
                ["raUsername"] = prefs.RaUsername,
                ["raApiKey"] = prefs.RaApiKey,
                ["virtualGamepad"] = prefs.VirtualGamepad,
                ["virtualGamepadLefty"] = prefs.VirtualGamepadLefty
            }).GetAwaiter().GetResult();

            if (!string.IsNullOrWhiteSpace(prefs.PlatformCores))
            {
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(prefs.PlatformCores);
                if (dict != null)
                {
                    foreach (var (platform, core) in dict)
                    {
                        PreferenceService.SetPreferencesAsync(userId, "system", platform, new Dictionary<string, string?> { ["core"] = core }).GetAwaiter().GetResult();
                    }
                }
            }
        }
    }

    public class MockAppPaths : IApplicationPaths
    {
        public MockAppPaths(string dataPath)
        {
            DataPath = dataPath;
            PluginsPath = dataPath;
            PluginConfigurationsPath = dataPath;
            LogDirectoryPath = dataPath;
            ConfigurationDirectoryPath = dataPath;
            SystemConfigurationFilePath = dataPath;
            CachePath = dataPath;
            WebPath = dataPath;

            ProgramDataPath = dataPath;
            ProgramSystemPath = dataPath;
            ImageCachePath = dataPath;
        }

        public string ProgramDataPath { get; }
        public string ProgramSystemPath { get; }
        public string DataPath { get; }
        public string PluginsPath { get; }
        public string PluginConfigurationsPath { get; }
        public string LogDirectoryPath { get; }
        public string ConfigurationDirectoryPath { get; }
        public string SystemConfigurationFilePath { get; }
        public string CachePath { get; }
        public string WebPath { get; }
        public string ImageCachePath { get; }
        public string VirtualInternalPath => DataPath;
        public string TempDirectory => DataPath;
        public string VirtualDataPath => DataPath;
        public string TrickplayPath => DataPath;
        public string BackupPath => DataPath;

        public void MakeSanityCheckOrThrow() { }
        public void CreateAndCheckMarker(string p1, string p2, bool b) { }
    }

    public class CoreResolutionTests
    {
        [Fact]
        public void DefaultCoreResolution_PlayStation_ReturnsPsx()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "JellyEmuTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var controller = new CoreResolutionTestController(new MockAppPaths(tempDir));
                var item = new Book
                {
                    Id = Guid.NewGuid(),
                    Tags = new[] { "PlayStation" },
                    Path = "C:\\Games\\PlayStation\\Crash.cue"
                };

                var core = controller.TestResolveCore(item);
                var info = controller.TestResolveCoreInfo(item);

                Assert.Equal("pcsx_rearmed", core);
                Assert.True(info.NeedsThreads);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void AvailableCores_PlayStation_ReturnsMultipleCores()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "JellyEmuTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var controller = new CoreResolutionTestController(new MockAppPaths(tempDir));
                var item = new Book
                {
                    Id = Guid.NewGuid(),
                    Tags = new[] { "PlayStation" },
                    Path = "C:\\Games\\PlayStation\\Crash.cue"
                };

                var cores = controller.TestGetAvailableCores(item);

                Assert.Equal(2, cores.Count);
                Assert.Contains(cores, c => c.Id == "pcsx_rearmed");
                Assert.Contains(cores, c => c.Id == "mednafen_psx_hw");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void AvailableCores_PlayStationAliasTag_ReturnsFullCoresList()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "JellyEmuTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var controller = new CoreResolutionTestController(new MockAppPaths(tempDir));
                var item = new Book
                {
                    Id = Guid.NewGuid(),
                    Tags = new[] { "psx" },
                    Path = "C:\\ROMS\\Spyro.cue"
                };

                var cores = controller.TestGetAvailableCores(item);

                Assert.Equal(2, cores.Count);
                Assert.Contains(cores, c => c.Id == "pcsx_rearmed");
                Assert.Contains(cores, c => c.Id == "mednafen_psx_hw");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void UserPlatformCorePreference_OverridesDefault()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "JellyEmuTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var controller = new CoreResolutionTestController(new MockAppPaths(tempDir));
                var userId = "user123";
                var item = new Book
                {
                    Id = Guid.NewGuid(),
                    Tags = new[] { "PlayStation" },
                    Path = "C:\\Games\\PlayStation\\Crash.cue"
                };

                var prefs = new JellyEmuBaseController.UserFullPrefs(
                    Scale: "fit", Mute: "false", Controller: "auto", Haptics: "true", Autosave: "true",
                    Shader: "", VideoRotation: 0, Controls: "", ControllerControls: "", RaUsername: "", RaApiKey: "",
                    VirtualGamepad: "false", VirtualGamepadLefty: "false",
                    PlatformCores: "{\"PlayStation\":\"mednafen_psx_hw\"}",
                    GameCores: "{}"
                );

                controller.TestSaveUserPrefs(userId, prefs);

                var resolvedCore = controller.TestResolveCore(item, userId);
                var resolvedInfo = controller.TestResolveCoreInfo(item, userId);

                Assert.Equal("mednafen_psx_hw", resolvedCore);
                Assert.True(resolvedInfo.NeedsThreads);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }


        [Fact]
        public void DefaultCoreResolution_Nintendo3DS_ReturnsAzaharAndIsSupported()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "JellyEmuTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var controller = new CoreResolutionTestController(new MockAppPaths(tempDir));
                var item = new Book
                {
                    Id = Guid.NewGuid(),
                    Tags = new[] { "Nintendo 3DS" },
                    Path = "C:\\Games\\3DS\\Zelda.3ds"
                };

                var resolvedCore = controller.TestResolveCore(item);
                var resolvedInfo = controller.TestResolveCoreInfo(item);
                var availableCores = controller.TestGetAvailableCores(item);

                Assert.Equal("azahar", resolvedCore);
                Assert.True(resolvedInfo.NeedsThreads);
                Assert.True(PlatformResolver.IsEjsSupported("Nintendo 3DS"));
                Assert.Contains(availableCores, c => c.Id == "azahar");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        private static void WithController(Action<CoreResolutionTestController> body)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "JellyEmuTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                body(new CoreResolutionTestController(new MockAppPaths(tempDir)));
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Theory]
        [InlineData("Game Boy", "gambatte")]
        [InlineData("Game Boy Color", "gambatte")]
        [InlineData("Game Boy Advance", "mgba")]
        [InlineData("SNES", "snes9x")]
        [InlineData("Sega Saturn", "yabause")]
        [InlineData("Atari 2600", "stella2014")]
        [InlineData("Atari 7800", "prosystem")]
        public void ResolveCore_ConsoleTag_ResolvesExpectedCore(string tag, string expected)
        {
            WithController(controller =>
            {
                var item = new Book { Id = Guid.NewGuid(), Tags = new[] { tag }, Path = "game.bin" };

                Assert.Equal(expected, controller.TestResolveCore(item));
            });
        }

        [Theory]
        [InlineData("Pokemon Crystal (USA).gbc", "gambatte")]
        [InlineData("Tetris (World).gb", "gambatte")]
        [InlineData("Super Mario World (USA).sfc", "snes9x")]
        [InlineData("Sonic the Hedgehog (Japan).md", "genesis_plus_gx")]
        public void ResolveCore_Extension_ResolvesExpectedCore(string path, string expected)
        {
            WithController(controller =>
            {
                var item = new Book { Id = Guid.NewGuid(), Path = path };

                Assert.Equal(expected, controller.TestResolveCore(item));
            });
        }

        [Fact]
        public void ResolveCore_GbcTagAndExtension_AgreeOnGambatte()
        {
            WithController(controller =>
            {
                var tagged = new Book
                {
                    Id = Guid.NewGuid(),
                    Tags = new[] { "Game Boy Color" },
                    Path = "Pokemon Crystal (USA).gbc"
                };
                var untagged = new Book { Id = Guid.NewGuid(), Path = "Pokemon Crystal (USA).gbc" };

                Assert.Equal("gambatte", controller.TestResolveCore(tagged));
                Assert.Equal("gambatte", controller.TestResolveCore(untagged));
            });
        }

        [Theory]
        [InlineData("mystery.xyz")]
        [InlineData(null)]
        public void ResolveCore_Unresolvable_ReturnsEmptyRatherThanGuessing(string? path)
        {
            WithController(controller =>
            {
                var item = new Book { Id = Guid.NewGuid(), Path = path };

                Assert.Equal(string.Empty, controller.TestResolveCore(item));
            });
        }
    }
}
