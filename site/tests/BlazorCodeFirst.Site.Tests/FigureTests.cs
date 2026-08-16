using BlazorCodeFirst.Site.Content.Examples;
using Bunit;
using Microsoft.AspNetCore.Components;
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

    public static TheoryData<string, string> DocumentFigureNames()
    {
        var data = new TheoryData<string, string>();
        foreach (var figure in DocumentFigures.All)
        {
            data.Add(figure.Document, figure.Name);
        }

        return data;
    }

    [Fact]
    public void DocumentFigures_AreFound() =>
        // Both theories below read as a pass over an empty set, which is what a marker whose spelling
        // drifted would produce. Prove the documents carry figures before trusting what is checked
        // about them.
        Assert.NotEmpty(DocumentFigures.All);

    [Theory]
    [MemberData(nameof(DocumentFigureNames))]
    public void DocumentFigure_ShowsTheSourceOfTheComponentItNames(string document, string name)
    {
        var figure = Single(document, name);

        Assert.Equal(FigureRegion(File.ReadAllText(SourcePathOf(name))), figure.CSharp);
    }

    [Theory]
    [MemberData(nameof(DocumentFigureNames))]
    public void DocumentFigure_ShowsWhatThatComponentRenders(string document, string name)
    {
        var figure = Single(document, name);

        Render(Fragment(TypeOf(name))).MarkupMatches(figure.Html);
    }

    private static DocumentFigure Single(string document, string name) =>
        DocumentFigures.All.Single(f => f.Document == document && f.Name == name);

    /// <summary>The example's source file, which the marker names by type rather than by path.</summary>
    private static string SourcePathOf(string name)
    {
        var path = RepositoryPath.From(
            Path.Combine("site", "BlazorCodeFirst.Site", "Content", "Examples", name + ".cs"));

        Assert.True(
            File.Exists(path),
            $"A figure names '{name}', but Content/Examples/{name}.cs does not exist. The marker names " +
            "the example type, and the file is named after it.");

        return path;
    }

    /// <summary>The example type the marker names.</summary>
    /// <remarks>
    /// Resolved by name rather than passed as a generic argument, because the figure list comes from
    /// the documents. A name with no type behind it fails here rather than dropping out of the run.
    /// </remarks>
    private static Type TypeOf(string name)
    {
        var type = typeof(Hero).Assembly.GetType(
            $"BlazorCodeFirst.Site.Content.Examples.{name}", throwOnError: false);

        Assert.True(
            type is not null,
            $"A figure names '{name}', but no type BlazorCodeFirst.Site.Content.Examples.{name} exists.");

        return type!;
    }

    /// <summary>
    /// Renders the example, supplying a stand-in for a routed page when the component takes one.
    /// </summary>
    /// <remarks>
    /// A layout's chrome is the one figure whose subject has a parameter: ChromeLayoutBase inherits
    /// Body from LayoutComponentBase, and a layout with none renders its shell around nothing. The
    /// stand-in is text rather than markup so the figure shows where the routed page lands without
    /// implying a shape for it, and the html half quotes it like any other content.
    /// </remarks>
    private static RenderFragment Fragment(Type type) =>
        builder =>
        {
            builder.OpenComponent(0, type);

            if (type.GetProperty("Body")?.PropertyType == typeof(RenderFragment))
            {
                builder.AddComponentParameter(1, "Body", (RenderFragment)(inner =>
                    inner.AddContent(0, "the routed page")));
            }

            builder.CloseComponent();
        };
}
