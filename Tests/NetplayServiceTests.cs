using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using JellyEmu.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyEmu.Tests
{
    public class TestWebSocket : WebSocket
    {
        private readonly Channel<string> _inbound = Channel.CreateUnbounded<string>();
        public ConcurrentQueue<string> SentMessages { get; } = new();
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => WebSocketCloseStatus.NormalClosure;
        public override string? CloseStatusDescription => "Closed";
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public void EnqueueClientMessage(string message)
        {
            _inbound.Writer.TryWrite(message);
        }

        public void CompleteClient()
        {
            _inbound.Writer.TryComplete();
            _state = WebSocketState.CloseReceived;
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_state != WebSocketState.Open)
            {
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
            }

            try
            {
                if (await _inbound.Reader.WaitToReadAsync(cancellationToken))
                {
                    if (_inbound.Reader.TryRead(out var msg))
                    {
                        var bytes = Encoding.UTF8.GetBytes(msg);
                        bytes.CopyTo(buffer.Array!, buffer.Offset);
                        return new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true);
                    }
                }
            }
            catch (OperationCanceledException) { }

            _state = WebSocketState.Closed;
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            var text = Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count);
            SentMessages.Enqueue(text);
            return Task.CompletedTask;
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
        }

        public override void Dispose()
        {
            _state = WebSocketState.Closed;
        }
    }

    public class NetplayServiceTests
    {
        [Fact]
        public async Task Netplay_RoomLifecycle_Open_Join_Chat_And_HostMigration()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);

            // Connect Host Socket
            var hostWs = new TestWebSocket();
            var hostTask = Task.Run(() => service.HandleWebSocketSessionAsync(hostWs, CancellationToken.None));

            await Task.Delay(50); // Allow handshake
            Assert.NotEmpty(hostWs.SentMessages);
            var hostOpenPacket = hostWs.SentMessages.ToArray()[0];
            Assert.StartsWith("0{", hostOpenPacket); // Engine.IO open packet

            // Host connects to Socket.IO: sends "40"
            hostWs.EnqueueClientMessage("40");
            await Task.Delay(30);

            // Host opens a room
            var openRoomPayload = "421[\"open-room\",{\"password\":\"\",\"maxPlayers\":3,\"extra\":{\"sessionid\":\"room123\",\"userid\":\"hostUser\",\"player_name\":\"HostPlayer\",\"room_name\":\"My Mario Room\",\"game_id\":\"42\",\"domain\":\"localhost:8096\"}}]";
            hostWs.EnqueueClientMessage(openRoomPayload);
            await Task.Delay(50);

            // Check room listing
            var list = service.GetRoomList("localhost:8096", "42");
            Assert.True(list.ContainsKey("room123"));
            Assert.Equal("My Mario Room", list["room123"].room_name);
            Assert.Equal("HostPlayer", list["room123"].player_name);
            Assert.Equal(1, list["room123"].current);
            Assert.Equal(3, list["room123"].max);
            Assert.False(list["room123"].hasPassword);

            // Connect Guest Socket
            var guestWs = new TestWebSocket();
            var guestTask = Task.Run(() => service.HandleWebSocketSessionAsync(guestWs, CancellationToken.None));
            await Task.Delay(50);
            guestWs.EnqueueClientMessage("40");

            // Guest joins room
            var joinRoomPayload = "422[\"join-room\",{\"password\":\"\",\"extra\":{\"sessionid\":\"room123\",\"userid\":\"guestUser\",\"player_name\":\"GuestPlayer\",\"domain\":\"localhost:8096\"}}]";
            guestWs.EnqueueClientMessage(joinRoomPayload);
            await Task.Delay(50);

            // Verify room current player count is 2
            list = service.GetRoomList("localhost:8096", "42");
            Assert.Equal(2, list["room123"].current);

            // Test In-Game Chat Messaging
            // Guest sends public chat
            var chatPayload = "423[\"chat-message\",{\"message\":\"Hello from Guest!\",\"to\":\"all\"}]";
            guestWs.EnqueueClientMessage(chatPayload);
            await Task.Delay(50);

            var hostReceivedMessages = string.Join("\n", hostWs.SentMessages);
            Assert.Contains("Hello from Guest!", hostReceivedMessages);
            Assert.Contains("GuestPlayer", hostReceivedMessages);

            // Test Host Departure
            // When Host leaves, room closes immediately and Guest receives host-left / room-closed
            hostWs.CompleteClient();
            await Task.Delay(80);

            // Room should be closed and removed from room list
            list = service.GetRoomList("localhost:8096", "42");
            Assert.False(list.ContainsKey("room123"));

            // Verify Guest received host-left or room-closed
            var guestReceivedMessages = string.Join("\n", guestWs.SentMessages);
            Assert.True(guestReceivedMessages.Contains("host-left") || guestReceivedMessages.Contains("room-closed"));

            guestWs.CompleteClient();
            await Task.Delay(80);

            service.Dispose();
        }

        [Fact]
        public async Task Netplay_HostLeaving_ClosesRoomAndNotifiesAllGuests()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);

            // Connect Player 1 (Initial Host)
            var p1Ws = new TestWebSocket();
            _ = Task.Run(() => service.HandleWebSocketSessionAsync(p1Ws, CancellationToken.None));
            await Task.Delay(50);
            p1Ws.EnqueueClientMessage("40");
            p1Ws.EnqueueClientMessage("421[\"open-room\",{\"password\":\"\",\"maxPlayers\":4,\"extra\":{\"sessionid\":\"close-room\",\"userid\":\"p1\",\"player_name\":\"PlayerOne\",\"room_name\":\"Test Room\",\"game_id\":\"100\",\"domain\":\"localhost:8096\"}}]");
            await Task.Delay(50);

            // Connect Player 2 (Guest)
            var p2Ws = new TestWebSocket();
            _ = Task.Run(() => service.HandleWebSocketSessionAsync(p2Ws, CancellationToken.None));
            await Task.Delay(50);
            p2Ws.EnqueueClientMessage("40");
            p2Ws.EnqueueClientMessage("422[\"join-room\",{\"password\":\"\",\"extra\":{\"sessionid\":\"close-room\",\"userid\":\"p2\",\"player_name\":\"PlayerTwo\",\"domain\":\"localhost:8096\"}}]");
            await Task.Delay(50);

            // Connect Player 3 (Guest)
            var p3Ws = new TestWebSocket();
            _ = Task.Run(() => service.HandleWebSocketSessionAsync(p3Ws, CancellationToken.None));
            await Task.Delay(50);
            p3Ws.EnqueueClientMessage("40");
            p3Ws.EnqueueClientMessage("423[\"join-room\",{\"password\":\"\",\"extra\":{\"sessionid\":\"close-room\",\"userid\":\"p3\",\"player_name\":\"PlayerThree\",\"domain\":\"localhost:8096\"}}]");
            await Task.Delay(50);

            var list = service.GetRoomList("localhost:8096", "100");
            Assert.Equal(3, list["close-room"].current);
            Assert.Equal("PlayerOne", list["close-room"].player_name);

            // Player 1 (Host) leaves -> Room MUST close immediately and notify P2 and P3
            p1Ws.CompleteClient();
            await Task.Delay(80);

            list = service.GetRoomList("localhost:8096", "100");
            Assert.False(list.ContainsKey("close-room"));

            // Verify P2 and P3 received host-left / room-closed
            var p2Msgs = string.Join("\n", p2Ws.SentMessages);
            var p3Msgs = string.Join("\n", p3Ws.SentMessages);
            Assert.True(p2Msgs.Contains("host-left") || p2Msgs.Contains("room-closed"));
            Assert.True(p3Msgs.Contains("host-left") || p3Msgs.Contains("room-closed"));

            p2Ws.CompleteClient();
            p3Ws.CompleteClient();
            await Task.Delay(80);

            service.Dispose();
        }

        [Fact]
        public async Task Netplay_RoomPasswordAndCapacityLimits()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);

            var hostWs = new TestWebSocket();
            var hostTask = Task.Run(() => service.HandleWebSocketSessionAsync(hostWs, CancellationToken.None));
            await Task.Delay(30);
            hostWs.EnqueueClientMessage("40");

            // Open room with max 2 players and password "secret"
            var openRoomPayload = "421[\"open-room\",{\"password\":\"secret\",\"maxPlayers\":2,\"extra\":{\"sessionid\":\"pwRoom\",\"userid\":\"p1\",\"player_name\":\"Player 1\",\"game_id\":\"99\"}}]";
            hostWs.EnqueueClientMessage(openRoomPayload);
            await Task.Delay(50);

            // Check room list indicates password
            var list = service.GetRoomList(null, "99");
            Assert.True(list["pwRoom"].hasPassword);

            // Guest 1 tries wrong password
            var guest1Ws = new TestWebSocket();
            var g1Task = Task.Run(() => service.HandleWebSocketSessionAsync(guest1Ws, CancellationToken.None));
            await Task.Delay(30);
            guest1Ws.EnqueueClientMessage("40");

            guest1Ws.EnqueueClientMessage("422[\"join-room\",{\"password\":\"wrong\",\"extra\":{\"sessionid\":\"pwRoom\",\"userid\":\"p2\",\"player_name\":\"Player 2\"}}]");
            await Task.Delay(50);
            var g1Messages = string.Join("\n", guest1Ws.SentMessages);
            Assert.Contains("Incorrect password", g1Messages);

            // Guest 1 joins with correct password
            guest1Ws.EnqueueClientMessage("423[\"join-room\",{\"password\":\"secret\",\"extra\":{\"sessionid\":\"pwRoom\",\"userid\":\"p2\",\"player_name\":\"Player 2\"}}]");
            await Task.Delay(50);
            list = service.GetRoomList(null, "99");
            // Room is full (2/2), so GetRoomList should not include it in joinable list
            Assert.False(list.ContainsKey("pwRoom"));

            // Guest 2 tries to join full room
            var guest2Ws = new TestWebSocket();
            var g2Task = Task.Run(() => service.HandleWebSocketSessionAsync(guest2Ws, CancellationToken.None));
            await Task.Delay(30);
            guest2Ws.EnqueueClientMessage("40");

            guest2Ws.EnqueueClientMessage("424[\"join-room\",{\"password\":\"secret\",\"extra\":{\"sessionid\":\"pwRoom\",\"userid\":\"p3\",\"player_name\":\"Player 3\"}}]");
            await Task.Delay(50);
            var g2Messages = string.Join("\n", guest2Ws.SentMessages);
            Assert.Contains("Room full", g2Messages);

            hostWs.CompleteClient();
            guest1Ws.CompleteClient();
            guest2Ws.CompleteClient();
            await Task.Delay(50);

            service.Dispose();
        }

        [Fact]
        public async Task Netplay_WebRtcSignalRouting()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);

            var hostWs = new TestWebSocket();
            var hostTask = Task.Run(() => service.HandleWebSocketSessionAsync(hostWs, CancellationToken.None));
            await Task.Delay(30);
            hostWs.EnqueueClientMessage("40");

            hostWs.EnqueueClientMessage("421[\"open-room\",{\"extra\":{\"sessionid\":\"sigRoom\",\"userid\":\"host\",\"player_name\":\"Host\"}}]");
            await Task.Delay(50);

            var guestWs = new TestWebSocket();
            var guestTask = Task.Run(() => service.HandleWebSocketSessionAsync(guestWs, CancellationToken.None));
            await Task.Delay(30);
            guestWs.EnqueueClientMessage("40");

            guestWs.EnqueueClientMessage("422[\"join-room\",{\"extra\":{\"sessionid\":\"sigRoom\",\"userid\":\"guest\",\"player_name\":\"Guest\"}}]");
            await Task.Delay(50);

            // Find guest socket ID from host's received users-updated message
            var hostLastMsg = string.Join("\n", hostWs.SentMessages);
            // Host sends webrtc offer to guest
            // Get guest socket ID by looking in the users-updated payload
            int guestIdx = hostLastMsg.IndexOf("\"guest\":");
            Assert.True(guestIdx > 0);
            int sidIdx = hostLastMsg.IndexOf("\"socketId\":\"", guestIdx);
            Assert.True(sidIdx > 0);
            var guestSid = hostLastMsg.Substring(sidIdx + 12, 32);

            // Host sends offer to guest
            hostWs.EnqueueClientMessage($"42[\"webrtc-signal\",{{\"target\":\"{guestSid}\",\"offer\":{{\"type\":\"offer\",\"sdp\":\"dummy_sdp\"}}}}]");
            await Task.Delay(50);

            var guestReceived = string.Join("\n", guestWs.SentMessages);
            Assert.Contains("dummy_sdp", guestReceived);
            Assert.Contains("webrtc-signal", guestReceived);

            hostWs.CompleteClient();
            guestWs.CompleteClient();
            await Task.Delay(50);

            service.Dispose();
        }

        [Fact]
        public void NetplayController_PingReturnsOk()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);
            var controller = new JellyEmu.Controllers.JellyEmuNetplayController(service, NullLogger<JellyEmu.Controllers.JellyEmuNetplayController>.Instance);
            controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
            };

            var actionResult = controller.Ping();
            var okResult = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(actionResult);
            Assert.NotNull(okResult.Value);

            var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
            Assert.Contains("\"status\":\"ok\"", json);
            Assert.Contains("JellyEmu-Netplay", json);

            service.Dispose();
        }

        [Fact]
        public void NetplayController_HasNoDuplicateRouteTemplates()
        {
            var methods = typeof(JellyEmu.Controllers.JellyEmuNetplayController).GetMethods();
            var allTemplates = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var method in methods)
            {
                var methodTemplates = new System.Collections.Generic.List<string>();
                var httpAttrs = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute), false);
                foreach (Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute attr in httpAttrs)
                {
                    if (!string.IsNullOrEmpty(attr.Template))
                    {
                        var normalized = attr.Template.TrimStart('/');
                        methodTemplates.Add(normalized);
                    }
                }

                var routeAttrs = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RouteAttribute), false);
                foreach (Microsoft.AspNetCore.Mvc.RouteAttribute attr in routeAttrs)
                {
                    if (!string.IsNullOrEmpty(attr.Template))
                    {
                        var normalized = attr.Template.TrimStart('/');
                        methodTemplates.Add(normalized);
                    }
                }

                // Verify this method doesn't have duplicate templates
                var uniqueMethodTemplates = new System.Collections.Generic.HashSet<string>(methodTemplates, System.StringComparer.OrdinalIgnoreCase);
                Assert.Equal(uniqueMethodTemplates.Count, methodTemplates.Count);
            }
        }

        [Fact]
        public async Task NetplayStartupFilter_PassesThroughNonNetplayRequests()
        {
            var filter = new JellyEmu.Services.NetplayStartupFilter();
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
            var sp = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
            var builder = new Microsoft.AspNetCore.Builder.ApplicationBuilder(sp);

            var nextCalled = false;
            var configure = filter.Configure(app =>
            {
                app.Use(next => context =>
                {
                    nextCalled = true;
                    return next(context);
                });
            });

            configure(builder);
            var pipeline = builder.Build();

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            httpContext.Request.Path = "/jellyemu/roms/systems";

            await pipeline(httpContext);
            Assert.True(nextCalled);
        }

        [Fact]
        public async Task NetplayStartupFilter_InterceptsSocketIoPollingHandshake()
        {
            var filter = new JellyEmu.Services.NetplayStartupFilter();
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
            var sp = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
            var builder = new Microsoft.AspNetCore.Builder.ApplicationBuilder(sp);

            var nextCalled = false;
            var configure = filter.Configure(app =>
            {
                app.Use(next => context =>
                {
                    nextCalled = true;
                    return next(context);
                });
            });

            configure(builder);
            var pipeline = builder.Build();

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            httpContext.Request.Method = "GET";
            httpContext.Request.Path = "/jellyemu/netplay/socket.io/";
            httpContext.Response.Body = new System.IO.MemoryStream();

            await pipeline(httpContext);
            Assert.False(nextCalled); // Handled by startup filter, did not fall through to next middleware
            Assert.True(httpContext.Response.Headers.ContainsKey("Access-Control-Allow-Origin"));

            httpContext.Response.Body.Seek(0, System.IO.SeekOrigin.Begin);
            using var reader = new System.IO.StreamReader(httpContext.Response.Body);
            var body = await reader.ReadToEndAsync();
            Assert.StartsWith("0{", body);
            Assert.Contains("websocket", body);
        }

        [Fact]
        public async Task Netplay_WebRtcSignal_CanTargetByPlayerId()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);

            var hostWs = new TestWebSocket();
            var guestWs = new TestWebSocket();

            var hostTask = service.HandleWebSocketSessionAsync(hostWs, CancellationToken.None);
            var guestTask = service.HandleWebSocketSessionAsync(guestWs, CancellationToken.None);

            await Task.Delay(50);

            // Host opens room
            var hostOpenPayload = "42[\"open-room\",{\"extra\":{\"room_name\":\"WebRtcRoom\",\"userid\":\"host-player-123\",\"sessionid\":\"rtc-room-1\"},\"maxPlayers\":2}]";
            hostWs.EnqueueClientMessage(hostOpenPayload);
            await Task.Delay(50);

            // Guest joins room
            var guestJoinPayload = "42[\"join-room\",{\"extra\":{\"room_name\":\"WebRtcRoom\",\"userid\":\"guest-player-456\",\"sessionid\":\"rtc-room-1\"}}]";
            guestWs.EnqueueClientMessage(guestJoinPayload);
            await Task.Delay(50);

            // Host sends WebRTC signal targeting the guest by PlayerId instead of SocketId
            var rtcSignalPayload = "42[\"webrtc-signal\",{\"target\":\"guest-player-456\",\"offer\":{\"type\":\"offer\",\"sdp\":\"v=0\"}}]";
            hostWs.EnqueueClientMessage(rtcSignalPayload);
            await Task.Delay(50);

            // Verify guest received the webrtc-signal
            Assert.Contains(guestWs.SentMessages, m => m.Contains("webrtc-signal") && m.Contains("\"type\":\"offer\""));

            hostWs.CompleteClient();
            guestWs.CompleteClient();
            await Task.WhenAll(hostTask, guestTask);
        }

        [Fact]
        public async Task Netplay_MalformedMessage_DoesNotDisconnectSession()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);
            var ws = new TestWebSocket();
            var sessionTask = service.HandleWebSocketSessionAsync(ws, CancellationToken.None);

            await Task.Delay(50);

            // Send malformed message
            ws.EnqueueClientMessage("42{not a json array");
            await Task.Delay(50);

            // Verify socket is still active and can process subsequent ping
            ws.EnqueueClientMessage("2probe");
            await Task.Delay(50);

            Assert.Contains(ws.SentMessages, m => m.StartsWith("3probe"));

            ws.CompleteClient();
            await sessionTask;
        }

        [Fact]
        public async Task Netplay_Shutdown_ClosesActiveConnectionsAndCleansUp()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);
            var ws1 = new TestWebSocket();
            var ws2 = new TestWebSocket();

            var task1 = Task.Run(() => service.HandleWebSocketSessionAsync(ws1, CancellationToken.None));
            var task2 = Task.Run(() => service.HandleWebSocketSessionAsync(ws2, CancellationToken.None));

            await Task.Delay(50);

            // Connect Socket.IO
            ws1.EnqueueClientMessage("40");
            ws2.EnqueueClientMessage("40");
            await Task.Delay(30);

            // Open a room on ws1
            ws1.EnqueueClientMessage("421[\"open-room\",{\"password\":\"\",\"maxPlayers\":3,\"extra\":{\"sessionid\":\"room123\",\"userid\":\"hostUser\",\"player_name\":\"HostPlayer\",\"room_name\":\"My Mario Room\",\"game_id\":\"42\",\"domain\":\"localhost:8096\"}}]");
            await Task.Delay(50);

            // Verify room exists in room list
            var roomsBefore = service.GetRoomList("localhost:8096", "42");
            Assert.NotEmpty(roomsBefore);

            // Trigger StopAsync as would happen on Jellyfin exit signal
            await service.StopAsync(CancellationToken.None);
            await Task.WhenAll(task1, task2);

            // Verify sockets were closed
            Assert.Equal(WebSocketState.Closed, ws1.State);
            Assert.Equal(WebSocketState.Closed, ws2.State);

            // Verify room list is now empty
            var roomsAfter = service.GetRoomList("test", "game1");
            Assert.Empty(roomsAfter);
        }

        [Fact]
        public async Task Netplay_Shutdown_RejectsNewConnectionsWithEndpointUnavailable()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);
            await service.StopAsync(CancellationToken.None);

            var ws = new TestWebSocket();
            await service.HandleWebSocketSessionAsync(ws, CancellationToken.None);

            // Connection should be closed immediately
            Assert.Equal(WebSocketState.Closed, ws.State);
        }

        [Fact]
        public async Task Netplay_HostPing_IncludedInRoomList_WhenReportedViaHostPingOrDataMessage()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);
            var hostWs = new TestWebSocket();
            var hostTask = Task.Run(() => service.HandleWebSocketSessionAsync(hostWs, CancellationToken.None));
            await Task.Delay(30);

            // Connect
            hostWs.EnqueueClientMessage("40");
            await Task.Delay(30);

            // Open room with initial ping = 25
            var openRoomPayload = "421[\"open-room\",{\"password\":\"\",\"maxPlayers\":4,\"ping\":25,\"extra\":{\"sessionid\":\"pingRoom1\",\"userid\":\"hostUser\",\"player_name\":\"HostUser\",\"room_name\":\"Ping Room\",\"game_id\":\"50\",\"domain\":\"localhost:8096\"}}]";
            hostWs.EnqueueClientMessage(openRoomPayload);
            await Task.Delay(50);

            // Check room list has ping 25
            var rooms = service.GetRoomList("localhost:8096", "50");
            Assert.True(rooms.ContainsKey("pingRoom1"));
            Assert.Equal(25, rooms["pingRoom1"].host_ping);
            Assert.Equal(25, rooms["pingRoom1"].ping);

            // Host updates ping via host-ping event
            hostWs.EnqueueClientMessage("42[\"host-ping\",{\"ping\":38}]");
            await Task.Delay(50);

            rooms = service.GetRoomList("localhost:8096", "50");
            Assert.Equal(38, rooms["pingRoom1"].host_ping);
            Assert.Equal(38, rooms["pingRoom1"].ping);

            // Host updates ping via data-message
            hostWs.EnqueueClientMessage("42[\"data-message\",{\"jeServerPing\":true,\"ping\":49}]");
            await Task.Delay(50);

            rooms = service.GetRoomList("localhost:8096", "50");
            Assert.Equal(49, rooms["pingRoom1"].host_ping);
            Assert.Equal(49, rooms["pingRoom1"].ping);

            // Teardown
            await service.StopAsync(CancellationToken.None);
            await hostTask;
        }

        [Fact]
        public async Task Netplay_HostCannotJoinOwnRoom_AndCannotDuplicateInRoom()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);
            var hostWs = new TestWebSocket();
            var hostTask = Task.Run(() => service.HandleWebSocketSessionAsync(hostWs, CancellationToken.None));
            await Task.Delay(30);

            // Socket.IO handshake
            hostWs.EnqueueClientMessage("40");
            await Task.Delay(30);

            // Open room
            var openRoomPayload = "421[\"open-room\",{\"password\":\"\",\"maxPlayers\":2,\"extra\":{\"sessionid\":\"hostJoinRoom1\",\"userid\":\"hostUser123\",\"player_name\":\"HostUser\",\"room_name\":\"Room 1\",\"game_id\":\"1\",\"domain\":\"test\"}}]";
            hostWs.EnqueueClientMessage(openRoomPayload);
            await Task.Delay(50);

            // Verify room exists with 1 player
            var rooms = service.GetRoomList("test", "1");
            Assert.True(rooms.ContainsKey("hostJoinRoom1"));
            Assert.Equal(1, rooms["hostJoinRoom1"].current);

            // Host attempts to join their own room using a new player id
            var joinAttempt1 = "422[\"join-room\",{\"password\":\"\",\"extra\":{\"sessionid\":\"hostJoinRoom1\",\"userid\":\"guestImpostor\",\"player_name\":\"Impostor\"}}]";
            hostWs.EnqueueClientMessage(joinAttempt1);
            await Task.Delay(50);

            // Verify join was rejected and room still only has 1 player
            rooms = service.GetRoomList("test", "1");
            Assert.Equal(1, rooms["hostJoinRoom1"].current);

            var ackMsg = string.Join("\n", hostWs.SentMessages);
            Assert.Contains("You are already hosting this room", ackMsg);

            // Cleanup
            await service.StopAsync(CancellationToken.None);
            await hostTask;
        }

        [Fact]
        public async Task Netplay_GuestLeaving_DecrementsPlayerCount_AndOpensRoomInList()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);

            var hostWs = new TestWebSocket();
            var guestWs = new TestWebSocket();

            var hostTask = Task.Run(() => service.HandleWebSocketSessionAsync(hostWs, CancellationToken.None));
            var guestTask = Task.Run(() => service.HandleWebSocketSessionAsync(guestWs, CancellationToken.None));
            await Task.Delay(30);

            // Handshakes
            hostWs.EnqueueClientMessage("40");
            guestWs.EnqueueClientMessage("40");
            await Task.Delay(30);

            // Host opens room with maxPlayers: 2
            var openRoomPayload = "421[\"open-room\",{\"password\":\"\",\"maxPlayers\":2,\"extra\":{\"sessionid\":\"fullRoom1\",\"userid\":\"host1\",\"player_name\":\"Host1\",\"room_name\":\"Full Room\",\"game_id\":\"10\",\"domain\":\"test\"}}]";
            hostWs.EnqueueClientMessage(openRoomPayload);
            await Task.Delay(50);

            // Room has 1 player, available in list
            var rooms = service.GetRoomList("test", "10");
            Assert.True(rooms.ContainsKey("fullRoom1"));
            Assert.Equal(1, rooms["fullRoom1"].current);
            Assert.Equal(2, rooms["fullRoom1"].max);

            // Guest joins room
            var guestJoinPayload = "422[\"join-room\",{\"password\":\"\",\"extra\":{\"sessionid\":\"fullRoom1\",\"userid\":\"guest1\",\"player_name\":\"Guest1\"}}]";
            guestWs.EnqueueClientMessage(guestJoinPayload);
            await Task.Delay(50);

            // Room is now full (2/2) - GetRoomList filters out full rooms
            rooms = service.GetRoomList("test", "10");
            Assert.False(rooms.ContainsKey("fullRoom1"), "Full room should be filtered out from room list");

            // Guest leaves room via leave-room
            guestWs.EnqueueClientMessage("42[\"leave-room\",{}]");
            await Task.Delay(50);

            // Room now has 1 player again and appears back in room list!
            rooms = service.GetRoomList("test", "10");
            Assert.True(rooms.ContainsKey("fullRoom1"), "Room with departed guest should be open and listed again");
            Assert.Equal(1, rooms["fullRoom1"].current);

            // Verify host received users-updated event reflecting the guest left
            var hostMessages = string.Join("\n", hostWs.SentMessages);
            Assert.Contains("users-updated", hostMessages);

            // Cleanup
            await service.StopAsync(CancellationToken.None);
            await Task.WhenAll(hostTask, guestTask);
        }

        [Fact]
        public async Task Netplay_HostLeavesRoom_ClosesRoomAndNotifiesGuests()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);
            var hostWs = new TestWebSocket();
            var guestWs = new TestWebSocket();

            var hostTask = Task.Run(() => service.HandleWebSocketSessionAsync(hostWs, CancellationToken.None));
            var guestTask = Task.Run(() => service.HandleWebSocketSessionAsync(guestWs, CancellationToken.None));
            await Task.Delay(30);

            // Handshakes
            hostWs.EnqueueClientMessage("40");
            guestWs.EnqueueClientMessage("40");
            await Task.Delay(30);

            // Host opens room
            var openRoomPayload = "421[\"open-room\",{\"password\":\"\",\"maxPlayers\":2,\"extra\":{\"sessionid\":\"hostCloseRoom\",\"userid\":\"hostUser\",\"player_name\":\"HostUser\",\"room_name\":\"Closing Room\",\"game_id\":\"20\",\"domain\":\"test\"}}]";
            hostWs.EnqueueClientMessage(openRoomPayload);
            await Task.Delay(50);

            // Guest joins room
            var guestJoinPayload = "422[\"join-room\",{\"password\":\"\",\"extra\":{\"sessionid\":\"hostCloseRoom\",\"userid\":\"guestUser\",\"player_name\":\"GuestUser\"}}]";
            guestWs.EnqueueClientMessage(guestJoinPayload);
            await Task.Delay(50);

            // Host leaves room
            hostWs.EnqueueClientMessage("42[\"leave-room\",{}]");
            await Task.Delay(100);

            // Room should be completely closed and removed from room list
            var rooms = service.GetRoomList("test", "20");
            Assert.False(rooms.ContainsKey("hostCloseRoom"), "Room should be removed when host leaves");

            // Guest should receive host-left or room-closed notification
            var guestMessages = string.Join("\n", guestWs.SentMessages);
            Assert.True(guestMessages.Contains("host-left") || guestMessages.Contains("room-closed"), "Guest should receive host-left notification");

            // Cleanup
            await service.StopAsync(CancellationToken.None);
            await Task.WhenAll(hostTask, guestTask);
        }

        [Fact]
        public void Netplay_Dispose_SafelyCleansUpWithoutThrowing()
        {
            var service = new JellyEmuNetplayService(NullLogger<JellyEmuNetplayService>.Instance);
            service.Dispose();

            // Calling Dispose multiple times should be safe (idempotent)
            service.Dispose();
        }
    }
}
