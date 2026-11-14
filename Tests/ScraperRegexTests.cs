using System.Text.RegularExpressions;
using Xunit;

namespace JellyEmu.Tests
{
    public class ScraperRegexTests
    {
        [Fact]
        public void SearchScraperRegex_ShouldMatchVimmSearchResultRows()
        {
            // Arrange
            var html = @"
            <tr>
                <td>GBA</td>
                <td><a href = ""/vault/1234"">Spyro 2: Season of Flame</a></td>
            </tr>
            <tr>
                <td class=""system"">PS1</td>
                <td><a href=""/vault/5678"">Spyro the Dragon</a></td>
            </tr>
            ";

            // Act
            var matches = Regex.Matches(
                html,
                @"<td[^>]*>(?<system>[^<]+)</td>\s*<td[^>]*>(?:<a href\s*=\s*""[^""]*""></a>\s*)?<a href\s*=\s*""(?<url>/vault/(?<id>\d+))""[^>]*>(?<title>[^<]+)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );

            // Assert
            Assert.Equal(2, matches.Count);
            
            Assert.Equal("GBA", matches[0].Groups["system"].Value.Trim());
            Assert.Equal("/vault/1234", matches[0].Groups["url"].Value);
            Assert.Equal("1234", matches[0].Groups["id"].Value);
            Assert.Equal("Spyro 2: Season of Flame", matches[0].Groups["title"].Value.Trim());

            Assert.Equal("PS1", matches[1].Groups["system"].Value.Trim());
            Assert.Equal("/vault/5678", matches[1].Groups["url"].Value);
            Assert.Equal("5678", matches[1].Groups["id"].Value);
            Assert.Equal("Spyro the Dragon", matches[1].Groups["title"].Value.Trim());
        }

        [Fact]
        public void BrowseScraperRegex_ShouldMatchVimmBrowseAnchorTags()
        {
            // Arrange
            var html = @"
            <ul>
                <li><a href=""/vault/10"">Game A</a></li>
                <li><a href = ""/vault/20"">Game B</a></li>
            </ul>
            ";

            // Act
            var matches = Regex.Matches(
                html,
                @"<a href\s*=\s*""(?<url>/vault/(?<id>\d+))""[^>]*>(?<title>[^<]+)</a>",
                RegexOptions.IgnoreCase
            );

            // Assert
            Assert.Equal(2, matches.Count);

            Assert.Equal("/vault/10", matches[0].Groups["url"].Value);
            Assert.Equal("10", matches[0].Groups["id"].Value);
            Assert.Equal("Game A", matches[0].Groups["title"].Value.Trim());

            Assert.Equal("/vault/20", matches[1].Groups["url"].Value);
            Assert.Equal("20", matches[1].Groups["id"].Value);
            Assert.Equal("Game B", matches[1].Groups["title"].Value.Trim());
        }

        [Fact]
        public void ScraperProvider_Serialization_ShouldDeserializeCorrectly()
        {
            // Arrange
            var json = @"[
                {
                    ""name"": ""Test Provider"",
                    ""domain"": ""test.net"",
                    ""searchUrl"": ""https://test.net/search?q={query}"",
                    ""searchRegex"": ""pattern1"",
                    ""browseUrl"": ""https://test.net/browse?sys={system}"",
                    ""browseRegex"": ""pattern2"",
                    ""downloadActionRegex"": ""pattern3"",
                    ""downloadMediaIdRegexes"": [""m1"", ""m2""],
                    ""downloadMethod"": ""POST"",
                    ""downloadParamName"": ""mediaId"",
                    ""systemMap"": { ""NES"": ""Nintendo"" }
                }
            ]";

            // Act
            var providers = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<JellyEmu.Services.ScraperProvider>>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Assert
            Assert.NotNull(providers);
            Assert.Single(providers);
            var provider = providers[0];
            Assert.Equal("Test Provider", provider.Name);
            Assert.Equal("test.net", provider.Domain);
            Assert.Equal("https://test.net/search?q={query}", provider.SearchUrl);
            Assert.Equal("pattern1", provider.SearchRegex);
            Assert.Equal("https://test.net/browse?sys={system}", provider.BrowseUrl);
            Assert.Equal("pattern2", provider.BrowseRegex);
            Assert.Equal("pattern3", provider.DownloadActionRegex);
            Assert.Equal(2, provider.DownloadMediaIdRegexes.Count);
            Assert.Equal("m1", provider.DownloadMediaIdRegexes[0]);
            Assert.Equal("m2", provider.DownloadMediaIdRegexes[1]);
            Assert.Equal("POST", provider.DownloadMethod);
            Assert.Equal("mediaId", provider.DownloadParamName);
            Assert.True(provider.SystemMap.ContainsKey("NES"));
            Assert.Equal("Nintendo", provider.SystemMap["NES"]);
        }

