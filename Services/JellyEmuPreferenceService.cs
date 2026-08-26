using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
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
        string ShowFps);

    public record OverrideSummaryItem(
        string Scope,
        string TargetId,
        int PreferenceCount,
        DateTime LastUpdatedUtc);

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
            ShowFps: "0");

        public JellyEmuPreferenceService(IApplicationPaths appPaths, ILogger<JellyEmuPreferenceService>? logger = null)
        {
            _appPaths = appPaths;
            _logger = logger ?? NullLogger<JellyEmuPreferenceService>.Instance;

            var dbPath = Path.Combine(_appPaths.DataPath, "jellyemu-preferences.db");
            _connectionString = $"Data Source={dbPath}";

            EnsureDatabaseCreated();
        }

        public void EnsureDatabaseCreated()
        {
            if (_dbInitialized) return;
            lock (_dbLock)
            {
                if (_dbInitialized) return;

                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    using var command = connection.CreateCommand();
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS UserPreferences (
                            UserId TEXT NOT NULL,
                            Scope TEXT NOT NULL,
                            TargetId TEXT NOT NULL,
                            Key TEXT NOT NULL,
                            Value TEXT NOT NULL,
                            UpdatedAt TEXT NOT NULL,
                            PRIMARY KEY (UserId, Scope, TargetId, Key)
                        );
                        CREATE INDEX IF NOT EXISTS IX_UserPreferences_Lookup 
                        ON UserPreferences(UserId, Scope, TargetId);
                    ";
                    command.ExecuteNonQuery();

                    MigrateLegacyPreferences(connection);
                    _dbInitialized = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyEmu] Failed to initialize SQLite preferences database.");
                }
            }
        }

        private void MigrateLegacyPreferences(SqliteConnection connection)
        {
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

                            var now = DateTime.UtcNow.ToString("o");
                            using var transaction = connection.BeginTransaction();

                            void Upsert(string scope, string targetId, string key, string value)
                            {
                                using var cmd = connection.CreateCommand();
                                cmd.Transaction = transaction;
                                cmd.CommandText = @"
                                    INSERT INTO UserPreferences (UserId, Scope, TargetId, Key, Value, UpdatedAt)
                                    VALUES ($userId, $scope, $targetId, $key, $value, $updatedAt)
                                    ON CONFLICT(UserId, Scope, TargetId, Key) DO UPDATE SET
                                        Value = excluded.Value,
                                        UpdatedAt = excluded.UpdatedAt;
                                ";
                                cmd.Parameters.AddWithValue("$userId", userId);
                                cmd.Parameters.AddWithValue("$scope", scope);
                                cmd.Parameters.AddWithValue("$targetId", targetId);
                                cmd.Parameters.AddWithValue("$key", key);
                                cmd.Parameters.AddWithValue("$value", value);
                                cmd.Parameters.AddWithValue("$updatedAt", now);
                                cmd.ExecuteNonQuery();
                            }

                            string Str(string k, string def) => root.TryGetProperty(k, out var v) ? (v.GetString() ?? def) : def;
                            int Int(string k, int def) => root.TryGetProperty(k, out var v) ? v.GetInt32() : def;

                            // Global settings
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

                            // Extract platformCores into 'system' scope
                            var platformCoresJson = Str("platformCores", string.Empty);
                            if (!string.IsNullOrWhiteSpace(platformCoresJson))
                            {
                                try
                                {
                                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(platformCoresJson);
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

                            transaction.Commit();

                            var migratedPath = Path.Combine(userDir, "prefs.json.migrated");
                            if (File.Exists(migratedPath)) File.Delete(migratedPath);
                            File.Move(prefsPath, migratedPath);

                            _logger.LogInformation("[JellyEmu] Successfully migrated prefs.json to SQLite for user {UserId}", userId);
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
        public async Task<EffectiveUserPrefs> GetEffectivePreferencesAsync(string userId, string? platformTag = null)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT Scope, Key, Value 
                    FROM UserPreferences
                    WHERE UserId = $userId AND (
                        (Scope = 'global' AND TargetId = '') OR
                        ($platformTag IS NOT NULL AND Scope = 'system' AND TargetId = $platformTag)
                    )
                    ORDER BY CASE Scope 
                        WHEN 'global' THEN 1 
                        WHEN 'system' THEN 2 
                        ELSE 0 END;
                ";
                cmd.Parameters.AddWithValue("$userId", userId);
                cmd.Parameters.AddWithValue("$platformTag", (object?)platformTag ?? DBNull.Value);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var key = reader.GetString(1);
                    var val = reader.GetString(2);
                    dict[key] = val;
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

            return new EffectiveUserPrefs(
                Scale: GetScale(),
                Mute: Get("mute", SystemDefaults.Mute),
                Volume: Get("volume", SystemDefaults.Volume),
                Controller: Get("controller", SystemDefaults.Controller),
                Haptics: Get("haptics", SystemDefaults.Haptics),
                Autosave: Get("autosave", SystemDefaults.Autosave),
                Shader: GetShader(),
                VideoRotation: GetInt("videoRotation", SystemDefaults.VideoRotation),
                Controls: Get("controls", SystemDefaults.Controls),
                ControllerControls: Get("controllerControls", SystemDefaults.ControllerControls),
                RaUsername: Get("raUsername", SystemDefaults.RaUsername),
                RaApiKey: Get("raApiKey", SystemDefaults.RaApiKey),
                VirtualGamepad: Get("virtualGamepad", SystemDefaults.VirtualGamepad),
                VirtualGamepadLefty: Get("virtualGamepadLefty", SystemDefaults.VirtualGamepadLefty),
                Core: Get("core", SystemDefaults.Core),
                Vsync: Get("vsync", SystemDefaults.Vsync),
                FfRate: Get("ffrate", SystemDefaults.FfRate),
                SmRate: Get("smrate", SystemDefaults.SmRate),
                ShowFps: Get("showFps", SystemDefaults.ShowFps));
        }

        /// <summary>
        /// Retrieves explicit scoped preferences (raw key-value pairs) configured at that exact level.
        /// </summary>
        public async Task<Dictionary<string, string>> GetScopedPreferencesAsync(string userId, string scope, string targetId)
        {
            var normalizedScope = NormalizeScope(scope);
            var normalizedTarget = NormalizeTarget(normalizedScope, targetId);
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT Key, Value 
                    FROM UserPreferences 
                    WHERE UserId = $userId AND Scope = $scope AND TargetId = $targetId;
                ";
                cmd.Parameters.AddWithValue("$userId", userId);
                cmd.Parameters.AddWithValue("$scope", normalizedScope);
                cmd.Parameters.AddWithValue("$targetId", normalizedTarget);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results[reader.GetString(0)] = reader.GetString(1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to get scoped preferences for user {UserId}, scope={Scope}, target={Target}", userId, scope, targetId);
            }

            return results;
        }

        /// <summary>
        /// Upserts preferences at the specified scope (global or system).
        /// </summary>
        public async Task SetPreferencesAsync(string userId, string scope, string targetId, IDictionary<string, string?> preferences)
        {
            if (preferences == null || preferences.Count == 0) return;

            var normalizedScope = NormalizeScope(scope);
            var normalizedTarget = NormalizeTarget(normalizedScope, targetId);
            var now = DateTime.UtcNow.ToString("o");

            try
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();

                foreach (var (key, value) in preferences)
                {
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    if (value == null)
                    {
                        await using var deleteCmd = connection.CreateCommand();
                        deleteCmd.Transaction = (SqliteTransaction)transaction;
                        deleteCmd.CommandText = @"
                            DELETE FROM UserPreferences 
                            WHERE UserId = $userId AND Scope = $scope AND TargetId = $targetId AND Key = $key;
                        ";
                        deleteCmd.Parameters.AddWithValue("$userId", userId);
                        deleteCmd.Parameters.AddWithValue("$scope", normalizedScope);
                        deleteCmd.Parameters.AddWithValue("$targetId", normalizedTarget);
                        deleteCmd.Parameters.AddWithValue("$key", key);
                        await deleteCmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        await using var upsertCmd = connection.CreateCommand();
                        upsertCmd.Transaction = (SqliteTransaction)transaction;
                        upsertCmd.CommandText = @"
                            INSERT INTO UserPreferences (UserId, Scope, TargetId, Key, Value, UpdatedAt)
                            VALUES ($userId, $scope, $targetId, $key, $value, $updatedAt)
                            ON CONFLICT(UserId, Scope, TargetId, Key) DO UPDATE SET
                                Value = excluded.Value,
                                UpdatedAt = excluded.UpdatedAt;
                        ";
                        upsertCmd.Parameters.AddWithValue("$userId", userId);
                        upsertCmd.Parameters.AddWithValue("$scope", normalizedScope);
                        upsertCmd.Parameters.AddWithValue("$targetId", normalizedTarget);
                        upsertCmd.Parameters.AddWithValue("$key", key);
                        upsertCmd.Parameters.AddWithValue("$value", value);
                        upsertCmd.Parameters.AddWithValue("$updatedAt", now);
                        await upsertCmd.ExecuteNonQueryAsync();
                    }
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to set preferences for user {UserId}, scope={Scope}, target={Target}", userId, scope, targetId);
                throw;
            }
        }

        /// <summary>
        /// Deletes a specific preference key or all preferences at a scope.
        /// </summary>
        public async Task<bool> DeletePreferenceAsync(string userId, string scope, string targetId, string? key = null)
        {
            var normalizedScope = NormalizeScope(scope);
            var normalizedTarget = NormalizeTarget(normalizedScope, targetId);

            try
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await using var cmd = connection.CreateCommand();
                if (string.IsNullOrWhiteSpace(key))
                {
                    cmd.CommandText = @"
                        DELETE FROM UserPreferences 
                        WHERE UserId = $userId AND Scope = $scope AND TargetId = $targetId;
                    ";
                }
                else
                {
                    cmd.CommandText = @"
                        DELETE FROM UserPreferences 
                        WHERE UserId = $userId AND Scope = $scope AND TargetId = $targetId AND Key = $key;
                    ";
                    cmd.Parameters.AddWithValue("$key", key);
                }

                cmd.Parameters.AddWithValue("$userId", userId);
                cmd.Parameters.AddWithValue("$scope", normalizedScope);
                cmd.Parameters.AddWithValue("$targetId", normalizedTarget);

                var rows = await cmd.ExecuteNonQueryAsync();
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to delete preferences for user {UserId}, scope={Scope}, target={Target}, key={Key}", userId, scope, targetId, key);
                return false;
            }
        }

        /// <summary>
        /// Completely resets all preferences for a user back to factory defaults.
        /// </summary>
        public async Task<bool> ResetUserPreferencesAsync(string userId)
        {
            try
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM UserPreferences WHERE UserId = $userId;";
                cmd.Parameters.AddWithValue("$userId", userId);

                await cmd.ExecuteNonQueryAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to reset preferences for user {UserId}", userId);
                return false;
            }
        }

        /// <summary>
        /// Summarizes all custom system overrides configured for a user.
        /// </summary>
        public async Task<List<OverrideSummaryItem>> GetAllOverridesSummaryAsync(string userId)
        {
            var list = new List<OverrideSummaryItem>();
            try
            {
                await using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT Scope, TargetId, COUNT(*), MAX(UpdatedAt)
                    FROM UserPreferences
                    WHERE UserId = $userId AND Scope = 'system'
                    GROUP BY Scope, TargetId
                    ORDER BY TargetId;
                ";
                cmd.Parameters.AddWithValue("$userId", userId);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var scope = reader.GetString(0);
                    var targetId = reader.GetString(1);
                    var count = reader.GetInt32(2);
                    var updatedStr = reader.GetString(3);
                    var updated = DateTime.TryParse(updatedStr, out var d) ? d : DateTime.UtcNow;

                    list.Add(new OverrideSummaryItem(scope, targetId, count, updated));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to fetch override summary for user {UserId}", userId);
            }

            return list;
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
            return targetId?.Trim() ?? string.Empty;
        }
    }
}
