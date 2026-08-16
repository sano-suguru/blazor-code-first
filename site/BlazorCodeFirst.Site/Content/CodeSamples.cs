using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Content;

/// <summary>
/// The landing page's code figures.
/// </summary>
/// <remarks>
/// Three of the four are generated: DocGen converts a source file under site/snippets to highlighted
/// HTML at authoring time, and the figure places it with <see cref="Html.Raw"/>. The classes are
/// ColorCode's, the same ones a prose code block carries, which is what keeps the site to one code
/// vocabulary and puts these figures under HighlightCssEmitterTests' parity check.
///
/// <see cref="Diagnostic"/> is the exception and stays hand-written, because a build error is not
/// code. It carries diag-loc and diag-id rather than borrowing the code classes, so the one thing on
/// this page that is not a code sample does not read as though it were.
///
/// Each method is [ViewPart] so it expands into the caller's RenderView with no component
/// boundary — a figure is a piece of one page's markup, not a reusable component. Every value placed
/// here is a constant, so each figure still costs the single AddMarkupContent frame it cost when the
/// spans were spelled by hand.
/// </remarks>
internal static class CodeSamples
{
    /// <summary>The hero figure: an ordinary BlazorCodeFirst page component.</summary>
    /// <remarks>
    /// Deliberately close to this site's own Pages/Home.cs. The claim in the headline is checkable
    /// against the file next to it.
    /// </remarks>
    /// <remarks>
    /// site/snippets/hero.cs keeps its lines under about 45 characters. The figure sits in half of a
    /// two-column hero, and a code block that needs a horizontal scrollbar to show its own first
    /// statement is not a figure, it is a puzzle.
    /// </remarks>
    [ViewPart]
    public static View Component() =>
        Div.Class("slab")[Raw(Snippets.Hero)];

    /// <summary>The left half of the comparison: the design-time expression an author writes.</summary>
    [ViewPart]
    public static View DesignTime() =>
        Div.Class("slab slab--light")[Raw(Snippets.DesignTime)];

    /// <summary>
    /// The right half of the comparison: the RenderView body the generator emits for it.
    /// </summary>
    /// <remarks>
    /// Kept in step with ARCHITECTURE.md §2.4, which carries this same pair as its worked example of
    /// the class channel folding while every other decoration costs one frame each.
    /// LandingPageFigureTests compiles site/snippets/design-time.cs and holds site/snippets/generated.cs
    /// to the frames the generator emits for it, frame for frame. The only text the figure may differ in
    /// is what its /* … */ placeholder stands for: the callback wrapper, elided so the figure reads at a
    /// glance.
    /// </remarks>
    [ViewPart]
    public static View Generated() =>
        Div.Class("slab")[Raw(Snippets.Generated)];

    /// <summary>A build failure, quoted from the diagnostic the compiler actually reports.</summary>
    /// <remarks>
    /// The message is BCF3016's messageFormat verbatim, wrapped by hand so it does not need a
    /// horizontal scroll on a phone. If that wording changes, this figure has to change with it, and
    /// LandingPageFigureTests fails until it does: it reads the literals below back out of this file and
    /// holds them to the descriptor's ID, severity and message, taking each hand-written line break as
    /// the space it replaced.
    /// </remarks>
    /// <remarks>
    /// Hand-written, unlike the three above, because a build error is not code. Its own two classes
    /// keep it out of the code vocabulary rather than borrowing a comment and a member colour to
    /// stand for a location and an identifier.
    /// </remarks>
    [ViewPart]
    public static View Diagnostic() =>
        Pre.Class("slab")[Code[
            Span.Class("diag-loc")["Pages/Broken.cs(14,13): "],
            Span.Class("diag-id")["error BCF3016"], ":\n",
            "'img' is a void element and cannot have\n",
            "children; prerendering pushes them out of the\n",
            "element while interactive rendering keeps them\n",
            "inside, so the two disagree. Remove the children,\n",
            "or place them beside the element."]];
}
