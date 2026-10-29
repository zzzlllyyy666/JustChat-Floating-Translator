namespace JustChat.Core;

public sealed class TranslationTargetLanguage
{
    private TranslationTargetLanguage(string code, string chineseName, string nativeName)
    {
        Code = code;
        ChineseName = chineseName;
        NativeName = nativeName;
    }

    public static TranslationTargetLanguage English { get; } = new("en", "英语", "English");
    public static TranslationTargetLanguage Japanese { get; } = new("ja", "日语", "日本語");
    public static TranslationTargetLanguage Korean { get; } = new("ko", "韩语", "한국어");
    public static TranslationTargetLanguage Spanish { get; } = new("es", "西班牙语", "Español");
    public static TranslationTargetLanguage French { get; } = new("fr", "法语", "Français");
    public static TranslationTargetLanguage German { get; } = new("de", "德语", "Deutsch");

    public static IReadOnlyList<TranslationTargetLanguage> All { get; } =
        Array.AsReadOnly(new[] { English, Japanese, Korean, Spanish, French, German });

    public string Code { get; }
    public string ChineseName { get; }
    public string NativeName { get; }
    public string EnglishName => Code switch
    {
        "en" => "English",
        "ja" => "Japanese",
        "ko" => "Korean",
        "es" => "Spanish",
        "fr" => "French",
        "de" => "German",
        _ => throw new InvalidOperationException($"Unsupported language code: {Code}")
    };
    public string ShortCode => Code.ToUpperInvariant();
    public string MenuLabel => $"{ChineseName} {NativeName}";
}
