using System.Collections.Immutable;
using System.Linq;
using BlazorCompose.Compiler.Analysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class KeyabilityResolverTests
{
    private static ExpressionTemplate Lit(string text) => ExpressionTemplate.Literal(text);

    private static ElementTemplateNode Span(ExpressionTemplate content) =>
        new("span", default, default, default, ImmutableArray.Create<RenderTemplateNode>(new TextContentTemplateNode(content)));

    [Fact]
    public void ResolveRootKind_Div_IsElement()
    {
        var node = new ElementTemplateNode(
            "div", default, default, default,
            ImmutableArray.Create<RenderTemplateNode>(Span(Lit("\"x\""))));

        Assert.Equal(ContentRootKind.Element, KeyabilityResolver.ResolveRootKind(node, ComposableRegistry.Empty));
    }

    [Fact]
    public void ResolveRootKind_BareIf_IsRegion()
    {
        var node = new IfTemplateNode(Lit("true"), Span(Lit("\"x\"")), null);

        Assert.Equal(ContentRootKind.Region, KeyabilityResolver.ResolveRootKind(node, ComposableRegistry.Empty));
    }

    [Fact]
    public void ResolveRootKind_Component_IsElement()
    {
        var node = new ComponentTemplateNode("global::X.C", EquatableArray<ComponentParameter>.Empty);

        Assert.Equal(ContentRootKind.Element, KeyabilityResolver.ResolveRootKind(node, ComposableRegistry.Empty));
    }

    [Fact]
    public void ResolveRootKind_ComposableCallToUnknown_IsUnresolved()
    {
        var node = new ComposableCallTemplateNode(
            "K:Missing", "Missing", default, new TemplateLocation("f", default, default));

        Assert.Equal(ContentRootKind.Unresolved, KeyabilityResolver.ResolveRootKind(node, ComposableRegistry.Empty));
    }

    [Fact]
    public void CollectForEachContentDiagnostics_RegionRootedContent_EmitsBc3003()
    {
        // ForEach whose content root is a bare If (region).
        var forEach = new ForEachTemplateNode(
            Lit("_xs"), Lit("__bc_item_0.Id"),
            new IfTemplateNode(Lit("true"), Span(Lit("\"x\"")), null),
            new TemplateLocation("f", default, default));
        var sink = ImmutableArray.CreateBuilder<BlazorCompose.Compiler.Diagnostics.DiagnosticInfo>();

        KeyabilityResolver.CollectForEachContentDiagnostics(forEach, ComposableRegistry.Empty, sink);

        Assert.Single(sink, d => d.Id == "BC3003");
    }

    [Fact]
    public void CollectForEachContentDiagnostics_ElementRootedContent_EmitsNothing()
    {
        var forEach = new ForEachTemplateNode(
            Lit("_xs"), Lit("__bc_item_0.Id"),
            Span(Lit("__bc_item_0.Name")),
            new TemplateLocation("f", default, default));
        var sink = ImmutableArray.CreateBuilder<BlazorCompose.Compiler.Diagnostics.DiagnosticInfo>();

        KeyabilityResolver.CollectForEachContentDiagnostics(forEach, ComposableRegistry.Empty, sink);

        Assert.Empty(sink);
    }

    [Fact]
    public void ResolveRootKind_Fragment_IsRegion()
    {
        var node = new FragmentTemplateNode(
            ImmutableArray.Create<RenderTemplateNode>(Span(Lit("\"x\""))));
        Assert.Equal(ContentRootKind.Region, KeyabilityResolver.ResolveRootKind(node, ComposableRegistry.Empty));
    }

    [Fact]
    public void ResolveRootKind_RawMarkup_IsRegion()
    {
        var node = new RawMarkupTemplateNode(Lit("\"<b>x</b>\""));
        Assert.Equal(ContentRootKind.Region, KeyabilityResolver.ResolveRootKind(node, ComposableRegistry.Empty));
    }

    [Fact]
    public void CollectForEachContentDiagnostics_FragmentContentRoot_EmitsBc3003()
    {
        // ForEach whose content root is a Fragment (non-keyable) — even wrapping a single Div.
        var forEach = new ForEachTemplateNode(
            Lit("_xs"), Lit("__bc_item_0.Id"),
            new FragmentTemplateNode(ImmutableArray.Create<RenderTemplateNode>(
                new ElementTemplateNode("div", default, default, default,
                    ImmutableArray.Create<RenderTemplateNode>(Span(Lit("\"x\"")))))),
            new TemplateLocation("f", default, default));

        var sink = ImmutableArray.CreateBuilder<BlazorCompose.Compiler.Diagnostics.DiagnosticInfo>();
        KeyabilityResolver.CollectForEachContentDiagnostics(forEach, ComposableRegistry.Empty, sink);

        Assert.Contains(sink, d => d.Id == "BC3003");
    }

    [Fact]
    public void CollectForEachContentDiagnostics_ForEachNestedInFragment_IsWalked()
    {
        // A Fragment child that itself holds a region-rooted ForEach must still surface BC3003 — proves the
        // walker recurses into Fragment children.
        var innerForEach = new ForEachTemplateNode(
            Lit("_ys"), Lit("__bc_item_1.Id"),
            new IfTemplateNode(Lit("true"), Span(Lit("\"y\"")), null),
            new TemplateLocation("f", default, default));
        var root = new ElementTemplateNode("div", default, default, default,
            ImmutableArray.Create<RenderTemplateNode>(
                new FragmentTemplateNode(ImmutableArray.Create<RenderTemplateNode>(innerForEach))));

        var sink = ImmutableArray.CreateBuilder<BlazorCompose.Compiler.Diagnostics.DiagnosticInfo>();
        KeyabilityResolver.CollectForEachContentDiagnostics(root, ComposableRegistry.Empty, sink);

        Assert.Contains(sink, d => d.Id == "BC3003");
    }
}
