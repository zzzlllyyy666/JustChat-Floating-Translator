import Foundation

struct TranslationBoundaryResult: Equatable {
    let lockedPrefix: String
    let translatableText: String
}

struct RepeatedDraft: Equatable {
    let unit: String
    let separator: String
    let count: Int
}

struct TranslationSegment: Decodable, Equatable {
    let id: Int
    let translation: String
}

struct SegmentedTranslationPlan {
    struct SourceSegment: Equatable {
        let id: Int
        let text: String
        let leadingWhitespace: String
        let trailingWhitespace: String
    }

    let lines: [String]
    let separators: [String]
    let segments: [SourceSegment]
}

extension TranslationTargetLanguage {
    var englishName: String {
        switch self {
        case .english: "English"
        case .japanese: "Japanese"
        case .korean: "Korean"
        case .spanish: "Spanish"
        case .french: "French"
        case .german: "German"
        }
    }

    fileprivate var usesJapaneseRules: Bool { self == .japanese }
}

enum TranslationBoundary {
    private static let chineseCharacterPattern = "[\\u3400-\\u9fff]"
    private static let chineseContentPattern = "[\\u3400-\\u9fff，。！？；：、…]"
    private static let punctuationMap: [String: String] = [
        "，": ",", "。": ".", "！": "!", "？": "?", "；": ";", "：": ":",
        "、": ",", "……": "...", "…": "..."
    ]

    static func resolveTargetLanguage(_ identifier: String) -> TranslationTargetLanguage? {
        TranslationTargetLanguage(rawValue: identifier)
    }

    static func containsTranslatableContent(_ text: String) -> Bool {
        matches(chineseContentPattern, in: text)
    }

    static func splitLockedPrefix(_ text: String) -> TranslationBoundaryResult {
        guard let match = firstMatch(chineseContentPattern, in: text), match.range.location > 0 else {
            return TranslationBoundaryResult(lockedPrefix: "", translatableText: text)
        }

        let source = text as NSString
        let firstTranslatableCharacter = source.substring(with: match.range)
        let prefix = source.substring(to: match.range.location)
        let startsWithChinesePunctuation = matches("[，。！？；：、…]", in: firstTranslatableCharacter)
        let hasExplicitEnglishBoundary = matches("[.,!?;:]\\s*$", in: prefix)

        if !startsWithChinesePunctuation && !hasExplicitEnglishBoundary {
            return TranslationBoundaryResult(lockedPrefix: "", translatableText: text)
        }
        return TranslationBoundaryResult(
            lockedPrefix: prefix,
            translatableText: source.substring(from: match.range.location)
        )
    }

    static func resolveTranslationBoundary(
        _ text: String,
        explicitLockedPrefix: String = "",
        targetLanguage: TranslationTargetLanguage = .english
    ) -> TranslationBoundaryResult {
        if !explicitLockedPrefix.isEmpty, text.hasPrefix(explicitLockedPrefix) {
            let remaining = String(text.dropFirst(explicitLockedPrefix.count))
            if (targetLanguage.usesJapaneseRules || !containsTranslatableContent(explicitLockedPrefix)),
               containsTranslatableContent(remaining) {
                return TranslationBoundaryResult(
                    lockedPrefix: explicitLockedPrefix,
                    translatableText: remaining
                )
            }
        }
        return splitLockedPrefix(text)
    }

    static func joinLockedPrefix(
        _ lockedPrefix: String,
        _ translatedText: String,
        targetLanguage: TranslationTargetLanguage = .english
    ) -> String {
        if lockedPrefix.isEmpty { return translatedText }
        if translatedText.isEmpty { return lockedPrefix }

        let punctuationOnlyPattern = targetLanguage.usesJapaneseRules
            ? "^[、。！？；：…]+$"
            : "^[.,!?;:]+$"
        if matches(punctuationOnlyPattern, in: translatedText) {
            let trailingWhitespace = firstMatch("\\s*$", in: lockedPrefix)
                .map { (lockedPrefix as NSString).substring(with: $0.range) } ?? ""
            let prefixBody = String(lockedPrefix.dropLast(trailingWhitespace.count))
            if matches("[.,!?;:、。！？；：…]+$", in: prefixBody) {
                return replacing("[.,!?;:、。！？；：…]+$", in: prefixBody, with: translatedText)
                    + trailingWhitespace
            }
            return prefixBody + translatedText + trailingWhitespace
        }

        let needsSpace = !targetLanguage.usesJapaneseRules
            && !matches("\\s$", in: lockedPrefix)
            && !matches("^[\\s.,!?;:)\\]}]", in: translatedText)
        return lockedPrefix + (needsSpace ? " " : "") + translatedText
    }

    static func translateChinesePunctuation(
        _ text: String,
        targetLanguage: TranslationTargetLanguage = .english
    ) -> String {
        if targetLanguage.usesJapaneseRules {
            return text.replacingOccurrences(of: "，", with: "、")
        }
        var result = text.replacingOccurrences(of: "……", with: "...")
        for (source, target) in punctuationMap where source != "……" && source != "…" {
            result = result.replacingOccurrences(of: source, with: target)
        }
        return result.replacingOccurrences(of: "…", with: "...")
    }

