using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using JellyEmu.Controllers;
using JellyEmu.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using JellyfinUser = Jellyfin.Database.Implementations.Entities.User;

namespace JellyEmu.Tests
{
    public class RetroArchCloudSyncTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly Mock<ILibraryManager> _libraryManagerMock;
        private readonly Mock<IApplicationPaths> _appPathsMock;
        private readonly Mock<IUserManager> _userManagerMock;
        private readonly Mock<IAuthenticationManager> _authManagerMock;
        private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
        private readonly JellyfinUser _testUser;

        public RetroArchCloudSyncTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "je_cloudsync_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _libraryManagerMock = new Mock<ILibraryManager>();
            _appPathsMock = new Mock<IApplicationPaths>();
            _appPathsMock.Setup(a => a.DataPath).Returns(_tempDir);
            _appPathsMock.Setup(a => a.PluginConfigurationsPath).Returns(_tempDir);

            _userManagerMock = new Mock<IUserManager>();
            _authManagerMock = new Mock<IAuthenticationManager>();
            _httpClientFactoryMock = new Mock<IHttpClientFactory>();

            _testUser = new JellyfinUser("sean", "default", "default");
            _userManagerMock.Setup(u => u.GetUserByName("sean")).Returns(_testUser);
            _userManagerMock.Setup(u => u.GetUserById(_testUser.Id)).Returns(_testUser);
            _userManagerMock.Setup(u => u.AuthenticateUser("sean", "correctpwd", It.IsAny<string>(), false))
                .ReturnsAsync(_testUser);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        private RetroArchCloudSyncController CreateController(string? basicAuthUser = null, string? basicAuthPass = null, bool useClaims = false)
        {
            var controller = new RetroArchCloudSyncController(
                _libraryManagerMock.Object,
                _appPathsMock.Object,
                NullLogger<RetroArchCloudSyncController>.Instance,
                null!,
                null!,
                _httpClientFactoryMock.Object,
                _userManagerMock.Object,
                _authManagerMock.Object);

            var httpContext = new DefaultHttpContext();
            if (useClaims)
            {
                var identity = new ClaimsIdentity(new[]
                {
                    new Claim("Jellyfin-UserId", _testUser.Id.ToString())
                }, "TestAuth");
                httpContext.User = new ClaimsPrincipal(identity);
            }
            else if (!string.IsNullOrEmpty(basicAuthUser))
            {
                var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{basicAuthUser}:{basicAuthPass ?? string.Empty}"));
                httpContext.Request.Headers["Authorization"] = $"Basic {creds}";
            }

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            return controller;
        }

        [Fact]
        public void Options_Returns_200_And_DAV_Headers()
        {
            var controller = CreateController();
            var result = controller.Options();

            var okResult = Assert.IsType<OkResult>(result);
            Assert.Equal(200, okResult.StatusCode);

            var headers = controller.Response.Headers;
            Assert.Equal("1, 2", headers["DAV"].ToString());
            Assert.Equal("DAV", headers["MS-Author-Via"].ToString());
            Assert.Contains("PROPFIND", headers["Allow"].ToString());
            Assert.Contains("MKCOL", headers["Allow"].ToString());
            Assert.Contains("PUT", headers["Allow"].ToString());
            Assert.Contains("GET", headers["Allow"].ToString());
        }

        [Fact]
        public async Task Unauthenticated_Requests_Return_401_With_WWWAuthenticate_Header()
        {
            var controller = CreateController(); // No auth
            var result = await controller.Propfind(null);

            Assert.IsType<UnauthorizedResult>(result);
            Assert.Contains("Basic realm=\"JellyEmu WebDAV\"", controller.Response.Headers["WWW-Authenticate"].ToString());
        }

        [Fact]
        public async Task BasicAuth_ValidCredentials_Authenticates_User()
        {
            var controller = CreateController(basicAuthUser: "sean", basicAuthPass: "correctpwd");
            var result = await controller.Propfind(null);

            var contentResult = Assert.IsType<ContentResult>(result);
            Assert.Equal(207, controller.Response.StatusCode);
            Assert.Contains("<D:multistatus", contentResult.Content);
        }

