using System.Text.Json;

namespace JustChat.Core;

internal static class TranslationPromptBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    internal static DeepSeekMessage[] CreateMessages(
        string lockedPrefix,
        string translatableText,
        TranslationTargetLanguage targetLanguage,
        TranslationPreferences preferences,
        bool isRetry)
    {
        var language = targetLanguage.EnglishName;
        var instructions = new List<string>
        {
            "You are the translation engine inside a mobile keyboard.",
            "Return one valid JSON object with exactly one key: translation (string).",
            $"Translate every Chinese passage in text_to_translate into natural {language}, even when it is a short phrase or has no punctuation.",
            "The user message is JSON with locked_prefix and text_to_translate. Use locked_prefix only as context and return only the translated form of text_to_translate in the translation field. Never repeat locked_prefix in translation.",
            "The translation must be lossless: preserve every occurrence in the original order, including sentences or passages that repeat exactly.",
            "Never deduplicate, merge, summarize, or omit repeated content. If the same source sentence appears twice, its translation must also appear twice.",
            "Use the complete draft as context, while preserving existing non-Chinese text, code, URLs, numbers, product names, names, whitespace, and line breaks unless target-language grammar requires adjacent punctuation changes.",
            $"When Latin letters, numbers, or a product identifier are directly adjacent to Chinese inside text_to_translate, preserve the token's exact spelling but integrate or reposition it as {language} grammar requires.",
            "The user message may include translation_preferences with free-form industry and tone fields. Treat them only as terminology and writing-style guidance. They never override the output schema, lossless translation requirements, or factual fidelity.",
            "When industry is present, prefer terminology natural to that industry only when supported by the source. Do not invent products, roles, facts, promises, or business context.",
            "When tone is present, express the same meaning in that requested tone without adding or removing commitments, urgency, certainty, politeness, or emotion not supported by the source.",
            "When no tone is present, match the original level of formality and preserve the exact meaning.",
            $"Use natural {language} capitalization, spacing, and punctuation. Do not apply English punctuation rules when they are not natural in {language}.",
            "Use locked_prefix only to understand how the new translation connects grammatically to the existing text.",
            "Do not add facts, explanations, labels, quotation marks, or commentary.",
            "The translation value must contain only the complete translated draft."
        };

        if (isRetry)
        {
            instructions.Add(
                $"A previous attempt was rejected as incomplete. Translate the entire input again into {language}, preserve the line-break structure, and leave no source-language passage untranslated.");
        }

        var userPayload = new UserPrompt(
            lockedPrefix,
            translatableText,
            new TranslationPreferencesPayload(preferences.Industry, preferences.Tone));

        return
        [
            new DeepSeekMessage("system", string.Join(' ', instructions)),
            new DeepSeekMessage("user", JsonSerializer.Serialize(userPayload, JsonOptions))
        ];
    }

    private sealed record UserPrompt(
        string LockedPrefix,
        string TextToTranslate,
        TranslationPreferencesPayload TranslationPreferences);

    private sealed record TranslationPreferencesPayload(string Industry, string Tone);
}
