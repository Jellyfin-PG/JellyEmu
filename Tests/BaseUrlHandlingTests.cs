using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using JellyEmu.Controllers;
using JellyEmu.Services;

namespace JellyEmu.Tests
{
    [NonController]
    public class TestableBaseController : JellyEmuBaseController
    {
        public TestableBaseController() : base(null!, null!, null!, null!, null!, null!)
        {
        }

        [NonAction]
        public string TestGetPathBase() => GetPathBase();

        [NonAction]
        public string TestGetServerBase() => GetServerBase();

        [NonAction]
        public string TestToAppUrl(string relativePath) => ToAppUrl(relativePath);
    }

    public class BaseUrlHandlingTests
    {
        [Fact]
        public void JellyEmuUIInjector_IncludesDynamicBaseUrlHandling()
        {
            var payload = new PatchRequestPayload
            {
                Contents = "<!DOCTYPE html><html><head><title>Jellyfin</title></head><body><div id=\"app\"></div></body></html>"
            };

            var result = JellyEmuUIInjector.InjectMods(payload);

            Assert.Contains("bundle.css", result);
            Assert.Contains("bundle.js", result);
            Assert.Contains("data-jellyemu-mods", result);
            Assert.Contains("__JELLYEMU_CONFIG__", result);
        }

        [Theory]
        [InlineData("", "/jellyemu/rom/123", "/jellyemu/rom/123")]
        [InlineData("/jellyfin", "/jellyemu/rom/123", "/jellyfin/jellyemu/rom/123")]
        [InlineData("/jellyfin", "jellyemu/rom/123", "/jellyfin/jellyemu/rom/123")]
        [InlineData("/custom/base", "/jellyemu/play/abc", "/custom/base/jellyemu/play/abc")]
        [InlineData("/custom/base/", "/jellyemu/play/abc", "/custom/base/jellyemu/play/abc")]
        public void Controller_ToAppUrl_CorrectlyPrefixesPathBase(string pathBase, string relativePath, string expectedUrl)
        {
            var controller = new TestableBaseController();
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "http";
            httpContext.Request.Host = new HostString("localhost", 8096);
            httpContext.Request.PathBase = new PathString(pathBase);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            var actual = controller.TestToAppUrl(relativePath);
            Assert.Equal(expectedUrl, actual);
        }

        [Theory]
        [InlineData("", "http://localhost:8096")]
        [InlineData("/jellyfin", "http://localhost:8096/jellyfin")]
        [InlineData("/jellyfin/", "http://localhost:8096/jellyfin")]
        public void Controller_GetServerBase_IncludesPathBase(string pathBase, string expectedServerBase)
        {
            var controller = new TestableBaseController();
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "http";
            httpContext.Request.Host = new HostString("localhost", 8096);
            httpContext.Request.PathBase = new PathString(pathBase);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            var actual = controller.TestGetServerBase();
            Assert.Equal(expectedServerBase, actual);
        }
    }
}
