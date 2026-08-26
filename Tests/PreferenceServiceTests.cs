using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyEmu.Tests
{
    public class PreferenceServiceTests
    {
        private (JellyEmuPreferenceService Service, string TempDir) CreateTestService()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "JellyEmuPrefTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var appPaths = new MockAppPaths(tempDir);
            var service = new JellyEmuPreferenceService(appPaths, NullLogger<JellyEmuPreferenceService>.Instance);
            return (service, tempDir);
        }

        [Fact]
        public async Task GetEffectivePreferences_DefaultWhenNoPreferencesConfigured()
        {
            var (service, tempDir) = CreateTestService();
            try
            {
                var userId = "user123";
                var prefs = await service.GetEffectivePreferencesAsync(userId, "SNES");

                Assert.Equal("fit", prefs.Scale);
                Assert.Equal("0", prefs.Mute);
                Assert.Equal("crt-easymode.glslp", prefs.Shader);
                Assert.Equal(0, prefs.VideoRotation);
                Assert.Equal(string.Empty, prefs.Core);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task HierarchicalResolution_GlobalAndSystemOverrides()
        {
            var (service, tempDir) = CreateTestService();
            try
            {
                var userId = "user123";

                // Set Global Preferences (shader = "scanlines", scale = "fit")
                await service.SetPreferencesAsync(userId, "global", "", new Dictionary<string, string?>
                {
                    ["shader"] = "scanlines",
                    ["scale"] = "fit"
                });

                // Set System Preferences for SNES (shader = "2xsal", core = "snes9x")
                await service.SetPreferencesAsync(userId, "system", "SNES", new Dictionary<string, string?>
                {
                    ["shader"] = "2xsal",
                    ["core"] = "snes9x"
                });

                // Assertions for SNES:
                // - Core should be "snes9x" (from System)
                // - Shader should be "2xsal" (from System)
                // - Scale should be "fit" (inherited from Global)
                var effSnes = await service.GetEffectivePreferencesAsync(userId, "SNES");
                Assert.Equal("snes9x", effSnes.Core);
                Assert.Equal(0, effSnes.VideoRotation);
                Assert.Equal("2xsal", effSnes.Shader);
                Assert.Equal("fit", effSnes.Scale);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task DeletePreference_KeyAndScopeDeletion()
        {
            var (service, tempDir) = CreateTestService();
            try
            {
                var userId = "user123";
                var system = "SNES";

                await service.SetPreferencesAsync(userId, "global", "", new Dictionary<string, string?>
                {
                    ["shader"] = "global_shader.glslp",
                    ["videoRotation"] = "0"
                });

                await service.SetPreferencesAsync(userId, "system", system, new Dictionary<string, string?>
                {
                    ["shader"] = "snes_shader.glslp",
                    ["videoRotation"] = "180"
                });

                var effBefore = await service.GetEffectivePreferencesAsync(userId, system);
                Assert.Equal("snes_shader.glslp", effBefore.Shader);
                Assert.Equal(180, effBefore.VideoRotation);

                var deletedKey = await service.DeletePreferenceAsync(userId, "system", system, "shader");
                Assert.True(deletedKey);

                var effAfterKey = await service.GetEffectivePreferencesAsync(userId, system);
                Assert.Equal("global_shader.glslp", effAfterKey.Shader);
                Assert.Equal(180, effAfterKey.VideoRotation);

                var deletedScope = await service.DeletePreferenceAsync(userId, "system", system);
                Assert.True(deletedScope);

                var effAfterScope = await service.GetEffectivePreferencesAsync(userId, system);
                Assert.Equal("global_shader.glslp", effAfterScope.Shader);
                Assert.Equal(0, effAfterScope.VideoRotation);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetAllOverridesSummary_ListsConfiguredSystems()
        {
            var (service, tempDir) = CreateTestService();
            try
            {
                var userId = "user_summary";
                await service.SetPreferencesAsync(userId, "global", "", new Dictionary<string, string?> { ["shader"] = "crt" });
                await service.SetPreferencesAsync(userId, "system", "SNES", new Dictionary<string, string?> { ["core"] = "snes9x" });
                await service.SetPreferencesAsync(userId, "system", "GBA", new Dictionary<string, string?> { ["core"] = "mgba" });

                var summary = await service.GetAllOverridesSummaryAsync(userId);
                Assert.Equal(2, summary.Count);
                Assert.Contains(summary, s => s.Scope == "system" && s.TargetId == "SNES");
                Assert.Contains(summary, s => s.Scope == "system" && s.TargetId == "GBA");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ResetUserPreferences_WipesAllDataForUser()
        {
            var (service, tempDir) = CreateTestService();
            try
            {
                var userId = "user_reset";
                await service.SetPreferencesAsync(userId, "global", "", new Dictionary<string, string?> { ["shader"] = "custom_shader.glslp" });
                await service.SetPreferencesAsync(userId, "system", "SNES", new Dictionary<string, string?> { ["core"] = "custom_core" });

                var reset = await service.ResetUserPreferencesAsync(userId);
                Assert.True(reset);

                var eff = await service.GetEffectivePreferencesAsync(userId, "SNES");
                Assert.Equal("crt-easymode.glslp", eff.Shader);
                Assert.Equal(string.Empty, eff.Core);

                var summary = await service.GetAllOverridesSummaryAsync(userId);
                Assert.Empty(summary);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task Migration_LegacyPrefsJsonMigratedToSQLite()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "JellyEmuPrefMigrate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var userGuid = Guid.NewGuid().ToString("D");
                var userDir = Path.Combine(tempDir, "jellyemu-saves", userGuid);
                Directory.CreateDirectory(userDir);

                var legacyJson = @"{
                    ""scale"": ""fit"",
                    ""shader"": ""2xsal"",
                    ""videoRotation"": 180,
                    ""platformCores"": ""{\""SNES\"":\""bsnes\""}""
                }";
                File.WriteAllText(Path.Combine(userDir, "prefs.json"), legacyJson);

                var appPaths = new MockAppPaths(tempDir);
                var service = new JellyEmuPreferenceService(appPaths, NullLogger<JellyEmuPreferenceService>.Instance);

                var eff = await service.GetEffectivePreferencesAsync(userGuid, "SNES");
                Assert.Equal("fit", eff.Scale);
                Assert.Equal("2xsal", eff.Shader);
                Assert.Equal(180, eff.VideoRotation);
                Assert.Equal("bsnes", eff.Core);

                Assert.True(File.Exists(Path.Combine(userDir, "prefs.json.migrated")));
                Assert.False(File.Exists(Path.Combine(userDir, "prefs.json")));
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
        [Fact]
        public async Task GlobalAudioPreferences_SaveAndRetrieve()
        {
            var (service, tempDir) = CreateTestService();
            try
            {
                var userId = "audioUser123";

                // Initial defaults: Volume = 1, Mute = 0
                var initial = await service.GetEffectivePreferencesAsync(userId, "SNES");
                Assert.Equal("1", initial.Volume);
                Assert.Equal("0", initial.Mute);

                // Update volume to 0.75 and mute to 1
                await service.SetPreferencesAsync(userId, "global", "", new Dictionary<string, string?>
                {
                    ["volume"] = "0.75",
                    ["mute"] = "1"
                });

                var updated = await service.GetEffectivePreferencesAsync(userId, "SNES");
                Assert.Equal("0.75", updated.Volume);
                Assert.Equal("1", updated.Mute);

                // Ensure it applies globally across different platforms (e.g. GBA)
                var updatedGba = await service.GetEffectivePreferencesAsync(userId, "Game Boy Advance");
                Assert.Equal("0.75", updatedGba.Volume);
                Assert.Equal("1", updatedGba.Mute);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
