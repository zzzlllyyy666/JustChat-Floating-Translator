import Foundation

enum TranslationTargetLanguage: String, CaseIterable, Identifiable {
    case english = "en"
    case japanese = "ja"
    case korean = "ko"
    case spanish = "es"
    case french = "fr"
    case german = "de"

    var id: String { rawValue }

    var shortCode: String {
        rawValue.uppercased()
    }

    var chineseName: String {
        switch self {
        case .english: "英语"
        case .japanese: "日语"
        case .korean: "韩语"
        case .spanish: "西班牙语"
        case .french: "法语"
        case .german: "德语"
        }
    }

    var nativeName: String {
        switch self {
        case .english: "English"
        case .japanese: "日本語"
        case .korean: "한국어"
        case .spanish: "Español"
        case .french: "Français"
        case .german: "Deutsch"
        }
    }

    var previewTitle: String {
        "\(chineseName)预览"
    }

    var previewActionTitle: String {
        "预览\(chineseName)"
    }
}
