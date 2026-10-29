using System.Text.RegularExpressions;

namespace JustChat.Core;

public sealed record TranslationBoundaryResult(string LockedPrefix, string TranslatableText);

public sealed record RepeatedDraft(string Unit, string Separator, int Count);

public sealed record TranslationSegment(int Id, string Translation);

public sealed record SegmentedTranslationPlan(
    string[] Lines,
    string[] Separators,
    SegmentedTranslationPlan.SourceSegment[] Segments)
{
    public sealed record SourceSegment(
        int Id,
        string Text,
        string LeadingWhitespace,
        string TrailingWhitespace);
}

public static class TranslationBoundary
{
    private const string ChineseCharacterPattern = "[\\u3400-\\u9fff]";
    private const string ChineseContentPattern = "[\\u3400-\\u9fff，。！？；：、…]";

    private static readonly IReadOnlyDictionary<string, string> PunctuationMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["，"] = ",",
            ["\u3002"] = ".",
            ["！"] = "!",
            ["？"] = "?",
            ["；"] = ";",
            ["："] = ":",
            ["、"] = ",",
            ["……"] = "...",
            ["…"] = "..."
        };

    public static bool ContainsTranslatableContent(string text) =>
        IsMatch(ChineseContentPattern, text);

    public static TranslationBoundaryResult SplitLockedPrefix(string text)
    {
        var match = FirstMatch(ChineseContentPattern, text);
        if (match is null || match.Index == 0)
        {
            return new TranslationBoundaryResult(string.Empty, text);
        }

        var firstTranslatableCharacter = match.Value;
        var prefix = text[..match.Index];
        var startsWithChinesePunctuation = IsMatch("[，。！？；：、…]", firstTranslatableCharacter);
        var hasExplicitEnglishBoundary = IsMatch("[.,!?;:]\\s*$", prefix);
        if (!startsWithChinesePunctuation && !hasExplicitEnglishBoundary)
        {
            return new TranslationBoundaryResult(string.Empty, text);
        }

        return new TranslationBoundaryResult(prefix, text[match.Index..]);
    }

    public static TranslationBoundaryResult ResolveTranslationBoundary(
        string text,
        string explicitLockedPrefix = "",
        TranslationTargetLanguage? targetLanguage = null)
    {
        targetLanguage ??= TranslationTargetLanguage.English;
        if (explicitLockedPrefix.Length > 0 && text.StartsWith(explicitLockedPrefix, StringComparison.Ordinal))
        {
            var remaining = text[explicitLockedPrefix.Length..];
            if ((UsesJapaneseRules(targetLanguage) || !ContainsTranslatableContent(explicitLockedPrefix)) &&
                ContainsTranslatableContent(remaining))
            {
                return new TranslationBoundaryResult(explicitLockedPrefix, remaining);
            }
        }

        return SplitLockedPrefix(text);
    }

    public static string JoinLockedPrefix(
        string lockedPrefix,
        string translatedText,
        TranslationTargetLanguage? targetLanguage = null)
    {
        targetLanguage ??= TranslationTargetLanguage.English;
        if (lockedPrefix.Length == 0)
        {
            return translatedText;
        }

        if (translatedText.Length == 0)
        {
            return lockedPrefix;
        }

        var punctuationOnlyPattern = UsesJapaneseRules(targetLanguage)
            ? "^[、。！？；：…]+$"
            : "^[.,!?;:]+$";
        if (IsMatch(punctuationOnlyPattern, translatedText))
        {
            var trailingWhitespace = FirstMatch("\\s*$", lockedPrefix)?.Value ?? string.Empty;
            var prefixBody = lockedPrefix[..(lockedPrefix.Length - trailingWhitespace.Length)];
            if (IsMatch("[.,!?;:、。！？；：…]+$", prefixBody))
            {
                return Replacing(
                    "[.,!?;:、。！？；：…]+$",
                    prefixBody,
                    translatedText) + trailingWhitespace;
            }

            return prefixBody + translatedText + trailingWhitespace;
        }

        var needsSpace = !UsesJapaneseRules(targetLanguage) &&
            !IsMatch("\\s$", lockedPrefix) &&
            !IsMatch("^[\\s.,!?;:)\\]}]", translatedText);
        return lockedPrefix + (needsSpace ? " " : string.Empty) + translatedText;
    }

    public static string TranslateChinesePunctuation(
        string text,
        TranslationTargetLanguage? targetLanguage = null)
    {
        targetLanguage ??= TranslationTargetLanguage.English;
        if (UsesJapaneseRules(targetLanguage))
        {
            return text.Replace("，", "、", StringComparison.Ordinal);
        }

        var result = text.Replace("……", "...", StringComparison.Ordinal);
        foreach (var (source, target) in PunctuationMap)
        {
            if (source is "……" or "…")
            {
                continue;
            }

            result = result.Replace(source, target, StringComparison.Ordinal);
        }

        return result.Replace("…", "...", StringComparison.Ordinal);
    }

    public static bool HasChineseCharacters(string text) =>
        IsMatch(ChineseCharacterPattern, text);

    public static RepeatedDraft? SplitExactRepeatedDraft(string text)
    {
        string[] separators = ["\r\n\r\n", "\n\n", "\r\n", "\n", " ", ""];
        foreach (var separator in separators)
        {
            var contentLength = text.Length - separator.Length;
            if (contentLength < 16 || contentLength % 2 != 0)
            {
                continue;
            }

            var unitLength = contentLength / 2;
            var first = text[..unitLength];
            var actualSeparator = text.Substring(unitLength, separator.Length);
            var second = text[(unitLength + separator.Length)..];
            if (actualSeparator == separator &&
                first == second &&
                HasChineseCharacters(first))
            {
                return new RepeatedDraft(
                    first,
                    separator.Length == 0 ? " " : separator,
                    2);
            }
        }

        return null;
    }

    public static bool IsCompleteTargetTranslation(
        string sourceText,
        string translatedText,
        TranslationTargetLanguage? targetLanguage = null)
    {
        targetLanguage ??= TranslationTargetLanguage.English;
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return false;
        }

        if (!UsesJapaneseRules(targetLanguage) && ContainsTranslatableContent(translatedText))
        {
            return false;
        }

        return LineBreakCount(sourceText) == LineBreakCount(translatedText);
    }

    public static bool IsCompleteEnglishTranslation(string sourceText, string translatedText) =>
        IsCompleteTargetTranslation(sourceText, translatedText, TranslationTargetLanguage.English);

    public static SegmentedTranslationPlan MakeSegmentedTranslationPlan(string text)
    {
        var (lines, separators) = SplitLines(text);
        var segments = new List<SegmentedTranslationPlan.SourceSegment>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!HasChineseCharacters(line))
            {
                continue;
            }

            var leadingLength = line.TakeWhile(character => character is ' ' or '\t').Count();
            var trailingLength = line.Reverse().TakeWhile(character => character is ' ' or '\t').Count();
            segments.Add(new SegmentedTranslationPlan.SourceSegment(
                index,
                line.Substring(leadingLength, line.Length - leadingLength - trailingLength),
                line[..leadingLength],
                line[(line.Length - trailingLength)..]));
        }

        return new SegmentedTranslationPlan(lines, separators, segments.ToArray());
    }

    public static string? AssembleSegmentedTranslation(
        SegmentedTranslationPlan plan,
        IReadOnlyList<TranslationSegment> translatedSegments,
        string lockedPrefix = "",
        TranslationTargetLanguage? targetLanguage = null)
    {
        targetLanguage ??= TranslationTargetLanguage.English;
        if (translatedSegments.Count != plan.Segments.Length)
        {
            return null;
        }

        var translationsById = new Dictionary<int, string>();
        foreach (var item in translatedSegments)
        {
            if (string.IsNullOrWhiteSpace(item.Translation) ||
                !translationsById.TryAdd(item.Id, item.Translation))
            {
                return null;
            }
        }

        var segmentsById = plan.Segments.ToDictionary(segment => segment.Id);
        var translatedSequence = 0;
        var outputLines = new List<string>();
        for (var lineIndex = 0; lineIndex < plan.Lines.Length; lineIndex++)
        {
            var line = plan.Lines[lineIndex];
            if (!segmentsById.TryGetValue(lineIndex, out var segment))
            {
                outputLines.Add(TranslateChinesePunctuation(line, targetLanguage));
                continue;
            }

            if (!translationsById.TryGetValue(segment.Id, out var rawTranslation))
            {
                return null;
            }

            var normalized = NormalizeTargetTranslation(
                rawTranslation,
                segment.Text,
                translatedSequence == 0 ? lockedPrefix : string.Empty,
                targetLanguage);
            translatedSequence++;
            if (!IsCompleteTargetTranslation(segment.Text, normalized, targetLanguage))
            {
                return null;
            }

            outputLines.Add(segment.LeadingWhitespace + normalized + segment.TrailingWhitespace);
        }

        var result = outputLines.FirstOrDefault() ?? string.Empty;
        for (var index = 0;
             index < plan.Separators.Length && index + 1 < outputLines.Count;
             index++)
        {
            result += plan.Separators[index] + outputLines[index + 1];
        }

        return result;
    }

    public static string NormalizeEnglishTranslation(
        string translatedText,
        string sourceText,
        string lockedPrefix = "")
    {
        var normalized = TranslateChinesePunctuation(translatedText).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var prefix = lockedPrefix.Trim();
        var beginsNewSentence = prefix.Length == 0 ||
            IsMatch("[.!?][\"'”’\\)\\]]*$", prefix);
        if (beginsNewSentence)
        {
            normalized = UppercasingSecondCapture(
                "^(\\s*[\"'“‘(\\[]*)([a-z])",
                normalized);
        }

        var closing = FirstMatch("([\"'”’\\)\\]]*)$", normalized)?.Groups[1].Value ?? string.Empty;
        var body = closing.Length == 0 ? normalized : normalized[..^closing.Length];
        var source = sourceText.Trim();
        var sourceEnding = FirstMatch("(……|[，。！？；：、…])$", source)?.Value;
        if (sourceEnding is not null && PunctuationMap.TryGetValue(sourceEnding, out var expectedEnding))
        {
            body = IsMatch("[.,!?;:]+$", body)
                ? Replacing("[.,!?;:]+$", body, expectedEnding)
                : body + expectedEnding;
        }
        else if (!IsMatch("[.,!?;:]$", body))
        {
            body += ".";
        }

        var sourceBeforeStructuralClosing = Replacing("[）)\\]】}》>]+$", source, string.Empty);
        var sourceParentheticalHasOwnEnding = IsMatch(
            "(……|[。！？；：….!?;:])$",
            sourceBeforeStructuralClosing);
        var hasOnlyStructuralClosings = IsMatch("^[)\\]}]+$", closing);
        var misplaced = FirstMatch("(\\.{1,3}|[!?;:])$", body);
        if (hasOnlyStructuralClosings && !sourceParentheticalHasOwnEnding && misplaced is not null)
        {
            var ending = misplaced.Value;
            body = body.Remove(misplaced.Index, misplaced.Length);
            return body + closing + ending;
        }

        return body + closing;
    }

    public static string NormalizeTargetTranslation(
        string translatedText,
        string sourceText,
        string lockedPrefix = "",
        TranslationTargetLanguage? targetLanguage = null)
    {
        targetLanguage ??= TranslationTargetLanguage.English;
        if (targetLanguage == TranslationTargetLanguage.English)
        {
            return NormalizeEnglishTranslation(translatedText, sourceText, lockedPrefix);
        }

        var normalized = TranslateChinesePunctuation(translatedText, targetLanguage).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (!UsesJapaneseRules(targetLanguage))
        {
            var prefix = lockedPrefix.Trim();
            var beginsNewSentence = prefix.Length == 0 ||
                IsMatch("[.!?][\"'”’\\)\\]]*$", prefix);
            if (beginsNewSentence)
            {
                normalized = UppercasingSecondCapture(
                    "^(\\s*[\"'“‘(\\[]*)([a-záéíóúüñàâæçéèêëîïôœùûüÿäöß])",
                    normalized);
            }
        }

        return normalized;
    }

    private static bool UsesJapaneseRules(TranslationTargetLanguage targetLanguage) =>
        targetLanguage == TranslationTargetLanguage.Japanese;

    private static (string[] Lines, string[] Separators) SplitLines(string text)
    {
        var matches = Regex.Matches(text, "\\r\\n|\\n", RegexOptions.CultureInvariant);
        var lines = new List<string>();
        var separators = new List<string>();
        var currentLocation = 0;
        foreach (Match match in matches)
        {
            lines.Add(text.Substring(currentLocation, match.Index - currentLocation));
            separators.Add(match.Value);
            currentLocation = match.Index + match.Length;
        }

        lines.Add(text[currentLocation..]);
        return (lines.ToArray(), separators.ToArray());
    }

    private static int LineBreakCount(string text) => text.Count(character => character == '\n');

    private static Match? FirstMatch(string pattern, string text)
    {
        var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
        return match.Success ? match : null;
    }

    private static bool IsMatch(string pattern, string text) =>
        Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant);

    private static string Replacing(string pattern, string text, string replacement) =>
        Regex.Replace(text, pattern, replacement, RegexOptions.CultureInvariant);

    private static string UppercasingSecondCapture(string pattern, string text)
    {
        var match = FirstMatch(pattern, text);
        if (match is null || match.Groups.Count < 3)
        {
            return text;
        }

        var group = match.Groups[2];
        return text.Remove(group.Index, group.Length)
            .Insert(group.Index, group.Value.ToUpperInvariant());
    }
}
