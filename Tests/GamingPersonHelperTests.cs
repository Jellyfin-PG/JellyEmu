using System;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Data.Enums;
using JellyEmu.Utilities;
using Xunit;

namespace JellyEmu.Tests
{
    public class GamingPersonHelperTests
    {
        [Theory]
        [InlineData("Shigeru Miyamoto", "Shigeru Miyamoto (Gaming)")]
        [InlineData("Shigeru Miyamoto (RAWG)", "Shigeru Miyamoto (Gaming)")]
        [InlineData("Shigeru Miyamoto (rawg)", "Shigeru Miyamoto (Gaming)")]
        [InlineData("Shigeru Miyamoto (Gaming)", "Shigeru Miyamoto (Gaming)")]
        [InlineData("Shigeru Miyamoto (gaming)", "Shigeru Miyamoto (Gaming)")]
        [InlineData("  Hideo Kojima (RAWG)  ", "Hideo Kojima (Gaming)")]
        public void ToGamingPersonName_ProducesCorrectDisambiguatedName(string input, string expected)
        {
            var result = GamingPersonHelper.ToGamingPersonName(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Shigeru Miyamoto (Gaming)", "Shigeru Miyamoto")]
        [InlineData("Shigeru Miyamoto (gaming)", "Shigeru Miyamoto")]
        [InlineData("Shigeru Miyamoto (RAWG)", "Shigeru Miyamoto")]
        [InlineData("Shigeru Miyamoto (rawg)", "Shigeru Miyamoto")]
        [InlineData("Connie Booth (Gaming)", "Connie Booth")]
        [InlineData("David Jaffe", "David Jaffe")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void CleanPersonName_StripsTagsAndDisambiguationMarkers(string? input, string expected)
        {
            var result = GamingPersonHelper.CleanPersonName(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Connie Booth (Gaming)", true)]
        [InlineData("Connie Booth (gaming)", true)]
        [InlineData("Shigeru Miyamoto (Gaming)  ", true)]
        [InlineData("Connie Booth", false)]
        [InlineData("John Cleese", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsGamingPerson_CorrectlyIdentifiesGamingCreators(string? input, bool expected)
        {
            var result = GamingPersonHelper.IsGamingPerson(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("designer", "Designer")]
        [InlineData("lead programmer", "Lead Programmer")]
        [InlineData("sound designer", "Sound Designer")]
        [InlineData("lead character artist", "Lead Character Artist")]
        [InlineData("", "Developer")]
        [InlineData(null, "Developer")]
        public void FormatRole_CapitalizesWords(string? input, string expected)
        {
            var result = GamingPersonHelper.FormatRole(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Game Designer", PersonKind.Creator)]
        [InlineData("Lead Programmer", PersonKind.Creator)]
        [InlineData("Director", PersonKind.Director)]
        [InlineData("Producer", PersonKind.Producer)]
        [InlineData("Sound Designer", PersonKind.Engineer)]
        [InlineData("Music Composer", PersonKind.Composer)]
        [InlineData("Character Artist", PersonKind.Artist)]
        [InlineData("Scenario Writer", PersonKind.Writer)]
        [InlineData("Voice Actor", PersonKind.Actor)]
        [InlineData("Editor", PersonKind.Editor)]
        [InlineData("Illustrator", PersonKind.Illustrator)]
        public void MapPersonKind_ReturnsValidJellyfinPersonKind(string role, PersonKind expected)
        {
            var result = GamingPersonHelper.MapPersonKind(role);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GamingName_ProducesDifferentHashThanStandardName()
        {
            var plainName = "David Jaffe";
            var gamingName = GamingPersonHelper.ToGamingPersonName(plainName);

            Assert.NotEqual(plainName, gamingName);

            using var md5 = MD5.Create();
            var plainHash = new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(plainName + "MediaBrowser.Controller.Entities.Person")));
            var gamingHash = new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(gamingName + "MediaBrowser.Controller.Entities.Person")));

            Assert.NotEqual(plainHash, gamingHash);
        }
    }
}
