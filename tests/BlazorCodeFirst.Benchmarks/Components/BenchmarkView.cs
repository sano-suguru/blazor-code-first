using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Benchmarks.Components;

/// <summary>
/// The BlazorCodeFirst side of the §7.1 allocation comparison.
/// </summary>
/// <remarks>
/// Kept to constructs entirely inside the statically analysable subset, because that is what §7.1
/// claims: "SSC内側のレンダリングコスト・アロケーション特性は等価なRazorコンポーネントと同等". The
/// dynamic-content (Opaque) path that sentence goes on to mention is a separate comparison, measured by
/// <see cref="OpaqueContentView"/> against <see cref="OpaqueContentViewRazor"/>.
/// <para>
/// Every text node comes from a property rather than a literal, because the Razor compiler folds a
/// fully static element subtree into one <c>AddMarkupContent</c> frame while this generator, before
/// #140, emitted an element frame plus a text frame. That difference was real and was recorded against
/// §7.1, but it is not what an allocation comparison can measure: two components emitting different
/// frame counts are not the same workload. Driving the content from properties puts both compilers on
/// the same frames so the allocated bytes are attributable to the compilation strategy.
/// </para>
/// <para>
/// Since #140 the generator folds too, so the static spelling would now match Razor as well — that is
/// what <see cref="StaticParityView"/> gates. This pair is left on the property-driven spelling
/// regardless: the §7.1 figures were measured against it, and a non-constant value keeps both sides off
/// the fold path entirely, which is the narrower comparison those figures describe.
/// </para>
/// </remarks>
public partial class BenchmarkView : BodyComponentBase
{
    public string Title { get; set; } = "Benchmark";

    public int Count { get; set; }

    public string Description { get; set; } = "A paragraph of content.";

    protected override View Body =>
        Div.Class("card")[
            H1[Title],
            Span[$"Count: {Count}"],
            P[Description]];

    /// <summary>Exposes the generated render path to the benchmark and the equivalence gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);

    /// <summary>
    /// Requests a re-render. The render-cycle benchmark needs this because
    /// <c>Renderer.RenderRootComponentAsync</c> may be called only once per root component id.
    /// </summary>
    public void Invalidate() => StateHasChanged();
}
