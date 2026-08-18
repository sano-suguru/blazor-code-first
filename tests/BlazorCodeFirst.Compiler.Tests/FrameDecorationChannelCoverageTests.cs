using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Holds every non-attribute frame decoration channel to the two contracts a per-channel field cannot
/// state for itself: it must survive <c>[ViewPart]</c> expansion, and it must stop the static fold.
/// </summary>
/// <remarks>
/// <para>
/// The channels are fields on <c>ElementTemplateNode</c> and <c>ComponentTemplateNode</c>
/// (<c>ARCHITECTURE.md</c> §2.7(E)), and each one has to be threaded by hand through
/// <c>ViewPartExpander.ExpandNode</c> and <c>StaticMarkupSerializer.IsFoldableElement</c>. Both sites fail
/// silently if a channel is missed: the expander drops it from every node with no diagnostic, and the fold
/// predicate's default for a channel it does not know is "foldable", which erases the channel from the
/// output entirely. That second one is the failure <c>IsFoldableElement</c>'s own remarks say the predicate
/// must not be able to produce.
/// </para>
/// <para>
/// Discovered reflectively rather than listed, so a sixth channel is covered by declaring it. This is the
/// same both-directions guard <c>DiagnosticTableTests</c> gives Appendix A and <c>KnownSymbolsSyncTests</c>
/// gives the curated tables: the test reads the real declaration instead of a transcription of it, so it
/// cannot be satisfied by copying the thing it checks.
/// </para>
/// <para>
/// The emitter is deliberately not covered here. Emission order (<c>SetKey</c>, parameters,
/// <c>AddComponentRenderMode</c>, the capture) is forced by <c>AssertCanAddComponentParameter</c> and is
/// not derivable from a set of channels, so it is fixed per channel by
/// <c>FrameDecorationGeneratorTests</c> instead.
/// </para>
/// </remarks>
public sealed class FrameDecorationChannelCoverageTests
{
    /// <summary>
    /// The channel properties of a template node: every <c>ExpressionTemplate?</c> the record declares as
    /// an init-only property. The positional parameters of these records are the attribute, event and
    /// child channels, which are collections and are not this rule's subject.
    /// </summary>
    private static PropertyInfo[] Channels<TNode>() =>
        [.. typeof(TNode)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static p => p.PropertyType == typeof(ExpressionTemplate) && p.CanWrite)
            .OrderBy(static p => p.Name, System.StringComparer.Ordinal)];

    private static ExpressionTemplate Marker(string code) => ExpressionTemplate.Literal(code);

    public static TheoryData<string> ElementChannelNames() =>
        new([.. Channels<ElementTemplateNode>().Select(static p => p.Name)]);

    public static TheoryData<string> ComponentChannelNames() =>
        new([.. Channels<ComponentTemplateNode>().Select(static p => p.Name)]);

    [Fact]
    public void TheChannelsAreDiscovered_SoTheGuardsBelowAreNotVacuous()
    {
        Assert.Equal(
            ["FormName", "Key", "Ref"],
            Channels<ElementTemplateNode>().Select(static p => p.Name));
        Assert.Equal(
            ["Key", "Ref", "RenderMode"],
            Channels<ComponentTemplateNode>().Select(static p => p.Name));
    }

    [Theory]
    [MemberData(nameof(ElementChannelNames))]
    public void EveryElementChannel_SurvivesExpansion(string channel)
    {
        var node = new ElementTemplateNode("div");
        Channels<ElementTemplateNode>().Single(p => p.Name == channel)
            .SetValue(node, Marker("__marker"));

        var expanded = Assert.IsType<ElementNode>(Expand(node));
        Assert.NotNull(
            typeof(ElementNode).GetProperty(channel)!.GetValue(expanded));
    }

    [Theory]
    [MemberData(nameof(ComponentChannelNames))]
    public void EveryComponentChannel_SurvivesExpansion(string channel)
    {
        var node = new ComponentTemplateNode("global::T.Card", default);
        Channels<ComponentTemplateNode>().Single(p => p.Name == channel)
            .SetValue(node, Marker("__marker"));

        var expanded = Assert.IsType<ComponentNode>(Expand(node));
        Assert.NotNull(
            typeof(ComponentNode).GetProperty(channel)!.GetValue(expanded));
    }

    /// <summary>
    /// An element carrying any channel is not foldable. The baseline in the same test is what makes this
    /// meaningful: the identical element without the channel <em>is</em> foldable, so a predicate that
    /// simply answered false for everything would not pass.
    /// </summary>
    [Theory]
    [MemberData(nameof(ElementChannelNames))]
    public void EveryElementChannel_StopsTheFold(string channel)
    {
        var foldable = new ElementNode("div");
        Assert.True(
            Generation.StaticMarkupSerializer.IsFoldable(foldable),
            "the baseline element must fold, or this guard proves nothing");

        var decorated = new ElementNode("div");
        typeof(ElementNode).GetProperty(channel)!.SetValue(decorated, Marker("__marker"));

        Assert.False(
            Generation.StaticMarkupSerializer.IsFoldable(decorated),
            $"an element carrying '{channel}' folded into markup, which drops the channel from the output");
    }

    private static RenderNode Expand(RenderTemplateNode node)
    {
        var result = Generation.ViewPartExpander.Expand(node, Analysis.ViewPartRegistry.Empty, []);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Node);
        return result.Node!;
    }
}
