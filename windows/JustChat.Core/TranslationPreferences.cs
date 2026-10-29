using System.Globalization;

namespace JustChat.Core;

public sealed class TranslationPreferences
{
    public const int MaximumFieldLength = 160;

    public TranslationPreferences(string? industry, string? tone)
    {
        Industry = Normalize(industry);
        Tone = Normalize(tone);
    }

    public static TranslationPreferences Empty { get; } = new(string.Empty, string.Empty);

    public string Industry { get; }
    public string Tone { get; }
    public bool IsEmpty => Industry.Length == 0 && Tone.Length == 0;

    public string Summary
    {
        get
        {
            if (IsEmpty)
            {
                return "设置行业与语气 · 翻译会更像你";
            }

            var industry = Industry.Length == 0 ? "未设置行业" : Industry;
            var tone = Tone.Length == 0 ? "未设置语气" : Tone;
            return $"{industry} · {tone}";
        }
    }

    private static string Normalize(string? value)
    {
        var compacted = string.Join(
            ' ',
            (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var text = new StringInfo(compacted);
        if (text.LengthInTextElements <= MaximumFieldLength)
        {
            return compacted;
        }

        return text.SubstringByTextElements(0, MaximumFieldLength);
    }
}