        [Fact]
        public void SearchScraperRegex_ShouldMatchVimmSearchResultRowsWithDecoyLinks()
        {
            // Arrange
            var html = @"
            <tr><td style=""width:80px; text-align:center"">GBA</td><td style=""width:auto""><a href=""/vault/999999"" style=""display:  none"">9</a><a href= ""/vault/48075"">2 Games in 1: Dr. Mario + Puzzle League</a></td><td style=""width:65px; text-align:center""><div style=""display:flex; flex-wrap:wrap; justify-content:center; gap:3px""><img src=""/images/flags/europe.png"" class=""flag"" title=""Europe""></div></td><td style=""width:85px; text-align:center"">1.0</td><td style=""width:110px; text-align:center; font-size:10pt"" class=""responsive"">de en es fr it</td></tr>
            <tr><td style=""width:80px; text-align:center"">GBA</td><td style=""width:auto""><a href=""/vault/999999"" style=""display:  none"">9</a><a href= ""/vault/5267"" onmouseover=""buildTooltip(this, 5267, 240, 160)"">2 Games in One! Dr. Mario + Puzzle League</a></td><td style=""width:65px; text-align:center""><div style=""display:flex; flex-wrap:wrap; justify-content:center; gap:3px""><img src=""/images/flags/usa.png"" class=""flag"" title=""USA""></div></td><td style=""width:85px; text-align:center"">1.0</td><td style=""width:110px; text-align:center; font-size:10pt"" class=""responsive"">-</td></tr>
            ";

            var pattern = @"<td[^>]*>(?<system>[^<]+)</td>\s*<td[^>]*>(?:<a href=""[^""]*""[^>]*style=""display:\s*none""[^>]*>.*?</a>\s*|<a href\s*=\s*""[^""]*""></a>\s*)?<a href\s*=\s*""(?<url>/vault/(?<id>\d+))""[^>]*>(?<title>[^<]+)</a>(?<extra>.*?)</td>(?:\s*<td[^>]*>(?<regions>.*?)</td>\s*<td[^>]*>(?<version>[^<]*)</td>\s*<td[^>]*>(?<languages>[^<]*)</td>)?";

            // Act
            var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Assert
            Assert.Equal(2, matches.Count);
            
            Assert.Equal("GBA", matches[0].Groups["system"].Value.Trim());
            Assert.Equal("/vault/48075", matches[0].Groups["url"].Value);
            Assert.Equal("48075", matches[0].Groups["id"].Value);
            Assert.Equal("2 Games in 1: Dr. Mario + Puzzle League", matches[0].Groups["title"].Value.Trim());

            Assert.Equal("GBA", matches[1].Groups["system"].Value.Trim());
            Assert.Equal("/vault/5267", matches[1].Groups["url"].Value);
            Assert.Equal("5267", matches[1].Groups["id"].Value);
            Assert.Equal("2 Games in One! Dr. Mario + Puzzle League", matches[1].Groups["title"].Value.Trim());
        }

        [Fact]
        public void BrowseScraperRegex_ShouldMatchVimmBrowseAnchorTagsWithDecoyLinks()
        {
            // Arrange
            var html = @"
            <tr><td style=""width:auto""><a href=""/vault/999999"" style=""display:  none"">9</a><a href= ""/vault/583"" onmouseover=""buildTooltip(this, 583, 256, 224)"">Monster Party</a></td><td style=""width:65px; text-align:center""><div style=""display:flex; flex-wrap:wrap; justify-content:center; gap:3px""><img src=""/images/flags/usa.png"" class=""flag"" title=""USA""></div></td><td style=""width:85px; text-align:center"">1.0</td><td style=""width:110px; text-align:center; font-size:10pt"" class=""responsive"">-</td><td style=""width:50px; text-align:center"" class=""responsive""><a href=""/vault/?p=rating&amp;id=583"">7.5</a></td></tr>
            ";

            var pattern = @"<td[^>]*>(?:<a href=""[^""]*""[^>]*style=""display:\s*none""[^>]*>.*?</a>\s*|<a href\s*=\s*""[^""]*""></a>\s*)?<a href\s*=\s*""(?<url>/vault/(?<id>\d+))""[^>]*>(?<title>[^<]+)</a>(?<extra>.*?)</td>(?:\s*<td[^>]*>(?<regions>.*?)</td>\s*<td[^>]*>(?<version>[^<]*)</td>\s*<td[^>]*>(?<languages>[^<]*)</td>)?";

            // Act
            var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Assert
            Assert.Single(matches);
            Assert.Equal("/vault/583", matches[0].Groups["url"].Value);
            Assert.Equal("583", matches[0].Groups["id"].Value);
            Assert.Equal("Monster Party", matches[0].Groups["title"].Value.Trim());
        }

