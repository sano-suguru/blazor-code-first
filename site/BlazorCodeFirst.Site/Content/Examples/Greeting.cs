using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Content.Examples;

/// <summary>getting-started.md's first component, as a component that actually renders.</summary>
/// <remarks>
/// The figure region is the expression alone, as every example's is. What surrounds it -- the using
/// directives, the [Route], the partial class -- is shown in the document in an ordinary fence,
/// because that half is about the shape of a file rather than about what an expression renders, and
/// only the second is a claim this suite can hold anything to.
/// </remarks>
internal sealed partial class Greeting : BodyComponentBase
{
    // <figure>
    protected override View Body =>
        Div[
            H1["Hello"],
            Span["Welcome to BlazorCodeFirst."]];
    // </figure>
}
