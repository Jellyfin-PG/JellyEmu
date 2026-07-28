using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Entities;
using System.Security.Cryptography;
using System.IO;
using System.Security.Claims;
using Microsoft.Data.Sqlite;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Shared base for all JellyEmu controllers.
    /// Provides injected services, path helpers, and read/write helpers for
    /// playtime, slot prefs, and full user prefs.
    /// </summary>
    [ApiController]
    public abstract class JellyEmuBaseController : ControllerBase
    {
        protected bool VerifyUser(string userId)
        {
            var authenticatedUserId = User.FindFirstValue("Jellyfin-UserId")
                                   ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(authenticatedUserId, out var authGuid) ||
                !Guid.TryParse(userId, out var targetGuid) ||
                authGuid != targetGuid)
            {
                return false;
            }
            return true;
        }
        protected readonly ILibraryManager LibraryManager;
        protected readonly IApplicationPaths AppPaths;
        protected readonly ILogger Logger;
        protected readonly JellyEmuEjsManager EjsManager;
        protected readonly JellyEmuSessionService SessionService;
        protected readonly IHttpClientFactory HttpClientFactory;

        protected record CoreInfo(string Core, bool NeedsThreads, string Launcher);

        protected JellyEmuBaseController(
            ILibraryManager libraryManager,
            IApplicationPaths appPaths,
            ILogger logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuSessionService sessionService,
            IHttpClientFactory httpClientFactory)
        {
            LibraryManager = libraryManager;
            AppPaths = appPaths;
            Logger = logger;
            EjsManager = ejsManager;
            SessionService = sessionService;
            HttpClientFactory = httpClientFactory;
        }

        protected record UserPrefs(int Slot, string Shader, int VideoRotation);

        protected record UserFullPrefs(
            string Scale,
            string Mute,
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
            string VirtualGamepadLefty);

        protected static readonly UserFullPrefs DefaultFullPrefs =
            new("fit", "false", "auto", "true", "true", string.Empty, 0, string.Empty, string.Empty, string.Empty, string.Empty, "false", "false");

        /// <summary>
        /// Maps Jellyfin console tag names to EmulatorJS core identifiers.
        /// https://emulatorjs.org/docs4devs/cores
        /// </summary>
        protected static readonly Dictionary<string, string> CoreMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "NES",              "nes"        },
                { "SNES",             "snes"       },
                { "N64",              "n64"         },
                { "Game Boy",         "gb"          },
                { "Game Boy Color",   "gb"          },
                { "Game Boy Advance", "gba"         },
                { "Nintendo DS",      "nds"         },
                { "Virtual Boy",      "vb"          },
                { "Master System",    "segaMS"      },
                { "Game Gear",        "segaGG"      },
                { "Sega Genesis",     "segaMD"      },
                { "Sega CD",          "segaCD"      },
                { "Sega 32X",         "sega32x"     },
                { "Sega Saturn",      "segaSaturn"  },
                { "PlayStation",      "psx"         },
                { "PSP",              "psp"         },
                { "3DO",              "3do"         },
                { "Atari 2600",       "atari2600"   },
                { "Atari 5200",       "a5200"       },
                { "Atari 7800",       "atari7800"   },
                { "Atari Lynx",       "lynx"        },
                { "Atari Jaguar",     "jaguar"      },
                { "WonderSwan",       "ws"          },
                { "TurboGrafx-16",    "pce"         },
                { "PC-FX",            "pcfx"        },
                { "ColecoVision",     "coleco"      },
                { "NeoGeo Pocket",    "ngp"         },
                { "Commodore 64",     "c64"         },
                { "Commodore 128",    "c128"        },
                { "Commodore Amiga",  "amiga"       },
                { "Commodore PET",    "pet"         },
                { "Commodore Plus/4", "plus4"       },
                { "Commodore VIC-20", "vic20"       },
                { "Arcade",           "arcade"      },
                { "MAME 2003",        "mame2003_plus" },
                { "DOS",              "dos"         },
                { "PICO-8",           "pico8"       },
            };

        /// <summary>
        /// Maps ROM file extensions to EmulatorJS core identifiers, used when an
        /// item carries no recognised console tag.
        /// https://emulatorjs.org/docs4devs/cores
        /// </summary>
        protected static readonly Dictionary<string, string> ExtensionCoreMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // NES
                { "nes", "nes" }, { "fds", "nes" }, { "unf", "nes" }, { "unif", "nes" },
                // SNES
                { "smc", "snes" }, { "sfc", "snes" }, { "swc", "snes" }, { "fig", "snes" },
                // N64
                { "z64", "n64" }, { "n64", "n64" }, { "v64", "n64" },
                // Game Boy / Game Boy Color (both run on the gambatte-backed "gb" core)
                { "gb", "gb" }, { "gbc", "gb" },
                // GBA
                { "gba", "gba" },
                // NDS
                { "nds", "nds" },
                // Virtual Boy
                { "vb", "vb" },
                // Sega
                { "sms", "segaMS" },
                { "gg",  "segaGG" },
                { "md",  "segaMD" }, { "smd", "segaMD" }, { "gen", "segaMD" }, { "68k", "segaMD" },
                { "32x", "sega32x" },
                // PlayStation
                { "pbp", "psx" }, { "cue", "psx" }, { "chd", "psx" },
                // PSP
                { "cso", "psp" }, { "iso", "psp" },
                // Atari
                { "a26", "atari2600" },
                { "a78", "atari7800" },
                { "lnx", "lynx" },
                { "jag", "jaguar" }, { "j64", "jaguar" },
                // WonderSwan
                { "ws",  "ws" }, { "wsc", "ws" },
                // TurboGrafx-16
                { "pce", "pce" },
                // ColecoVision
                { "col", "coleco" }, { "cv", "coleco" },
                // NeoGeo Pocket
                { "ngp", "ngp" }, { "ngc", "ngp" },
                // Commodore 64
                { "d64", "c64" }, { "t64", "c64" }, { "crt", "c64" },
                { "tap", "c64" }, { "prg", "c64" },
                // Amiga
                { "adf", "amiga" }, { "dms", "amiga" }, { "ipf", "amiga" }, { "adz", "amiga" },
                // PICO-8
                { "p8", "pico8" },
            };

        /// <summary>
        /// Maps Jellyfin console tags to their libretro cheat database folder names.
        /// https://github.com/libretro/libretro-database/tree/master/cht
        /// </summary>
        protected static readonly Dictionary<string, string> CheatDbFolderMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "NES",              "Nintendo - Nintendo Entertainment System"       },
                { "SNES",             "Nintendo - Super Nintendo Entertainment System" },
                { "N64",              "Nintendo - Nintendo 64"                         },
                { "Game Boy",         "Nintendo - Game Boy"                            },
                { "Game Boy Color",   "Nintendo - Game Boy Color"                      },
                { "Game Boy Advance", "Nintendo - Game Boy Advance"                    },
                { "Nintendo DS",      "Nintendo - Nintendo DS"                         },
                { "Virtual Boy",      "Nintendo - Virtual Boy"                         },
                { "Master System",    "Sega - Master System - Mark III"                },
                { "Game Gear",        "Sega - Game Gear"                               },
                { "Sega Genesis",     "Sega - Mega Drive - Genesis"                    },
                { "Sega CD",          "Sega - Mega-CD - Sega CD"                       },
                { "Sega 32X",         "Sega - 32X"                                     },
                { "PlayStation",      "Sony - PlayStation"                              },
                { "PSP",              "Sony - PlayStation Portable"                     },
                { "Atari 2600",       "Atari - 2600"                                   },
                { "Atari 7800",       "Atari - 7800"                                   },
                { "Atari Lynx",       "Atari - Lynx"                                   },
                { "TurboGrafx-16",    "NEC - PC Engine - TurboGrafx 16"               },
                { "ColecoVision",     "Coleco - ColecoVision"                          },
                { "NeoGeo Pocket",    "SNK - Neo Geo Pocket Color"                     },
                { "Arcade",           "FBNeo - Arcade Games"                           },
            };

        // Saves:    {DataPath}/jellyemu-saves/{userId}/slot{slot}/{itemId}.state
        // Slot/prefs: {DataPath}/jellyemu-saves/{userId}/active-slot.json
        // Full prefs: {DataPath}/jellyemu-saves/{userId}/prefs.json
        // Playtime:  {DataPath}/jellyemu-saves/{userId}/playtime.json

        protected string GetSavePath(string userId, string itemId, int slot)
        {
            var dir = Path.Combine(AppPaths.DataPath, "jellyemu-saves", userId, $"slot{slot}");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{itemId}.state");
        }

        protected string GetSramPath(string userId, string itemId, int slot)
        {
            var dir = Path.Combine(AppPaths.DataPath, "jellyemu-saves", userId, $"slot{slot}");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{itemId}.sav");
        }

        protected string GetSaveScreenshotPath(string userId, string itemId, int slot)
        {
            var dir = Path.Combine(AppPaths.DataPath, "jellyemu-saves", userId, $"slot{slot}");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{itemId}.screenshot.json");
        }

        protected string GetSlotFilePath(string userId)
        {
            var dir = Path.Combine(AppPaths.DataPath, "jellyemu-saves", userId);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "active-slot.json");
        }

        protected string GetPlaytimePath(string userId)
        {
            var dir = Path.Combine(AppPaths.DataPath, "jellyemu-saves", userId);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "playtime.json");
        }

        protected string GetPrefsFilePath(string userId)
        {
            var dir = Path.Combine(AppPaths.DataPath, "jellyemu-saves", userId);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "prefs.json");
        }

        private static bool _dbInitialized = false;
        private static readonly object _dbLock = new object();

        protected void EnsureDatabaseCreated()
        {
            if (_dbInitialized) return;
            lock (_dbLock)
            {
                if (_dbInitialized) return;

                var dbPath = Path.Combine(AppPaths.DataPath, "jellyemu-playtime.db");
                var connectionString = $"Data Source={dbPath}";

                try
                {
                    using (var connection = new SqliteConnection(connectionString))
                    {
                        connection.Open();
                        using var createTableCommand = connection.CreateCommand();
                        createTableCommand.CommandText =
                            @"CREATE TABLE IF NOT EXISTS Playtime (
                                UserId TEXT NOT NULL,
                                ItemId TEXT NOT NULL,
                                Seconds INTEGER NOT NULL,
                                PRIMARY KEY (UserId, ItemId)
                            );";
                        createTableCommand.ExecuteNonQuery();
                    }

                    // Run migration for existing playtime.json files
                    MigrateLegacyPlaytime(dbPath);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[JellyEmu] Failed to initialize SQLite database or migrate legacy playtime.");
                }

                _dbInitialized = true;
            }
        }

        private void MigrateLegacyPlaytime(string dbPath)
        {
            var savesDir = Path.Combine(AppPaths.DataPath, "jellyemu-saves");
            if (!Directory.Exists(savesDir)) return;

            var userDirs = Directory.GetDirectories(savesDir);
            var connectionString = $"Data Source={dbPath}";

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            foreach (var userDir in userDirs)
            {
                var userId = Path.GetFileName(userDir);
                if (!Guid.TryParse(userId, out _)) continue;

                var jsonPath = Path.Combine(userDir, "playtime.json");
                if (System.IO.File.Exists(jsonPath))
                {
                    try
                    {
                        var json = System.IO.File.ReadAllText(jsonPath);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        
                        using var transaction = connection.BeginTransaction();
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            var itemId = prop.Name;
                            var seconds = prop.Value.GetInt64();

                            using var insertCommand = connection.CreateCommand();
                            insertCommand.Transaction = transaction;
                            insertCommand.CommandText =
                                @"INSERT INTO Playtime (UserId, ItemId, Seconds)
                                  VALUES ($userId, $itemId, $seconds)
                                  ON CONFLICT(UserId, ItemId) DO UPDATE SET
                                    Seconds = Seconds + excluded.Seconds;";
                            insertCommand.Parameters.AddWithValue("$userId", userId);
                            insertCommand.Parameters.AddWithValue("$itemId", itemId);
                            insertCommand.Parameters.AddWithValue("$seconds", seconds);
                            insertCommand.ExecuteNonQuery();
                        }
                        transaction.Commit();

                        // Rename legacy file to prevent re-migration
                        var migratedPath = Path.Combine(userDir, "playtime.json.migrated");
                        if (System.IO.File.Exists(migratedPath)) System.IO.File.Delete(migratedPath);
                        System.IO.File.Move(jsonPath, migratedPath);

                        Logger.LogInformation("[JellyEmu] Successfully migrated playtime.json to SQLite for user {UserId}", userId);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "[JellyEmu] Failed to migrate playtime.json for user {UserId}", userId);
                    }
                }
            }
        }

        protected long ReadPlaytimeSeconds(string userId, string itemId)
        {
            EnsureDatabaseCreated();
            var dbPath = Path.Combine(AppPaths.DataPath, "jellyemu-playtime.db");
            var connectionString = $"Data Source={dbPath}";

            try
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT Seconds FROM Playtime WHERE UserId = $userId AND ItemId = $itemId LIMIT 1;";
                selectCommand.Parameters.AddWithValue("$userId", userId);
                selectCommand.Parameters.AddWithValue("$itemId", itemId);

                var result = selectCommand.ExecuteScalar();
                return result != null ? (long)result : 0;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to read playtime from SQLite for user {UserId}, itemId {ItemId}", userId, itemId);
                return 0;
            }
        }

        protected void AddPlaytimeSeconds(string userId, string itemId, long seconds)
        {
            if (seconds <= 0) return;
            EnsureDatabaseCreated();
            var dbPath = Path.Combine(AppPaths.DataPath, "jellyemu-playtime.db");
            var connectionString = $"Data Source={dbPath}";

            try
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                using var insertCommand = connection.CreateCommand();
                insertCommand.CommandText =
                    @"INSERT INTO Playtime (UserId, ItemId, Seconds)
                      VALUES ($userId, $itemId, $seconds)
                      ON CONFLICT(UserId, ItemId) DO UPDATE SET
                        Seconds = Seconds + excluded.Seconds;";
                insertCommand.Parameters.AddWithValue("$userId", userId);
                insertCommand.Parameters.AddWithValue("$itemId", itemId);
                insertCommand.Parameters.AddWithValue("$seconds", seconds);

                insertCommand.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[JellyEmu] Failed to save/update playtime in SQLite for user {UserId}, itemId {ItemId}", userId, itemId);
            }
        }

        protected UserPrefs ReadUserPrefs(string userId)
        {
            var path = GetSlotFilePath(userId);
            if (!System.IO.File.Exists(path)) return new UserPrefs(1, string.Empty, 0);
            try
            {
                var json = System.IO.File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                var slot   = root.TryGetProperty("slot",          out var s)  ? Math.Max(1, s.GetInt32())    : 1;
                var shader = root.TryGetProperty("shader",         out var sh) ? (sh.GetString() ?? string.Empty) : string.Empty;
                var rot    = root.TryGetProperty("videoRotation",  out var r)  ? r.GetInt32()                 : 0;
                return new UserPrefs(slot, shader, rot);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Slot prefs file corrupt for user {UserId}. Returning defaults.", userId);
                return new UserPrefs(1, string.Empty, 0);
            }
        }

        protected UserFullPrefs ReadFullPrefs(string userId)
        {
            var path = GetPrefsFilePath(userId);
            if (!System.IO.File.Exists(path)) return DefaultFullPrefs;
            try
            {
                var json = System.IO.File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var r = doc.RootElement;
                string Str(string key, string def) =>
                    r.TryGetProperty(key, out var v) ? (v.GetString() ?? def) : def;
                int Int(string key, int def) =>
                    r.TryGetProperty(key, out var v) ? v.GetInt32() : def;
                 return new UserFullPrefs(
                    Scale:              Str("scale",              DefaultFullPrefs.Scale),
                    Mute:               Str("mute",               DefaultFullPrefs.Mute),
                    Controller:         Str("controller",         DefaultFullPrefs.Controller),
                    Haptics:            Str("haptics",            DefaultFullPrefs.Haptics),
                    Autosave:           Str("autosave",           DefaultFullPrefs.Autosave),
                    Shader:             Str("shader",             DefaultFullPrefs.Shader),
                    VideoRotation:      Int("videoRotation",      DefaultFullPrefs.VideoRotation),
                    Controls:           Str("controls",           DefaultFullPrefs.Controls),
                    ControllerControls: Str("controllerControls", DefaultFullPrefs.ControllerControls),
                    RaUsername:         Str("raUsername",         DefaultFullPrefs.RaUsername),
                    RaApiKey:           Str("raApiKey",           DefaultFullPrefs.RaApiKey),
                    VirtualGamepad:     Str("virtualGamepad",     DefaultFullPrefs.VirtualGamepad),
                    VirtualGamepadLefty:Str("virtualGamepadLefty",DefaultFullPrefs.VirtualGamepadLefty));
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[JellyEmu] Prefs file corrupt for user {UserId}. Returning defaults.", userId);
                return DefaultFullPrefs;
            }
        }
 
        protected void WriteFullPrefs(string userId, UserFullPrefs prefs)
        {
            var path = GetPrefsFilePath(userId);
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
            {
                scale              = prefs.Scale,
                mute               = prefs.Mute,
                controller         = prefs.Controller,
                haptics            = prefs.Haptics,
                autosave           = prefs.Autosave,
                shader             = prefs.Shader,
                videoRotation      = prefs.VideoRotation,
                controls           = prefs.Controls,
                controllerControls = prefs.ControllerControls,
                raUsername         = prefs.RaUsername,
                raApiKey           = prefs.RaApiKey,
                virtualGamepad     = prefs.VirtualGamepad,
                virtualGamepadLefty= prefs.VirtualGamepadLefty
            }));
        }

        /// <summary>
        /// Resolves the EmulatorJS core for an item, preferring its console tag and
        /// falling back to the file extension.
        /// Returns <see cref="string.Empty"/> when the platform cannot be determined —
        /// callers must treat that as a failure rather than guessing a core.
        /// </summary>
        protected internal static string ResolveCore(MediaBrowser.Controller.Entities.BaseItem item)
        {
            if (item.Tags != null)
            {
                foreach (var tag in item.Tags)
                    if (CoreMap.TryGetValue(tag, out var core))
                        return core;
            }

            if (!string.IsNullOrEmpty(item.Path))
            {
                if (item.Path.EndsWith(".p8.png", StringComparison.OrdinalIgnoreCase))
                    return "pico8";

                var ext = Path.GetExtension(item.Path).TrimStart('.');
                if (ExtensionCoreMap.TryGetValue(ext, out var extCore))
                    return extCore;
            }

            return string.Empty;
        }

        protected static CoreInfo ResolveCoreInfo(MediaBrowser.Controller.Entities.BaseItem item)
        {
            var core = ResolveCore(item);
            return core switch
            {
                "pico8"    => new CoreInfo(core, false, "pico8"),
                "dos"      => new CoreInfo(core, true,  "ejs"),
                "psp"      => new CoreInfo(core, true,  "ejs"),
                "arcade"        => new CoreInfo(core, true,  "ejs"),
                "mame2003_plus" => new CoreInfo(core, true,  "ejs"),
                "amiga"         => new CoreInfo(core, true,  "ejs"),
                "3do"           => new CoreInfo(core, true,  "ejs"),
                "segaSaturn"    => new CoreInfo(core, true,  "ejs"),
                "jaguar"        => new CoreInfo(core, true,  "ejs"),
                _               => new CoreInfo(core, false, "ejs"),
            };
        }

        protected static string RommInstanceUrl =>
            (Plugin.Instance?.Configuration.RommInstanceUrl ?? string.Empty).TrimEnd('/');

        protected static bool RommEnabled =>
            Plugin.Instance?.Configuration.RommEnabled == true;

        protected HttpClient GetRommClient()
        {
            var client = HttpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JellyEmu/1.0");
            var cfg = Plugin.Instance?.Configuration;
            if (cfg == null) return client;

            var username = cfg.RommUsername;
            var password = cfg.RommPassword;
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                var creds = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Add("Authorization", $"Basic {creds}");
            }
            return client;
        }

        protected string? GetRommIdForItem(string itemId)
        {
            try
            {
                var item = LibraryManager.GetItemById(itemId);
                return item?.GetProviderId("Romm");
            }
            catch { return null; }
        }

        protected string GetFileHash(string path)
        {
            return RomExtensions.GetFileHash(path);
        }
    }
}
