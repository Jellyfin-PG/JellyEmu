using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Services
{
    /// <summary>
    /// Manages synthetic Jellyfin playback sessions for active emulator games.
    ///
    /// When a user launches a game, the player page calls POST /jellyemu/session/start.
    /// A 30-second ping from the player keeps the session alive with a progress report.
    /// POST /jellyemu/session/stop (fired from EJS_onExit) ends it cleanly.
    ///
    /// This makes active game sessions visible in the Jellyfin Dashboard → Active
    /// Sessions panel exactly like movie/TV playback, including the item name,
    /// user, and elapsed time — with no media actually streaming.
    /// </summary>
    public class JellyEmuSessionService
    {
        private readonly ISessionManager _sessionManager;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<JellyEmuSessionService> _logger;

        // Key: "{userId}:{itemId}"  Value: session id issued by Jellyfin
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ActiveGameSession> _activeSessions = new();

        public JellyEmuSessionService(
            ISessionManager sessionManager,
            IUserManager userManager,
            ILibraryManager libraryManager,
            ILogger<JellyEmuSessionService> logger)
        {
            _sessionManager = sessionManager;
            _userManager    = userManager;
            _libraryManager = libraryManager;
            _logger         = logger;
        }

        private static string Key(string userId, string itemId) => $"{userId}:{itemId}";

        /// <summary>
        /// Opens a Jellyfin playback session for the game.
        /// Safe to call multiple times — subsequent calls are no-ops if a session is already open.
        /// </summary>
        public async Task StartSessionAsync(string userId, string itemId, string clientName, string deviceId, string deviceName, string remoteEndPoint)
        {
            var key = Key(userId, itemId);
            if (_activeSessions.ContainsKey(key))
            {
                _logger.LogDebug("[JellyEmu] Session already active for key {Key}", key);
                return;
            }

            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                _logger.LogWarning("[JellyEmu] StartSession: item {ItemId} not found", itemId);
                return;
            }

            var user = _userManager.GetUserById(Guid.Parse(userId));
            if (user == null)
            {
                _logger.LogWarning("[JellyEmu] StartSession: user {UserId} not found", userId);
                return;
            }

            try
            {
                // Ensure there is a Jellyfin session for this client/device combination.
                // GetOrCreateSessionInfo returns an existing session or creates one.
                var session = await _sessionManager.LogSessionActivity(
                    clientName,
                    "1.0",
                    deviceId,
                    deviceName,
                    remoteEndPoint,
                    user).ConfigureAwait(false);

                var info = new PlaybackStartInfo
                {
                    ItemId        = item.Id,
                    SessionId     = session.Id,
                    PlayMethod    = PlayMethod.DirectPlay,
                    MediaSourceId = item.Id.ToString("N"),
                    PositionTicks = 0,
                    IsPaused      = false,
                    IsMuted       = false,
                };

                await _sessionManager.OnPlaybackStart(info).ConfigureAwait(false);

                _activeSessions[key] = new ActiveGameSession(session.Id, userId, itemId, DateTime.UtcNow);

                _logger.LogInformation("[JellyEmu] Session started — user:{UserId} item:{ItemId} session:{SessionId}",
                    userId, itemId, session.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to start session for user:{UserId} item:{ItemId}", userId, itemId);
            }
        }

        /// <summary>
        /// Reports progress to keep the session alive and update the elapsed-time ticker
        /// in the Dashboard. Call every ~30 seconds from the player page.
        /// </summary>
        public async Task PingSessionAsync(string userId, string itemId)
        {
            var key = Key(userId, itemId);
            if (!_activeSessions.TryGetValue(key, out var active))
            {
                _logger.LogDebug("[JellyEmu] Ping: no active session for key {Key}", key);
                return;
            }

            var item = _libraryManager.GetItemById(itemId);
            if (item == null) return;

            try
            {
                var elapsedTicks = (long)(DateTime.UtcNow - active.StartedAt).TotalSeconds * TimeSpan.TicksPerSecond;

                var info = new PlaybackProgressInfo
                {
                    ItemId        = item.Id,
                    SessionId     = active.SessionId,
                    PlayMethod    = PlayMethod.DirectPlay,
                    MediaSourceId = item.Id.ToString("N"),
                    PositionTicks = elapsedTicks,
                    IsPaused      = false,
                    IsMuted       = false,
                };

                await _sessionManager.OnPlaybackProgress(info).ConfigureAwait(false);

                _logger.LogDebug("[JellyEmu] Session ping — user:{UserId} item:{ItemId} elapsed:{Elapsed}s",
                    userId, itemId, elapsedTicks / TimeSpan.TicksPerSecond);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to ping session for user:{UserId} item:{ItemId}", userId, itemId);
            }
        }

        /// <summary>
        /// Closes the Jellyfin playback session for the game.
        /// </summary>
        public async Task StopSessionAsync(string userId, string itemId)
        {
            var key = Key(userId, itemId);
            if (!_activeSessions.TryRemove(key, out var active))
            {
                _logger.LogDebug("[JellyEmu] StopSession: no active session to stop for key {Key}", key);
                return;
            }

            var item = _libraryManager.GetItemById(itemId);
            if (item == null) return;

            try
            {
                var elapsedTicks = (long)(DateTime.UtcNow - active.StartedAt).TotalSeconds * TimeSpan.TicksPerSecond;

                var info = new PlaybackStopInfo
                {
                    ItemId        = item.Id,
                    SessionId     = active.SessionId,
                    MediaSourceId = item.Id.ToString("N"),
                    PositionTicks = elapsedTicks,
                    Failed        = false,
                };

                await _sessionManager.OnPlaybackStopped(info).ConfigureAwait(false);

                _logger.LogInformation("[JellyEmu] Session stopped — user:{UserId} item:{ItemId} elapsed:{Elapsed}s",
                    userId, itemId, elapsedTicks / TimeSpan.TicksPerSecond);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to stop session for user:{UserId} item:{ItemId}", userId, itemId);
            }
        }

        private record ActiveGameSession(string SessionId, string UserId, string ItemId, DateTime StartedAt);
    }
}
