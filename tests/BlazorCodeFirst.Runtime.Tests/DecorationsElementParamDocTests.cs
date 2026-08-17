using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BlazorCodeFirst.Runtime.Tests;

/// <summary>
/// Keeps <c>Decorations.cs</c>'s <c>element</c> parameter doc to the wordings this repository already
/// decided on, in both directions. #416 found the 35 copies had quietly split into four spellings, with
/// nothing to report it; consolidating them into <c>Decorations.doc.xml</c> answered that split, but
/// nothing stopped a future member from hand-writing a fifth. This resolves every occurrence's wording,
/// whether written inline or spliced in with <c>&lt;include&gt;</c>, to its plain text and checks the
/// resulting set against <see cref="KnownWordings"/>, rather than pinning member names or counts that a
/// new decoration would otherwise have to bump for no reason.
/// </summary>
public sealed partial class DecorationsElementParamDocTests
{
    /// <summary>Every wording this repository has decided the <c>element</c> parameter may carry.</summary>
    private static readonly HashSet<string> KnownWordings =
    [
        """The element being decorated (Div, Span, Element("…"), …).""",
        """The element being decorated (Input, Textarea, …).""",
        """The element being decorated (Div, Input, Element("…"), …).""",
    ];

    [Fact]
    public void ElementParamWordings_Encountered_AreAllKnown()
    {
        var unexpected = ReadOccurrences()
            .Where(occurrence => !KnownWordings.Contains(occurrence.Wording))
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            "Decorations.cs has element param wording(s) not recorded in " +
            $"{nameof(DecorationsElementParamDocTests)}.{nameof(KnownWordings)}:\n" +
            string.Join(
                "\n",
                unexpected.Select(o => $"  line {o.Line}: \"{o.Wording}\"")));
    }

    [Fact]
    public void KnownElementParamWordings_AreAllStillUsed()
    {
        var used = ReadOccurrences().Select(o => o.Wording).ToHashSet();
        var stale = KnownWordings.Where(wording => !used.Contains(wording)).ToArray();

        Assert.True(
            stale.Length == 0,
            $"{nameof(KnownWordings)} records wording(s) Decorations.cs no longer uses:\n" +
            string.Join("\n", stale.Select(w => $"  \"{w}\"")));
    }

    /// <summary>
    /// Every <c>&lt;param name="element"&gt;</c> in <c>Decorations.cs</c>, resolved to plain text: an
    /// inline one as written, an <c>&lt;include&gt;</c> one by reading the fragment it names out of
    /// <c>Decorations.doc.xml</c>. Markup (<c>&lt;c&gt;</c> and the rest) is stripped, because what is
    /// being guarded is the wording, not how it happens to be spelled in XML.
    /// </summary>
    private static IEnumerable<(int Line, string Wording)> ReadOccurrences()
    {
        var decorationsPath = Path.Combine(RepoRoot, "src", "BlazorCodeFirst.Runtime", "Decorations.cs");
        var fragmentsPath = Path.Combine(RepoRoot, "src", "BlazorCodeFirst.Runtime", "Decorations.doc.xml");
        var fragments = XDocument.Load(fragmentsPath);

        var lines = File.ReadAllLines(decorationsPath);
        for (var i = 0; i < lines.Length; i++)
        {
            if (InlineParam().Match(lines[i]) is { Success: true } inline)
            {
                yield return (i + 1, StripMarkup(inline.Groups["text"].Value));
                continue;
            }

            if (IncludedParam().Match(lines[i]) is { Success: true } included)
            {
                string id = included.Groups["id"].Value;
                var param = fragments.Root
                    ?.Elements("fragment")
                    .FirstOrDefault(f => (string?)f.Attribute("id") == id)
                    ?.Element("param")
                    ?? throw new InvalidOperationException(
                        $"Decorations.cs:{i + 1} includes fragment '{id}', which " +
                        $"{fragmentsPath} does not define.");

                yield return (i + 1, StripMarkup(param.Value));
            }
        }
    }

    private static string StripMarkup(string text) => Tag().Replace(text, "").Trim();

    private static string RepoRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "BlazorCodeFirst.slnx")))
                    return dir.FullName;
            }

            throw new InvalidOperationException(
                $"Could not locate BlazorCodeFirst.slnx above {AppContext.BaseDirectory}.");
        }
    }

    [GeneratedRegex("""^\s*/// <param name="element">(?<text>.*)</param>\s*$""")]
    private static partial Regex InlineParam();

    [GeneratedRegex(
        """^\s*/// <include file="Decorations\.doc\.xml" path="doc/fragment\[@id='(?<id>[^']+)'\]/param"\s*/>\s*$""")]
    private static partial Regex IncludedParam();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();
}