        [Fact]
        public async Task BasicAuth_InvalidCredentials_Returns_401()
        {
            _userManagerMock.Setup(u => u.AuthenticateUser("sean", "wrongpwd", It.IsAny<string>(), false))
                .ThrowsAsync(new Exception("Invalid password"));

            var controller = CreateController(basicAuthUser: "sean", basicAuthPass: "wrongpwd");
            var result = await controller.Propfind(null);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task ApiKey_Authentication_Works_When_Password_Is_ApiKey()
        {
            var apiKeyInfo = new AuthenticationInfo
            {
                AccessToken = "supersecretapikey123",
                AppName = "RetroArch"
            };
            _authManagerMock.Setup(a => a.GetApiKeys()).ReturnsAsync(new List<AuthenticationInfo> { apiKeyInfo });

            var controller = CreateController(basicAuthUser: "sean", basicAuthPass: "supersecretapikey123");
            var result = await controller.Propfind(null);

            Assert.IsType<ContentResult>(result);
            Assert.Equal(207, controller.Response.StatusCode);
        }

        [Fact]
        public async Task Propfind_Depth0_And_Depth1_Generates_Valid_MultiStatus_Xml()
        {
            var controller = CreateController(useClaims: true);
            controller.Request.Path = "/jellyemu/retroarch/cloudsync";

            // Put a file and directory in the user's sync folder
            var userRoot = Path.Combine(_tempDir, "retroarch", "cloudsync", _testUser.Id.ToString("N"));
            Directory.CreateDirectory(Path.Combine(userRoot, "saves"));
            File.WriteAllText(Path.Combine(userRoot, "manifest.server"), "dummy manifest");

            // Depth 0: only root
            controller.Request.Headers["Depth"] = "0";
            var depth0Result = await controller.Propfind(null);
            var content0 = Assert.IsType<ContentResult>(depth0Result);
            var xml0 = XDocument.Parse(content0.Content!);
            XNamespace d = "DAV:";
            var responses0 = xml0.Descendants(d + "response").ToList();
            Assert.Single(responses0);
            Assert.Equal("/jellyemu/retroarch/cloudsync/", responses0[0].Element(d + "href")?.Value);

            // Depth 1: root + direct children
            controller.Request.Headers["Depth"] = "1";
            var depth1Result = await controller.Propfind(null);
            var content1 = Assert.IsType<ContentResult>(depth1Result);
            var xml1 = XDocument.Parse(content1.Content!);
            var responses1 = xml1.Descendants(d + "response").ToList();
            Assert.Equal(3, responses1.Count); // root, saves/, manifest.server

            var hrefs = responses1.Select(r => r.Element(d + "href")?.Value).ToList();
            Assert.Contains("/jellyemu/retroarch/cloudsync/", hrefs);
            Assert.Contains("/jellyemu/retroarch/cloudsync/saves/", hrefs);
            Assert.Contains("/jellyemu/retroarch/cloudsync/manifest.server", hrefs);
        }

        [Fact]
        public async Task Mkcol_Creates_Collection_And_Handles_405_409()
        {
            var controller = CreateController(useClaims: true);

            // 1. Valid MKCOL creates folder
            var result1 = await controller.Mkcol("states");
            var statusCode1 = Assert.IsType<StatusCodeResult>(result1);
            Assert.Equal(StatusCodes.Status201Created, statusCode1.StatusCode);
            var userRoot = Path.Combine(_tempDir, "retroarch", "cloudsync", _testUser.Id.ToString("N"));
            Assert.True(Directory.Exists(Path.Combine(userRoot, "states")));

            // 2. Existing MKCOL returns 405 Method Not Allowed
            var result2 = await controller.Mkcol("states");
            var statusCode2 = Assert.IsType<StatusCodeResult>(result2);
            Assert.Equal(StatusCodes.Status405MethodNotAllowed, statusCode2.StatusCode);

            // 3. Parent not existing returns 409 Conflict
            var result3 = await controller.Mkcol("nested/parent/folder");
            var statusCode3 = Assert.IsType<StatusCodeResult>(result3);
            Assert.Equal(StatusCodes.Status409Conflict, statusCode3.StatusCode);
        }

        [Fact]
        public async Task Put_Get_Head_Delete_FileLifecycle_Works()
        {
            var controller = CreateController(useClaims: true);
            controller.Request.Path = "/jellyemu/retroarch/cloudsync/manifest.server";

            var fileContent = "12345678 saves/game.srm\n";
            controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(fileContent));

            // PUT
            var putResult = await controller.Put("manifest.server");
            var putStatus = Assert.IsType<StatusCodeResult>(putResult);
            Assert.Equal(StatusCodes.Status201Created, putStatus.StatusCode);

            // HEAD
            controller.Request.Method = "HEAD";
            var headResult = await controller.GetOrHead("manifest.server");
            Assert.IsType<OkResult>(headResult);
            Assert.Equal(fileContent.Length.ToString(), controller.Response.Headers["Content-Length"].ToString());
            Assert.False(string.IsNullOrEmpty(controller.Response.Headers["ETag"].ToString()));

            // GET
            controller.Request.Method = "GET";
            var getResult = await controller.GetOrHead("manifest.server");
            var fileStreamResult = Assert.IsType<FileStreamResult>(getResult);
            using (var reader = new StreamReader(fileStreamResult.FileStream))
            {
                var content = await reader.ReadToEndAsync();
                Assert.Equal(fileContent, content);
            }

            // PUT Overwrite returns 204 NoContent
            controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("updated manifest"));
            var overwriteResult = await controller.Put("manifest.server");
            Assert.IsType<NoContentResult>(overwriteResult);

            // DELETE
            var deleteResult = await controller.Delete("manifest.server");
            Assert.IsType<NoContentResult>(deleteResult);

