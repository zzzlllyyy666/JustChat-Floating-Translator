using System.Text.Json;
using JustChat.Core;
using Xunit;

namespace JustChat.Core.Tests;

public sealed class TranslationTargetLanguageTests
{
    [Fact]
    public void All_ContainsSixLanguagesInStableOrder()
    {
        Assert.Equal(6, TranslationTargetLanguage.All.Count);
        Assert.Equal(
            new[] { "en", "ja", "ko", "es", "fr", "de" },
            TranslationTargetLanguage.All.Select(language => language.Code));
    }

    [Fact]
    public void All_MatchesSharedLanguageContract()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "translation-target-languages.json");
        var expected = JsonSerializer.Deserialize<LanguageContract[]>(
            File.ReadAllText(fixturePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The language fixture must contain a JSON array.");

        var actual = TranslationTargetLanguage.All
            .Select(language => new LanguageContract(
                language.Code,
                language.ShortCode,
                language.ChineseName,
                language.NativeName,
                language.MenuLabel))
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private sealed record LanguageContract(
        string Code,
        string ShortCode,
        string ChineseName,
        string NativeName,
        string MenuLabel);
}
