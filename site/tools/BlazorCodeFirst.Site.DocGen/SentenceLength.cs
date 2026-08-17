using System.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>
/// Rejects a paragraph sentence longer than <see cref="Limit"/> words in the canonical edition.
/// </summary>
/// <remarks>
/// This is a check rather than a written style rule, because a style rule about prose is the least
/// enforceable kind there is. What it holds is narrow on purpose: not that the writing is clear, only
/// that no sentence has grown past the length where a reader has to go back and start it again.
///
/// It reads paragraphs off the parsed document rather than scanning the text. A scan would have to
/// re-derive fence tracking, table rows, list items and front matter to know what is prose, and would
/// count a `|`-delimited table row or a run of element names as one enormous sentence -- which is
/// exactly what the measurement that produced this limit did before it read the AST instead.
///
/// Only the canonical language. A word count means nothing for Japanese, which does not separate
/// words with spaces, so applying it there would either pass everything or fail everything depending
/// on the punctuation the sentence happened to use.
/// </remarks>
public static class SentenceLength
{
    /// <summary>How many words a sentence may run to.</summary>
    /// <remarks>
    /// Forty. The longest sentence in the canonical edition when this was written was 38 words, so
    /// the gate sits just above where the documents already are: it cannot be satisfied by rewriting
    /// nothing, and it does not demand a rewrite of what is there. The sentences it was built from
    /// ran to 43 and 51 words, and both were split rather than shortened.
    /// </remarks>
    public const int Limit = 40;

    public static void EnsureNoOverlongSentence(MarkdownDocument document, string fileName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fileName);

        foreach (var paragraph in document.Descendants<ParagraphBlock>())
        {
            // Top-level paragraphs only. A list item and a table cell each hold a ParagraphBlock of
            // their own, and neither is read as a sentence: a list is taken one item at a time, and
            // a cell is read against its row. Measuring them would reject a long table row for a
            // length no reader experiences.
            if (paragraph.Parent is not MarkdownDocument)
            {
                continue;
            }

            var (text, codeSpans, proseWords) = Read(paragraph);

            // A paragraph that is mostly code spans is a vocabulary list rather than a sentence --
            // element-reference.md lists 100 element helpers as comma-separated runs, which no
            // reader takes in as prose and no split would improve. Real prose keeps code spans in
            // the minority, so the two are told apart by which outnumbers which.
            if (codeSpans > proseWords)
            {
                continue;
            }

            foreach (string sentence in Sentences(text))
            {
                // Tokens carrying a letter or digit, for the same reason the prose count uses them:
                // a comma or a full stop left standing beside a code span is punctuation, not a
                // word, and counting it would make the limit depend on where the commas fell.
                int words = sentence
                    .Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
                    .Count(token => token.Any(char.IsLetterOrDigit));
                if (words <= Limit)
                {
                    continue;
                }

                throw KeyValueBlock.Invalid(
                    fileName,
                    "document",
                    $"a sentence runs to {words} words, and the limit is {Limit}. Split it. " +
                    $"The sentence begins: \"{Opening(sentence)}\".");
            }
        }
    }

    /// <summary>
    /// The paragraph's text with every inline flattened to what a reader would read, plus the two
    /// counts that say whether it is prose at all.
    /// </summary>
    private static (string Text, int CodeSpans, int ProseWords) Read(ParagraphBlock paragraph)
    {
        var text = new StringBuilder();
        int codeSpans = 0;
        int proseWords = 0;

        foreach (var inline in paragraph.Inline?.Descendants() ?? [])
        {
            switch (inline)
            {
                case LiteralInline literal:
                    string content = literal.Content.ToString();
                    text.Append(content);
                    // Only tokens carrying a letter or digit. The separators between code spans in
                    // a vocabulary list are literal inlines too -- ", " and "*, " -- and counting
                    // those as prose would make every such list look like a sentence with as many
                    // words as it has names.
                    proseWords += content
                        .Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
                        .Count(token => token.Any(char.IsLetterOrDigit));
                    break;

                // A code span is one thing a reader takes in at a glance, whatever it holds, so it
                // counts as one word rather than as however many spaces are inside it.
                case CodeInline:
                    text.Append(" code ");
                    codeSpans++;
                    break;

                case LineBreakInline:
                    text.Append(' ');
                    break;
            }
        }

        return (text.ToString(), codeSpans, proseWords);
    }

    /// <summary>Splits on sentence-ending punctuation followed by a space.</summary>
    private static IEnumerable<string> Sentences(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (text[i] is '.' or '?' or '!' or ':' && text[i + 1] == ' ')
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        }

        yield return text[start..];
    }

    /// <summary>Enough of the sentence to find it in the file.</summary>
    private static string Opening(string sentence)
    {
        string trimmed = sentence.Trim();
        return trimmed.Length <= 60 ? trimmed : trimmed[..60] + "…";
    }
}