            // Subsequent GET returns 404
            var notFoundResult = await controller.GetOrHead("manifest.server");
            Assert.IsType<NotFoundResult>(notFoundResult);
        }

        [Fact]
        public async Task Move_And_Copy_Handle_Destination_And_Overwrite()
        {
            var controller = CreateController(useClaims: true);
            controller.Request.Path = "/jellyemu/retroarch/cloudsync/source.txt";
            controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("hello world"));
            await controller.Put("source.txt");

            // COPY
            controller.Request.Headers["Destination"] = "http://localhost:8096/jellyemu/retroarch/cloudsync/copied.txt";
            var copyResult = await controller.Copy("source.txt");
            var copyStatus = Assert.IsType<StatusCodeResult>(copyResult);
            Assert.Equal(StatusCodes.Status201Created, copyStatus.StatusCode);

            var userRoot = Path.Combine(_tempDir, "retroarch", "cloudsync", _testUser.Id.ToString("N"));
            Assert.True(File.Exists(Path.Combine(userRoot, "source.txt")));
            Assert.True(File.Exists(Path.Combine(userRoot, "copied.txt")));

            // MOVE with Overwrite: F when target exists returns 412
            controller.Request.Headers["Destination"] = "/jellyemu/retroarch/cloudsync/copied.txt";
            controller.Request.Headers["Overwrite"] = "F";
            var moveFail = await controller.Move("source.txt");
            var failStatus = Assert.IsType<StatusCodeResult>(moveFail);
            Assert.Equal(StatusCodes.Status412PreconditionFailed, failStatus.StatusCode);

            // MOVE with Overwrite: T overwrites target and deletes source
            controller.Request.Headers["Overwrite"] = "T";
            var moveSuccess = await controller.Move("source.txt");
            var moveStatus = Assert.IsType<StatusCodeResult>(moveSuccess);
            Assert.Equal(StatusCodes.Status204NoContent, moveStatus.StatusCode);
            Assert.False(File.Exists(Path.Combine(userRoot, "source.txt")));
            Assert.True(File.Exists(Path.Combine(userRoot, "copied.txt")));
        }

        [Fact]
        public async Task PathTraversal_Attempts_Are_Safely_Rejected()
        {
            var controller = CreateController(useClaims: true);

            var result1 = await controller.Propfind("../secret.txt");
            Assert.IsType<BadRequestObjectResult>(result1);

            var result2 = await controller.Put("..\\windows\\system32\\evil.exe");
            Assert.IsType<BadRequestObjectResult>(result2);

            var result3 = await controller.GetOrHead("../../../etc/passwd");
            Assert.IsType<BadRequestObjectResult>(result3);

            var result4 = await controller.Delete("../secret");
            Assert.IsType<BadRequestObjectResult>(result4);
        }

        [Fact]
        public async Task SramPairing_Bidirectional_Sync_Works()
        {
            var gameId = Guid.NewGuid();
            var gameItem = new MediaBrowser.Controller.Entities.Book
            {
                Id = gameId,
                Name = "Chrono Trigger (USA)",
                Path = "/games/snes/Chrono Trigger (USA).sfc",
                Tags = new[] { "JellyEmu", "SNES" }
            };

            _libraryManagerMock.Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns(new[] { gameItem });

            var controller = CreateController(useClaims: true);

            // 1. RetroArch uploads Chrono Trigger (USA).srm via PUT
            var srmBytes = Encoding.UTF8.GetBytes("SRAM DATA FROM RETROARCH");
            controller.Request.Path = "/jellyemu/retroarch/cloudsync/saves/Chrono Trigger (USA).srm";
            controller.Request.Body = new MemoryStream(srmBytes);

            var putResult = await controller.Put("saves/Chrono Trigger (USA).srm");
            Assert.IsType<StatusCodeResult>(putResult);

            // Verify mirrored to JellyEmu web save: jellyemu-saves/<userId>/slot1/<itemId>.sav
            var webSavPath = Path.Combine(_tempDir, "jellyemu-saves", _testUser.Id.ToString("N"), "slot1", $"{gameId:N}.sav");
            Assert.True(File.Exists(webSavPath));
            Assert.Equal(srmBytes, File.ReadAllBytes(webSavPath));

            // 2. Now simulate web player playing and updating the save
            var newerWebSavBytes = Encoding.UTF8.GetBytes("UPDATED SRAM FROM WEB PLAYER");
            await Task.Delay(50); // Ensure timestamp difference
            File.WriteAllBytes(webSavPath, newerWebSavBytes);
            File.SetLastWriteTimeUtc(webSavPath, DateTime.UtcNow.AddMinutes(5));

            // RetroArch requests GET /jellyemu/retroarch/cloudsync/saves/Chrono Trigger (USA).srm
            controller.Request.Method = "GET";
            var getResult = await controller.GetOrHead("saves/Chrono Trigger (USA).srm");
            var fileStreamResult = Assert.IsType<FileStreamResult>(getResult);

            using (var reader = new StreamReader(fileStreamResult.FileStream))
            {
                var content = await reader.ReadToEndAsync();
                Assert.Equal("UPDATED SRAM FROM WEB PLAYER", content);
            }
        }
    }
}
