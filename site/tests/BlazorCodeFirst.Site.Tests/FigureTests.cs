using BlazorCodeFirst.Site.Content.Examples;
using Bunit;
using Xunit;

namespace BlazorCodeFirst.Site.Tests;

/// <summary>
/// Holds each output figure to the component it claims to describe.
/// </summary>
/// <remarks>
/// A figure that pairs an expression with the HTML it produces is a claim about this library, and a
/// hand-maintained one drifts silently: every other check the site has stays green whichever way the
/// surface moves. This is the site-side counterpart of LandingPageFigureTests, which holds the
/// generated-frames figure to the compiler from the other side of the slnx boundary.
/// </remarks>
public sealed class FigureTests : BunitContext
{
    private const string FigureOpen = "// <figure>";
    private const string FigureClose = "// </figure>";

    /// <summary>The lines between the two markers, dedented by the shallowest indent among them.</summary>
    /// <remarks>
    /// Dedented because a figure is read at the left margin while the source it comes from sits
    /// inside a class. Comparing the two verbatim would fail on indentation alone and say nothing
    /// about whether the figure is the code.
    /// </remarks>
    internal static string FigureRegion(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        int open = Array.FindIndex(lines, l => l.Trim() == FigureOpen);
        int close = Array.FindIndex(lines, l => l.Trim() == FigureClose);

        if (open < 0 || close < open)
        {
            throw new InvalidOperationException(
                $"The source has no '{FigureOpen}' … '{FigureClose}' region.");
        }

        string[] body = lines[(open + 1)..close];
        int indent = body
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join('\n', body.Select(l => l.Length >= indent ? l[indent..] : l)).TrimEnd();
    }

    [Fact]
    public void HeroFigure_IsTheSourceOfTheComponentItClaimsToDescribe()
    {
        string figure = File.ReadAllText(RepositoryPath.From("site/snippets/hero.cs"))
            .ReplaceLineEndings("\n")
            .TrimEnd();
        string source = FigureRegion(
            File.ReadAllText(
                RepositoryPath.From("site/BlazorCodeFirst.Site/Content/Examples/Hero.cs")));

        Assert.Equal(source, figure);
    }

    [Fact]
    public void HeroOutputFigure_IsWhatTheComponentRenders()
    {
        string expected = File.ReadAllText(RepositoryPath.From("site/snippets/hero.html"));

        Render<Hero>().MarkupMatches(expected);
    }

    [Fact]
    public void FigureRegion_SourceWithNoMarkers_ThrowsRatherThanReturningTheWholeFile() =>
        // Returning the file would compare a figure against usings and a class declaration, which
        // fails with a diff nobody can read instead of naming the missing markers.
        Assert.Throws<InvalidOperationException>(() => FigureRegion("class C { }"));
}
