using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Content;

/// <summary>
/// The landing page's code figures, written as BlazorCodeFirst views rather than as raw HTML.
/// </summary>
/// <remarks>
/// DocGen highlights Markdown code fences with ColorCode at authoring time, but it converts whole
/// documents: there is no way to ask it for one highlighted snippet to place on a page that is not
/// a document. The two remaining options were a hand-written HTML string behind
/// <see cref="Html.Raw"/>, which would silently drift from css/highlight.css with nothing checking
/// it, and this — spelling the spans in the surface itself.
///
/// This is the better one for a site whose purpose is to be the library's own first user, and it
/// costs nothing at runtime: every span here is a constant, so the fold collapses each figure into
/// a single AddMarkupContent frame. The class names (k, t, s, m, c) are the landing page's own
/// vocabulary, styled in css/app.css under .slab; they are deliberately not the ColorCode class
/// names, because these figures sit on a dark surface and the ColorCode theme is written for paper.
///
/// The gap itself is tracked as an issue against DocGen. If DocGen ever grows a snippet mode, this
/// file is what it replaces.
///
/// Each method is [ViewPart] so it expands into the caller's RenderView with no component
/// boundary — a figure is a piece of one page's markup, not a reusable component.
/// </remarks>
internal static class CodeSamples
{
    /// <summary>The hero figure: an ordinary BlazorCodeFirst page component.</summary>
    /// <remarks>
    /// Deliberately close to this site's own Pages/Home.cs. The claim in the headline is checkable
    /// against the file next to it.
    /// </remarks>
    /// <remarks>
    /// Lines are kept under about 45 characters. The figure sits in half of a two-column hero, and a
    /// code block that needs a horizontal scrollbar to show its own first statement is not a figure,
    /// it is a puzzle.
    /// </remarks>
    [ViewPart]
    public static View Component() =>
        Pre.Class("slab")[Code[
            "[", Span.Class("t")["Route"], "(", Span.Class("s")["\"/\""], ")]\n",
            Span.Class("k")["public sealed partial class "], Span.Class("t")["Home"], "\n",
            "    : ", Span.Class("t")["BodyComponentBase"], "\n",
            "{\n",
            "    ", Span.Class("k")["protected override "], Span.Class("t")["View"],
            " ", Span.Class("m")["Body"], " =>\n",
            "        ", Span.Class("m")["Section"], ".", Span.Class("m")["Class"],
            "(", Span.Class("s")["\"prose\""], ")[\n",
            "            ", Span.Class("m")["H1"], "[", Span.Class("s")["\"Blazor UI in C#\""], "],\n",
            "            ", Span.Class("m")["P"], "[", Span.Class("s")["\"Attributes first.\""], "],\n",
            "            ", Span.Class("m")["A"], ".", Span.Class("m")["Href"],
            "(", Span.Class("s")["\"/docs\""], ")[", Span.Class("s")["\"The guide\""], "]];\n",
            "}"]];

    /// <summary>The left half of the comparison: the design-time expression an author writes.</summary>
    [ViewPart]
    public static View DesignTime() =>
        Pre.Class("slab slab--light")[Code[
            Span.Class("m")["Button"], "\n",
            "    .", Span.Class("m")["Class"], "(", Span.Class("s")["\"btn\""], ")\n",
            "    .", Span.Class("m")["Class"], "(", Span.Class("s")["\"btn-primary\""], ")\n",
            "    .", Span.Class("m")["OnClick"], "(() => ", Span.Class("m")["Save"], "())[",
            Span.Class("s")["\"Save\""], "]"]];

    /// <summary>
    /// The right half of the comparison: the RenderView body the generator emits for it.
    /// </summary>
    /// <remarks>
    /// Kept in step with ARCHITECTURE.md §2.4, which carries this same pair as its worked example of
    /// the class channel folding while every other decoration costs one frame each.
    /// </remarks>
    [ViewPart]
    public static View Generated() =>
        Pre.Class("slab")[Code[
            Span.Class("c")["// Both .Class calls fold into one attribute frame."], "\n",
            Span.Class("m")["__builder"], ".", Span.Class("m")["OpenElement"], "(0, ",
            Span.Class("s")["\"button\""], ");\n",
            Span.Class("m")["__builder"], ".", Span.Class("m")["AddAttribute"], "(1, ",
            Span.Class("s")["\"class\""], ", ", Span.Class("s")["\"btn btn-primary\""], ");\n",
            Span.Class("m")["__builder"], ".", Span.Class("m")["AddAttribute"], "(2, ",
            Span.Class("s")["\"onclick\""], ", ", Span.Class("c")["/* () => Save() */"], ");\n",
            Span.Class("m")["__builder"], ".", Span.Class("m")["AddContent"], "(3, ",
            Span.Class("s")["\"Save\""], ");\n",
            Span.Class("m")["__builder"], ".", Span.Class("m")["CloseElement"], "();"]];

    /// <summary>A build failure, quoted from the diagnostic the compiler actually reports.</summary>
    /// <remarks>
    /// The message is BCF3016's messageFormat verbatim, wrapped by hand so it does not need a
    /// horizontal scroll on a phone. If that wording changes, this figure has to change with it —
    /// nothing checks the two against each other.
    /// </remarks>
    [ViewPart]
    public static View Diagnostic() =>
        Pre.Class("slab")[Code[
            Span.Class("c")["Pages/Broken.cs(14,13): "],
            Span.Class("m")["error BCF3016"], ":\n",
            Span.Class("s")["'img'"], " is a void element and cannot have\n",
            "children; prerendering pushes them out of the\n",
            "element while interactive rendering keeps them\n",
            "inside, so the two disagree. Remove the children,\n",
            "or place them beside the element."]];
}
