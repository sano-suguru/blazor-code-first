namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ForEachTransplantTests
{
    private const string AcceptedSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };

            protected override View Body =>
                Html.ForEach(_items, x => x, x =>
                {
                    var label = x.ToUpperInvariant();
                    return Html.Span[label];
                });
        }
        """;

    private const string ExpressionSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };

            protected override View Body =>
                Html.ForEach(_items, x => x, x => Html.Span[x.ToUpperInvariant()]);
        }
        """;

    private const string MultipleReturnsSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };

            protected override View Body =>
                Html.ForEach(_items, x => x, x =>
                {
                    if (x.Length == 0)
                        return Html.Span["empty"];

                    return Html.Span[x];
                });
        }
        """;

    private const string ReservedPrefixSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };

            protected override View Body =>
                Html.ForEach(_items, x => x, x =>
                {
                    var __bcf_item_0 = x.ToUpperInvariant();
                    return Html.Span[__bcf_item_0];
                });
        }
        """;

    [Fact]
    public void ForEachContent_WhenBlockBodiedWithOneTrailingReturn_TransplantsTheStatements()
    {
        var result = CompilationTestHost.RunGenerator(AcceptedSource);
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
    public void ForEachContent_WhenBlockBodiedWithOneTrailingReturn_KeepsTheContentSequenceWidth()
    {
        // Statements emit no sequence-consuming call, so the block form must allocate the same numbers
        // the expression form does.
        var blockResult = CompilationTestHost.RunGenerator(AcceptedSource);
        var expressionResult = CompilationTestHost.RunGenerator(ExpressionSource);

        Assert.Equal(
            SequenceArguments.InTextOrder(
                Assert.Single(expressionResult.GeneratedSources).SourceText.ToString()),
            SequenceArguments.InTextOrder(
                Assert.Single(blockResult.GeneratedSources).SourceText.ToString()));
    }

    [Fact]
    public void ForEachContent_WhenBlockHasMoreThanOneReturn_ReportsBCF3004()
    {
        var result = CompilationTestHost.RunGenerator(MultipleReturnsSource);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3004");
    }

    [Fact]
    public void ForEachContent_WhenBlockDeclaresAGeneratorReservedName_ReportsBCF3004()
    {
        var result = CompilationTestHost.RunGenerator(ReservedPrefixSource);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3004");
    }
}
