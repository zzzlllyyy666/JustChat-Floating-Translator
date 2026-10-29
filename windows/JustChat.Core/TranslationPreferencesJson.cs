using System.IO;
using System.Text.Json;

namespace JustChat.Core;

public static class TranslationPreferencesJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string Serialize(TranslationPreferences preferences) =>
        JsonSerializer.Serialize(
            new Document(preferences.Industry, preferences.Tone),
            JsonOptions);

    public static TranslationPreferences Deserialize(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<Document>(json, JsonOptions);
            return document is null
                ? TranslationPreferences.Empty
                : new TranslationPreferences(document.Industry, document.Tone);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("翻译偏好文件格式无效，请重新保存", exception);
        }
    }

    private sealed record Document(string? Industry, string? Tone);
}
