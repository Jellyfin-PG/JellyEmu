using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JellyEmu.Services
{
    /// <summary>
    /// Canonical cache key generators for JellyEmu endpoints.
    /// </summary>
    public static class JellyEmuCacheKeys
    {
        public static string Save(string itemId, string userId, int slot) => $"save:{itemId}:{userId}:{slot}";
        public static string SavePrefix(string itemId, string userId) => $"save:{itemId}:{userId}:";
        public static string Sram(string itemId, string userId, int slot) => $"sram:{itemId}:{userId}:{slot}";
        public static string SaveSlots(string itemId, string userId) => $"saveslots:{itemId}:{userId}";
        public static string UserSaves(string userId) => $"savesuser:{userId}";
        public static string SaveScreenshot(string itemId, string userId, int slot) => $"saveshot:{itemId}:{userId}:{slot}";
        
        public static string EffectivePrefs(string userId, string? platform) => $"prefseff:{userId}:{platform?.Trim().ToLowerInvariant() ?? string.Empty}";
        public static string EffectivePrefsUserPrefix(string userId) => $"prefseff:{userId}:";
        public static string ScopedPrefs(string userId, string scope, string targetId) => $"prefsscoped:{userId}:{scope.Trim().ToLowerInvariant()}:{targetId.Trim().ToLowerInvariant()}";
        public static string ScopedPrefsUserPrefix(string userId) => $"prefsscoped:{userId}:";
        public static string PrefsSummary(string userId) => $"prefssummary:{userId}";

        public static string Playtime(string itemId, string userId) => $"playtime:{itemId}:{userId}";
        public static string UserPlaytime(string userId) => $"playtimeuser:{userId}";

        public static string SettingOptions(string? scope, string? category) =>
            $"meta:settingoptions:{scope?.Trim().ToLowerInvariant() ?? string.Empty}:{category?.Trim().ToLowerInvariant() ?? string.Empty}";
        public static string Systems() => "meta:systems";

        public static string InputSchemes() => "input:schemes:all";
        public static string InputScheme(string platformOrCore) => $"input:scheme:{platformOrCore.Trim().ToLowerInvariant()}";

        public static string Cheats(string itemId) => $"cheats:{itemId}";
    }

    /// <summary>
    /// In-memory caching contract for JellyEmu endpoints with support for prefix-based eviction.
    /// </summary>
    public interface IJellyEmuCacheService
    {
        long MaxBinaryCacheSizeBytes { get; set; }
        bool ShouldCacheBinary(long byteLength);

        bool TryGetValue<T>(string key, out T? value);
        T? Get<T>(string key);
        void Set<T>(string key, T value, TimeSpan? slidingExpiration = null, TimeSpan? absoluteExpirationRelativeToNow = null);
        T GetOrCreate<T>(string key, Func<T> factory, TimeSpan? slidingExpiration = null, TimeSpan? absoluteExpirationRelativeToNow = null);
        Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? slidingExpiration = null, TimeSpan? absoluteExpirationRelativeToNow = null);

        void Evict(string key);
        void EvictByPrefix(string prefix);
        void ClearAll();
    }

    /// <summary>
    /// Thread-safe in-memory cache service wrapping IMemoryCache with dynamic prefix tracking and eviction.
    /// </summary>
    public class JellyEmuCacheService : IJellyEmuCacheService
    {
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<JellyEmuCacheService> _logger;
        private readonly ConcurrentDictionary<string, byte> _trackedKeys = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maximum size of binary files (save states, SRAM) allowed to be cached in RAM.
        /// Defaults to 15 MB. States larger than this stream directly from disk.
        /// </summary>
        public long MaxBinaryCacheSizeBytes { get; set; } = 15 * 1024 * 1024;

        public JellyEmuCacheService(IMemoryCache memoryCache, ILogger<JellyEmuCacheService>? logger = null)
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? NullLogger<JellyEmuCacheService>.Instance;
        }

        public bool ShouldCacheBinary(long byteLength) => byteLength > 0 && byteLength <= MaxBinaryCacheSizeBytes;

        public bool TryGetValue<T>(string key, out T? value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                value = default;
                return false;
            }

            if (_memoryCache.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }

        public T? Get<T>(string key)
        {
            TryGetValue<T>(key, out var value);
            return value;
        }

        public void Set<T>(string key, T value, TimeSpan? slidingExpiration = null, TimeSpan? absoluteExpirationRelativeToNow = null)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            var options = new MemoryCacheEntryOptions();
            if (slidingExpiration.HasValue)
                options.SetSlidingExpiration(slidingExpiration.Value);
            if (absoluteExpirationRelativeToNow.HasValue)
                options.SetAbsoluteExpiration(absoluteExpirationRelativeToNow.Value);

            // Default sliding expiration of 60 minutes if none specified
            if (!slidingExpiration.HasValue && !absoluteExpirationRelativeToNow.HasValue)
                options.SetSlidingExpiration(TimeSpan.FromMinutes(60));

            options.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
            {
                if (evictedKey is string k)
                {
                    _trackedKeys.TryRemove(k, out _);
                }
            });

            _trackedKeys[key] = 0;
            _memoryCache.Set(key, value, options);
            _logger.LogDebug("[JellyEmuCache] Cached entry for key '{Key}'", key);
        }

        public T GetOrCreate<T>(string key, Func<T> factory, TimeSpan? slidingExpiration = null, TimeSpan? absoluteExpirationRelativeToNow = null)
        {
            if (TryGetValue<T>(key, out var existing) && existing is not null)
                return existing;

            var created = factory();
            if (created is not null)
            {
                Set(key, created, slidingExpiration, absoluteExpirationRelativeToNow);
            }
            return created;
        }

        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? slidingExpiration = null, TimeSpan? absoluteExpirationRelativeToNow = null)
        {
            if (TryGetValue<T>(key, out var existing) && existing is not null)
                return existing;

            var created = await factory().ConfigureAwait(false);
            if (created is not null)
            {
                Set(key, created, slidingExpiration, absoluteExpirationRelativeToNow);
            }
            return created;
        }

        public void Evict(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            _trackedKeys.TryRemove(key, out _);
            _memoryCache.Remove(key);
            _logger.LogDebug("[JellyEmuCache] Evicted key '{Key}'", key);
        }

        public void EvictByPrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return;

            var matchingKeys = _trackedKeys.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in matchingKeys)
            {
                Evict(key);
            }

            if (matchingKeys.Count > 0)
            {
                _logger.LogInformation("[JellyEmuCache] Evicted {Count} entries matching prefix '{Prefix}'", matchingKeys.Count, prefix);
            }
        }

        public void ClearAll()
        {
            var keys = _trackedKeys.Keys.ToList();
            foreach (var key in keys)
            {
                Evict(key);
            }
        }
    }
}
