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
        public void SearchScraperRegex_ShouldMatchVimmSearchResultRowsWithMetadata()
        {
            // Arrange
            var html = @"
            <tr><td style=""width:80px; text-align:center"">GBC</td><td style=""width:auto""><a href=""""></a><a href= ""/vault/4232"" onmouseover=""buildTooltip(this, 4232, 320, 288)"">Pokemon: Silver Version</a>&nbsp; <b class=""redBorder"" style=""cursor:default"" title=""Demo"">D</b>&nbsp; <a href=""/manual/3145""><img src=""/images/manual_1.gif"" style=""vertical-align:bottom"" width=""17"" height=""16"" title=""Read the manual"" alt=""Read the manual""></a></td><td style=""width:65px; text-align:center""><div style=""display:flex; flex-wrap:wrap; justify-content:center; gap:3px""><img src=""/images/flags/usa.png"" class=""flag"" title=""USA""><img src=""/images/flags/europe.png"" class=""flag"" title=""Europe""></div></td><td style=""width:85px; text-align:center"">1.0</td><td style=""width:110px; text-align:center; font-size:10pt"" class=""responsive"">-</td></tr>
            ";

            var pattern = @"<td[^>]*>(?<system>[^<]+)</td>\s*<td[^>]*>(?:<a href\s*=\s*""[^""]*""></a>\s*)?<a href\s*=\s*""(?<url>/vault/(?<id>\d+))""[^>]*>(?<title>[^<]+)</a>(?<extra>.*?)</td>(?:\s*<td[^>]*>(?<regions>.*?)</td>\s*<td[^>]*>(?<version>[^<]*)</td>\s*<td[^>]*>(?<languages>[^<]*)</td>)?";

            // Act
            var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Assert
            Assert.Single(matches);
            Assert.Equal("GBC", matches[0].Groups["system"].Value.Trim());
            Assert.Equal("/vault/4232", matches[0].Groups["url"].Value);
            Assert.Equal("4232", matches[0].Groups["id"].Value);
            Assert.Equal("Pokemon: Silver Version", matches[0].Groups["title"].Value.Trim());
            Assert.Contains("Demo", matches[0].Groups["extra"].Value);
            Assert.Contains("manual", matches[0].Groups["extra"].Value);
            Assert.Contains("USA", matches[0].Groups["regions"].Value);
            Assert.Contains("Europe", matches[0].Groups["regions"].Value);
            Assert.Equal("1.0", matches[0].Groups["version"].Value.Trim());
            Assert.Equal("-", matches[0].Groups["languages"].Value.Trim());
        }

        [Fact]
        public void BrowseScraperRegex_ShouldMatchVimmBrowseAnchorTagsWithMetadata()
        {
            // Arrange
            var html = @"
            <tr><td style=""width:auto""><a href=""""></a><a href= ""/vault/10"" onmouseover=""buildTooltip(this, 10, 256, 224)"">Abadox: The Deadly Inner War</a>&nbsp; <b class=""redBorder"" style=""cursor:default"" title=""Prototype"">P</b></td><td style=""width:65px; text-align:center""><div style=""display:flex; flex-wrap:wrap; justify-content:center; gap:3px""><img src=""/images/flags/usa.png"" class=""flag"" title=""USA""></div></td><td style=""width:85px; text-align:center"">1.0</td><td style=""width:110px; text-align:center; font-size:10pt"" class=""responsive"">-</td><td style=""width:50px; text-align:center"" class=""responsive""><a href=""/vault/?p=rating&amp;id=10"">6.8</a></td></tr>
            ";

            var pattern = @"<a href\s*=\s*""(?<url>/vault/(?<id>\d+))""[^>]*>(?<title>[^<]+)</a>(?<extra>.*?)</td>(?:\s*<td[^>]*>(?<regions>.*?)</td>\s*<td[^>]*>(?<version>[^<]*)</td>\s*<td[^>]*>(?<languages>[^<]*)</td>)?";

            // Act
            var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // Assert
            Assert.Single(matches);
            Assert.Equal("/vault/10", matches[0].Groups["url"].Value);
            Assert.Equal("10", matches[0].Groups["id"].Value);
            Assert.Equal("Abadox: The Deadly Inner War", matches[0].Groups["title"].Value.Trim());
            Assert.Contains("Prototype", matches[0].Groups["extra"].Value);
            Assert.Contains("USA", matches[0].Groups["regions"].Value);
            Assert.Equal("1.0", matches[0].Groups["version"].Value.Trim());
            Assert.Equal("-", matches[0].Groups["languages"].Value.Trim());
        }
    }
}
