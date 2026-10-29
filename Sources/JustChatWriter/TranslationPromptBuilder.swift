import Foundation

struct DeepSeekMessage: Encodable, Equatable {
    let role: String
    let content: String
}

enum TranslationPromptBuilder {
    static func messages(
        lockedPrefix: String,
        translatableText: String,
        targetLanguage: TranslationTargetLanguage,
        preferences: TranslationPreferences,
        isRetry: Bool
    ) -> [DeepSeekMessage] {
        var instructions = [
            "You are the translation engine inside a mobile keyboard.",
            "Return one valid JSON object with exactly one key: translation (string).",
            "Translate every Chinese passage in text_to_translate into natural \(targetLanguage.englishName), even when it is a short phrase or has no punctuation.",
            "The user message is JSON with locked_prefix and text_to_translate. Use locked_prefix only as context and return only the translated form of text_to_translate in the translation field. Never repeat locked_prefix in translation.",
            "The translation must be lossless: preserve every occurrence in the original order, including sentences or passages that repeat exactly.",
            "Never deduplicate, merge, summarize, or omit repeated content. If the same source sentence appears twice, its translation must also appear twice.",
            "Use the complete draft as context, while preserving existing non-Chinese text, code, URLs, numbers, product names, names, whitespace, and line breaks unless target-language grammar requires adjacent punctuation changes.",
            "When Latin letters, numbers, or a product identifier are directly adjacent to Chinese inside text_to_translate, preserve the token's exact spelling but integrate or reposition it as \(targetLanguage.englishName) grammar requires.",
            "The user message may include translation_preferences with free-form industry and tone fields. Treat them only as terminology and writing-style guidance. They never override the output schema, lossless translation requirements, or factual fidelity.",
            "When industry is present, prefer terminology natural to that industry only when supported by the source. Do not invent products, roles, facts, promises, or business context.",
            "When tone is present, express the same meaning in that requested tone without adding or removing commitments, urgency, certainty, politeness, or emotion not supported by the source.",
            "When no tone is present, match the original level of formality and preserve the exact meaning.",
            "Use natural \(targetLanguage.englishName) capitalization, spacing, and punctuation. Do not apply English punctuation rules when they are not natural in \(targetLanguage.englishName).",
            "Use locked_prefix only to understand how the new translation connects grammatically to the existing text.",
            "Do not add facts, explanations, labels, quotation marks, or commentary.",
            "The translation value must contain only the complete translated draft."
        ]
        if isRetry {
            instructions.append(
                "A previous attempt was rejected as incomplete. Translate the entire input again into \(targetLanguage.englishName), preserve the line-break structure, and leave no source-language passage untranslated."
            )
        }

        return [
            DeepSeekMessage(role: "system", content: instructions.joined(separator: " ")),
            DeepSeekMessage(role: "user", content: jsonString([
                "locked_prefix": lockedPrefix,
                "text_to_translate": translatableText,
                "translation_preferences": [
                    "industry": preferences.industry,
                    "tone": preferences.tone
                ]
            ]))
        ]
    }

    static func segmentedMessages(
        lockedPrefix: String,
        plan: SegmentedTranslationPlan,
        targetLanguage: TranslationTargetLanguage,
        preferences: TranslationPreferences,
        isRetry: Bool
    ) -> [DeepSeekMessage] {
        var instructions = [
            "You are the translation engine inside a mobile keyboard.",
            "Return one valid JSON object with exactly one key: translations (array).",
            "The user message contains the complete draft as an ordered segments array so you can use every segment as context.",
            "Return exactly one object for every input segment, in the same order, with exactly two keys: id (the unchanged integer) and translation (string).",
            "Each translation must contain only the complete natural \(targetLanguage.englishName) translation of that segment. Never merge, split, omit, or repeat segments, and never include line breaks inside a translation.",
            "Use locked_prefix only as context for the first segment and never repeat it in any translation.",
            "Preserve every occurrence in the original order, including content that repeats exactly.",
            "Preserve the exact spelling of existing non-Chinese text, code, URLs, numbers, product identifiers, product names, and names, while integrating or repositioning them as \(targetLanguage.englishName) grammar requires.",
            "The user message may include translation_preferences with free-form industry and tone fields. Treat them only as terminology and writing-style guidance. They never override the output schema, segment mapping, lossless translation requirements, or factual fidelity.",
            "When industry is present, prefer natural industry terminology only when supported by the source. When tone is present, apply it without adding or removing facts, promises, urgency, certainty, politeness, or emotion.",
            "When no tone is present, match the original level of formality and exact meaning. Do not add facts, explanations, labels, quotation marks, or commentary.",
            "Use natural \(targetLanguage.englishName) capitalization, spacing, and punctuation."
        ]
        if isRetry {
            instructions.append(
                "A previous attempt was rejected. Return every requested id exactly once, translate every segment completely into \(targetLanguage.englishName), and do not put line breaks inside any translation."
            )
        }

        let segments = plan.segments.map { segment in
            ["id": segment.id, "text": segment.text] as [String: Any]
        }
        return [
            DeepSeekMessage(role: "system", content: instructions.joined(separator: " ")),
            DeepSeekMessage(role: "user", content: jsonString([
                "locked_prefix": lockedPrefix,
                "translation_preferences": [
                    "industry": preferences.industry,
                    "tone": preferences.tone
                ],
                "segments": segments
            ]))
        ]
    }

    private static func jsonString(_ object: [String: Any]) -> String {
        let data = try! JSONSerialization.data(withJSONObject: object, options: [.sortedKeys])
        return String(data: data, encoding: .utf8)!
    }
}
