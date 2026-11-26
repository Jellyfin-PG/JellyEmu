using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LiteDB;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JellyEmu.Services
{
    public record EffectiveUserPrefs(
        string Scale,
        string Mute,
        string Volume,
        string Controller,
        string Haptics,
        string Autosave,
        string Shader,
        int VideoRotation,
        string Controls,
        string ControllerControls,
        string RaUsername,
        string RaApiKey,
        string VirtualGamepad,
        string VirtualGamepadLefty,
        string Core,
        string Vsync,
        string FfRate,
        string SmRate,
        string ShowFps,
        string AudioEq);

    public record OverrideSummaryItem(
        string Scope,
        string TargetId,
        int PreferenceCount,
        DateTime LastUpdatedUtc);

    public class PreferenceEntry
    {
        public string Id { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }

    public class JellyEmuPreferenceService
    {
        private readonly IApplicationPaths _appPaths;
        private readonly ILogger<JellyEmuPreferenceService> _logger;
        private readonly string _connectionString;
        private bool _dbInitialized = false;
        private readonly object _dbLock = new object();

        public static readonly EffectiveUserPrefs SystemDefaults = new(
            Scale: "fit",
            Mute: "0",
            Volume: "1",
            Controller: "gamepad",
            Haptics: "1",
            Autosave: "0",
            Shader: "crt-easymode.glslp",
            VideoRotation: 0,
            Controls: string.Empty,
            ControllerControls: string.Empty,
            RaUsername: string.Empty,
            RaApiKey: string.Empty,
            VirtualGamepad: "0",
            VirtualGamepadLefty: "0",
            Core: string.Empty,
            Vsync: "1",
            FfRate: "3",
            SmRate: "3",
            ShowFps: "0",
            AudioEq: "{}");

        public JellyEmuPreferenceService(IApplicationPaths appPaths, ILogger<JellyEmuPreferenceService>? logger = null)
        {
            _appPaths = appPaths;
            _logger = logger ?? NullLogger<JellyEmuPreferenceService>.Instance;

            var dbPath = Path.Combine(_appPaths.DataPath, "jellyemu-preferences.litedb");
            _connectionString = $"Filename={dbPath};Connection=shared";

            EnsureDatabaseCreated();
        }

        private static string BuildEntryId(string userId, string scope, string targetId, string key)
            => $"{userId}|{scope}|{targetId}|{key}";

        public void EnsureDatabaseCreated()
        {
            if (_dbInitialized) return;
            lock (_dbLock)
            {
                if (_dbInitialized) return;

                try
                {
                    using var db = new LiteDatabase(_connectionString);
                    var col = db.GetCollection<PreferenceEntry>("UserPreferences");
                    col.EnsureIndex(x => x.UserId);
                    col.EnsureIndex(x => x.Scope);

                    MigrateLegacyPreferences(col);
                    _dbInitialized = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyEmu] Failed to initialize LiteDB preferences database.");
                }
            }
        }

        private void MigrateLegacyPreferences(ILiteCollection<PreferenceEntry> col)
        {
            var legacyDbPath = Path.Combine(_appPaths.DataPath, "jellyemu-preferences.db");
            if (File.Exists(legacyDbPath))
            {
                try
                {
                    int migratedCount = 0;
                    var readOk = LegacySqliteMigrator.TryReadTable(
                        legacyDbPath,
                        "SELECT UserId, Scope, TargetId, Key, Value, UpdatedAt FROM UserPreferences",
                        reader =>
                        {
                            var userId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                            var scope = reader.IsDBNull(1) ? "global" : reader.GetString(1);
                            var targetId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                            var key = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                            var value = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                            var updatedRaw = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
                            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(key)) return;

                            var updatedAt = DateTime.TryParse(updatedRaw, out var parsed)
                                ? parsed.ToUniversalTime()
                                : DateTime.UtcNow;

                            var id = BuildEntryId(userId, scope, targetId, key);
                            if (col.FindById(id) == null)
                            {
                                col.Upsert(new PreferenceEntry
                                {
                                    Id = id,
                                    UserId = userId,
                                    Scope = scope,
                                    TargetId = targetId,
                                    Key = key,
                                    Value = value,
                                    UpdatedAt = updatedAt
                                });
                            }
                            migratedCount++;
                        },
                        _logger);

                    if (readOk)
                    {
                        var bakPath = legacyDbPath + ".bak";
                        if (File.Exists(bakPath)) File.Delete(bakPath);
                        File.Move(legacyDbPath, bakPath);
                        _logger.LogInformation("[JellyEmu] Migrated {Count} preference records from {DbPath} to LiteDB and renamed to .bak", migratedCount, legacyDbPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[JellyEmu] Failed to migrate legacy preferences SQLite database {DbPath}", legacyDbPath);
                }
            }

            try
            {
                var savesDir = Path.Combine(_appPaths.DataPath, "jellyemu-saves");
                if (!Directory.Exists(savesDir)) return;

                var userDirs = Directory.GetDirectories(savesDir);
                foreach (var userDir in userDirs)
                {
                    var userId = Path.GetFileName(userDir);
                    if (!Guid.TryParse(userId, out _)) continue;

                    var prefsPath = Path.Combine(userDir, "prefs.json");
                    if (File.Exists(prefsPath))
                    {
                        try
                        {
                            var json = File.ReadAllText(prefsPath);
                            using var doc = JsonDocument.Parse(json);
                            var root = doc.RootElement;

                            var now = DateTime.UtcNow;

                            void Upsert(string scope, string targetId, string key, string value)
                            {
                                col.Upsert(new PreferenceEntry
                                {
                                    Id = BuildEntryId(userId, scope, targetId, key),
                                    UserId = userId,
                                    Scope = scope,
                                    TargetId = targetId,
                                    Key = key,
                                    Value = value,
                                    UpdatedAt = now
                                });
                            }

                            string Str(string k, string def) => root.TryGetProperty(k, out var v) ? (v.GetString() ?? def) : def;
                            int Int(string k, int def) => root.TryGetProperty(k, out var v) ? v.GetInt32() : def;

                            Upsert("global", "", "scale", Str("scale", SystemDefaults.Scale));
                            Upsert("global", "", "mute", Str("mute", SystemDefaults.Mute));
                            Upsert("global", "", "controller", Str("controller", SystemDefaults.Controller));
                            Upsert("global", "", "haptics", Str("haptics", SystemDefaults.Haptics));
                            Upsert("global", "", "autosave", Str("autosave", SystemDefaults.Autosave));
                            Upsert("global", "", "shader", Str("shader", SystemDefaults.Shader));
                            Upsert("global", "", "videoRotation", Int("videoRotation", SystemDefaults.VideoRotation).ToString());
                            Upsert("global", "", "controls", Str("controls", SystemDefaults.Controls));
                            Upsert("global", "", "controllerControls", Str("controllerControls", SystemDefaults.ControllerControls));
                            Upsert("global", "", "raUsername", Str("raUsername", SystemDefaults.RaUsername));
                            Upsert("global", "", "raApiKey", Str("raApiKey", SystemDefaults.RaApiKey));
                            Upsert("global", "", "virtualGamepad", Str("virtualGamepad", SystemDefaults.VirtualGamepad));
                            Upsert("global", "", "virtualGamepadLefty", Str("virtualGamepadLefty", SystemDefaults.VirtualGamepadLefty));
                            Upsert("global", "", "vsync", Str("vsync", SystemDefaults.Vsync));
                            Upsert("global", "", "ffrate", Str("ffrate", SystemDefaults.FfRate));
                            Upsert("global", "", "smrate", Str("smrate", SystemDefaults.SmRate));
                            Upsert("global", "", "showFps", Str("showFps", SystemDefaults.ShowFps));

                            var platformCoresJson = Str("platformCores", string.Empty);
                            if (!string.IsNullOrWhiteSpace(platformCoresJson))
                            {
                                try
                                {
                                    var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(platformCoresJson);
                                    if (dict != null)
                                    {
                                        foreach (var (platform, core) in dict)
                                        {
                                            if (!string.IsNullOrWhiteSpace(platform) && !string.IsNullOrWhiteSpace(core))
                                                Upsert("system", platform, "core", core);
                                        }
                                    }
                                }
                                catch { }
                            }

                            var migratedPath = Path.Combine(userDir, "prefs.json.migrated");
                            if (File.Exists(migratedPath)) File.Delete(migratedPath);
                            File.Move(prefsPath, migratedPath);

                            _logger.LogInformation("[JellyEmu] Successfully migrated prefs.json to LiteDB for user {UserId}", userId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[JellyEmu] Failed to migrate prefs.json for user {UserId}", userId);
                        }
                    }

                    var slotPath = Path.Combine(userDir, "active-slot.json");
                    if (File.Exists(slotPath))
                    {
                        try
                        {
                            var migratedSlotPath = Path.Combine(userDir, "active-slot.json.migrated");
                            if (File.Exists(migratedSlotPath)) File.Delete(migratedSlotPath);
                            File.Move(slotPath, migratedSlotPath);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Error during legacy preferences migration scan.");
            }
        }

        /// <summary>
        /// Retrieves effective runtime preferences for a user:
        /// Defaults -> Global -> System (platformTag).
        /// </summary>
        public Task<EffectiveUserPrefs> GetEffectivePreferencesAsync(string userId, string? platformTag = null)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var canonical = !string.IsNullOrWhiteSpace(platformTag) ? JellyEmuInputService.ResolveSchemeKey(platformTag) : null;
            if (canonical == "default") canonical = null;

            try
            {
                lock (_dbLock)
                {
                    using var db = new LiteDatabase(_connectionString);
                    var col = db.GetCollection<PreferenceEntry>("UserPreferences");
                    var entries = col.Find(x => x.UserId == userId).ToList();

                    foreach (var e in entries.Where(x => x.Scope == "global" && string.IsNullOrEmpty(x.TargetId)))
                    {
                        dict[e.Key] = e.Value;
                    }

                    if (!string.IsNullOrWhiteSpace(platformTag))
                    {
                        foreach (var e in entries.Where(x => x.Scope == "system" && x.TargetId == platformTag))
                        {
                            dict[e.Key] = e.Value;
                        }
                        if (!string.IsNullOrWhiteSpace(canonical))
                        {
                            foreach (var e in entries.Where(x => x.Scope == "system" && x.TargetId == canonical))
                            {
                                dict[e.Key] = e.Value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to query effective preferences for user {UserId}", userId);
            }

            string Get(string key, string def) => dict.TryGetValue(key, out var v) ? v : def;
            int GetInt(string key, int def) => dict.TryGetValue(key, out var v) && int.TryParse(v, out var parsed) ? parsed : def;

            string GetShader()
            {
                var raw = Get("shader", SystemDefaults.Shader);
                if (string.IsNullOrWhiteSpace(raw) || raw.Equals("none", StringComparison.OrdinalIgnoreCase) || raw.Equals("disabled", StringComparison.OrdinalIgnoreCase) || raw == "0")
                    return "disabled";

                if (raw.Equals("crt-easymode", StringComparison.OrdinalIgnoreCase)) return "crt-easymode.glslp";
                if (raw.Equals("2xScaleHQ", StringComparison.OrdinalIgnoreCase)) return "2xScaleHQ.glslp";
                if (raw.Equals("4xScaleHQ", StringComparison.OrdinalIgnoreCase)) return "4xScaleHQ.glslp";
                if (raw.Equals("crt-aperture", StringComparison.OrdinalIgnoreCase)) return "crt-aperture.glslp";
                if (raw.Equals("crt-geom", StringComparison.OrdinalIgnoreCase)) return "crt-geom.glslp";
                if (raw.Equals("crt-mattias", StringComparison.OrdinalIgnoreCase)) return "crt-mattias.glslp";

                return raw;
            }

            string GetScale()
            {
                var raw = Get("scale", SystemDefaults.Scale);
                if (string.IsNullOrWhiteSpace(raw)) return SystemDefaults.Scale;
                if (raw.Equals("aspect", StringComparison.OrdinalIgnoreCase)) return "fit";
                if (raw.Equals("native", StringComparison.OrdinalIgnoreCase)) return "1";
                if (raw.Equals("2x", StringComparison.OrdinalIgnoreCase)) return "2";
                if (raw.Equals("3x", StringComparison.OrdinalIgnoreCase)) return "3";
                if (raw.Equals("4x", StringComparison.OrdinalIgnoreCase)) return "4";
                return raw;
            }

            string GetControls()
            {
                var raw = Get("controls", SystemDefaults.Controls);
                if (!string.IsNullOrWhiteSpace(raw) && raw != "{}") return raw;
                var legacyJe = Get("jeBindings", string.Empty);
                if (!string.IsNullOrWhiteSpace(legacyJe) && legacyJe != "{}") return legacyJe;
                var legacyPb = Get("playerBindings", string.Empty);
                if (!string.IsNullOrWhiteSpace(legacyPb) && legacyPb != "{}") return legacyPb;
                return SystemDefaults.Controls;
            }

            return Task.FromResult(new EffectiveUserPrefs(
                Scale: GetScale(),
                Mute: Get("mute", SystemDefaults.Mute),
                Volume: Get("volume", SystemDefaults.Volume),
                Controller: Get("controller", SystemDefaults.Controller),
                Haptics: Get("haptics", SystemDefaults.Haptics),
                Autosave: Get("autosave", SystemDefaults.Autosave),
                Shader: GetShader(),
                VideoRotation: GetInt("videoRotation", SystemDefaults.VideoRotation),
                Controls: GetControls(),
                ControllerControls: Get("controllerControls", SystemDefaults.ControllerControls),
                RaUsername: Get("raUsername", SystemDefaults.RaUsername),
                RaApiKey: Get("raApiKey", SystemDefaults.RaApiKey),
                VirtualGamepad: Get("virtualGamepad", SystemDefaults.VirtualGamepad),
                VirtualGamepadLefty: Get("virtualGamepadLefty", SystemDefaults.VirtualGamepadLefty),
                Core: Get("core", SystemDefaults.Core),
                Vsync: Get("vsync", SystemDefaults.Vsync),
                FfRate: Get("ffrate", SystemDefaults.FfRate),
                SmRate: Get("smrate", SystemDefaults.SmRate),
                ShowFps: Get("showFps", SystemDefaults.ShowFps),
                AudioEq: Get("audioEq", "{}")));
        }

        /// <summary>
        /// Retrieves explicit scoped preferences (raw key-value pairs) configured at that exact level.
        /// </summary>
        public Task<Dictionary<string, string>> GetScopedPreferencesAsync(string userId, string scope, string targetId)
        {
            var normalizedScope = NormalizeScope(scope);
            var normalizedTarget = NormalizeTarget(normalizedScope, targetId);
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                lock (_dbLock)
                {
                    using var db = new LiteDatabase(_connectionString);
                    var col = db.GetCollection<PreferenceEntry>("UserPreferences");
                    foreach (var e in col.Find(x => x.UserId == userId && x.Scope == normalizedScope && (x.TargetId ?? "") == normalizedTarget))
                    {
                        results[e.Key] = e.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to get scoped preferences for user {UserId}, scope={Scope}, target={Target}", userId, scope, targetId);
            }

            return Task.FromResult(results);
        }

        /// <summary>
        /// Upserts preferences at the specified scope (global or system).
        /// </summary>
        public Task SetPreferencesAsync(string userId, string scope, string targetId, IDictionary<string, string?> preferences)
        {
            if (preferences == null || preferences.Count == 0) return Task.CompletedTask;

            var normalizedScope = NormalizeScope(scope);
            var normalizedTarget = NormalizeTarget(normalizedScope, targetId);
            var now = DateTime.UtcNow;

            try
            {
                lock (_dbLock)
                {
                    using var db = new LiteDatabase(_connectionString);
                    var col = db.GetCollection<PreferenceEntry>("UserPreferences");

                    foreach (var (key, value) in preferences)
                    {
                        if (string.IsNullOrWhiteSpace(key)) continue;
                        var id = BuildEntryId(userId, normalizedScope, normalizedTarget, key);

                        if (value == null)
                        {
                            col.Delete(id);
                        }
                        else
                        {
                            col.Upsert(new PreferenceEntry
                            {
                                Id = id,
                                UserId = userId,
                                Scope = normalizedScope,
                                TargetId = normalizedTarget,
                                Key = key,
                                Value = value,
                                UpdatedAt = now
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to set preferences for user {UserId}, scope={Scope}, target={Target}", userId, scope, targetId);
                throw;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Deletes a specific preference key or all preferences at a scope.
        /// </summary>
        public Task<bool> DeletePreferenceAsync(string userId, string scope, string targetId, string? key = null)
        {
            var normalizedScope = NormalizeScope(scope);
            var normalizedTarget = NormalizeTarget(normalizedScope, targetId);

            try
            {
                lock (_dbLock)
                {
                    using var db = new LiteDatabase(_connectionString);
                    var col = db.GetCollection<PreferenceEntry>("UserPreferences");

                    if (string.IsNullOrWhiteSpace(key))
                    {
                        var deleted = col.DeleteMany(x => x.UserId == userId && x.Scope == normalizedScope && x.TargetId == normalizedTarget);
                        return Task.FromResult(deleted > 0);
                    }

                    var id = BuildEntryId(userId, normalizedScope, normalizedTarget, key);
                    return Task.FromResult(col.Delete(id));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to delete preferences for user {UserId}, scope={Scope}, target={Target}, key={Key}", userId, scope, targetId, key);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Completely resets all preferences for a user back to factory defaults.
        /// </summary>
        public Task<bool> ResetUserPreferencesAsync(string userId)
        {
            try
            {
                lock (_dbLock)
                {
                    using var db = new LiteDatabase(_connectionString);
                    var col = db.GetCollection<PreferenceEntry>("UserPreferences");
                    col.DeleteMany(x => x.UserId == userId);
                    return Task.FromResult(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to reset preferences for user {UserId}", userId);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Summarizes all custom system overrides configured for a user.
        /// </summary>
        public Task<List<OverrideSummaryItem>> GetAllOverridesSummaryAsync(string userId)
        {
            var list = new List<OverrideSummaryItem>();
            try
            {
                lock (_dbLock)
                {
                    using var db = new LiteDatabase(_connectionString);
                    var col = db.GetCollection<PreferenceEntry>("UserPreferences");
                    var groups = col.Find(x => x.UserId == userId && x.Scope == "system")
                        .GroupBy(x => x.TargetId)
                        .OrderBy(g => g.Key);

                    foreach (var g in groups)
                    {
                        var count = g.Count();
                        var updated = g.Max(x => x.UpdatedAt);
                        list.Add(new OverrideSummaryItem("system", g.Key, count, updated));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to fetch override summary for user {UserId}", userId);
            }

            return Task.FromResult(list);
        }

        private static string NormalizeScope(string? scope)
        {
            if (string.IsNullOrWhiteSpace(scope)) return "global";
            var s = scope.Trim().ToLowerInvariant();
            return s switch
            {
                "system" or "platform" or "console" => "system",
                _ => "global"
            };
        }

        private static string NormalizeTarget(string scope, string? targetId)
        {
            if (scope == "global") return string.Empty;
            if (string.IsNullOrWhiteSpace(targetId)) return string.Empty;
            var canonical = JellyEmuInputService.ResolveSchemeKey(targetId);
            return !string.IsNullOrEmpty(canonical) && canonical != "default" ? canonical : targetId.Trim();
        }
    }
}
