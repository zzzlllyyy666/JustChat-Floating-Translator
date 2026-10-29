import Foundation

struct TranslationPreferences: Codable, Equatable {
    static let maximumFieldLength = 160
    static let empty = TranslationPreferences(industry: "", tone: "")

    let industry: String
    let tone: String

    init(industry: String, tone: String) {
        self.industry = Self.normalize(industry)
        self.tone = Self.normalize(tone)
    }

    var isEmpty: Bool {
        industry.isEmpty && tone.isEmpty
    }

    var summary: String {
        if isEmpty {
            return "设置行业与语气 · 翻译会更像你"
        }
        return [
            industry.isEmpty ? "未设置行业" : industry,
            tone.isEmpty ? "未设置语气" : tone
        ].joined(separator: " · ")
    }

    private static func normalize(_ value: String) -> String {
        let compacted = value
            .components(separatedBy: .whitespacesAndNewlines)
            .filter { !$0.isEmpty }
            .joined(separator: " ")
        return String(compacted.prefix(maximumFieldLength))
    }
}
