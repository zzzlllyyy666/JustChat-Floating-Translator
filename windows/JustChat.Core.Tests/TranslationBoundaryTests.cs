using JustChat.Core;
using Xunit;

namespace JustChat.Core.Tests;

public sealed class TranslationBoundaryTests
{
    [Fact]
    public void EnglishPrefix_IsSeparatedFromNewChinese()
    {
        Assert.Equal(
            new TranslationBoundaryResult("Let me think about it.", "让我想一想"),
            TranslationBoundary.SplitLockedPrefix("Let me think about it.让我想一想"));
    }

    [Theory]
    [InlineData("A1光纤的价格是每公里多少钱")]
    [InlineData("SKU-42价格是多少")]
    public void ProductIdentifiersAttachedToChinese_StayInTranslatableText(string text)
    {
        Assert.Equal(
            new TranslationBoundaryResult(string.Empty, text),
            TranslationBoundary.SplitLockedPrefix(text));
    }

    [Fact]
    public void ExplicitPrefix_IsValidatedBeforeLocking()
    {
        Assert.Equal(
            new TranslationBoundaryResult("This plan is practical.", "但是成本比较高。"),
            TranslationBoundary.ResolveTranslationBoundary(
                "This plan is practical.但是成本比较高。",
                "This plan is practical."));
        Assert.Equal(
            new TranslationBoundaryResult(string.Empty, "A1光纤的价格是多少"),
            TranslationBoundary.ResolveTranslationBoundary(
                "A1光纤的价格是多少",
                "Old draft."));
        Assert.Equal(
            new TranslationBoundaryResult("A1", "光纤的价格是多少"),
            TranslationBoundary.ResolveTranslationBoundary(
                "A1光纤的价格是多少",
                "A1"));
    }

    [Fact]
    public void JoinLockedPrefix_PreservesSpacingAndReplacesPunctuation()
    {
        Assert.Equal(
            "Hi John, I can start tomorrow.",
            TranslationBoundary.JoinLockedPrefix("Hi John, ", "I can start tomorrow."));
        Assert.Equal("I understand.", TranslationBoundary.JoinLockedPrefix("I understand", "."));
        Assert.Equal("I understand?", TranslationBoundary.JoinLockedPrefix("I understand.", "?"));
    }

    [Fact]
    public void ChinesePunctuation_TriggersTranslationBoundary()
    {
        Assert.True(TranslationBoundary.ContainsTranslatableContent("。"));
        Assert.Equal(
            new TranslationBoundaryResult("I understand", "。"),
            TranslationBoundary.SplitLockedPrefix("I understand。"));
    }

    [Fact]
    public void EnglishNormalization_AddsCapitalAndSourceEnding()
    {
        Assert.Equal(
            "Let me think.",
            TranslationBoundary.NormalizeEnglishTranslation("let me think", "让我想一想"));
        Assert.Equal(
            "Are you free?",
            TranslationBoundary.NormalizeEnglishTranslation("are you free", "你有空吗？"));
        Assert.Equal(
            "Are you free?",
            TranslationBoundary.NormalizeEnglishTranslation("are you free.", "你有空吗？"));
        Assert.Equal(
            "Sounds good!",
            TranslationBoundary.NormalizeEnglishTranslation("sounds good!", "听起来不错"));
    }

    [Fact]
    public void EnglishNormalization_MovesOnlyStructuralParentheticalPunctuation()
    {
        Assert.Equal(
            "Hello (test).",
            TranslationBoundary.NormalizeEnglishTranslation("hello (test.)", "你好（测试）"));
        Assert.Equal(
            "Hello (test).",
            TranslationBoundary.NormalizeEnglishTranslation("hello (test)", "你好（测试）"));
        Assert.Equal(
            "Hello (test.)",
            TranslationBoundary.NormalizeEnglishTranslation("hello (test.)", "你好（测试。）"));
    }

    [Fact]
    public void EnglishNormalization_UsesLockedPrefixSentenceBoundary()
    {
        Assert.Equal(
            "what do you think?",
            TranslationBoundary.NormalizeEnglishTranslation(
                "what do you think?",
                "你怎么看？",
                "Let me think about it,"));
        Assert.Equal(
            "What do you think?",
            TranslationBoundary.NormalizeEnglishTranslation(
                "what do you think?",
                "你怎么看？",
                "Let me think about it."));
    }

