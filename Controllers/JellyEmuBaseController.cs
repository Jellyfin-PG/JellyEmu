using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Entities;
using System.IO;
using System.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
        /// <summary>
        /// Gets whether or not the browser should consider the origin trustworthy.
        /// HTTPS or localhost are trustworthy in most browsers. This is important
        /// for determining whether or not to send the
        /// "Cross-Origin-Opener-Policy" header.
        /// </summary>
        protected bool IsTrustworthyOrigin()
        {
            if (Request.IsHttps) return true;

            var proto = Request.Headers["X-Forwarded-Proto"].ToString();
            if (proto.Contains("https", StringComparison.OrdinalIgnoreCase)) return true;

            var host = Request.Host.Host;
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host is "127.0.0.1" or "::1" or "[::1]"
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Applies the cross-origin isolation headers.
        /// This is skipped on insecure requests to avoid browser security errors.
        /// </summary>
        protected void ApplyCrossOriginIsolationHeaders()
        {
            if (!IsTrustworthyOrigin()) return;

            Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";
        }

        protected readonly ILibraryManager LibraryManager;
        protected readonly IApplicationPaths AppPaths;
        protected readonly ILogger Logger;
        protected readonly JellyEmuEjsManager EjsManager;
        protected readonly JellyEmuSessionService SessionService;
        protected readonly IHttpClientFactory HttpClientFactory;

        private JellyEmuPreferenceService? _preferenceService;
        protected JellyEmuPreferenceService PreferenceService => _preferenceService ??= 
            HttpContext?.RequestServices?.GetService(typeof(JellyEmuPreferenceService)) as JellyEmuPreferenceService 
            ?? new JellyEmuPreferenceService(AppPaths, null!);

        public record CoreInfo(string Core, bool NeedsThreads, string Launcher);

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

        public record UserPrefs(int Slot, string Shader, int VideoRotation);

        public record UserFullPrefs(
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
            string VirtualGamepadLefty,
            string PlatformCores = "{}",
            string GameCores = "{}");

        protected static readonly UserFullPrefs DefaultFullPrefs =
            new("fit", "false", "auto", "true", "true", string.Empty, 0, string.Empty, string.Empty, string.Empty, string.Empty, "false", "false", "{}", "{}");

        public record CoreOption(string Id, string Name, bool NeedsThreads);

        public static readonly Dictionary<string, List<CoreOption>> PlatformCoreRegistry =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "PlayStation", new List<CoreOption>
                    {
                        new("pcsx_rearmed", "PCSX ReARMed", true),
                        new("mednafen_psx_hw", "Beetle PSX HW", true)
                    }
                },
                { "Arcade", new List<CoreOption>
                    {
                        new("fbneo", "FinalBurn Neo", true),
                        new("mame2003_plus", "MAME 2003-Plus", true)
                    }
                },
                { "MAME 2003", new List<CoreOption>
                    {
                        new("mame2003_plus", "MAME 2003-Plus", true),
                        new("mame2003", "MAME 2003", true),
                        new("fbneo", "FinalBurn Neo", true)
                    }
                },
                { "SNES", new List<CoreOption>
                    {
                        new("snes9x", "Snes9x", true)
                    }
                },
                { "NES", new List<CoreOption>
                    {
                        new("nestopia", "Nestopia", true),
                        new("fceumm", "FCEUmm", true)
                    }
                },
                { "N64", new List<CoreOption>
                    {
                        new("mupen64plus_next", "Mupen64Plus Next", true),
                        new("parallel_n64", "ParaLLEl N64", true)
                    }
                },
                { "Game Boy Advance", new List<CoreOption>
                    {
                        new("mgba", "mGBA", true)
                    }
                },
                { "Game Boy", new List<CoreOption>
                    {
                        new("gambatte", "Gambatte", true),
                        new("mgba", "mGBA", true)
                    }
                },
                { "Game Boy Color", new List<CoreOption>
                    {
                        new("gambatte", "Gambatte", true),
                        new("mgba", "mGBA", true)
                    }
                },
                { "Nintendo DS", new List<CoreOption>
                    {
                        new("desmume", "DeSmuME", true),
                        new("melonds", "melonDS", true)
                    }
                },
                { "Nintendo 3DS", new List<CoreOption>
                    {
                        new("azahar", "Azahar", true)
                    }
                },
                { "Sega Genesis", new List<CoreOption>
                    {
                        new("genesis_plus_gx", "Genesis Plus GX", true),
                        new("picodrive", "PicoDrive", true)
                    }
                },
                { "Sega CD", new List<CoreOption>
                    {
                        new("genesis_plus_gx", "Genesis Plus GX", true),
                        new("picodrive", "PicoDrive", true)
                    }
                },
                { "Sega 32X", new List<CoreOption>
                    {
                        new("picodrive", "PicoDrive", true)
                    }
                },
                { "Master System", new List<CoreOption>
                    {
                        new("genesis_plus_gx", "Genesis Plus GX", true),
                        new("smsplus", "SMS Plus", true)
                    }
                },
                { "Game Gear", new List<CoreOption>
                    {
                        new("genesis_plus_gx", "Genesis Plus GX", true),
                        new("smsplus", "SMS Plus", true)
                    }
                },
                { "Sega Saturn", new List<CoreOption>
                    {
                        new("yabause", "Yabause", true)
                    }
                },
                { "PSP", new List<CoreOption>
                    {
                        new("ppsspp", "PPSSPP", true)
                    }
                },
                { "3DO", new List<CoreOption>
                    {
                        new("opera", "Opera", true)
                    }
                },
                { "Atari 2600", new List<CoreOption>
                    {
                        new("stella2014", "Stella 2014", true)
                    }
                },
                { "Atari 5200", new List<CoreOption>
                    {
                        new("a5200", "Atari 5200", true)
                    }
                },
                { "Atari 7800", new List<CoreOption>
                    {
                        new("prosystem", "ProSystem", true)
                    }
                },
                { "Atari Lynx", new List<CoreOption>
                    {
                        new("handy", "Handy", true)
                    }
                },
                { "Atari Jaguar", new List<CoreOption>
                    {
                        new("virtualjaguar", "Virtual Jaguar", true)
                    }
                },
                { "DOS", new List<CoreOption>
                    {
                        new("dosbox_pure", "DOSBox Pure", true)
                    }
                },
                { "Commodore Amiga", new List<CoreOption>
                    {
                        new("puae", "PUAE", true)
                    }
                },
                { "Commodore 64", new List<CoreOption>
                    {
                        new("vice_x64", "VICE x64", true),
                        new("vice_x64sc", "VICE x64sc", true)
                    }
                },
                { "Virtual Boy", new List<CoreOption>
                    {
                        new("beetle_vb", "Beetle VB", true)
                    }
                },
                { "PICO-8", new List<CoreOption>
                    {
                        new("pico8", "Lexaloffle HTML5", false)
                    }
                }
            };

        /// <summary>
        /// Maps Jellyfin console tag names to EmulatorJS core identifiers.
        /// https://emulatorjs.org/docs4devs/cores
        /// </summary>
        protected static readonly Dictionary<string, string> CoreMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "NES",              "nestopia"      },
                { "SNES",             "snes9x"        },
                { "N64",              "mupen64plus_next" },
                { "Game Boy",         "gambatte"      },
                { "Game Boy Color",   "gambatte"      },
                { "Game Boy Advance", "mgba"          },
                { "Nintendo DS",      "desmume"       },
                { "Nintendo 3DS",     "azahar"        },
                { "Virtual Boy",      "beetle_vb"     },
                { "Master System",    "genesis_plus_gx" },
                { "Game Gear",        "genesis_plus_gx" },
                { "Sega Genesis",     "genesis_plus_gx" },
                { "Sega CD",          "genesis_plus_gx" },
                { "Sega 32X",         "picodrive"     },
                { "Sega Saturn",      "yabause"       },
                { "PlayStation",      "pcsx_rearmed"  },
                { "PSP",              "ppsspp"        },
                { "3DO",              "opera"         },
                { "Atari 2600",       "stella2014"    },
                { "Atari 5200",       "a5200"         },
                { "Atari 7800",       "prosystem"     },
                { "Atari Lynx",       "handy"         },
                { "Atari Jaguar",     "virtualjaguar" },
                { "WonderSwan",       "mednafen_wswan"},
                { "TurboGrafx-16",    "mednafen_pce"  },
                { "PC-FX",            "mednafen_pcfx" },
                { "ColecoVision",     "gearcoleco"    },
                { "NeoGeo Pocket",    "mednafen_ngp"  },
                { "Commodore 64",     "vice_x64"      },
                { "Commodore 128",    "vice_x128"     },
                { "Commodore Amiga",  "puae"          },
                { "Commodore PET",    "vice_xpet"     },
                { "Commodore Plus/4", "vice_xplus4"   },
                { "Commodore VIC-20", "vice_xvic"     },
                { "Arcade",           "fbneo"         },
                { "MAME 2003",        "mame2003_plus" },
                { "DOS",              "dosbox_pure"   },
                { "PICO-8",           "pico8"         },
            };

        /// <summary>
        /// Maps ROM file extensions to core identifiers, used when an item carries
        /// no recognised console tag.
        /// https://emulatorjs.org/docs4devs/cores
        /// </summary>
        protected static readonly Dictionary<string, string> ExtensionCoreMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // NES
                { "nes", "nestopia" }, { "fds", "nestopia" }, { "unf", "nestopia" }, { "unif", "nestopia" },
                // SNES
                { "smc", "snes9x" }, { "sfc", "snes9x" }, { "swc", "snes9x" }, { "fig", "snes9x" },
                // N64
                { "z64", "mupen64plus_next" }, { "n64", "mupen64plus_next" }, { "v64", "mupen64plus_next" },
                // Game Boy / Game Boy Color (both run on gambatte)
                { "gb", "gambatte" }, { "gbc", "gambatte" },
                // GBA
                { "gba", "mgba" },
                // Nintendo DS
                { "nds", "desmume" },
                // Nintendo 3DS
                { "3ds", "azahar" }, { "cci", "azahar" }, { "cia", "azahar" },
                // Virtual Boy
                { "vb", "beetle_vb" },
                // Sega
                { "sms", "genesis_plus_gx" },
                { "gg",  "genesis_plus_gx" },
                { "md",  "genesis_plus_gx" }, { "smd", "genesis_plus_gx" }, { "gen", "genesis_plus_gx" }, { "68k", "genesis_plus_gx" },
                { "32x", "picodrive" },
                // PlayStation
                { "pbp", "pcsx_rearmed" }, { "cue", "pcsx_rearmed" }, { "chd", "pcsx_rearmed" }, { "bin", "pcsx_rearmed" }, { "img", "pcsx_rearmed" }, { "iso", "pcsx_rearmed" },
                // PSP
                { "cso", "ppsspp" },
                // Atari
                { "a26", "stella2014" }, { "a78", "prosystem" }, { "lnx", "handy" }, { "jag", "virtualjaguar" }, { "j64", "virtualjaguar" },
                // Commodore
                { "d64", "vice_x64" }, { "t64", "vice_x64" }, { "crt", "vice_x64" }, { "prg", "vice_x64" }, { "adf", "puae" },
                // DOS
                { "exe", "dosbox_pure" }, { "com", "dosbox_pure" }, { "bat", "dosbox_pure" },
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
                { "Nintendo 3DS",     "Nintendo - Nintendo 3DS"                        },
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

        private static readonly Regex SafeIdRegex = new Regex("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);

        [NonAction]
        public static bool IsValidId(string? id)
        {
            return !string.IsNullOrWhiteSpace(id) && SafeIdRegex.IsMatch(id);
        }

        [NonAction]
        public static string SanitizeForLog(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Replace("\r", string.Empty).Replace("\n", string.Empty);
        }

        [NonAction]
        protected string GetSafeUserSavesDir(string userId)
        {
            if (!IsValidId(userId))
                throw new ArgumentException("Invalid user ID format.", nameof(userId));

            var safeUserId = userId.TrimStart('/', '\\');
            var baseDir = Path.GetFullPath(Path.Combine(AppPaths.DataPath, "jellyemu-saves"));
            var userDir = Path.GetFullPath(Path.Combine(baseDir, safeUserId));
            if (!userDir.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Path traversal detected in user ID.");

            return userDir;
        }

        [NonAction]
        protected string GetSafeSlotDir(string userId, int slot)
        {
            var userDir = GetSafeUserSavesDir(userId);
            var slotNum = Math.Max(1, slot);
            var safeSlotName = $"slot{slotNum}".TrimStart('/', '\\');
            var slotDir = Path.GetFullPath(Path.Combine(userDir, safeSlotName));
            if (!slotDir.StartsWith(userDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Path traversal detected in slot.");

            Directory.CreateDirectory(slotDir);
            return slotDir;
        }

        [NonAction]
        protected string GetSafeSaveFilePath(string userId, string itemId, int slot, string extension)
        {
            if (!IsValidId(itemId))
                throw new ArgumentException("Invalid item ID format.", nameof(itemId));

            var slotDir = GetSafeSlotDir(userId, slot);
            var cleanExt = extension.TrimStart('.', '/', '\\');
            var safeFileName = $"{itemId}.{cleanExt}".TrimStart('/', '\\');
            var filePath = Path.GetFullPath(Path.Combine(slotDir, safeFileName));
            if (!filePath.StartsWith(slotDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Path traversal detected in item ID.");

            return filePath;
        }

        protected string GetSavePath(string userId, string itemId, int slot)
        {
            return GetSafeSaveFilePath(userId, itemId, slot, "state");
        }

        protected string GetSramPath(string userId, string itemId, int slot)
        {
            return GetSafeSaveFilePath(userId, itemId, slot, "sav");
        }

        protected string GetSaveScreenshotPath(string userId, string itemId, int slot)
        {
            return GetSafeSaveFilePath(userId, itemId, slot, "screenshot.json");
        }

        protected string GetPlaytimePath(string userId)
        {
            var dir = GetSafeUserSavesDir(userId);
            Directory.CreateDirectory(dir);
            var filePath = Path.GetFullPath(Path.Join(dir, "playtime.json"));
            if (!filePath.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Path traversal detected in playtime path.");
            return filePath;
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



        protected static bool IsThreadedCore(string core)
        {
            var lower = (core ?? string.Empty).ToLowerInvariant();
            if (lower == "pico8") return false;
            return true;
        }

        protected static string MapLegacyCore(string? core)
        {
            var lower = (core ?? string.Empty).ToLowerInvariant();
            return lower switch
            {
                "psx" => "pcsx_rearmed",
                "snes" => "snes9x",
                "nes" => "nestopia",
                "n64" => "mupen64plus_next",
                "gba" => "mgba",
                "gb" => "gambatte",
                "gbc" => "gambatte",
                "nds" => "desmume",
                "segamd" => "genesis_plus_gx",
                "segacd" => "genesis_plus_gx",
                "sega32x" => "picodrive",
                "segams" => "genesis_plus_gx",
                "segagg" => "genesis_plus_gx",
                "segasaturn" => "yabause",
                "psp" => "ppsspp",
                "3do" => "opera",
                "atari2600" => "stella2014",
                "atari7800" => "prosystem",
                "lynx" => "handy",
                "jaguar" => "virtualjaguar",
                "dos" => "dosbox_pure",
                "amiga" => "puae",
                "c64" => "vice_x64",
                "arcade" => "fbneo",
                "vb" => "beetle_vb",
                "mednafen_psx" => "mednafen_psx_hw",
                "3ds" or "citra" or "citra_canary" => "azahar",
                _ => core ?? string.Empty
            };
        }

        protected static string ResolvePlatformTag(MediaBrowser.Controller.Entities.BaseItem item)
        {
            var resolver = new PlatformResolver(null!);

            if (item.Tags != null)
            {
                foreach (var tag in item.Tags)
                {
                    if (PlatformCoreRegistry.ContainsKey(tag))
                        return tag;

                    if (PlatformResolver.Aliases.TryGetValue(tag, out var canonical) && PlatformCoreRegistry.ContainsKey(canonical))
                        return canonical;

                    if (CoreMap.TryGetValue(tag, out _))
                    {
                        var resolvedFromTag = resolver.ResolvePlatform(item.Path ?? string.Empty, tag);
                        if (!string.IsNullOrEmpty(resolvedFromTag) && resolvedFromTag != "Unknown")
                            return resolvedFromTag;
                    }
                }
            }

            if (!string.IsNullOrEmpty(item.Path))
            {
                var platform = resolver.ResolvePlatform(item.Path, item.Name);
                if (!string.IsNullOrEmpty(platform) && platform != "Unknown")
                    return platform;
            }

            return "Unknown";
        }

        protected static List<CoreOption> GetAvailableCoresForPlatform(string platformTag)
        {
            if (PlatformCoreRegistry.TryGetValue(platformTag, out var list))
                return list;

            if (CoreMap.TryGetValue(platformTag, out var defaultCore))
            {
                return new List<CoreOption> { new(defaultCore, $"{platformTag} Default", IsThreadedCore(defaultCore)) };
            }

            return new List<CoreOption> { new("nes", "NES Default", false) };
        }

        protected static List<CoreOption> GetAvailableCoresForItem(MediaBrowser.Controller.Entities.BaseItem item)
        {
            var platformTag = ResolvePlatformTag(item);
            var list = GetAvailableCoresForPlatform(platformTag);
            var defaultCore = ResolveCoreDefault(item);

            if (list.Count <= 1 && !string.IsNullOrEmpty(defaultCore))
            {
                foreach (var entry in PlatformCoreRegistry.Values)
                {
                    if (entry.Any(c => string.Equals(c.Id, defaultCore, StringComparison.OrdinalIgnoreCase)))
                    {
                        return entry;
                    }
                }
            }

            if (!string.IsNullOrEmpty(defaultCore) &&
                !list.Any(c => string.Equals(c.Id, defaultCore, StringComparison.OrdinalIgnoreCase)))
            {
                var updatedList = new List<CoreOption>(list)
                {
                    new(defaultCore, $"{defaultCore} (Default)", IsThreadedCore(defaultCore))
                };
                return updatedList;
            }

            return list;
        }

        protected static Dictionary<string, string> ParseCoreDictionary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return dict != null
                    ? new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        protected string ResolveCore(MediaBrowser.Controller.Entities.BaseItem item, string? userId = null, string? queryCoreOverride = null)
        {
            if (!string.IsNullOrWhiteSpace(queryCoreOverride))
                return MapLegacyCore(queryCoreOverride);

            if (!string.IsNullOrWhiteSpace(userId))
            {
                var platformTag = ResolvePlatformTag(item);
                var prefs = PreferenceService.GetEffectivePreferencesAsync(userId, platformTag).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(prefs.Core))
                    return MapLegacyCore(prefs.Core);
            }

            return ResolveCoreDefault(item);
        }

        /// <summary>
        /// Resolves the default core for an item from its console tag, file extension,
        /// then resolved platform.
        /// Returns <see cref="string.Empty"/> when the platform cannot be determined —
        /// callers must treat that as a failure rather than guessing a core.
        /// </summary>
        protected static string ResolveCoreDefault(MediaBrowser.Controller.Entities.BaseItem item)
        {
            if (item.Tags != null)
            {
                foreach (var tag in item.Tags)
                    if (CoreMap.TryGetValue(tag, out var core))
                        return MapLegacyCore(core);
            }

            if (!string.IsNullOrEmpty(item.Path))
            {
                if (item.Path.EndsWith(".p8.png", StringComparison.OrdinalIgnoreCase))
                    return "pico8";

                var ext = Path.GetExtension(item.Path).TrimStart('.');
                if (ExtensionCoreMap.TryGetValue(ext, out var extCore))
                    return MapLegacyCore(extCore);
            }

            var platformTag = ResolvePlatformTag(item);
            if (CoreMap.TryGetValue(platformTag, out var fallbackCore))
                return MapLegacyCore(fallbackCore);

            return string.Empty;
        }

        protected static string ResolveCore(MediaBrowser.Controller.Entities.BaseItem item)
        {
            return ResolveCoreDefault(item);
        }

        protected static CoreInfo ResolveCoreInfo(MediaBrowser.Controller.Entities.BaseItem item)
        {
            var core = ResolveCoreDefault(item);
            var needsThreads = IsThreadedCore(core);
            var launcher = core == "pico8" ? "pico8" : "ejs";
            return new CoreInfo(core, needsThreads, launcher);
        }

        protected CoreInfo ResolveCoreInfo(MediaBrowser.Controller.Entities.BaseItem item, string? userId = null, string? queryCoreOverride = null)
        {
            var core = ResolveCore(item, userId, queryCoreOverride);
            var needsThreads = IsThreadedCore(core);
            var launcher = core == "pico8" ? "pico8" : "ejs";
            return new CoreInfo(core, needsThreads, launcher);
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
