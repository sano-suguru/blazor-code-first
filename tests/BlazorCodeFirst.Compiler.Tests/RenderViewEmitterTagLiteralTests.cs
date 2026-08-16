using System.Collections.Immutable;
using BlazorCodeFirst.Compiler;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The tag's trip into the literal position of <c>OpenElement</c> (#388). Driven through the emitter
/// rather than the generator on purpose: BCF3009 now rejects every tag whose spelling this escaping
/// would rescue, so no source text reaches <c>EmitElement</c> with one. What is checked here is the
/// emitter's own invariant, that a tag arrives at generated source as the string it was and not as
/// whatever C# reads its characters to mean, which has to hold whether or not another file's analyzer
/// happens to filter its callers.
/// </summary>
public sealed class RenderViewEmitterTagLiteralTests
{
    private static string EmitRoot(RenderNode root) =>
        RenderViewEmitter.Emit(new ComponentModel(
            HintName: "T.g.cs", ClassName: "T", TypeParameters: default, Namespace: null, RootNode: root)).ToString();

    /// <summary>A tag and one dynamic child, the shape that stays out of the static fold.</summary>
    private static ElementNode Element(string tag) =>
        new(
            tag,
            default,
            default,
            default,
            ImmutableArray.Create<RenderNode>(new TextContentNode(ExpressionTemplate.Literal("_x"))));

    [Fact]
    public void Emit_TagWithAQuote_EscapesItRatherThanClosingTheLiteral()
    {
        var generated = EmitRoot(Element("a\"b"));

        Assert.Contains("__builder.OpenElement(0, \"a\\\"b\");", generated);
    }

    [Fact]
    public void Emit_TagWithABackslash_EscapesItRatherThanStartingAnEscape()
    {
        var generated = EmitRoot(Element("foo\\bar"));

        Assert.Contains("__builder.OpenElement(0, \"foo\\\\bar\");", generated);
    }

    [Fact]
    public void Emit_OrdinaryTag_IsWrittenPlainly()
    {
        var generated = EmitRoot(Element("nav"));

        Assert.Contains("__builder.OpenElement(0, \"nav\");", generated);
    }
}