    [Fact]
    public void RepeatedDraft_OnlyMatchesTwoExactChineseUnits()
    {
        const string paragraph = "当前限制是基础词库较小而且候选词仍需继续优化";
        Assert.Equal(
            new RepeatedDraft(paragraph, " ", 2),
            TranslationBoundary.SplitExactRepeatedDraft(paragraph + paragraph));
        Assert.Equal(
            new RepeatedDraft(paragraph, "\n", 2),
            TranslationBoundary.SplitExactRepeatedDraft(paragraph + "\n" + paragraph));
        Assert.Null(TranslationBoundary.SplitExactRepeatedDraft(
            "我们今天测试重复内容但这不是两份相同段落"));
    }

    [Fact]
    public void Completeness_RejectsResidualChineseAndChangedLineBreaks()
    {
        Assert.False(TranslationBoundary.IsCompleteEnglishTranslation(
            "第一段\n第二段",
            "第一段\nSecond paragraph."));
        Assert.False(TranslationBoundary.IsCompleteEnglishTranslation(
            "第一段\n第二段",
            "First paragraph. Second paragraph."));
        Assert.True(TranslationBoundary.IsCompleteEnglishTranslation(
            "第一段\n第二段",
            "First paragraph.\nSecond paragraph."));
    }

    [Fact]
    public void SegmentedTranslation_PreservesWhitespaceAndLineEndings()
    {
        var plan = TranslationBoundary.MakeSegmentedTranslationPlan(
            "  第一段  \n第二段\r\nEnglish only");
        Assert.Equal([0, 1], plan.Segments.Select(segment => segment.Id).ToArray());
        Assert.Equal(
            ["第一段", "第二段"],
            plan.Segments.Select(segment => segment.Text).ToArray());

        var result = TranslationBoundary.AssembleSegmentedTranslation(plan,
        [
            new TranslationSegment(0, "first paragraph"),
            new TranslationSegment(1, "second paragraph")
        ]);

        Assert.Equal(
            "  First paragraph.  \nSecond paragraph.\r\nEnglish only",
            result);
    }

    [Fact]
    public void SegmentedTranslation_RejectsMissingDuplicateAndResidualChinese()
    {
        var plan = TranslationBoundary.MakeSegmentedTranslationPlan("第一段\n第二段");
        Assert.Null(TranslationBoundary.AssembleSegmentedTranslation(plan,
        [
            new TranslationSegment(0, "First paragraph.")
        ]));
        Assert.Null(TranslationBoundary.AssembleSegmentedTranslation(plan,
        [
            new TranslationSegment(0, "First paragraph."),
            new TranslationSegment(0, "Second paragraph.")
        ]));
        Assert.Null(TranslationBoundary.AssembleSegmentedTranslation(plan,
        [
            new TranslationSegment(0, "First paragraph."),
            new TranslationSegment(1, "第二段")
        ]));
    }

    [Fact]
    public void Japanese_AllowsHanCharactersAndJapanesePunctuation()
    {
        Assert.True(TranslationBoundary.IsCompleteTargetTranslation(
            "请确认价格。",
            "価格をご確認ください。",
            TranslationTargetLanguage.Japanese));
        Assert.Equal(
            "価格をご確認ください。",
            TranslationBoundary.NormalizeTargetTranslation(
                "価格をご確認ください。",
                "请确认价格。",
                targetLanguage: TranslationTargetLanguage.Japanese));
    }

    [Fact]
    public void LatinLanguagesAndKorean_RejectResidualChinese()
    {
        var languages = new[]
        {
            TranslationTargetLanguage.English,
            TranslationTargetLanguage.Korean,
            TranslationTargetLanguage.Spanish,
            TranslationTargetLanguage.French,
            TranslationTargetLanguage.German
        };
        foreach (var language in languages)
        {
            Assert.False(TranslationBoundary.IsCompleteTargetTranslation(
                "请确认价格。",
                "请确认 price.",
                language));
        }
    }

    [Fact]
    public void JapaneseSegmentationAndPrefix_PreserveNativeFormatting()
    {
        var plan = TranslationBoundary.MakeSegmentedTranslationPlan("  第一段  \n第二段");
        Assert.Equal(
            "  第一段です。  \n第二段です。",
            TranslationBoundary.AssembleSegmentedTranslation(plan,
            [
                new TranslationSegment(0, "第一段です。"),
                new TranslationSegment(1, "第二段です。")
            ],
            targetLanguage: TranslationTargetLanguage.Japanese));
        Assert.Equal(
            "承知しました。明日確認します。",
            TranslationBoundary.JoinLockedPrefix(
                "承知しました。",
                "明日確認します。",
                TranslationTargetLanguage.Japanese));
        Assert.Equal(
            new TranslationBoundaryResult("価格を確認しました。", "请明天发货。"),
            TranslationBoundary.ResolveTranslationBoundary(
                "価格を確認しました。请明天发货。",
                "価格を確認しました。",
                TranslationTargetLanguage.Japanese));
    }
}
