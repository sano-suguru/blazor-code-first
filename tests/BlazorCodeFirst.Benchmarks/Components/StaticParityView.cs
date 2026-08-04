using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Benchmarks.Components;

/// <summary>
/// The BlazorCodeFirst side of the static-spelling parity gate: the same shape as
/// <see cref="BenchmarkView"/>, but with every text node written as a literal instead of driven from a
/// property.
/// </summary>
/// <remarks>
/// This pair exists because of what #140 changed about <see cref="BenchmarkView"/>'s remarks. Before
/// folding, a statically written component could not be compared against a statically written Razor
/// component at all: Razor folded the whole subtree into one <c>AddMarkupContent</c> frame while this
/// generator emitted an element frame plus a text frame per node, so §7.1's measurement had to drive
/// every text node from a property to put both compilers on the same frames. Now that the emitter folds,
/// the static spelling reaches Razor's shape on its own, and this pair is what proves it rather than
/// asserting it in prose.
/// <para>
/// It carries no benchmark. §7.1's published allocation figures still come from
/// <see cref="BenchmarkView"/> and <c>BenchmarkViewRazor</c>, whose property-driven spelling those
/// numbers were measured against; re-spelling them would invalidate published values. What this pair
/// gates is the DESIGN.md §7.1 <em>claim</em> that the two compilers now emit the same frames for a
/// static subtree — see <c>Program.Main</c>.
/// </para>
/// </remarks>
public partial class StaticParityView : BodyComponentBase
{
    protected override View Body =>
        Div.Class("card")[
            H1["Static parity"],
            Span["A literal count"],
            P["A paragraph of content."]];

    /// <summary>Exposes the generated render path to the equivalence gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}
