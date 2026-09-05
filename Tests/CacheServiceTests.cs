using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JellyEmu.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyEmu.Tests
{
    public class CacheServiceTests
    {
        private JellyEmuCacheService CreateService()
        {
            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            return new JellyEmuCacheService(memoryCache, NullLogger<JellyEmuCacheService>.Instance);
        }

        [Fact]
        public void SetAndGet_ReturnsCachedValue()
        {
            var cache = CreateService();
            var key = "test:item:1";
            var data = new byte[] { 1, 2, 3, 4, 5 };

            cache.Set(key, data);

            var retrieved = cache.Get<byte[]>(key);
            Assert.NotNull(retrieved);
            Assert.Equal(data, retrieved);
        }

        [Fact]
        public void TryGetValue_ReturnsFalseForMissingOrEvicted()
        {
            var cache = CreateService();

            var found = cache.TryGetValue<string>("nonexistent", out var value);
            Assert.False(found);
            Assert.Null(value);
        }

        [Fact]
        public void Evict_RemovesSpecificKey()
        {
            var cache = CreateService();
            var key = "save:game1:user1:1";
            cache.Set(key, "state-data");

            Assert.True(cache.TryGetValue<string>(key, out _));

            cache.Evict(key);

            Assert.False(cache.TryGetValue<string>(key, out _));
        }

        [Fact]
        public void EvictByPrefix_RemovesAllMatchingPrefixes()
        {
            var cache = CreateService();
            var user1 = "user123";
            var user2 = "user456";

            // Populate user1 effective prefs for multiple consoles
            cache.Set(JellyEmuCacheKeys.EffectivePrefs(user1, "snes"), "snes-prefs");
            cache.Set(JellyEmuCacheKeys.EffectivePrefs(user1, "gba"), "gba-prefs");
            cache.Set(JellyEmuCacheKeys.EffectivePrefs(user1, null), "global-prefs");

            // Populate user2 effective prefs
            cache.Set(JellyEmuCacheKeys.EffectivePrefs(user2, "snes"), "user2-snes-prefs");

            // Evict all user1 effective prefs
            cache.EvictByPrefix(JellyEmuCacheKeys.EffectivePrefsUserPrefix(user1));

            // User 1 entries should be gone
            Assert.False(cache.TryGetValue<string>(JellyEmuCacheKeys.EffectivePrefs(user1, "snes"), out _));
            Assert.False(cache.TryGetValue<string>(JellyEmuCacheKeys.EffectivePrefs(user1, "gba"), out _));
            Assert.False(cache.TryGetValue<string>(JellyEmuCacheKeys.EffectivePrefs(user1, null), out _));

            // User 2 entries should remain intact
            Assert.True(cache.TryGetValue<string>(JellyEmuCacheKeys.EffectivePrefs(user2, "snes"), out var u2Val));
            Assert.Equal("user2-snes-prefs", u2Val);
        }

        [Fact]
        public void ShouldCacheBinary_RespectsMaxBinaryCacheSizeBytes()
        {
            var cache = CreateService();
            cache.MaxBinaryCacheSizeBytes = 10 * 1024 * 1024; // 10 MB

            Assert.True(cache.ShouldCacheBinary(500 * 1024)); // 500 KB -> True
            Assert.True(cache.ShouldCacheBinary(10 * 1024 * 1024)); // 10 MB -> True
            Assert.False(cache.ShouldCacheBinary(10 * 1024 * 1024 + 1)); // 10 MB + 1 -> False
            Assert.False(cache.ShouldCacheBinary(0)); // Empty -> False
            Assert.False(cache.ShouldCacheBinary(-10)); // Negative -> False
        }

        [Fact]
        public void GetOrCreate_OnlyCallsFactoryOnCacheMiss()
        {
            var cache = CreateService();
            var key = "test:factory";
            int callCount = 0;

            var first = cache.GetOrCreate(key, () =>
            {
                callCount++;
                return "first-value";
            });

            var second = cache.GetOrCreate(key, () =>
            {
                callCount++;
                return "second-value";
            });

            Assert.Equal(1, callCount);
            Assert.Equal("first-value", first);
            Assert.Equal("first-value", second);
        }

        [Fact]
        public async Task GetOrCreateAsync_OnlyCallsFactoryOnCacheMiss()
        {
            var cache = CreateService();
            var key = "test:async:factory";
            int callCount = 0;

            var first = await cache.GetOrCreateAsync(key, async () =>
            {
                await Task.Yield();
                callCount++;
                return "async-value";
            });

            var second = await cache.GetOrCreateAsync(key, async () =>
            {
                await Task.Yield();
                callCount++;
                return "new-async-value";
            });

            Assert.Equal(1, callCount);
            Assert.Equal("async-value", first);
            Assert.Equal("async-value", second);
        }

        [Fact]
        public void SaveCacheEvictionScenario_EvictsSaveSlotsAndUserCatalog()
        {
            var cache = CreateService();
            var itemId = "gameA";
            var userId = "userB";
            int slot = 2;

            cache.Set(JellyEmuCacheKeys.Save(itemId, userId, slot), new byte[] { 10, 20 });
            cache.Set(JellyEmuCacheKeys.SaveSlots(itemId, userId), "slot-list-json");
            cache.Set(JellyEmuCacheKeys.UserSaves(userId), "user-catalog-json");

            // PostSave simulation:
            cache.Evict(JellyEmuCacheKeys.Save(itemId, userId, slot));
            cache.Evict(JellyEmuCacheKeys.SaveSlots(itemId, userId));
            cache.Evict(JellyEmuCacheKeys.UserSaves(userId));

            Assert.False(cache.TryGetValue<byte[]>(JellyEmuCacheKeys.Save(itemId, userId, slot), out _));
            Assert.False(cache.TryGetValue<string>(JellyEmuCacheKeys.SaveSlots(itemId, userId), out _));
            Assert.False(cache.TryGetValue<string>(JellyEmuCacheKeys.UserSaves(userId), out _));
        }

        [Fact]
        public void BindingUpdateScenario_EvictsEffectiveAndScopedCaches()
        {
            var cache = CreateService();
            var userId = "userX";

            cache.Set(JellyEmuCacheKeys.EffectivePrefs(userId, "snes"), "snes-effective");
            cache.Set(JellyEmuCacheKeys.EffectivePrefs(userId, "gba"), "gba-effective");
            cache.Set(JellyEmuCacheKeys.ScopedPrefs(userId, "system", "snes"), "snes-custom-binds");
            cache.Set(JellyEmuCacheKeys.PrefsSummary(userId), "summary-data");

            // User updates SNES controller binding in UI:
            cache.EvictByPrefix(JellyEmuCacheKeys.EffectivePrefsUserPrefix(userId));
            cache.EvictByPrefix(JellyEmuCacheKeys.ScopedPrefsUserPrefix(userId));
            cache.Evict(JellyEmuCacheKeys.PrefsSummary(userId));

            Assert.False(cache.TryGetValue<string>(JellyEmuCacheKeys.EffectivePrefs(userId, "snes"), out _));
            Assert.False(cache.TryGetValue<string>(JellyEmuCacheKeys.EffectivePrefs(userId, "gba"), out _));
            Assert.False(cache.TryGetValue<string>(JellyEmuCacheKeys.ScopedPrefs(userId, "system", "snes"), out _));
            Assert.False(cache.TryGetValue<string>(JellyEmuCacheKeys.PrefsSummary(userId), out _));
        }

        [Fact]
        public void PlaytimeWriteThroughScenario_UpdatesItemAndEvictsUser()
        {
            var cache = CreateService();
            var itemId = "mario";
            var userId = "luigi";

            cache.Set(JellyEmuCacheKeys.Playtime(itemId, userId), 100L);
            cache.Set(JellyEmuCacheKeys.UserPlaytime(userId), "aggregate-json");

            // Session reports +50s:
            long newTotal = 150L;
            cache.Set(JellyEmuCacheKeys.Playtime(itemId, userId), newTotal);
            cache.Evict(JellyEmuCacheKeys.UserPlaytime(userId));

            Assert.True(cache.TryGetValue<long>(JellyEmuCacheKeys.Playtime(itemId, userId), out var updated));
            Assert.Equal(150L, updated);
            Assert.False(cache.TryGetValue<string>(JellyEmuCacheKeys.UserPlaytime(userId), out _));
        }
    }
}
