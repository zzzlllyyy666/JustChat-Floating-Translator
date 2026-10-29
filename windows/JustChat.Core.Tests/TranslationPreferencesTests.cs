using JustChat.Core;
using Xunit;

namespace JustChat.Core.Tests;

public sealed class TranslationPreferencesTests
{
    [Fact]
    public void Constructor_NormalizesIndustryAndToneWhitespace()
    {
        var preferences = new TranslationPreferences(
            "  跨境 \t  电商\r\n",
            " 自然、专业，\n不要太客套  ");

        Assert.Equal("跨境 电商", preferences.Industry);
        Assert.Equal("自然、专业， 不要太客套", preferences.Tone);
    }

    [Fact]
    public void Constructor_TreatsNullAsEmpty()
    {
        var preferences = new TranslationPreferences(null, null);

        Assert.Equal(string.Empty, preferences.Industry);
        Assert.Equal(string.Empty, preferences.Tone);
        Assert.True(preferences.IsEmpty);
    }

    [Fact]
    public void Constructor_LimitsEachFieldToOneHundredSixtyCharacters()
    {
        var preferences = new TranslationPreferences(
            new string('行', 200),
            new string('。', 200));

        Assert.Equal(TranslationPreferences.MaximumFieldLength, preferences.Industry.Length);
        Assert.Equal(TranslationPreferences.MaximumFieldLength, preferences.Tone.Length);
        Assert.Equal(new string('行', 160), preferences.Industry);
        Assert.Equal(new string('。', 160), preferences.Tone);
    }
}
