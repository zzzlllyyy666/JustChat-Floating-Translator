using System.IO;
using JustChat.Core;
using Xunit;

namespace JustChat.Core.Tests;

public sealed class TranslationPreferencesJsonTests
{
    [Fact]
    public void RoundTrip_PreservesPreferences()
    {
        var original = new TranslationPreferences("跨境电商", "自然、专业，不要太客套");

        var restored = TranslationPreferencesJson.Deserialize(
            TranslationPreferencesJson.Serialize(original));

        Assert.Equal(original.Industry, restored.Industry);
        Assert.Equal(original.Tone, restored.Tone);
    }

    [Fact]
    public void Deserialize_NormalizesStoredValues()
    {
        const string json = """
            {
              "industry": "  跨境 \n 电商  ",
              "tone": " 自然、专业，\t不要太客套 "
            }
            """;

        var preferences = TranslationPreferencesJson.Deserialize(json);

        Assert.Equal("跨境 电商", preferences.Industry);
        Assert.Equal("自然、专业， 不要太客套", preferences.Tone);
    }

    [Fact]
    public void Deserialize_MissingFieldsReturnsEmptyPreferences()
    {
        var preferences = TranslationPreferencesJson.Deserialize("{}");

        Assert.True(preferences.IsEmpty);
    }

    [Fact]
    public void Deserialize_InvalidJsonThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() =>
            TranslationPreferencesJson.Deserialize("{not-json}"));
    }

    [Fact]
    public void Summary_DescribesEmptyAndConfiguredPreferences()
    {
        Assert.Equal(
            "设置行业与语气 · 翻译会更像你",
            TranslationPreferences.Empty.Summary);
        Assert.Equal(
            "跨境电商 · 未设置语气",
            new TranslationPreferences("跨境电商", null).Summary);
    }
}