        [Fact]
        public void SearchScraperRegex_ShouldMatchCoolRomSearchResults()
        {
            // Sample snippet from https://coolrom.com.au/search?q=sonic
            var html = @"
            <div class=""main"">
                <a href=""/roms/genesis/"">Sega Genesis</a> &raquo; <a href=""/roms/genesis/1234/Sonic_The_Hedgehog.php"">Sonic The Hedgehog</a><br>
                <a href=""https://coolrom.com.au/roms/snes/"">SNES</a> » <a href=""https://coolrom.com.au/roms/snes/5678/Sonic_Blast_Man.php"">Sonic Blast Man</a><br>
            </div>";

            var pattern = @"<a[^>]+href=""(?:\/roms\/(?<systemSlug>[^\/]+)\/|https?:\/\/[^\/]+\/roms\/(?<systemSlug>[^\/]+)\/)""[^>]*>(?<system>[^<]+)<\/a>\s*(?:&raquo;|»|&raquo)\s*<a[^>]+href=""(?<url>(?:\/roms\/[^\/]+\/(?<id>\d+)\/[^""]+\.php|https?:\/\/[^\/]+\/roms\/[^\/]+\/(?<id>\d+)\/[^""]+\.php))""[^>]*>(?<title>[^<]+)<\/a>";

            var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            Assert.Equal(2, matches.Count);
            Assert.Equal("Sega Genesis", matches[0].Groups["system"].Value);
            Assert.Equal("genesis", matches[0].Groups["systemSlug"].Value);
            Assert.Equal("1234", matches[0].Groups["id"].Value);
            Assert.Equal("Sonic The Hedgehog", matches[0].Groups["title"].Value);

            Assert.Equal("SNES", matches[1].Groups["system"].Value);
            Assert.Equal("snes", matches[1].Groups["systemSlug"].Value);
            Assert.Equal("5678", matches[1].Groups["id"].Value);
            Assert.Equal("Sonic Blast Man", matches[1].Groups["title"].Value);
        }

        [Fact]
        public void BrowseScraperRegex_ShouldMatchCoolRomBrowseResults()
        {
            // Sample snippet from https://coolrom.com.au/roms/genesis/S/
            var html = @"
            <div class=""main"">
                <a href=""/roms/genesis/6013/Sagaia.php"">Sagaia</a><br>
                <a href=""/roms/genesis/1057/Sonic_and_Knuckles.php"">Sonic & Knuckles</a><br>
            </div>";

            var pattern = @"<a[^>]+href=""(?<url>(?:\/roms\/[^\/]+\/(?<id>\d+)\/[^""]+\.php|https?:\/\/[^\/]+\/roms\/[^\/]+\/(?<id>\d+)\/[^""]+\.php))""[^>]*>(?<title>[^<]+)<\/a>";

            var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            Assert.Equal(2, matches.Count);
            Assert.Equal("/roms/genesis/6013/Sagaia.php", matches[0].Groups["url"].Value);
            Assert.Equal("6013", matches[0].Groups["id"].Value);
            Assert.Equal("Sagaia", matches[0].Groups["title"].Value);

            Assert.Equal("/roms/genesis/1057/Sonic_and_Knuckles.php", matches[1].Groups["url"].Value);
            Assert.Equal("1057", matches[1].Groups["id"].Value);
            Assert.Equal("Sonic & Knuckles", matches[1].Groups["title"].Value);
        }

        [Fact]
        public void DownloaderRegex_ShouldMatchFormActionAndMediaId()
        {
            var html = @"<form action=""downloader.php"" method=""POST""><input type=""hidden"" name=""id"" value=""6013"" /></form>";
            var actionRegex = @"<form\s+([^>]*action=""[^""]*downloader\.php""[^>]*)>";
            var idRegex = @"<input\s+[^>]*name=""id""[^>]*value=""(?<id>\d+)""";

            var formMatch = Regex.Match(html, actionRegex, RegexOptions.IgnoreCase);
            var idMatch = Regex.Match(html, idRegex, RegexOptions.IgnoreCase);

            Assert.True(formMatch.Success);
            Assert.True(idMatch.Success);
            Assert.Equal("6013", idMatch.Groups["id"].Value);
        }

        [Theory]
        [InlineData("NES", "NES")]
        [InlineData("SNES", "SNES")]
        [InlineData("Sega Genesis", "Sega Genesis")]
        [InlineData("genesis", "Sega Genesis")]
        [InlineData("GBA", "Game Boy Advance")]
        [InlineData("PS1", "PlayStation")]
        [InlineData("N64", "N64")]
        [InlineData("Nintendo DS", "Nintendo DS")]
        [InlineData("Game Gear", "Game Gear")]
        [InlineData("Atari 2600", "Atari 2600")]
        public void GetSystemFolderName_ShouldReturnCanonicalFolderNames(string inputSystem, string expectedFolder)
        {
            var service = new JellyEmu.Services.MarketplaceService(null!, null!, null!);
            var folder = service.GetSystemFolderName(inputSystem);
            Assert.Equal(expectedFolder, folder);
        }
    }
}
