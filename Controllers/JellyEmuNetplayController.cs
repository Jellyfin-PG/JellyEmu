using System;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Tasks;
using JellyEmu.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Controllers
{
    /// <summary>
    /// Embedded Netplay relay server endpoint controller.
    /// Provides room listing and WebSocket / Socket.IO signaling.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public class JellyEmuNetplayController : ControllerBase
    {
        private readonly JellyEmuNetplayService _netplayService;
        private readonly ILogger<JellyEmuNetplayController> _logger;

        public JellyEmuNetplayController(
            JellyEmuNetplayService netplayService,
            ILogger<JellyEmuNetplayController> logger)
        {
            _netplayService = netplayService;
            _logger = logger;
        }

        private void ApplyCorsHeaders()
        {
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            Response.Headers["Access-Control-Allow-Headers"] = "*";
        }

        /// <summary>
        /// Room list discovery endpoint compatible with EmulatorJS /list query.
        /// </summary>
        [HttpGet("/jellyemu/netplay/list")]
        [HttpGet("/list")]
        public IActionResult GetRooms([FromQuery] string? domain, [FromQuery] string? game_id)
        {
            ApplyCorsHeaders();
            var rooms = _netplayService.GetRoomList(domain, game_id);
            return Ok(rooms);
        }

        /// <summary>
        /// Games list endpoint compatible with EmulatorJS /games query.
        /// </summary>
        [HttpGet("/jellyemu/netplay/games")]
        [HttpGet("/games")]
        public IActionResult GetGames()
        {
            ApplyCorsHeaders();
            return Ok(new object());
        }

        /// <summary>
        /// Lightweight health check &amp; ping endpoint.
        /// </summary>
        [HttpGet("/jellyemu/netplay/ping")]
        [HttpGet("/ping")]
        public IActionResult Ping()
        {
            ApplyCorsHeaders();
            return Ok(new
            {
                status = "ok",
                server = "JellyEmu-Netplay",
                time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        /// <summary>
        /// WebSocket &amp; Socket.IO v4 signaling endpoint for netplay rooms, host migration, chat, and WebRTC streaming.
        /// </summary>
        [Route("/jellyemu/netplay/ws")]
        [Route("/jellyemu/netplay/socket.io")]
        [Route("/jellyemu/netplay/socket.io/{**catchall}")]
        [Route("/socket.io")]
        [Route("/socket.io/{**catchall}")]
        public async Task HandleSocket()
        {
            ApplyCorsHeaders();

            if (HttpMethods.IsOptions(Request.Method))
            {
                Response.StatusCode = StatusCodes.Status200OK;
                return;
            }

            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                await _netplayService.HandleWebSocketSessionAsync(webSocket, HttpContext.RequestAborted).ConfigureAwait(false);
                return;
            }

            if (HttpMethods.IsPost(Request.Method))
            {
                Response.ContentType = "text/plain; charset=UTF-8";
                await Response.WriteAsync("ok", HttpContext.RequestAborted).ConfigureAwait(false);
                return;
            }

            // HTTP Polling fallback for Engine.IO v4 handshake:
            // Client asks: ?EIO=4&transport=polling
            var sid = Guid.NewGuid().ToString("N");
            var handshake = new
            {
                sid = sid,
                upgrades = new[] { "websocket" },
                pingInterval = 25000,
                pingTimeout = 20000,
                maxPayload = 1000000
            };

            Response.ContentType = "text/plain; charset=UTF-8";
            // Engine.IO v4 open packet: '0' + json
            await Response.WriteAsync("0" + JsonSerializer.Serialize(handshake), HttpContext.RequestAborted).ConfigureAwait(false);
        }
    }
}
