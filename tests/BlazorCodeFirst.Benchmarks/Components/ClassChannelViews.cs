using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Benchmarks.Components;

/// <summary>
/// Two class terms, neither null: the shape that decided #236. A generation rule that carries no
/// residue is only worth taking if the spelling that never had a residue keeps costing what it cost.
/// </summary>
/// <remarks>
/// Every view here keeps one non-constant term so the element stays off the static fold path, which is
/// where the join under measurement lives. A fully constant spelling folds to markup and measures
/// nothing.
/// <para>
/// Deliberately not a comparison against Razor, unlike <see cref="BenchmarkView"/>. Razor has no
/// additive class channel, so there is nothing to put on the other side, and these figures are this
/// generator against itself before and after a rule change. <c>DESIGN.md</c> §7.1 does not publish
/// them.
/// </para>
/// </remarks>
public partial class ClassJoinNonNullView : BodyComponentBase
{
    public string Variant { get; set; } = "wide";

    protected override View Body => Div.Class("card").Class(Variant)["x"];

    /// <summary>Exposes the generated render path to the benchmark.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>A conditional term that is null at render time: the shape #236 is about.</summary>
public partial class ClassJoinNullView : BodyComponentBase
{
    public bool On { get; set; }

    protected override View Body => Div.Class("card").Class(On ? "active" : null)["x"];

    /// <summary>Exposes the generated render path to the benchmark.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>Three terms, none null: the arity where the concatenation reaches a params array.</summary>
public partial class ClassJoinThreeView : BodyComponentBase
{
    public string Variant { get; set; } = "wide";

    public string Size { get; set; } = "lg";

    protected override View Body => Div.Class("card").Class(Variant).Class(Size)["x"];

    /// <summary>Exposes the generated render path to the benchmark.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}
