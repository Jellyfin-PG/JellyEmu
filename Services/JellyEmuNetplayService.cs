using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Services
{
    public class NetplayPeer
    {
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
    }

    public class NetplayPlayer
    {
        public string PlayerId { get; set; } = string.Empty;
        public string SocketId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = "Player";
        public int JoinOrder { get; set; }
        public int? Ping { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object?> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class NetplayRoom
    {
        public string SessionId { get; set; } = string.Empty;
        public string RoomName { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string GameId { get; set; } = string.Empty;
        public string? Password { get; set; }
        public string OwnerSocketId { get; set; } = string.Empty;
        public string OwnerPlayerId { get; set; } = string.Empty;
        public int MaxPlayers { get; set; } = 4;
        public int? HostPing { get; set; }
        private int _nextJoinOrder;
        public int GetNextJoinOrder() => Interlocked.Increment(ref _nextJoinOrder);
        public ConcurrentDictionary<string, NetplayPlayer> Players { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<NetplayPeer> Peers { get; } = new();
        public object PeerLock { get; } = new();
    }

    public class NetplaySocketSession : IDisposable
    {
        public string SocketId { get; } = Guid.NewGuid().ToString("N");
        public WebSocket WebSocket { get; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);
        public string? SessionId { get; set; }
        public string? PlayerId { get; set; }
        public long LastChatAtMs { get; set; }
        private int _isDisposed;

        public NetplaySocketSession(WebSocket webSocket)
        {
            WebSocket = webSocket;
        }

        public async Task SendRawAsync(string message, CancellationToken ct = default)
        {
            if (WebSocket.State != WebSocketState.Open || Volatile.Read(ref _isDisposed) != 0) return;
            var bytes = Encoding.UTF8.GetBytes(message);
            bool lockTaken = false;
            try
            {
                lockTaken = await SendLock.WaitAsync(3000, ct).ConfigureAwait(false);
                if (lockTaken && WebSocket.State == WebSocketState.Open && Volatile.Read(ref _isDisposed) == 0)
                {
                    await WebSocket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        ct).ConfigureAwait(false);
                }
            }
            catch { }
            finally
            {
                if (lockTaken)
                {
                    try { SendLock.Release(); } catch { }
                }
            }
        }

        public string? Namespace { get; set; }

        public Task SendEngineIoPacketAsync(char packetType, string data = "", CancellationToken ct = default)
        {
            return SendRawAsync(packetType + data, ct);
        }

        public Task SendSocketIoEventAsync(string eventName, object? payload, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(new object?[] { eventName, payload });
            var prefix = string.IsNullOrEmpty(Namespace) ? "42" : ($"42{Namespace},");
            return SendRawAsync(prefix + json, ct);
        }

        public Task SendSocketIoAckAsync(string ackId, object?[] results, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(results);
            var prefix = string.IsNullOrEmpty(Namespace) ? ("43" + ackId) : ($"43{Namespace}," + ackId);
            return SendRawAsync(prefix + json, ct);
        }

        public async Task CloseAsync(
            WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure,
            string description = "Server is shutting down",
            CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

            try
            {
                if (WebSocket.State == WebSocketState.Open || WebSocket.State == WebSocketState.CloseReceived)
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                    await WebSocket.CloseAsync(status, description, linked.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                try { WebSocket.Abort(); } catch { }
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _isDisposed, 1);
            try
            {
                if (WebSocket.State == WebSocketState.Open || WebSocket.State == WebSocketState.CloseReceived)
                {
                    WebSocket.Abort();
                }
                WebSocket.Dispose();
            }
            catch { }
            try
            {
                SendLock.Dispose();
            }
            catch { }
        }
    }

    public class NetplayRoomInfo
    {
        public string room_name { get; set; } = string.Empty;
        public int current { get; set; }
        public int max { get; set; }
        public string player_name { get; set; } = "Unknown";
        public bool hasPassword { get; set; }
        public string gameId { get; set; } = string.Empty;
        public int? ping { get; set; }
        public int? host_ping { get; set; }
    }

    /// <summary>
    /// Embedded Netplay relay &amp; WebRTC signaling service.
    /// Implements room matchmaking, host migration on disconnect, chat messaging, and WebRTC signal routing.
    /// Fully compatible with EmulatorJS socket.io-client protocol.
    /// </summary>
    public class JellyEmuNetplayService : IHostedService, IDisposable, IAsyncDisposable
    {
        private readonly ILogger<JellyEmuNetplayService> _logger;
        private readonly IHostApplicationLifetime? _hostApplicationLifetime;
        private readonly ConcurrentDictionary<string, NetplayRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, NetplaySocketSession> _sockets = new(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _cleanupTimer;
        private readonly Timer _heartbeatTimer;
        private readonly CancellationTokenSource _shutdownCts = new();
        private int _isDisposed;

        public JellyEmuNetplayService(
            ILogger<JellyEmuNetplayService> logger,
            IHostApplicationLifetime? hostApplicationLifetime = null)
        {
            _logger = logger;
            _hostApplicationLifetime = hostApplicationLifetime;
            _cleanupTimer = new Timer(SweepEmptyRooms, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            _heartbeatTimer = new Timer(SendHeartbeats, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

            _hostApplicationLifetime?.ApplicationStopping.Register(() =>
            {
                _logger.LogInformation("[JellyEmu Netplay] Host application stopping signal received.");
                _ = StopAsync(CancellationToken.None);
            });

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        private void OnProcessExit(object? sender, EventArgs e)
        {
            try
            {
                _logger.LogInformation("[JellyEmu Netplay] Process exit signal detected.");
                Task.Run(() => StopAsync(CancellationToken.None)).Wait(TimeSpan.FromSeconds(3));
            }
            catch { }
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static string? NormalizePassword(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return null;
            var trimmed = p.Trim();
            return trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
        }

        public Dictionary<string, NetplayRoomInfo> GetRoomList(string? domain, string? gameId)
        {
            var result = new Dictionary<string, NetplayRoomInfo>(StringComparer.OrdinalIgnoreCase);
            var normalizedGid = (gameId ?? string.Empty).Trim();
            var normalizedDom = (domain ?? string.Empty).Trim();

            foreach (var kvp in _rooms)
            {
                var room = kvp.Value;

                // Self-healing: prune any players whose sockets are dead or disconnected
                var deadPlayerKeys = room.Players
                    .Where(kvpP => !_sockets.TryGetValue(kvpP.Value.SocketId, out var s) || s.WebSocket.State != WebSocketState.Open)
                    .Select(kvpP => kvpP.Key)
                    .ToList();

                if (deadPlayerKeys.Count > 0)
                {
                    foreach (var dKey in deadPlayerKeys)
                    {
                        room.Players.TryRemove(dKey, out _);
                    }
                }

                if (room.Players.IsEmpty)
                {
                    _rooms.TryRemove(kvp.Key, out _);
                    continue;
                }

                if (!string.IsNullOrEmpty(normalizedGid) && !room.GameId.Equals(normalizedGid, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(normalizedDom) && !room.Domain.Equals(normalizedDom, StringComparison.OrdinalIgnoreCase))
                {
                    // Allow match if either room or query is empty or equal
                    if (!string.IsNullOrEmpty(room.Domain) && !room.Domain.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (room.Players.Count >= room.MaxPlayers)
                {
                    continue;
                }

                var ownerPlayer = room.Players.Values.FirstOrDefault(p => p.SocketId == room.OwnerSocketId)
                                  ?? room.Players.Values.FirstOrDefault();

                var hostPing = ownerPlayer?.Ping ?? room.HostPing;

                result[kvp.Key] = new NetplayRoomInfo
                {
                    room_name = room.RoomName,
                    current = room.Players.Count,
                    max = room.MaxPlayers,
                    player_name = ownerPlayer?.PlayerName ?? "Unknown",
                    hasPassword = !string.IsNullOrEmpty(room.Password),
                    gameId = room.GameId,
                    ping = hostPing,
                    host_ping = hostPing
                };
            }

            return result;
        }

        public async Task HandleWebSocketSessionAsync(WebSocket webSocket, CancellationToken ct)
        {
            if (Volatile.Read(ref _isDisposed) != 0 || _shutdownCts.IsCancellationRequested)
            {
                try
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.EndpointUnavailable,
                        "Server is shutting down",
                        ct).ConfigureAwait(false);
                }
                catch { }
                return;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token);
            var sessionToken = linkedCts.Token;

            var session = new NetplaySocketSession(webSocket);
            _sockets[session.SocketId] = session;
            _logger.LogInformation("[JellyEmu Netplay] Client connected: {SocketId}", session.SocketId);

            try
            {
                // Engine.IO v4 Open packet: 0{"sid":"...","upgrades":[],"pingInterval":25000,"pingTimeout":20000}
                var openObj = new
                {
                    sid = session.SocketId,
                    upgrades = Array.Empty<string>(),
                    pingInterval = 25000,
                    pingTimeout = 20000,
                    maxPayload = 1000000
                };
                await session.SendEngineIoPacketAsync('0', JsonSerializer.Serialize(openObj), sessionToken).ConfigureAwait(false);

                var buffer = new byte[1024 * 32];
                using var ms = new MemoryStream();

                while (webSocket.State == WebSocketState.Open && !sessionToken.IsCancellationRequested && Volatile.Read(ref _isDisposed) == 0)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), sessionToken).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("[JellyEmu Netplay] Socket {SocketId} received close frame: {Status} ({Desc})",
                            session.SocketId, result.CloseStatus, result.CloseStatusDescription);
                        try
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", sessionToken).ConfigureAwait(false);
                        }
                        catch { }
                        break;
                    }

                    try
                    {
                        var rawText = Encoding.UTF8.GetString(ms.ToArray());
                        await ProcessIncomingMessageAsync(session, rawText, sessionToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception msgEx)
                    {
                        _logger.LogWarning(msgEx, "[JellyEmu Netplay] Error processing message from socket {SocketId}: {Message}",
                            session.SocketId, msgEx.Message);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (Volatile.Read(ref _isDisposed) == 0)
                {
                    _logger.LogError(ex, "[JellyEmu Netplay] Socket session {SocketId} error: {Message}", session.SocketId, ex.Message);
                }
            }
            finally
            {
                _sockets.TryRemove(session.SocketId, out _);
                try
                {
                    await HandleDisconnectAsync(session).ConfigureAwait(false);
                }
                catch { }
                session.Dispose();
                _logger.LogInformation("[JellyEmu Netplay] Client disconnected: {SocketId}", session.SocketId);
            }
        }

        private async Task ProcessIncomingMessageAsync(NetplaySocketSession session, string rawText, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(rawText)) return;

            // Engine.IO packet types:
            // '2' = ping -> respond with '3' (pong)
            if (rawText[0] == '2')
            {
                await session.SendEngineIoPacketAsync('3', rawText.Length > 1 ? rawText.Substring(1) : string.Empty, ct).ConfigureAwait(false);
                return;
            }

            // '3' = pong
            if (rawText[0] == '3') return;

            // '5' = upgrade confirmation
            if (rawText[0] == '5') return;

            // '4' = message
            if (rawText[0] == '4')
            {
                if (rawText.Length < 2) return;
                var subType = rawText[1];

                // '40' = Socket.IO CONNECT
                if (subType == '0')
                {
                    var rest = rawText.Substring(2).Trim();
                    string ns = string.Empty;
                    int commaIdx = rest.IndexOf(',');
                    if (commaIdx >= 0)
                    {
                        ns = rest.Substring(0, commaIdx).Trim();
                    }
                    else if (rest.StartsWith('/'))
                    {
                        ns = rest;
                    }

                    if (!string.IsNullOrEmpty(ns) && !ns.Equals("/", StringComparison.Ordinal))
                    {
                        session.Namespace = ns;
                    }

                    var connectAck = string.IsNullOrEmpty(session.Namespace)
                        ? "40" + JsonSerializer.Serialize(new { sid = session.SocketId })
                        : $"40{session.Namespace}," + JsonSerializer.Serialize(new { sid = session.SocketId });

                    await session.SendRawAsync(connectAck, ct).ConfigureAwait(false);
                    return;
                }

                // '41' = Socket.IO DISCONNECT
                if (subType == '1')
                {
                    await HandleDisconnectAsync(session).ConfigureAwait(false);
                    return;
                }

                // '42' = Socket.IO EVENT
                if (subType == '2')
                {
                    var payload = rawText.Substring(2);

                    // Check for namespace prefix: /something,
                    if (payload.StartsWith('/'))
                    {
                        int commaIdx = payload.IndexOf(',');
                        if (commaIdx >= 0)
                        {
                            var ns = payload.Substring(0, commaIdx).Trim();
                            if (!string.IsNullOrEmpty(ns) && !ns.Equals("/", StringComparison.Ordinal))
                            {
                                session.Namespace = ns;
                            }
                            payload = payload.Substring(commaIdx + 1);
                        }
                    }

                    string? ackId = null;

                    // Check for leading Ack ID digits: e.g. 4212[...]
                    int idx = 0;
                    while (idx < payload.Length && char.IsDigit(payload[idx]))
                    {
                        idx++;
                    }

                    if (idx > 0)
                    {
                        ackId = payload.Substring(0, idx);
                        payload = payload.Substring(idx);
                    }

                    await DispatchSocketIoEventAsync(session, payload, ackId, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task DispatchSocketIoEventAsync(NetplaySocketSession session, string jsonPayload, string? ackId, CancellationToken ct)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonPayload);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return;

                var eventName = root[0].GetString();
                if (string.IsNullOrEmpty(eventName)) return;

                var data = root.GetArrayLength() > 1 ? root[1] : default;

                switch (eventName)
                {
                    case "open-room":
                        await HandleOpenRoomAsync(session, data, ackId, ct).ConfigureAwait(false);
                        break;
                    case "join-room":
                        await HandleJoinRoomAsync(session, data, ackId, ct).ConfigureAwait(false);
                        break;
                    case "leave-room":
                        await HandleLeaveRoomAsync(session, ct).ConfigureAwait(false);
                        break;
                    case "chat-message":
                        await HandleChatMessageAsync(session, data, ackId, ct).ConfigureAwait(false);
                        break;
                    case "host-ping":
                    case "ping":
                        HandleReportedPing(session, data);
                        break;
                    case "webrtc-signal":
                        await HandleWebRtcSignalAsync(session, data, ct).ConfigureAwait(false);
                        break;
                    case "data-message":
                    case "input":
                    case "snapshot":
                        await BroadcastToRoomAsync(session, eventName, data, ct).ConfigureAwait(false);
                        break;
                    default:
                        _logger.LogDebug("[JellyEmu Netplay] Unhandled event: {Event}", eventName);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[JellyEmu Netplay] Event dispatch error: {Message}", ex.Message);
            }
        }

        private void HandleReportedPing(NetplaySocketSession session, JsonElement data)
        {
            if (string.IsNullOrEmpty(session.SessionId) || !_rooms.TryGetValue(session.SessionId, out var r)) return;

            int? reportedPing = null;
            if (data.ValueKind == JsonValueKind.Number && data.TryGetInt32(out var pNum))
            {
                reportedPing = pNum;
            }
            else if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("ping", out var pProp) && pProp.TryGetInt32(out var pNum2))
                {
                    reportedPing = pNum2;
                }
                else if (data.TryGetProperty("host_ping", out var hpProp) && hpProp.TryGetInt32(out var hpNum2))
                {
                    reportedPing = hpNum2;
                }
            }

            if (reportedPing.HasValue)
            {
                if (r.Players.TryGetValue(session.PlayerId ?? string.Empty, out var pl))
                {
                    pl.Ping = reportedPing.Value;
                }
                else
                {
                    var pBySocket = r.Players.Values.FirstOrDefault(p => p.SocketId == session.SocketId);
                    if (pBySocket != null) pBySocket.Ping = reportedPing.Value;
                }

                if (session.SocketId == r.OwnerSocketId || session.PlayerId == r.OwnerPlayerId)
                {
                    r.HostPing = reportedPing.Value;
                }
            }
        }

        private async Task HandleOpenRoomAsync(NetplaySocketSession session, JsonElement data, string? ackId, CancellationToken ct)
        {
            var extraDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            string? password = null;
            int maxPlayers = 4;
            int? initialPing = null;

            if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("password", out var pwElem))
                {
                    password = NormalizePassword(pwElem.GetString());
                }

                if (data.TryGetProperty("maxPlayers", out var maxElem) && maxElem.TryGetInt32(out var mp))
                {
                    maxPlayers = Math.Clamp(mp, 2, 8);
                }

                if (data.TryGetProperty("ping", out var pElem) && pElem.TryGetInt32(out var pVal))
                {
                    initialPing = pVal;
                }
                else if (data.TryGetProperty("host_ping", out var hpElem) && hpElem.TryGetInt32(out var hpVal))
                {
                    initialPing = hpVal;
                }

                if (data.TryGetProperty("extra", out var extraElem) && extraElem.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in extraElem.EnumerateObject())
                    {
                        extraDict[prop.Name] = prop.Value.Clone();
                    }
                }
            }

            if (!initialPing.HasValue)
            {
                if (extraDict.TryGetValue("ping", out var pObj) && pObj is JsonElement pe && pe.TryGetInt32(out var peVal))
                {
                    initialPing = peVal;
                }
                else if (extraDict.TryGetValue("host_ping", out var hpObj) && hpObj is JsonElement hpe && hpe.TryGetInt32(out var hpeVal))
                {
                    initialPing = hpeVal;
                }
            }

            var sessionId = GetString(extraDict, "sessionid");
            var playerId = GetString(extraDict, "userid") ?? GetString(extraDict, "playerId");

            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(playerId))
            {
                if (!string.IsNullOrEmpty(ackId))
                {
                    await session.SendSocketIoAckAsync(ackId, new object?[] { "Invalid data: sessionId and playerId required" }, ct).ConfigureAwait(false);
                }
                return;
            }

            var roomName = GetString(extraDict, "room_name") ?? $"Room {sessionId}";
            var gameId = GetString(extraDict, "game_id") ?? "default";
            var domain = GetString(extraDict, "domain") ?? "unknown";
            var playerName = GetString(extraDict, "player_name") ?? "Player";

            extraDict["socketId"] = session.SocketId;

            var room = new NetplayRoom
            {
                SessionId = sessionId,
                OwnerSocketId = session.SocketId,
                OwnerPlayerId = playerId,
                RoomName = roomName,
                GameId = gameId,
                Domain = domain,
                Password = password,
                MaxPlayers = maxPlayers,
                HostPing = initialPing
            };

            var player = new NetplayPlayer
            {
                PlayerId = playerId,
                SocketId = session.SocketId,
                PlayerName = playerName,
                JoinOrder = room.GetNextJoinOrder(),
                JoinedAt = DateTime.UtcNow,
                Ping = initialPing,
                Extra = extraDict
            };

            room.Players[playerId] = player;

            if (!_rooms.TryAdd(sessionId, room))
            {
                if (!string.IsNullOrEmpty(ackId))
                {
                    await session.SendSocketIoAckAsync(ackId, new object?[] { "Room already exists" }, ct).ConfigureAwait(false);
                }
                return;
            }

            session.SessionId = sessionId;
            session.PlayerId = playerId;

            _logger.LogInformation("[JellyEmu Netplay] Room opened: {RoomName} ({SessionId}) by {PlayerName}", roomName, sessionId, playerName);

            // Ack success: (null,)
            if (!string.IsNullOrEmpty(ackId))
            {
                await session.SendSocketIoAckAsync(ackId, new object?[] { null }, ct).ConfigureAwait(false);
            }

            // Emit users-updated to the creator
            var playersDict = BuildPlayersSnapshot(room);
            await session.SendSocketIoEventAsync("users-updated", playersDict, ct).ConfigureAwait(false);
        }

        private async Task HandleJoinRoomAsync(NetplaySocketSession session, JsonElement data, string? ackId, CancellationToken ct)
        {
            var extraDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            string? password = null;

            if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("password", out var pwElem))
                {
                    password = NormalizePassword(pwElem.GetString());
                }

                if (data.TryGetProperty("extra", out var extraElem) && extraElem.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in extraElem.EnumerateObject())
                    {
                        extraDict[prop.Name] = prop.Value.Clone();
                    }
                }
            }

            var sessionId = GetString(extraDict, "sessionid");
            var playerId = GetString(extraDict, "userid") ?? GetString(extraDict, "playerId");

            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(playerId))
            {
                if (!string.IsNullOrEmpty(ackId))
                {
                    await session.SendSocketIoAckAsync(ackId, new object?[] { "Invalid data: sessionId and playerId required" }, ct).ConfigureAwait(false);
                }
                return;
            }

            if (!_rooms.TryGetValue(sessionId, out var room))
            {
                if (!string.IsNullOrEmpty(ackId))
                {
                    await session.SendSocketIoAckAsync(ackId, new object?[] { "Room not found" }, ct).ConfigureAwait(false);
                }
                return;
            }

            if (!string.IsNullOrEmpty(room.Password))
            {
                if (password != room.Password)
                {
                    if (!string.IsNullOrEmpty(ackId))
                    {
                        await session.SendSocketIoAckAsync(ackId, new object?[] { "Incorrect password" }, ct).ConfigureAwait(false);
                    }
                    return;
                }
            }

            // Reject if this session is already the owner of the room
            if (session.SocketId == room.OwnerSocketId || (!string.IsNullOrEmpty(room.OwnerPlayerId) && playerId == room.OwnerPlayerId))
            {
                if (!string.IsNullOrEmpty(ackId))
                {
                    await session.SendSocketIoAckAsync(ackId, new object?[] { "You are already hosting this room" }, ct).ConfigureAwait(false);
                }
                return;
            }

            // Reject if this socket or player is already in this room
            if (room.Players.ContainsKey(playerId) || room.Players.Values.Any(p => p.SocketId == session.SocketId))
            {
                if (!string.IsNullOrEmpty(ackId))
                {
                    await session.SendSocketIoAckAsync(ackId, new object?[] { "You are already in this room" }, ct).ConfigureAwait(false);
                }
                return;
            }

            if (room.Players.Count >= room.MaxPlayers)
            {
                if (!string.IsNullOrEmpty(ackId))
                {
                    await session.SendSocketIoAckAsync(ackId, new object?[] { "Room full" }, ct).ConfigureAwait(false);
                }
                return;
            }

            var playerName = GetString(extraDict, "player_name") ?? "Player";
            extraDict["socketId"] = session.SocketId;

            var player = new NetplayPlayer
            {
                PlayerId = playerId,
                SocketId = session.SocketId,
                PlayerName = playerName,
                JoinOrder = room.GetNextJoinOrder(),
                JoinedAt = DateTime.UtcNow,
                Extra = extraDict
            };

            room.Players[playerId] = player;
            session.SessionId = sessionId;
            session.PlayerId = playerId;

            _logger.LogInformation("[JellyEmu Netplay] Player {PlayerName} joined room {SessionId}", playerName, sessionId);

            var playersSnapshot = BuildPlayersSnapshot(room);

            // Ack success: (null, playersSnapshot)
            if (!string.IsNullOrEmpty(ackId))
            {
                await session.SendSocketIoAckAsync(ackId, new object?[] { null, playersSnapshot }, ct).ConfigureAwait(false);
            }

            // Broadcast users-updated to ALL players in the room
            await BroadcastToRoomSocketsAsync(room, "users-updated", playersSnapshot, null, ct).ConfigureAwait(false);
        }

        private async Task HandleLeaveRoomAsync(NetplaySocketSession session, CancellationToken ct)
        {
            await LeaveInternalAsync(session, ct).ConfigureAwait(false);
        }

        private async Task HandleDisconnectAsync(NetplaySocketSession session)
        {
            await LeaveInternalAsync(session, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles room exit with automatic host migration if the owner leaves.
        /// </summary>
        private async Task LeaveInternalAsync(NetplaySocketSession session, CancellationToken ct)
        {
            var sessionId = session.SessionId;
            var playerId = session.PlayerId;
            var leavingSocketId = session.SocketId;

            session.SessionId = null;
            session.PlayerId = null;

            NetplayRoom? room = null;
            if (!string.IsNullOrEmpty(sessionId))
            {
                _rooms.TryGetValue(sessionId, out room);
            }

            // Fallback: if sessionId was not tracked on session or room not found, locate room by socket id or owner
            if (room == null)
            {
                room = _rooms.Values.FirstOrDefault(r => r.OwnerSocketId == leavingSocketId || r.Players.Values.Any(p => p.SocketId == leavingSocketId));
                if (room != null)
                {
                    sessionId = room.SessionId;
                }
            }

            if (room == null) return;

            // Remove any player entry matching this socket or playerId
            var playersToRemove = room.Players.Where(kvp =>
                kvp.Value.SocketId == leavingSocketId ||
                (!string.IsNullOrEmpty(playerId) && (kvp.Key == playerId || kvp.Value.PlayerId == playerId))
            ).ToList();

            foreach (var kvp in playersToRemove)
            {
                room.Players.TryRemove(kvp.Key, out _);
            }

            lock (room.PeerLock)
            {
                room.Peers.RemoveAll(p => p.Source == leavingSocketId || p.Target == leavingSocketId);
            }

            var wasOwner = (room.OwnerSocketId == leavingSocketId || (!string.IsNullOrEmpty(playerId) && room.OwnerPlayerId == playerId));

            // If the host leaves, close the room immediately, notify all guests, and disconnect them to return to single-player
            if (wasOwner)
            {
                _logger.LogInformation("[JellyEmu Netplay] Host left room {SessionId}. Closing room and returning guests to local emulation.", sessionId);

                var closePayload = new
                {
                    sessionId = sessionId,
                    reason = "Host left the game",
                    type = "host-left"
                };

                // Broadcast host-left, room-closed, and data-message to all remaining sockets in the room
                await BroadcastToRoomSocketsAsync(room, "host-left", closePayload, null, ct).ConfigureAwait(false);
                await BroadcastToRoomSocketsAsync(room, "room-closed", closePayload, null, ct).ConfigureAwait(false);
                await BroadcastToRoomSocketsAsync(room, "data-message", new { type = "host-left", reason = "Host left the game" }, null, ct).ConfigureAwait(false);

                // Clear room sessions on remaining guests so they return to single player
                foreach (var player in room.Players.Values)
                {
                    if (_sockets.TryGetValue(player.SocketId, out var guestSession))
                    {
                        guestSession.SessionId = null;
                        guestSession.PlayerId = null;
                    }
                }

                _rooms.TryRemove(room.SessionId, out _);
                return;
            }

            // Normal guest left: if room is now empty, delete it
            if (room.Players.IsEmpty)
            {
                _rooms.TryRemove(room.SessionId, out _);
                _logger.LogInformation("[JellyEmu Netplay] Room closed (empty): {SessionId}", room.SessionId);
                return;
            }

            // Broadcast updated user roster to remaining players
            var playersSnapshot = BuildPlayersSnapshot(room);
            await BroadcastToRoomSocketsAsync(room, "users-updated", playersSnapshot, null, ct).ConfigureAwait(false);
        }

        private async Task HandleChatMessageAsync(NetplaySocketSession session, JsonElement data, string? ackId, CancellationToken ct)
        {
            var sessionId = session.SessionId;
            var playerId = session.PlayerId;

            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(playerId) || !_rooms.TryGetValue(sessionId, out var room))
            {
                if (!string.IsNullOrEmpty(ackId))
                {
                    await session.SendSocketIoAckAsync(ackId, new object?[] { new { ok = false, error = "Not in a room" } }, ct).ConfigureAwait(false);
                }
                return;
            }

            var now = NowMs();
            if (now - session.LastChatAtMs < 400)
            {
                if (!string.IsNullOrEmpty(ackId))
                {
                    await session.SendSocketIoAckAsync(ackId, new object?[] { new { ok = false, error = "Slow down" } }, ct).ConfigureAwait(false);
                }
                return;
            }
            session.LastChatAtMs = now;

            var toStr = "all";
            var message = string.Empty;

            if (data.ValueKind == JsonValueKind.String)
            {
                message = data.GetString() ?? string.Empty;
            }
            else if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("to", out var toElem))
                {
                    toStr = toElem.GetString() ?? "all";
                }
                if (data.TryGetProperty("message", out var msgElem))
                {
                    message = msgElem.GetString() ?? string.Empty;
                }
            }

            // Sanitize message: normalize whitespace, limit to 300 chars
            message = string.Join(" ", message.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            if (message.Length > 300) message = message.Substring(0, 300);

            if (string.IsNullOrWhiteSpace(message))
            {
                if (!string.IsNullOrEmpty(ackId))
                {
                    await session.SendSocketIoAckAsync(ackId, new object?[] { new { ok = false, error = "Empty message" } }, ct).ConfigureAwait(false);
                }
                return;
            }

            var playerName = room.Players.TryGetValue(playerId, out var p) ? p.PlayerName : "Unknown";
            if ((playerName == "Unknown" || playerName == "Player") && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("player_name", out var pnElem) && !string.IsNullOrWhiteSpace(pnElem.GetString()))
            {
                playerName = pnElem.GetString()!;
                if (p != null)
                {
                    p.PlayerName = playerName;
                }
            }
            var isPrivate = !toStr.Equals("all", StringComparison.OrdinalIgnoreCase) && !toStr.Equals(playerId, StringComparison.OrdinalIgnoreCase);

            NetplaySocketSession? targetSocket = null;
            if (isPrivate)
            {
                if (room.Players.TryGetValue(toStr, out var targetPlayer))
                {
                    _sockets.TryGetValue(targetPlayer.SocketId, out targetSocket);
                }
                else
                {
                    var found = room.Players.Values.FirstOrDefault(pl => pl.SocketId == toStr);
                    if (found != null)
                    {
                        _sockets.TryGetValue(found.SocketId, out targetSocket);
                    }
                }
            }

            var payload = new
            {
                ts = now,
                to = targetSocket != null ? toStr : "all",
                userid = playerId,
                player_name = playerName,
                message = message
            };

            if (targetSocket != null)
            {
                // Send to sender and recipient
                await session.SendSocketIoEventAsync("chat-message", payload, ct).ConfigureAwait(false);
                await targetSocket.SendSocketIoEventAsync("chat-message", payload, ct).ConfigureAwait(false);
            }
            else
            {
                // Broadcast to all players in the room
                await BroadcastToRoomSocketsAsync(room, "chat-message", payload, null, ct).ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(ackId))
            {
                await session.SendSocketIoAckAsync(ackId, new object?[] { new { ok = true } }, ct).ConfigureAwait(false);
            }
        }

        private async Task HandleWebRtcSignalAsync(NetplaySocketSession session, JsonElement data, CancellationToken ct)
        {
            if (data.ValueKind != JsonValueKind.Object) return;

            string? target = null;
            if (data.TryGetProperty("target", out var targetElem))
            {
                target = targetElem.GetString();
            }

            bool requestRenegotiate = false;
            if (data.TryGetProperty("requestRenegotiate", out var renegElem))
            {
                requestRenegotiate = renegElem.ValueKind == JsonValueKind.True;
            }

            if (!requestRenegotiate && string.IsNullOrEmpty(target)) return;

            var senderSocketId = session.SocketId;

            // If an offer is included, track the peer connection in the room
            if (data.TryGetProperty("offer", out _) && !string.IsNullOrEmpty(session.SessionId) && !string.IsNullOrEmpty(target))
            {
                if (_rooms.TryGetValue(session.SessionId, out var room))
                {
                    lock (room.PeerLock)
                    {
                        if (!room.Peers.Any(p => p.Source == senderSocketId && p.Target == target))
                        {
                            room.Peers.Add(new NetplayPeer { Source = senderSocketId, Target = target });
                        }
                    }
                }
            }

            // Build forwarded payload with sender filled in
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["sender"] = senderSocketId
            };

            if (requestRenegotiate)
            {
                dict["requestRenegotiate"] = true;
                if (data.TryGetProperty("reason", out var reasonElem)) dict["reason"] = reasonElem.GetString();
                if (!string.IsNullOrEmpty(target)) dict["target"] = target;
            }
            else
            {
                if (data.TryGetProperty("candidate", out var candElem)) dict["candidate"] = candElem.Clone();
                if (data.TryGetProperty("offer", out var offerElem)) dict["offer"] = offerElem.Clone();
                if (data.TryGetProperty("answer", out var ansElem)) dict["answer"] = ansElem.Clone();
            }

            if (!string.IsNullOrEmpty(target))
            {
                NetplaySocketSession? targetSession = null;
                if (!_sockets.TryGetValue(target, out targetSession) && !string.IsNullOrEmpty(session.SessionId) && _rooms.TryGetValue(session.SessionId, out var currentRoom))
                {
                    if (currentRoom.Players.TryGetValue(target, out var targetPlayer))
                    {
                        _sockets.TryGetValue(targetPlayer.SocketId, out targetSession);
                    }
                }

                if (targetSession != null)
                {
                    await targetSession.SendSocketIoEventAsync("webrtc-signal", dict, ct).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug("[JellyEmu Netplay] WebRTC signal target not found: {Target}", target);
                }
            }
            else if (requestRenegotiate && !string.IsNullOrEmpty(session.SessionId) && _rooms.TryGetValue(session.SessionId, out var currentRoom))
            {
                // Fallback: route renegotiation request directly to room owner socket
                if (!string.IsNullOrEmpty(currentRoom.OwnerSocketId) && _sockets.TryGetValue(currentRoom.OwnerSocketId, out var ownerSession))
                {
                    await ownerSession.SendSocketIoEventAsync("webrtc-signal", dict, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task BroadcastToRoomAsync(NetplaySocketSession session, string eventName, JsonElement data, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(session.SessionId) || !_rooms.TryGetValue(session.SessionId, out var room)) return;

            if (eventName == "data-message" && data.ValueKind == JsonValueKind.Object)
            {
                int? reportedPing = null;
                if (data.TryGetProperty("ping", out var pingElem) && pingElem.TryGetInt32(out var pingVal))
                {
                    reportedPing = pingVal;
                }
                else if (data.TryGetProperty("host_ping", out var hostPingElem) && hostPingElem.TryGetInt32(out var hpVal))
                {
                    reportedPing = hpVal;
                }

                if (reportedPing.HasValue)
                {
                    if (room.Players.TryGetValue(session.PlayerId ?? string.Empty, out var pl))
                    {
                        pl.Ping = reportedPing.Value;
                    }
                    else
                    {
                        var pBySocket = room.Players.Values.FirstOrDefault(p => p.SocketId == session.SocketId);
                        if (pBySocket != null) pBySocket.Ping = reportedPing.Value;
                    }

                    if (session.SocketId == room.OwnerSocketId || session.PlayerId == room.OwnerPlayerId)
                    {
                        room.HostPing = reportedPing.Value;
                    }
                }
            }

            await BroadcastToRoomSocketsAsync(room, eventName, data.Clone(), session.SocketId, ct).ConfigureAwait(false);
        }

        private async Task BroadcastToRoomSocketsAsync(NetplayRoom room, string eventName, object? payload, string? excludeSocketId, CancellationToken ct)
        {
            var tasks = new List<Task>();
            foreach (var p in room.Players.Values)
            {
                if (excludeSocketId != null && p.SocketId == excludeSocketId) continue;
                if (_sockets.TryGetValue(p.SocketId, out var clientSession))
                {
                    tasks.Add(clientSession.SendSocketIoEventAsync(eventName, payload, ct));
                }
            }
            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        private static Dictionary<string, object?> BuildPlayersSnapshot(NetplayRoom room)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var orderedPlayers = room.Players.Values.OrderBy(p => p.JoinOrder).ToList();
            foreach (var player in orderedPlayers)
            {
                var isOwner = (player.SocketId == room.OwnerSocketId || player.PlayerId == room.OwnerPlayerId);
                var extra = new Dictionary<string, object?>(player.Extra, StringComparer.OrdinalIgnoreCase)
                {
                    ["socketId"] = player.SocketId,
                    ["playerId"] = player.PlayerId,
                    ["userid"] = player.PlayerId,
                    ["player_name"] = player.PlayerName,
                    ["isOwner"] = isOwner,
                    ["owner"] = isOwner,
                    ["joinOrder"] = player.JoinOrder
                };
                result[player.PlayerId] = extra;
            }
            return result;
        }

        private static string? GetString(Dictionary<string, object?> dict, string key)
        {
            if (!dict.TryGetValue(key, out var val) || val == null) return null;
            if (val is JsonElement je)
            {
                return je.ValueKind switch
                {
                    JsonValueKind.String => je.GetString(),
                    JsonValueKind.Number => je.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null
                };
            }
            return val.ToString();
        }

        private void SweepEmptyRooms(object? state)
        {
            foreach (var kvp in _rooms)
            {
                if (kvp.Value.Players.IsEmpty)
                {
                    _rooms.TryRemove(kvp.Key, out _);
                }
            }
        }

        private void SendHeartbeats(object? state)
        {
            foreach (var kvp in _sockets)
            {
                var session = kvp.Value;
                if (session.WebSocket.State == WebSocketState.Open)
                {
                    _ = session.SendEngineIoPacketAsync('2', "probe");
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

            _logger.LogInformation("[JellyEmu Netplay] Safely shutting down Netplay service: closing connections...");

            try
            {
                _cleanupTimer.Dispose();
                _heartbeatTimer.Dispose();
            }
            catch { }

            try
            {
                _shutdownCts.Cancel();
            }
            catch { }

            var activeSessions = _sockets.Values.ToArray();
            _sockets.Clear();
            _rooms.Clear();

            if (activeSessions.Length > 0)
            {
                _logger.LogInformation("[JellyEmu Netplay] Gracefully closing {Count} active netplay client connection(s)...", activeSessions.Length);

                var closeTasks = activeSessions.Select(async session =>
                {
                    try
                    {
                        await session.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Jellyfin server is shutting down",
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[JellyEmu Netplay] Error closing session {SocketId} on shutdown", session.SocketId);
                    }
                    finally
                    {
                        session.Dispose();
                    }
                });

                try
                {
                    await Task.WhenAll(closeTasks).ConfigureAwait(false);
                }
                catch { }
            }

            try
            {
                _shutdownCts.Dispose();
            }
            catch { }

            _logger.LogInformation("[JellyEmu Netplay] Netplay service safely stopped and disposed.");
        }

        public void Dispose()
        {
            try
            {
                StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch { }
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
            GC.SuppressFinalize(this);
        }
    }
}