    static func hasChineseCharacters(_ text: String) -> Bool {
        matches(chineseCharacterPattern, in: text)
    }

    static func splitExactRepeatedDraft(_ text: String) -> RepeatedDraft? {
        let separators = ["\r\n\r\n", "\n\n", "\r\n", "\n", " ", ""]
        let source = text as NSString

        for separator in separators {
            let separatorLength = (separator as NSString).length
            let contentLength = source.length - separatorLength
            if contentLength < 16 || contentLength % 2 != 0 { continue }

            let unitLength = contentLength / 2
            let first = source.substring(with: NSRange(location: 0, length: unitLength))
            let actualSeparator = source.substring(with: NSRange(location: unitLength, length: separatorLength))
            let second = source.substring(from: unitLength + separatorLength)
            if actualSeparator == separator, first == second, hasChineseCharacters(first) {
                return RepeatedDraft(unit: first, separator: separator.isEmpty ? " " : separator, count: 2)
            }
        }
        return nil
    }

    static func isCompleteTargetTranslation(
        _ sourceText: String,
        _ translatedText: String,
        targetLanguage: TranslationTargetLanguage = .english
    ) -> Bool {
        if translatedText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return false }
        if !targetLanguage.usesJapaneseRules && containsTranslatableContent(translatedText) {
            return false
        }
        return lineBreakCount(in: sourceText) == lineBreakCount(in: translatedText)
    }

    static func isCompleteEnglishTranslation(_ sourceText: String, _ translatedText: String) -> Bool {
        isCompleteTargetTranslation(sourceText, translatedText, targetLanguage: .english)
    }

    static func makeSegmentedTranslationPlan(_ text: String) -> SegmentedTranslationPlan {
        let (lines, separators) = splitLines(in: text)
        let segments = lines.enumerated().compactMap { index, line -> SegmentedTranslationPlan.SourceSegment? in
            guard hasChineseCharacters(line) else { return nil }
            let leadingLength = line.prefix { $0 == " " || $0 == "\t" }.count
            let trailingLength = line.reversed().prefix { $0 == " " || $0 == "\t" }.count
            let leading = String(line.prefix(leadingLength))
            let trailing = String(line.suffix(trailingLength))
            let contentStart = line.index(line.startIndex, offsetBy: leadingLength)
            let contentEnd = line.index(line.endIndex, offsetBy: -trailingLength)
            return SegmentedTranslationPlan.SourceSegment(
                id: index,
                text: String(line[contentStart..<contentEnd]),
                leadingWhitespace: leading,
                trailingWhitespace: trailing
            )
        }
        return SegmentedTranslationPlan(lines: lines, separators: separators, segments: segments)
    }

    static func assembleSegmentedTranslation(
        _ plan: SegmentedTranslationPlan,
        _ translatedSegments: [TranslationSegment],
        lockedPrefix: String = "",
        targetLanguage: TranslationTargetLanguage = .english
    ) -> String? {
        guard translatedSegments.count == plan.segments.count else { return nil }

        var translationsByID: [Int: String] = [:]
        for item in translatedSegments {
            if item.translation.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
               true { return nil }
            if translationsByID[item.id] != nil { return nil }
            translationsByID[item.id] = item.translation
        }

        let segmentByID = Dictionary(uniqueKeysWithValues: plan.segments.map { ($0.id, $0) })
        var translatedSequence = 0
        var outputLines: [String] = []
        for (lineIndex, line) in plan.lines.enumerated() {
            guard let segment = segmentByID[lineIndex] else {
                outputLines.append(translateChinesePunctuation(line, targetLanguage: targetLanguage))
                continue
            }
            guard let rawTranslation = translationsByID[segment.id] else { return nil }
            let normalized = normalizeTargetTranslation(
                rawTranslation,
                sourceText: segment.text,
                lockedPrefix: translatedSequence == 0 ? lockedPrefix : "",
                targetLanguage: targetLanguage
            )
            translatedSequence += 1
            guard isCompleteTargetTranslation(segment.text, normalized, targetLanguage: targetLanguage) else {
                return nil
            }
            outputLines.append(segment.leadingWhitespace + normalized + segment.trailingWhitespace)
        }

        var result = outputLines.first ?? ""
        for (index, separator) in plan.separators.enumerated() where index + 1 < outputLines.count {
            result += separator + outputLines[index + 1]
        }
        return result
    }

    static func normalizeEnglishTranslation(
        _ translatedText: String,
        sourceText: String,
        lockedPrefix: String = ""
    ) -> String {
        var normalized = translateChinesePunctuation(translatedText)
            .trimmingCharacters(in: .whitespacesAndNewlines)
        if normalized.isEmpty { return "" }

        let prefix = lockedPrefix.trimmingCharacters(in: .whitespacesAndNewlines)
        let beginsNewSentence = prefix.isEmpty || matches("[.!?][\"'”’\\)\\]]*$", in: prefix)
        if beginsNewSentence {
            normalized = uppercasingSecondCapture(
                "^(\\s*[\"'“‘(\\[]*)([a-z])",
                in: normalized
            )
        }

        let closing = firstMatch("([\"'”’\\)\\]]*)$", in: normalized)
            .map { (normalized as NSString).substring(with: $0.range(at: 1)) } ?? ""
        var body = closing.isEmpty ? normalized : String(normalized.dropLast(closing.count))
        let source = sourceText.trimmingCharacters(in: .whitespacesAndNewlines)
        let sourceEnding = firstMatch("(……|[，。！？；：、…])$", in: source)
            .map { (source as NSString).substring(with: $0.range) }

        if let sourceEnding, let expectedEnding = punctuationMap[sourceEnding] {
            body = matches("[.,!?;:]+$", in: body)
                ? replacing("[.,!?;:]+$", in: body, with: expectedEnding)
                : body + expectedEnding
        } else if !matches("[.,!?;:]$", in: body) {
            body += "."
        }

        let sourceBeforeStructuralClosing = replacing("[）)\\]】}》>]+$", in: source, with: "")
        let sourceParentheticalHasOwnEnding = matches("(……|[。！？；：….!?;:])$", in: sourceBeforeStructuralClosing)
        let hasOnlyStructuralClosings = matches("^[)\\]}]+$", in: closing)
        if hasOnlyStructuralClosings && !sourceParentheticalHasOwnEnding,
           let misplaced = firstMatch("(\\.{1,3}|[!?;:])$", in: body) {
            let ending = (body as NSString).substring(with: misplaced.range)
            body = (body as NSString).replacingCharacters(in: misplaced.range, with: "")
            return body + closing + ending
        }
        return body + closing
    }

    static func normalizeTargetTranslation(
        _ translatedText: String,
        sourceText: String,
        lockedPrefix: String = "",
        targetLanguage: TranslationTargetLanguage = .english
    ) -> String {
        if targetLanguage == .english {
            return normalizeEnglishTranslation(
                translatedText,
                sourceText: sourceText,
                lockedPrefix: lockedPrefix
            )
        }

        var normalized = translateChinesePunctuation(translatedText, targetLanguage: targetLanguage)
            .trimmingCharacters(in: .whitespacesAndNewlines)
        if normalized.isEmpty { return "" }
        if !targetLanguage.usesJapaneseRules {
            let prefix = lockedPrefix.trimmingCharacters(in: .whitespacesAndNewlines)
            let beginsNewSentence = prefix.isEmpty || matches("[.!?][\"'”’\\)\\]]*$", in: prefix)
            if beginsNewSentence {
                normalized = uppercasingSecondCapture(
                    "^(\\s*[\"'“‘(\\[]*)([a-záéíóúüñàâæçéèêëîïôœùûüÿäöß])",
                    in: normalized
                )
            }
        }
        return normalized
    }

    private static func splitLines(in text: String) -> ([String], [String]) {
        let source = text as NSString
        let regex = try! NSRegularExpression(pattern: "\\r\\n|\\n")
        let matches = regex.matches(in: text, range: NSRange(location: 0, length: source.length))
        var lines: [String] = []
        var separators: [String] = []
        var currentLocation = 0
        for match in matches {
            lines.append(source.substring(with: NSRange(
                location: currentLocation,
                length: match.range.location - currentLocation
            )))
            separators.append(source.substring(with: match.range))
            currentLocation = match.range.location + match.range.length
        }
        lines.append(source.substring(from: currentLocation))
        return (lines, separators)
    }

    private static func lineBreakCount(in text: String) -> Int {
        text.reduce(into: 0) { count, character in
            if character == "\n" { count += 1 }
        }
    }

    private static func firstMatch(_ pattern: String, in text: String) -> NSTextCheckingResult? {
        let regex = try! NSRegularExpression(pattern: pattern)
        return regex.firstMatch(in: text, range: NSRange(location: 0, length: (text as NSString).length))
    }

    private static func matches(_ pattern: String, in text: String) -> Bool {
        firstMatch(pattern, in: text) != nil
    }

    private static func replacing(_ pattern: String, in text: String, with template: String) -> String {
        let regex = try! NSRegularExpression(pattern: pattern)
        return regex.stringByReplacingMatches(
            in: text,
            range: NSRange(location: 0, length: (text as NSString).length),
            withTemplate: template
        )
    }

    private static func uppercasingSecondCapture(_ pattern: String, in text: String) -> String {
        guard let match = firstMatch(pattern, in: text), match.numberOfRanges >= 3 else { return text }
        let source = text as NSString
        let replacement = source.substring(with: match.range(at: 2)).uppercased()
        return source.replacingCharacters(in: match.range(at: 2), with: replacement)
    }
}
