using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.WebAppTestHost.Components;

/// <summary>
/// A fully static tree whose text and attribute content contain characters HTML must escape. The
/// generator folds a run of fully-static siblings into a single <c>AddMarkupContent</c> frame (#140),
/// and the .NET <c>HtmlRenderer</c> writes markup frames to the wire verbatim, so this is the only place
/// in the test suite where an escaping defect in the fold's serialization would actually surface: bUnit's
/// <c>MarkupMatches</c> compares parsed DOM trees, which would swallow a raw-versus-escaped difference
/// that the browser's HTML parser treats as equivalent, and the compiler's own unit tests assert against
/// the generated C# source rather than against rendered output.
/// </summary>
[Route("/fold-escaping")]
public partial class FoldEscapingPage : BodyComponentBase
{
    protected override View Body =>
        Div.Class("fold-escaping")[
            P["Q&A: <script>alert(1)</script> & more"],
            Span.Attr("title", "say \"hi\" & bye")["ok"]];
}
