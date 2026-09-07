using System;
using System.Text.Json;
using System.Threading.Tasks;
using JellyEmu.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace JellyEmu.Services
{
    /// <summary>
    /// Startup filter that intercepts Netplay WebSocket &amp; Socket.IO traffic before Jellyfin's built-in
    /// WebSocketHandlerMiddleware, preventing collisions with Jellyfin's internal WebSocket message protocol.
    /// </summary>
    public class NetplayStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.UseWebSockets(new WebSocketOptions());
                app.Use(async (context, nextMiddleware) =>
                {
                    var pathStr = context.Request.Path.Value ?? string.Empty;
                    bool isNetplaySocket = pathStr.IndexOf("/jellyemu/netplay/socket.io", StringComparison.OrdinalIgnoreCase) >= 0
                        || pathStr.IndexOf("/jellyemu/netplay/ws", StringComparison.OrdinalIgnoreCase) >= 0
                        || pathStr.IndexOf("/socket.io", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isNetplaySocket)
                    {
                        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                        context.Response.Headers["Access-Control-Allow-Headers"] = "*";

                        if (HttpMethods.IsOptions(context.Request.Method))
                        {
                            context.Response.StatusCode = StatusCodes.Status200OK;
                            return;
                        }

                        if (context.WebSockets.IsWebSocketRequest)
                        {
                            var netplayService = context.RequestServices.GetRequiredService<JellyEmuNetplayService>();
                            using var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                            await netplayService.HandleWebSocketSessionAsync(webSocket, context.RequestAborted).ConfigureAwait(false);
                            return;
                        }

                        if (HttpMethods.IsPost(context.Request.Method))
                        {
                            context.Response.ContentType = "text/plain; charset=UTF-8";
                            await context.Response.WriteAsync("ok", context.RequestAborted).ConfigureAwait(false);
                            return;
                        }

                        if (HttpMethods.IsGet(context.Request.Method))
                        {
                            // Engine.IO v4 open handshake packet
                            var sid = Guid.NewGuid().ToString("N");
                            var handshake = new
                            {
                                sid = sid,
                                upgrades = new[] { "websocket" },
                                pingInterval = 25000,
                                pingTimeout = 20000,
                                maxPayload = 1000000
                            };

                            context.Response.ContentType = "text/plain; charset=UTF-8";
                            await context.Response.WriteAsync("0" + JsonSerializer.Serialize(handshake), context.RequestAborted).ConfigureAwait(false);
                            return;
                        }
                    }

                    await nextMiddleware().ConfigureAwait(false);
                });

                next(app);
            };
        }
    }
}
