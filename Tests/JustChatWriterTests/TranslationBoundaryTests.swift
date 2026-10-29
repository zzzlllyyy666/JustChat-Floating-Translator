import XCTest
@testable import JustChatWriter

final class TranslationBoundaryTests: XCTestCase {
    func testEnglishPrefixIsSeparatedFromNewChinese() {
        XCTAssertEqual(
            TranslationBoundary.splitLockedPrefix("Let me think about it.让我想一想"),
            TranslationBoundaryResult(lockedPrefix: "Let me think about it.", translatableText: "让我想一想")
        )
    }

    func testProductIdentifiersAttachedToChineseStayInTranslatableText() {
        for text in ["A1光纤的价格是每公里多少钱", "SKU-42价格是多少"] {
            XCTAssertEqual(
                TranslationBoundary.splitLockedPrefix(text),
                TranslationBoundaryResult(lockedPrefix: "", translatableText: text)
            )
        }
    }

    func testExplicitEnglishPrefixIsPreserved() {
        XCTAssertEqual(
            TranslationBoundary.resolveTranslationBoundary(
                "This plan is practical.但是成本比较高。",
                explicitLockedPrefix: "This plan is practical."
            ),
            TranslationBoundaryResult(
                lockedPrefix: "This plan is practical.",
                translatableText: "但是成本比较高。"
            )
        )
    }

    func testInvalidPrefixFallsBackAndValidProtocolPrefixIsAccepted() {
        XCTAssertEqual(
            TranslationBoundary.resolveTranslationBoundary("A1光纤的价格是多少", explicitLockedPrefix: "Old draft."),
            TranslationBoundaryResult(lockedPrefix: "", translatableText: "A1光纤的价格是多少")
        )
        XCTAssertEqual(
            TranslationBoundary.resolveTranslationBoundary("A1光纤的价格是多少", explicitLockedPrefix: "A1"),
            TranslationBoundaryResult(lockedPrefix: "A1", translatableText: "光纤的价格是多少")
        )
    }

    func testRepeatedTranslationDoesNotOverwriteLockedPrefix() {
        XCTAssertEqual(
            TranslationBoundary.joinLockedPrefix("Let me think about it.", "Let me think about it."),
            "Let me think about it. Let me think about it."
        )
    }

    func testExistingWhitespaceIsNotDuplicated() {
        XCTAssertEqual(
            TranslationBoundary.joinLockedPrefix("Hi John, ", "I can start tomorrow."),
            "Hi John, I can start tomorrow."
        )
    }

    func testChinesePunctuationTriggersTranslationBoundary() {
        XCTAssertTrue(TranslationBoundary.containsTranslatableContent("。"))
        XCTAssertEqual(
            TranslationBoundary.splitLockedPrefix("I understand。"),
            TranslationBoundaryResult(lockedPrefix: "I understand", translatableText: "。")
        )
    }

    func testEnglishNormalizationAddsCapitalAndFinalPunctuation() {
        XCTAssertEqual(
            TranslationBoundary.normalizeEnglishTranslation("let me think", sourceText: "让我想一想"),
            "Let me think."
        )
        XCTAssertEqual(
            TranslationBoundary.normalizeEnglishTranslation("are you free", sourceText: "你有空吗？"),
            "Are you free?"
        )
        XCTAssertEqual(
            TranslationBoundary.normalizeEnglishTranslation("are you free.", sourceText: "你有空吗？"),
            "Are you free?"
        )
    }

    func testExistingEnglishPunctuationIsNotDuplicated() {
        XCTAssertEqual(
            TranslationBoundary.normalizeEnglishTranslation("sounds good!", sourceText: "听起来不错"),
            "Sounds good!"
        )
    }

    func testParentheticalSentencePunctuationIsNormalized() {
        XCTAssertEqual(
            TranslationBoundary.normalizeEnglishTranslation("hello (test.)", sourceText: "你好（测试）"),
            "Hello (test)."
        )
        XCTAssertEqual(
            TranslationBoundary.normalizeEnglishTranslation("hello (test)", sourceText: "你好（测试）"),
            "Hello (test)."
        )
    }

    func testParentheticalOwnPunctuationStaysInside() {
        XCTAssertEqual(
            TranslationBoundary.normalizeEnglishTranslation("hello (test.)", sourceText: "你好（测试。）"),
            "Hello (test.)"
        )
    }

    func testCommaPrefixKeepsLowercaseContinuation() {
        XCTAssertEqual(
            TranslationBoundary.normalizeEnglishTranslation(
                "what do you think?",
                sourceText: "你怎么看？",
                lockedPrefix: "Let me think about it,"
            ),
            "what do you think?"
        )
    }

    func testSentencePrefixCapitalizesContinuation() {
        XCTAssertEqual(
            TranslationBoundary.normalizeEnglishTranslation(
                "what do you think?",
                sourceText: "你怎么看？",
                lockedPrefix: "Let me think about it."
            ),
            "What do you think?"
        )
    }

    func testPunctuationOnlyTranslationReplacesExistingEnding() {
        XCTAssertEqual(TranslationBoundary.joinLockedPrefix("I understand", "."), "I understand.")
        XCTAssertEqual(TranslationBoundary.joinLockedPrefix("I understand.", "?"), "I understand?")
    }

