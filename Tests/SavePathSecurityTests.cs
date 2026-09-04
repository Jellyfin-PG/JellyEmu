using System;
using Xunit;
using JellyEmu.Controllers;

namespace JellyEmu.Tests
{
    public class SavePathSecurityTests
    {
        [Theory]
        [InlineData("2999f82e6359450a8885d75fadc93aaa", true)]
        [InlineData("550e8400-e29b-41d4-a716-446655440000", true)]
        [InlineData("game_123", true)]
        [InlineData("save-slot_1", true)]
        [InlineData("abcABC123", true)]
        [InlineData("../etc/passwd", false)]
        [InlineData("..\\windows\\system32", false)]
        [InlineData("foo/bar", false)]
        [InlineData("foo\\bar", false)]
        [InlineData("foo..bar", false)]
        [InlineData("foo bar", false)]
        [InlineData("", false)]
        [InlineData(" ", false)]
        [InlineData(null, false)]
        [InlineData("itemId;DROP TABLE;", false)]
        [InlineData("foo\r\nbar", false)]
        public void IsValidId_ValidatesCorrectly(string? id, bool expected)
        {
            var result = JellyEmuBaseController.IsValidId(id);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("test\r\nvalue", "testvalue")]
        [InlineData("safe-log-entry", "safe-log-entry")]
        [InlineData("line1\nline2\rline3", "line1line2line3")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void SanitizeForLog_StripsNewlines(string? input, string expected)
        {
            var result = JellyEmuBaseController.SanitizeForLog(input);
            Assert.Equal(expected, result);
        }
    }
}
