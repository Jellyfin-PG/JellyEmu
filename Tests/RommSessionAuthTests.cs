using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using JellyEmu.Controllers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JellyEmu.Tests
{
    /// <summary>
    /// The RomM and session endpoints act on the userId in the URL, so a signed-in user
    /// must not be able to act for anyone else, and anonymous callers must be rejected.
    /// </summary>
    public class RommSessionAuthTests
    {
        private static readonly string Caller = Guid.NewGuid().ToString("N");
        private static readonly string Victim = Guid.NewGuid().ToString("N");
        private static readonly string Item = Guid.NewGuid().ToString("N");

        private static T WithCaller<T>(T controller) where T : ControllerBase
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim("Jellyfin-UserId", Caller) }, "Test"))
                }
            };
            return controller;
        }

        private static JellyEmuRommController Romm() => WithCaller(new JellyEmuRommController(
            new Mock<ILibraryManager>().Object, new Mock<IApplicationPaths>().Object,
            NullLogger<JellyEmuRommController>.Instance, null!, null!, new Mock<IHttpClientFactory>().Object));

        private static JellyEmuSessionController Session() => WithCaller(new JellyEmuSessionController(
            new Mock<ILibraryManager>().Object, new Mock<IApplicationPaths>().Object,
            NullLogger<JellyEmuSessionController>.Instance, null!, null!, new Mock<IHttpClientFactory>().Object));

        [Fact]
        public async Task Romm_UserScopedActions_ForbidOtherUsers()
        {
            var c = Romm();
            Assert.IsType<ForbidResult>(await c.RommSyncStatus(Item, Victim, 1));
            Assert.IsType<ForbidResult>(await c.RommPush(Item, Victim, 1));
            Assert.IsType<ForbidResult>(await c.RommPull(Item, Victim, 1));
            Assert.IsType<ForbidResult>(await c.RommSyncOnLaunch(Item, Victim, 1));
            Assert.IsType<ForbidResult>(await c.RommSyncAfterSave(Item, Victim, 1));
            Assert.IsType<ForbidResult>(await c.RommReportPlaytime(Item, Victim));
            Assert.IsType<ForbidResult>(await c.RommPushScreenshot(Item, Victim));
        }

        [Fact]
        public async Task Session_Actions_ForbidOtherUsers()
        {
            var c = Session();
            Assert.IsType<ForbidResult>(await c.SessionStart(Item, Victim));
            Assert.IsType<ForbidResult>(await c.SessionPing(Item, Victim));
            Assert.IsType<ForbidResult>(await c.SessionStop(Item, Victim));
        }

        [Theory]
        [InlineData(typeof(JellyEmuRommController))]
        [InlineData(typeof(JellyEmuSessionController))]
        public void Controllers_RequireAuthentication(Type controller)
        {
            Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
            Assert.DoesNotContain(controller.GetMethods(), m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);
        }

        [Theory]
        [InlineData(nameof(JellyEmuRommController.RommHealth))]
        [InlineData(nameof(JellyEmuRommController.RommGetCollections))]
        [InlineData(nameof(JellyEmuRommController.RommSyncCollections))]
        public void Romm_ConfigEndpoints_AreAdminOnly(string action)
        {
            var policies = typeof(JellyEmuRommController).GetMethod(action)!
                .GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy);
            Assert.Contains("RequiresElevation", policies);
        }
    }
}