    func testDetectsExactlyRepeatedDraft() {
        let paragraph = "当前限制是基础词库较小而且候选词仍需继续优化"
        XCTAssertEqual(
            TranslationBoundary.splitExactRepeatedDraft(paragraph + paragraph),
            RepeatedDraft(unit: paragraph, separator: " ", count: 2)
        )
        XCTAssertEqual(
            TranslationBoundary.splitExactRepeatedDraft(paragraph + "\n" + paragraph),
            RepeatedDraft(unit: paragraph, separator: "\n", count: 2)
        )
    }

    func testOrdinarySentenceIsNotMisclassifiedAsRepeated() {
        XCTAssertNil(TranslationBoundary.splitExactRepeatedDraft("我们今天测试重复内容但这不是两份相同段落"))
    }

    func testRejectsResidualChineseAndChangedLineBreakCount() {
        XCTAssertFalse(TranslationBoundary.isCompleteEnglishTranslation("第一段\n第二段", "第一段\nSecond paragraph."))
        XCTAssertFalse(TranslationBoundary.isCompleteEnglishTranslation("第一段\n第二段", "First paragraph. Second paragraph."))
        XCTAssertTrue(TranslationBoundary.isCompleteEnglishTranslation("第一段\n第二段", "First paragraph.\nSecond paragraph."))
    }

    func testSegmentedTranslationPreservesWhitespaceAndLineEndings() {
        let plan = TranslationBoundary.makeSegmentedTranslationPlan("  第一段  \n第二段\r\nEnglish only")
        XCTAssertEqual(plan.segments.map(\.id), [0, 1])
        XCTAssertEqual(plan.segments.map(\.text), ["第一段", "第二段"])
        XCTAssertEqual(
            TranslationBoundary.assembleSegmentedTranslation(plan, [
                TranslationSegment(id: 0, translation: "first paragraph"),
                TranslationSegment(id: 1, translation: "second paragraph")
            ]),
            "  First paragraph.  \nSecond paragraph.\r\nEnglish only"
        )
    }

    func testSegmentedTranslationRejectsMissingDuplicateAndResidualChinese() {
        let plan = TranslationBoundary.makeSegmentedTranslationPlan("第一段\n第二段")
        XCTAssertNil(TranslationBoundary.assembleSegmentedTranslation(plan, [
            TranslationSegment(id: 0, translation: "First paragraph.")
        ]))
        XCTAssertNil(TranslationBoundary.assembleSegmentedTranslation(plan, [
            TranslationSegment(id: 0, translation: "First paragraph."),
            TranslationSegment(id: 0, translation: "Second paragraph.")
        ]))
        XCTAssertNil(TranslationBoundary.assembleSegmentedTranslation(plan, [
            TranslationSegment(id: 0, translation: "First paragraph."),
            TranslationSegment(id: 1, translation: "第二段")
        ]))
    }

    func testSupportsExactlySixTargetLanguages() {
        let expected = [
            "en": "English", "ja": "Japanese", "ko": "Korean",
            "es": "Spanish", "fr": "French", "de": "German"
        ]
        for (identifier, name) in expected {
            XCTAssertEqual(TranslationBoundary.resolveTargetLanguage(identifier)?.englishName, name)
        }
        XCTAssertNil(TranslationBoundary.resolveTargetLanguage("zh"))
    }

    func testJapaneseAllowsHanCharactersAndJapanesePunctuation() {
        XCTAssertTrue(TranslationBoundary.isCompleteTargetTranslation(
            "请确认价格。",
            "価格をご確認ください。",
            targetLanguage: .japanese
        ))
        XCTAssertEqual(
            TranslationBoundary.normalizeTargetTranslation(
                "価格をご確認ください。",
                sourceText: "请确认价格。",
                targetLanguage: .japanese
            ),
            "価格をご確認ください。"
        )
    }

    func testLatinLanguagesAndKoreanRejectResidualChinese() {
        for language in [TranslationTargetLanguage.english, .korean, .spanish, .french, .german] {
            XCTAssertFalse(TranslationBoundary.isCompleteTargetTranslation(
                "请确认价格。",
                "请确认 price.",
                targetLanguage: language
            ))
        }
    }

    func testJapaneseSegmentedTranslationPreservesWhitespace() {
        let plan = TranslationBoundary.makeSegmentedTranslationPlan("  第一段  \n第二段")
        XCTAssertEqual(
            TranslationBoundary.assembleSegmentedTranslation(
                plan,
                [
                    TranslationSegment(id: 0, translation: "第一段です。"),
                    TranslationSegment(id: 1, translation: "第二段です。")
                ],
                targetLanguage: .japanese
            ),
            "  第一段です。  \n第二段です。"
        )
    }

    func testJapanesePrefixDoesNotInsertSpace() {
        XCTAssertEqual(
            TranslationBoundary.joinLockedPrefix(
                "承知しました。",
                "明日確認します。",
                targetLanguage: .japanese
            ),
            "承知しました。明日確認します。"
        )
    }

    func testJapaneseHistoryCanBeExplicitLockedPrefix() {
        XCTAssertEqual(
            TranslationBoundary.resolveTranslationBoundary(
                "価格を確認しました。请明天发货。",
                explicitLockedPrefix: "価格を確認しました。",
                targetLanguage: .japanese
            ),
            TranslationBoundaryResult(
                lockedPrefix: "価格を確認しました。",
                translatableText: "请明天发货。"
            )
        )
    }
}
