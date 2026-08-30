namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ForEachTransplantTests
{
    /// <summary>A component whose <c>ForEach</c> content is <c>$CONTENT$</c>.</summary>
    private const string Host = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };

            protected override View Body => Html.ForEach(_items, x => x, $CONTENT$);
        }
        """;

    /// <summary>
    /// A component whose <c>ForEach</c> content is <c>$CONTENT$</c>, with no key: a native `if`'s
    /// content is region-rooted and cannot carry a key (BCF3003, the same rule <c>If()</c>'s content
    /// already answers to), so this host declines the key entirely rather than writing one.
    /// </summary>
    private const string NoKeyHost = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };

            protected override View Body => Html.ForEach(_items, null, $CONTENT$);
        }
        """;

    private const string BlockContent = """
        x =>
                {
                    var label = x.ToUpperInvariant();
                    return Html.Span[label];
                }
        """;

    private const string ExpressionContent = "x => Html.Span[x.ToUpperInvariant()]";

    private const string BlockIfContent = """
        x =>
                {
                    if (x == "a")
                    {
                        return Html.Span[x];
                    }
                    else
                    {
                        return Html.Span["-"];
                    }
                }
        """;

    private const string BlockSwitchContent = """
        x =>
                {
                    switch (x)
                    {
                        case "a":
                            return Html.Span[x];
                        default:
                            return Html.Span["-"];
                    }
                }
        """;

    private const string SwitchWhenClauseReferencingItemContent = """
        x =>
                {
                    switch (x)
                    {
                        case var v when v == x:
                            return Html.Span[x];
                        default:
                            return Html.Span["-"];
                    }
                }
        """;

    /// <summary>A component whose <c>ForEach</c> key is <c>$KEY$</c>.</summary>
    private const string KeyHost = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };

            protected override View Body => Html.ForEach(_items, $KEY$, x => Html.Span[x]);
        }
        """;

    private static GeneratorRunResult Run(string content) =>
        CompilationTestHost.RunGenerator(Host.Replace("$CONTENT$", content));

    private static GeneratorRunResult RunNoKey(string content) =>
        CompilationTestHost.RunGenerator(NoKeyHost.Replace("$CONTENT$", content));

    private static GeneratorRunResult RunKey(string key) =>
        CompilationTestHost.RunGenerator(KeyHost.Replace("$KEY$", key));

    [Fact]
    public void ForEachContent_WhenBlockBodiedWithOneTrailingReturn_TransplantsTheStatements()
    {
        var result = Run(BlockContent);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3004");

        // The statement is transplanted with the iteration variable substituted for the lambda parameter,
        // and `var` resolved the way every other transplanted type reference is: the generated file
        // carries no using directives.
        Assert.Contains("string label = __bcf_item_0.ToUpperInvariant();", generated);

        // The key still lands on the content root, past the statements.
        Assert.Contains("__builder.SetKey(__bcf_item_0);", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenBlockEndsInNativeIfElse_TransplantsIntoARegion()
    {
        var result = RunNoKey(BlockIfContent);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF3003" or "BCF3004");

        // The iteration variable is substituted into the condition the same way it already is into a
        // transplanted statement (the test above's assertion on `label`).
        Assert.Contains("__bcf_item_0 == \"a\"", generated);
        Assert.DoesNotContain("FragmentOf", generated);

        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenBlockEndsInNativeSwitch_TransplantsIntoARegion()
    {
        var result = RunNoKey(BlockSwitchContent);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF3003" or "BCF3004");

        // The iteration variable is substituted into the discriminant the same way it already is into a
        // transplanted statement/`if` condition.
        Assert.Contains("switch (__bcf_item_0)", generated);
        Assert.Contains("case \"a\":", generated);
        Assert.DoesNotContain("FragmentOf", generated);

        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenKeyedAndBlockEndsInNativeSwitch_ReportsBcf3003()
    {
        // A native `switch`'s content is region-rooted (KeyabilityResolver.ResolveRoot classifies
        // TransplantedSwitchNode the same way as TransplantedIfNode), so a keyed ForEach with switch
        // content has nowhere for SetKey to land -- the same rule a keyed ForEach with `if` content
        // already answers to. Run (not RunNoKey): this host writes a real key (`x => x`).
        var result = Run(BlockSwitchContent);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3003");
    }

    [Fact]
    public void ForEachContent_WhenSwitchCaseLabelReferencesTheIterationVariable_SubstitutesInsideTheLabel()
    {
        // A case label is transplanted verbatim as source text (AnalyzeSwitch), not run through
        // ExpressionTemplateFactory the way a discriminant or a condition is. A `when` clause referencing
        // the lambda parameter (`x`) therefore risks emitting the author's own name into a scope where
        // only the generator's iteration variable (`__bcf_item_0`) exists -- CS0103 if the label's
        // reference is not substituted the same way the discriminant's is.
        var result = RunNoKey(SwitchWhenClauseReferencingItemContent);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF3003" or "BCF3004");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenBlockDeconstructs_KeepsTheVarThatDesignationRequires()
    {
        // The other position the deconstruction was measured in (#342). The iteration variable is
        // substituted as it is in any other transplanted statement; what changes is the type ahead of the
        // designation list, which stays `var` because no other type can be written there.
        var result = Run("""
            x =>
                    {
                        var (a, b) = (x, x);
                        return Html.Span[a + b];
                    }
            """);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("var (a, b) = (__bcf_item_0, __bcf_item_0);", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenBlockBodiedWithOneTrailingReturn_KeepsTheContentSequenceWidth()
    {
        // Statements emit no sequence-consuming call, so the block form must allocate the same numbers
        // the expression form does.
        Assert.Equal(
            SequenceArguments.InTextOrder(
                Assert.Single(Run(ExpressionContent).GeneratedSources).SourceText.ToString()),
            SequenceArguments.InTextOrder(
                Assert.Single(Run(BlockContent).GeneratedSources).SourceText.ToString()));
    }

    [Theory]
    // Two returns: each would need a sequence space of its own, which is the wider Transplantable slice.
    [InlineData(
        "two returns",
        """
        x =>
                {
                    if (x.Length == 0)
                        return Html.Span["empty"];

                    return Html.Span[x];
                }
        """)]
    // A local spelled with the generator's reserved prefix, which the rename plan cannot carry across the
    // several templates a block becomes.
    [InlineData(
        "generator-reserved local name",
        """
        x =>
                {
                    var __bcf_item_0 = x.ToUpperInvariant();
                    return Html.Span[__bcf_item_0];
                }
        """)]
    // The builder the transplanted statements are written beside. Refused from the same reader that
    // refuses it in a design-time expression getter, so both positions hold the reserved set alike.
    [InlineData(
        "the builder's name",
        """
        x =>
                {
                    var __builder = x.ToUpperInvariant();
                    return Html.Span[__builder];
                }
        """)]
    // The same name bound by a designation. Inside the generated loop it shadowed the builder the frames
    // around it are written against, and nothing reported it (#348).
    [InlineData(
        "the builder's name through a designation",
        """
        x =>
                {
                    int.TryParse(x, out var __builder);
                    return Html.Span[x];
                }
        """)]
    public void ForEachContent_WhenBlockIsOutsideTheAcceptedShape_ReportsBCF3004(
        string shape, string content)
    {
        var result = Run(content);

        Assert.True(
            result.Diagnostics.Any(d => d.Id == "BCF3004"),
            $"{shape}: expected BCF3004, got [{string.Join(", ", result.Diagnostics.Select(d => d.Id))}].");
    }

    [Theory]
    // The builder the key call is written against: `__builder.SetKey(…)` sits in RenderView's own scope,
    // so a key body declaring that name shadows the builder it is passed to.
    [InlineData("the builder's name", """x => x is string __builder ? __builder : "k" """)]
    // The other form an expression body binds a local through. Scanning patterns alone let `out var`
    // reach the generated scope once already (#348).
    [InlineData("the builder's name through an out designation", """x => int.TryParse(x, out var __builder) ? "a" : "b" """)]
    // The generated iteration variable, which the key body is transplanted beside.
    [InlineData("a generator-reserved name", """x => x is string __bcf_item_0 ? __bcf_item_0 : "k" """)]
    public void ForEachKey_WhenItDeclaresAReservedName_ReportsBCF3004(string shape, string key)
    {
        // The key's body is transplanted into SetKey under the author's own names, so it holds the
        // reserved set every such position holds. The getter's scan stops at this lambda, which left the
        // collision to reach a generated file the author cannot edit (#413).
        var result = RunKey(key);

        Assert.True(
            result.Diagnostics.Any(d => d.Id == "BCF3004"),
            $"{shape}: expected BCF3004, got [{string.Join(", ", result.Diagnostics.Select(d => d.Id))}].");
    }

    [Fact]
    public void ForEachContent_WhenAnExpressionBodiedLambdaDeclaresTheBuildersName_ReportsBCF3004()
    {
        // An expression-bodied content lambda is transplanted into the generated loop with the author's
        // names kept, exactly as a block-bodied one is, so it holds the same reserved set. The scan reached
        // only the block arm, and this shape emitted the collision instead (#389).
        var result = Run("""x => Html.Span[x is string __builder ? __builder : "x"]""");

        Assert.True(
            result.Diagnostics.Any(d => d.Id == "BCF3004"),
            $"expected BCF3004, got [{string.Join(", ", result.Diagnostics.Select(d => d.Id))}].");
    }
}
