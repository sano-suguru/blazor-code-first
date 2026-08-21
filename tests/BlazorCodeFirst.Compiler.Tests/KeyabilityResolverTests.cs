using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Analysis;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class KeyabilityResolverTests
{
    private static ExpressionTemplate Lit(string text) => ExpressionTemplate.Literal(text);

    private static ElementNode Span(ExpressionTemplate content) =>
        new("span", default, default, default, ImmutableArray.Create<RenderNode>(new TextContentNode(content)));

    [Fact]
    public void ResolveRoot_Div_IsElement()
    {
        var node = new ElementNode(
            "div", default, default, default,
            ImmutableArray.Create<RenderNode>(Span(Lit("\"x\""))));

        Assert.Equal(ContentRootKind.Element, KeyabilityResolver.ResolveRoot(node, ViewPartRegistry.Empty).Kind);
    }

    [Fact]
    public void ResolveRoot_BareIf_IsRegion()
    {
        var node = new IfNode(Lit("true"), Span(Lit("\"x\"")), null);

        Assert.Equal(ContentRootKind.Region, KeyabilityResolver.ResolveRoot(node, ViewPartRegistry.Empty).Kind);
    }

    [Fact]
    public void ResolveRoot_Component_IsElement()
    {
        var node = new ComponentNode("global::X.C", EquatableArray<ComponentParameter>.Empty);

        Assert.Equal(ContentRootKind.Element, KeyabilityResolver.ResolveRoot(node, ViewPartRegistry.Empty).Kind);
    }

    [Fact]
    public void ResolveRoot_ViewPartCallToUnknown_IsUnresolved()
    {
        var node = new ViewPartCallNode(
            "K:Missing", "Missing", default, new TemplateLocation("f", default, default));

        Assert.Equal(ContentRootKind.Unresolved, KeyabilityResolver.ResolveRoot(node, ViewPartRegistry.Empty).Kind);
    }

    [Fact]
    public void CollectForEachContentDiagnostics_RegionRootedContent_EmitsBcf3003()
    {
        // ForEach whose content root is a bare If (region).
        var forEach = new ForEachNode(
            Lit("_xs"), Lit("__bcf_item_0.Id"),
            new IfNode(Lit("true"), Span(Lit("\"x\"")), null),
            new TemplateLocation("f", default, default));
        var sink = ImmutableArray.CreateBuilder<BlazorCodeFirst.Compiler.Diagnostics.DiagnosticInfo>();

        KeyabilityResolver.CollectForEachContentDiagnostics(forEach, ViewPartRegistry.Empty, sink);

        Assert.Single(sink, d => d.Id == "BCF3003");
    }

    [Fact]
    public void CollectForEachContentDiagnostics_ElementRootedContent_EmitsNothing()
    {
        var forEach = new ForEachNode(
            Lit("_xs"), Lit("__bcf_item_0.Id"),
            Span(Lit("__bcf_item_0.Name")),
            new TemplateLocation("f", default, default));
        var sink = ImmutableArray.CreateBuilder<BlazorCodeFirst.Compiler.Diagnostics.DiagnosticInfo>();

        KeyabilityResolver.CollectForEachContentDiagnostics(forEach, ViewPartRegistry.Empty, sink);

        Assert.Empty(sink);
    }

    [Fact]
    public void ResolveRoot_Fragment_IsRegion()
    {
        var node = new FragmentNode(
            ImmutableArray.Create<RenderNode>(Span(Lit("\"x\""))));
        Assert.Equal(ContentRootKind.Region, KeyabilityResolver.ResolveRoot(node, ViewPartRegistry.Empty).Kind);
    }

    [Fact]
    public void ResolveRoot_RawMarkup_IsRegion()
    {
        var node = new RawMarkupNode(Lit("\"<b>x</b>\""));
        Assert.Equal(ContentRootKind.Region, KeyabilityResolver.ResolveRoot(node, ViewPartRegistry.Empty).Kind);
    }

    [Fact]
    public void CollectForEachContentDiagnostics_FragmentContentRoot_EmitsBcf3003()
    {
        // ForEach whose content root is a Fragment (non-keyable), even wrapping a single Div.
        var forEach = new ForEachNode(
            Lit("_xs"), Lit("__bcf_item_0.Id"),
            new FragmentNode(ImmutableArray.Create<RenderNode>(
                new ElementNode("div", default, default, default,
                    ImmutableArray.Create<RenderNode>(Span(Lit("\"x\"")))))),
            new TemplateLocation("f", default, default));

        var sink = ImmutableArray.CreateBuilder<BlazorCodeFirst.Compiler.Diagnostics.DiagnosticInfo>();
        KeyabilityResolver.CollectForEachContentDiagnostics(forEach, ViewPartRegistry.Empty, sink);

        Assert.Contains(sink, d => d.Id == "BCF3003");
    }

    [Fact]
    public void CollectForEachContentDiagnostics_ForEachNestedInFragment_IsWalked()
    {
        // A Fragment child that itself holds a region-rooted ForEach must still surface BCF3003, proves the
        // walker recurses into Fragment children.
        var innerForEach = new ForEachNode(
            Lit("_ys"), Lit("__bcf_item_1.Id"),
            new IfNode(Lit("true"), Span(Lit("\"y\"")), null),
            new TemplateLocation("f", default, default));
        var root = new ElementNode("div", default, default, default,
            ImmutableArray.Create<RenderNode>(
                new FragmentNode(ImmutableArray.Create<RenderNode>(innerForEach))));

        var sink = ImmutableArray.CreateBuilder<BlazorCodeFirst.Compiler.Diagnostics.DiagnosticInfo>();
        KeyabilityResolver.CollectForEachContentDiagnostics(root, ViewPartRegistry.Empty, sink);

        Assert.Contains(sink, d => d.Id == "BCF3003");
    }

    [Fact]
    public void CollectForEachContentDiagnostics_WalksIntoComponentSlots()
    {
        var forEach = new ForEachNode(
            Lit("_items"),
            Lit("__bcf_item_0"),
            // region-rooted content
            new IfNode(Lit("true"), Span(Lit("\"x\"")), null),
            // Not `default`: a default TemplateLocation has a null FilePath, and reporting BCF3003 calls
            // ToLocation() -> Location.Create(filePath: null, …) which throws ArgumentNullException.
            // Every existing case in this file uses this same spelling.
            new TemplateLocation("f", default, default));

        var node = new ComponentNode(
            "global::X.C",
            EquatableArray<ComponentParameter>.Empty,
            ImmutableArray.Create(new ComponentSlotNode("ChildContent", forEach)));

        var sink = ImmutableArray.CreateBuilder<BlazorCodeFirst.Compiler.Diagnostics.DiagnosticInfo>();
        KeyabilityResolver.CollectForEachContentDiagnostics(node, ViewPartRegistry.Empty, sink);

        Assert.Single(sink);
        // DiagnosticInfo is symbol-free and stores only the Id string: it has no Descriptor property.
        Assert.Equal("BCF3003", sink[0].Id);
    }

    [Fact]
    public void CollectForEachContentDiagnostics_WalksIntoContextualGenericSlots()
    {
        var forEach = new ForEachNode(
            Lit("_items"),
            Lit("__bcf_item_0"),
            new IfNode(Lit("true"), Span(Lit("\"x\"")), null),
            new TemplateLocation("f", default, default));
        var slot = new ComponentSlotNode("RowTemplate", forEach)
        {
            Kind = ComponentSlotKind.GenericContextual,
            ContextTypeName = "global::System.Int32",
        };
        var node = new ComponentNode(
            "global::X.C",
            EquatableArray<ComponentParameter>.Empty,
            ImmutableArray.Create(slot));

        var sink = ImmutableArray.CreateBuilder<BlazorCodeFirst.Compiler.Diagnostics.DiagnosticInfo>();
        KeyabilityResolver.CollectForEachContentDiagnostics(node, ViewPartRegistry.Empty, sink);

        Assert.Single(sink, diagnostic => diagnostic.Id == "BCF3003");
    }

    [Fact]
    public void ResolveRoot_ComponentWithSlots_IsStillElement()
    {
        // SetKey lands right after OpenComponent, before any parameter, so slots do not affect keyability.
        var node = new ComponentNode(
            "global::X.C",
            EquatableArray<ComponentParameter>.Empty,
            ImmutableArray.Create(
                new ComponentSlotNode("ChildContent", new TextContentNode(Lit("\"x\"")))));

        Assert.Equal(ContentRootKind.Element, KeyabilityResolver.ResolveRoot(node, ViewPartRegistry.Empty).Kind);
    }
}
