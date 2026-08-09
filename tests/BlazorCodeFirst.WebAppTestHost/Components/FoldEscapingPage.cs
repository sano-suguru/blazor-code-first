using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.WebAppTestHost.Components;

/// <summary>
/// A fully static tree whose text and attribute content contain characters HTML must escape. The
/// generator folds a run of fully-static siblings into a single <c>AddMarkupContent</c> frame (#140),
/// and the .NET <c>HtmlRenderer</c> writes markup frames to the wire verbatim, so this is meant to be the
/// one place in the test suite where an escaping defect in the fold's serialization would surface: bUnit's
/// <c>MarkupMatches</c> compares parsed DOM trees, which would swallow a raw-versus-escaped difference
/// that the browser's HTML parser treats as equivalent, and the compiler's own unit tests assert against
/// the generated C# source rather than against rendered output. That premise only holds while this page
/// keeps folding to one frame, which
/// <c>PrerenderTests.FoldEscaping_page_folds_to_a_single_markup_frame</c> pins directly: an ordinary,
/// unfolded <c>AddContent</c>/<c>AddAttribute</c> pair would be escaped by the .NET <c>HtmlRenderer</c>
/// itself, byte-identically to <c>StaticMarkupSerializer.WriteTo</c> for every character asserted here, so
/// the escaping test alone cannot tell the two paths apart.
/// </summary>
[Route("/fold-escaping")]
public partial class FoldEscapingPage : BodyComponentBase
{
    protected override View Body =>
        Div.Class("fold-escaping")[
            P["Q&A: <script>alert(1)</script> & more"],
            Span.Attr("title", "say \"hi\" & bye")["ok"]];

    /// <summary>Exposes the generated render path to the frame-count pin in <c>PrerenderTests</c>.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}
