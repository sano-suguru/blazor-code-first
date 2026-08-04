using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Benchmarks.Components;

/// <summary>
/// The BlazorCodeFirst side of the §7.1 allocation comparison.
/// </summary>
/// <remarks>
/// Kept to constructs entirely inside the statically analysable subset, because that is what §7.1
/// claims: "SSC内側のレンダリングコスト・アロケーション特性は等価なRazorコンポーネントと同等". The
/// dynamic-content path that sentence goes on to mention is not measured here, and cannot be: it is
/// unimplemented (see #57 and #17), so that clause keeps its 予測値.
/// <para>
/// Every text node comes from a property rather than a literal, because the Razor compiler folds a
/// fully static element subtree into one <c>AddMarkupContent</c> frame while this generator always
/// emits an element frame plus a text frame. That difference is real and is recorded against §7.1,
/// but it is not what an allocation comparison can measure: two components emitting different frame
/// counts are not the same workload. Driving the content from properties puts both compilers on the
/// same frames so the allocated bytes are attributable to the compilation strategy.
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
