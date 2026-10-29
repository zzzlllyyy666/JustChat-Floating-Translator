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

    private static string Normalize(string? value)
    {
        var compacted = string.Join(
            ' ',
            (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (StringInfo.LengthInTextElements(compacted) <= MaximumFieldLength)
        {
            return compacted;
        }

        return StringInfo.SubstringByTextElements(compacted, 0, MaximumFieldLength);
    }
}
