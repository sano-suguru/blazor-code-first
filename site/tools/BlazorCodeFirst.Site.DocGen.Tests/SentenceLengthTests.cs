using System.Globalization;
using BlazorCodeFirst.Site.DocGen;
using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class SentenceLengthTests
{
    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));

    private static void Check(string body) =>
        SentenceLength.EnsureNoOverlongSentence(MarkdownConverter.ParseBody(body, "a.md"), "a.md");

    [Fact]
    public void ASentenceAtTheLimit_IsAccepted() => Check(Words(SentenceLength.Limit) + ".\n");

    [Fact]
    public void ASentenceOverTheLimit_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Check(Words(SentenceLength.Limit + 1) + ".\n"));

        Assert.Contains(
            (SentenceLength.Limit + 1).ToString(CultureInfo.InvariantCulture),
            ex.Message,
            StringComparison.Ordinal);
        Assert.Contains("a.md", ex.Message, StringComparison.Ordinal);
        // The opening is quoted so the author can find the sentence without counting words.
        Assert.Contains("word word", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoShortSentencesInOneParagraph_AreMeasuredApart() =>
        Check(Words(SentenceLength.Limit) + ". " + Words(SentenceLength.Limit) + ".\n");

    [Theory]
    [InlineData('.')]
    [InlineData(':')]
    [InlineData('?')]
    [InlineData('!')]
    public void EverySentenceEnderSplits(char ender) =>
        Check(Words(SentenceLength.Limit) + ender + " " + Words(SentenceLength.Limit) + ".\n");

    [Fact]
    public void ACodeSpanCountsAsOneWordHoweverLongItIs() =>
        // Otherwise a sentence quoting one long call would be rejected for the call's spaces rather
        // than for its own length.
        Check(Words(SentenceLength.Limit - 1) + " `Div.Class(\"a\", \"b\", \"c\", \"d\", \"e\")`.\n");

    [Fact]
    public void AVocabularyListIsNotASentence() =>
        // element-reference.md lists 100 element helpers as comma-separated runs of code spans. No
        // split improves one, and nothing in it is prose.
        Check(string.Join(", ", Enumerable.Repeat("`Div`", 60)) + "\n");

    [Fact]
    public void AProseParagraphQuotingManyNamesIsStillMeasured()
    {
        // The other side of that boundary: prose words outnumber the code spans, so it is a
        // sentence, and a long one is caught even though it names types.
        string sentence = Words(SentenceLength.Limit) + " `Div` and `Span` and `P`.\n";
        Assert.Throws<InvalidOperationException>(() => Check(sentence));
    }

    [Fact]
    public void AListItemIsNotMeasured() =>
        // A run of items reads as a list. The one in components-and-reuse.md carries a 40-word item
        // that is four bullets a reader takes one at a time.
        Check("- " + Words(SentenceLength.Limit + 20) + "\n");

    [Fact]
    public void ATableRowIsNotMeasured() =>
        Check("| a | b |\n| --- | --- |\n| " + Words(SentenceLength.Limit + 20) + " | x |\n");

    [Fact]
    public void AFencedCodeBlockIsNotMeasured() =>
        Check("```csharp\n" + Words(SentenceLength.Limit + 40) + "\n```\n");
}
