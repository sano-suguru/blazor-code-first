using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class GeneratorTests
{
    // The canonical counter component used to verify generator output.
    private const string PartialCounterSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Counter : BodyComponentBase
        {
            protected override View Body => Span["Count"];
        }
        """;

    private const string DivCounterSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Counter : BodyComponentBase
        {
            private int _count = 0;

            protected override View Body =>
                Div[
                    Span[$"Count: {_count}"],
                    Button.OnClick(() => _count++)["Increment"]];
        }
        """;

    private const string NestedPartialCounterSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Outer
        {
            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Span["Count"];
            }
        }
        """;

    private const string BlockBodySource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Counter : BodyComponentBase
        {
            protected override View Body
            {
                get { return Span["Count"]; }
            }
        }
        """;

    private const string UnrecognizedChildSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Counter : BodyComponentBase
        {
            private View GetView() => Span["foo"];

            protected override View Body =>
                Div[GetView()];
        }
        """;

    [Fact]
    public void Generator_PartialComponent_EmitsRenderView()
    {
        var result = CompilationTestHost.RunGenerator(PartialCounterSource);

        var source = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains(
            "protected override void RenderView(global::Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder)",
            source);
        // The body is fully static, so it folds into one markup frame. What this test is about is that a
        // RenderView override is generated with the body's content in it at all.
        Assert.Contains("__builder.AddMarkupContent(0, \"<span>Count</span>\")", source);
    }

    [Fact]
    public void Generator_DesignTimeExpressionInDeclarationWithoutBaseList_Generates()
    {
        // A component split across declarations may put the design-time expression in the declaration
        // that carries no base list. The syntax predicate used to require a base list, so this
        // declaration was never offered to the transform and the author got a bare CS0534.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
            }
            public partial class Counter
            {
                protected override View Body => Span["Count"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains(
            "__builder.AddMarkupContent(0, \"<span>Count</span>\")", generated, StringComparison.Ordinal);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_ComponentSplitAcrossFiles_ExpressionInBaseListLessFile_Generates()
    {
        // The base-list-less declaration test above uses two declarations in one file. This confirms the
        // same shape across files: the declaration with the base list goes in one file, the one with the
        // expression in another. CompilationTestHost's multi-file RunGenerator makes this a trivial
        // addition, and it pins the cross-file behaviour that users actually hit.
        const string file1 = """
            using BlazorCodeFirst;

            public partial class SplitFiles : BodyComponentBase
            {
            }
            """;
        const string file2 = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class SplitFiles
            {
                protected override View Body => Span["split"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("File1.cs", file1),
            ("File2.cs", file2));

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains(
            "__builder.AddMarkupContent(0, \"<span>split</span>\")", generated, StringComparison.Ordinal);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_RecordDerivingFromBodyComponentBase_IsRejectedByTheLanguage()
    {
        // Widening the syntax predicate to TypeDeclarationSyntax so records are analyzed would be a dead
        // branch: a record may only inherit from object or another record (CS8864), and
        // BodyComponentBase is a class, so this shape can never compile. CS8864 names the real cause,
        // so BlazorCodeFirst deliberately adds no diagnostic of its own here.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial record Counter : BodyComponentBase
            {
                protected override View Body => Span["Count"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.OutputCompilation.GetDiagnostics(), d => d.Id == "CS8864");
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_DivWithSpanAndButton_EmitsLinearSscRenderView()
    {
        var result = CompilationTestHost.RunGenerator(DivCounterSource);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Sequence-literal builder calls in preorder
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.OpenElement(1, \"span\")", generated);
        Assert.Contains("__builder.AddContent(2, $\"Count: {_count}\")", generated);
        Assert.Contains("__builder.OpenElement(3, \"button\")", generated);
        Assert.Contains(
            "__builder.AddAttribute(4, \"onclick\", global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => _count++))",
            generated);
        Assert.Contains("__builder.AddContent(5, \"Increment\")", generated);

        // No runtime Body access, no runtime sequence variable
        Assert.DoesNotContain(".Body", generated);
        Assert.DoesNotContain("get_Body", generated);
    }

    [Fact]
    public void Generator_DivCounter_EmitsExactSource()
    {
        // Golden test: locks the full generated-source shape, indentation, blank lines, brace
        // placement, and preorder sequence: that the substring assertions elsewhere do not cover,
        // guarding the RenderViewEmitter's exact output. Line endings are normalized to '\n' because
        // the emitter uses Environment.NewLine (platform-dependent); structure must stay identical.
        var result = CompilationTestHost.RunGenerator(DivCounterSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString()
            .Replace("\r\n", "\n");

        const string expected = """
            // <auto-generated/>
            #nullable enable
            #pragma warning disable CS0219

            partial class Counter
            {
                protected override void RenderView(global::Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder)
                {
                    __builder.OpenElement(0, "div");
                    __builder.OpenElement(1, "span");
                    __builder.AddContent(2, $"Count: {_count}");
                    __builder.CloseElement();
                    __builder.OpenElement(3, "button");
                    __builder.AddAttribute(4, "onclick", global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => _count++));
                    __builder.AddContent(5, "Increment");
                    __builder.CloseElement();
                    __builder.CloseElement();
                }
            }
            """;

        Assert.Equal(expected, generated);
    }

    [Fact]
    public void Generator_NestedPartialComponent_ReportsBCF1005()
    {
        // Generating into a nested type means reproducing the enclosing type chain, which is not
        // supported. Before BCF1005 the author got zero BlazorCodeFirst diagnostics and a bare CS0534 that
        // names RenderView without ever mentioning that nesting is the cause.
        var result = CompilationTestHost.RunGenerator(NestedPartialCounterSource);

        Assert.Empty(result.GeneratedSources);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF1005");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Counter", message, StringComparison.Ordinal);
        Assert.Contains("Body", message, StringComparison.Ordinal);

        // BCF1003 is about constructs inside the expression; conflating the two would send the author
        // looking at their design-time syntax instead of at the nesting.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1003");

        // BCF1001 stays silent: the class already is partial, and partial is not the problem.
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1001");
    }

    [Fact]
    public void Generator_NestedClassInheritingWithoutDeclaringExpression_ReportsNothing()
    {
        // A nested type that merely inherits a BlazorCodeFirst base declares nothing to generate, so it must not
        // be told that nesting is a problem, same narrowing principle as BCF1001 in PR #59.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Base : BodyComponentBase
            {
                protected override View Body => Span["base"];
            }

            public partial class Outer
            {
                public partial class Leaf : Base
                {
                }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1005");
    }

    [Fact]
    public void Generator_BlockBodyGetter_GeneratesSameSourceAsExpressionBody()
    {
        // `get { return e; }` reduces to a single expression, so it must translate identically to
        // `=> e`. Comparing the generated source (not just the absence of a diagnostic) is what proves
        // the two spellings are the same program.
        const string expressionBodySource = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Span["Count"];
            }
            """;

        var blockBody = Assert.Single(CompilationTestHost.RunGenerator(BlockBodySource).GeneratedSources)
            .SourceText.ToString();
        var expressionBody = Assert.Single(
                CompilationTestHost.RunGenerator(expressionBodySource).GeneratedSources)
            .SourceText.ToString();

        Assert.Equal(expressionBody, blockBody);
    }

    [Fact]
    public void Generator_AccessorExpressionBodyGetter_GeneratesSameSourceAsExpressionBody()
    {
        const string accessorBodySource = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body { get => Span["Count"]; }
            }
            """;

        const string expressionBodySource = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Span["Count"];
            }
            """;

        var accessorBody = Assert.Single(CompilationTestHost.RunGenerator(accessorBodySource).GeneratedSources)
            .SourceText.ToString();
        var expressionBody = Assert.Single(
                CompilationTestHost.RunGenerator(expressionBodySource).GeneratedSources)
            .SourceText.ToString();

        Assert.Equal(expressionBody, accessorBody);
    }

    [Fact]
    public void Generator_PartialPropertyBody_GeneratesSameSourceAsExpressionBody()
    {
        // A partial property splits the design-time expression across two declarations: the definition
        // part has no getter body, the implementation part has the expression. GetMembers returns only
        // the definition part, so electing the declaration without following PartialImplementationPart
        // would classify this as Absent and silently generate nothing.
        const string partialPropertySource = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter
            {
                protected override partial View Body { get; }
            }
            public partial class Counter : BodyComponentBase
            {
                protected override partial View Body => Span["Count"];
            }
            """;

        const string expressionBodySource = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Span["Count"];
            }
            """;

        var partialProperty = CompilationTestHost.RunGenerator(partialPropertySource);
        CompilationTestHost.AssertOutputCompiles(partialProperty);

        var expressionBody = Assert.Single(
                CompilationTestHost.RunGenerator(expressionBodySource).GeneratedSources)
            .SourceText.ToString();

        Assert.Equal(expressionBody, Assert.Single(partialProperty.GeneratedSources).SourceText.ToString());
    }

    [Fact]
    public void Generator_MultiStatementGetter_ReportsBCF1004AtTheProperty()
    {
        // A getter with statements would need the Transplantable path, which is not implemented. The
        // point of BCF1004 is that the author is told THAT, instead of a bare CS0534 about RenderView
        // which says nothing about the getter.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body
                {
                    get
                    {
                        var label = "Count";
                        return Span[label];
                    }
                }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF1004");
        Assert.Contains("Body", diagnostic.GetMessage());
        // BCF1003 explains "the constructs inside are unanalyzable"; BCF1004 explains "the getter shape
        // is wrong". Only one of them should fire, or the author gets two contradictory fixes.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1003");

        // Diagnostics that flow through the generator's ComponentAnalysis/DiagnosticInfo round trip
        // carry a file-path-and-span Location (DiagnosticInfo.ToDiagnostic), not a live SyntaxTree
        // reference, so the located text is recovered from the original source rather than from
        // diagnostic.Location.SourceTree, which is null for that Location kind.
        var sourceText = Microsoft.CodeAnalysis.Text.SourceText.From(source);
        Assert.Equal("Body", sourceText.ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void Generator_ReAbstractedExpression_ReportsNoBlazorCodeFirstDiagnostic()
    {
        // `abstract override` is legal and has no getter body (spec F1): nothing to translate and
        // nothing to complain about. The complement-of-accepted-forms implementation would misfire here.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public abstract partial class Half : BodyComponentBase
            {
                protected abstract override View Body { get; }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");
    }

    [Fact]
    public void Generator_AutoPropertyOverride_ReportsBCF1004()
    {
        // An auto-property override is legal C# and has no getter body, so nothing can be translated.
        // CS0534 names RenderView and never mentions the auto-property that caused it, so the author has
        // no way to reach the real cause from the compiler's own error, which is exactly the asymmetry
        // BCF1004 exists for. Adding `partial` (BCF1001's remedy) does not help this shape.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body { get; } = default;
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF1004");
        Assert.Contains("Body", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void Generator_AutoPropertyOverrideWithHandWrittenRenderView_ReportsNothing()
    {
        // The same auto-property shape is CORRECT code when the author supplies RenderView themselves:
        // the design-time expression is unused, nothing needs translating, and C# reports no error at
        // all. BCF1004 here would be a false positive on working code.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            using Microsoft.AspNetCore.Components.Rendering;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body { get; } = default;

                protected override void RenderView(RenderTreeBuilder builder)
                    => builder.AddContent(0, "hand-written");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_NonPartialAutoPropertyOverride_ReportsBCF1001NotBCF1004()
    {
        // Two problems, reported one at a time: the partial check runs first, so the author sees BCF1001
        // and no BCF1004. Following it leads somewhere, after adding `partial` the author gets BCF1004
        // naming the auto property instead of a bare CS0534.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public class Counter : BodyComponentBase
            {
                protected override View Body { get; } = default;
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1001");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1004");
    }

    [Fact]
    public void Generator_PartialPropertyWithoutImplementation_ReportsNoBlazorCodeFirstDiagnostic()
    {
        // A partial property with no implementation part earns CS9248, which names the property
        // precisely ("Partial property 'Counter.Body' must have an implementation part"). BlazorCodeFirst
        // adds a diagnostic only when the compiler's own error does not name the real cause, so this
        // shape is deliberately left to CS9248 rather than double-reported as BCF1004.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                protected override partial View Body { get; }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");
        Assert.Contains(result.OutputCompilation.GetDiagnostics(), d => d.Id == "CS9248");
    }

    [Fact]
    public void Generator_PartialPropertyWithStatementBodyImplementation_ReportsBCF1004()
    {
        // A partial property's implementation part can carry a statement-bearing getter, which is legal
        // C# and still untranslatable. The classification must reach the IMPLEMENTATION part to see it,
        // reading the definition part would find `{ get; }`, classify it as NoDeclaration, and report
        // nothing at all.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                protected override partial View Body { get; }
            }
            public partial class Counter
            {
                protected override partial View Body
                {
                    get
                    {
                        var label = "Count";
                        return Span[label];
                    }
                }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1004");
    }

    [Fact]
    public async Task Generator_MultiStatementGetterWithMutation_ReportsBothBCF1004AndBCF3001()
    {
        // Two analyzers, two separate dedup paths: the generator reports BCF1004 and
        // RenderMutationAnalyzer independently reports BCF3001. Both are actionable, so both firing is
        // correct, pinned here so nobody "fixes" it into a single report.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private int _n;

                protected override View Body
                {
                    get
                    {
                        _n++;
                        return Span["x"];
                    }
                }
            }
            """;

        var generatorResult = CompilationTestHost.RunGenerator(source);
        Assert.Contains(generatorResult.Diagnostics, d => d.Id == "BCF1004");

        var analyzerDiagnostics =
            await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);
        Assert.Contains(analyzerDiagnostics, d => d.Id == "BCF3001");
    }

    [Fact]
    public void Generator_UnrecognizedChild_ReportsBCF1003AndCS0534AndNoSource()
    {
        var result = CompilationTestHost.RunGenerator(UnrecognizedChildSource);

        Assert.Empty(result.GeneratedSources);
        Assert.Single(result.OutputCompilation.GetDiagnostics(), static d => d.Id == "CS0534");
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
    }

    // -----------------------------------------------------------------------
    // If emission
    // -----------------------------------------------------------------------

    // Non-constant branch and sibling text, so the static fold leaves two frames per branch: what this test
    // is about is that the two branch ranges are disjoint, and a one-frame branch would barely show it.
    private const string IfCounterSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class IfCounter : BodyComponentBase
        {
            private bool _visible = true;
            private string _yes => "Yes";
            private string _no => "No";
            private string _always => "Always";

            protected override View Body =>
                Div[
                    If(_visible, () => Span[_yes], () => Span[_no]),
                    Span[_always]];
        }
        """;

    [Fact]
    public void Generator_IfWithBothBranches_EmitsDisjointSequenceRangesAndStableFollowingSequence()
    {
        var result = CompilationTestHost.RunGenerator(IfCounterSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Div outer element at 0
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);

        // If region boundary at 1 (one literal sequence for the conditional region)
        Assert.Contains("__builder.OpenRegion(1)", generated);

        // then branch: Span[_yes] uses [2, 3]
        Assert.Contains("__builder.OpenElement(2, \"span\")", generated);
        Assert.Contains("__builder.AddContent(3, _yes)", generated);

        // else branch: Span[_no] uses [4, 5], disjoint from then range [2, 3]
        Assert.Contains("__builder.OpenElement(4, \"span\")", generated);
        Assert.Contains("__builder.AddContent(5, _no)", generated);

        // Region close (no sequence argument)
        Assert.Contains("__builder.CloseRegion()", generated);

        // Span[_always] starts at 6, same sequence regardless of which branch ran
        Assert.Contains("__builder.OpenElement(6, \"span\")", generated);
        Assert.Contains("__builder.AddContent(7, _always)", generated);

        // All sequence numbers are compile-time literals; no runtime ++ variable
        Assert.DoesNotContain("__seq", generated);
        Assert.DoesNotContain("seqVar", generated);
    }

    private const string IfOnlyThenSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class IfThen : BodyComponentBase
        {
            private bool _show = true;

            protected override View Body =>
                If(_show, () => Span["Visible"], null);
        }
        """;

    [Fact]
    public void Generator_IfWithNullOtherwise_EmitsThenBranchOnly()
    {
        var result = CompilationTestHost.RunGenerator(IfOnlyThenSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenRegion(0)", generated);
        // The then branch is static and folds into one markup frame; the claim here is that only the then
        // branch is emitted, which the absent else below carries.
        Assert.Contains("__builder.AddMarkupContent(1, \"<span>Visible</span>\")", generated);
        Assert.Contains("__builder.CloseRegion()", generated);

        // No else branch
        Assert.DoesNotContain("else", generated);
    }

    private const string ForEachComponentSource = """
        using System.Collections.Generic;
        using static BlazorCodeFirst.Html;

        public partial class TodoPage : BlazorCodeFirst.BodyComponentBase
        {
            private readonly List<Todo> _items = new();

            protected override BlazorCodeFirst.View Body =>
                Div[
                    ForEach(_items, key: t => t.Id, content: item => Span[item.Title]),
                    Span["footer"]];

            private sealed record Todo(int Id, string Title);
        }
        """;

    [Fact]
    public void Generator_ForEachOverItems_EmitsKeyedForeachRegionWithStableFollowingSequence()
    {
        var result = CompilationTestHost.RunGenerator(ForEachComponentSource);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Div opens at 0; ForEach region at 1; content Span spans seq 2..3; SetKey consumes none.
        Assert.Contains("__builder.OpenElement(0, \"div\");", generated);
        Assert.Contains("__builder.OpenRegion(1);", generated);
        Assert.Contains("foreach (var __bcf_item_", generated);
        Assert.Contains(" in _items)", generated);
        Assert.Contains(".Id);", generated);       // SetKey uses the item
        Assert.Contains(".Title);", generated);    // content uses the item
        Assert.Contains("__builder.OpenElement(2, \"span\");", generated);
        Assert.Contains("__builder.AddContent(3,", generated);
        Assert.Contains("__builder.CloseRegion();", generated);
        // Following sibling is stable: Div(1) + ForEach region(1 + content 2) => next sibling at 4. It is
        // static, so it is one folded markup frame there rather than an element and a text frame.
        Assert.Contains("__builder.AddMarkupContent(4, \"<span>footer</span>\");", generated);

        // SetKey must land on the content root's span frame, after OpenElement(2, "span"), never
        // after OpenRegion(1). This is the regression guard for the render-time defect
        // ARCHITECTURE.md §2.7(B) records.
        int spanIdx = generated.IndexOf("__builder.OpenElement(2, \"span\");", System.StringComparison.Ordinal);
        int keyIdx = generated.IndexOf("__builder.SetKey(", System.StringComparison.Ordinal);
        Assert.True(spanIdx >= 0, "content root OpenElement should be emitted");
        Assert.True(keyIdx > spanIdx, "SetKey must be emitted after the content root's OpenElement");

        CompilationTestHost.AssertOutputCompiles(result);
    }

    private const string NestedForEachInComposableSource = """
        using System.Collections.Generic;
        using static BlazorCodeFirst.Html;

        public partial class BoardPage : BlazorCodeFirst.BodyComponentBase
        {
            private readonly List<Column> _columns = new();

            protected override BlazorCodeFirst.View Body => Section("Board", _columns);

            [BlazorCodeFirst.Composable]
            private static BlazorCodeFirst.View Section(string heading, List<Column> columns) =>
                Div[
                    Span[heading],
                    ForEach(columns, key: col => col.Id, content: col =>
                        Div[ForEach(col.Cards, key: card => card.Id, content: card =>
                            Span[$"{heading}:{col.Name}:{card.Title}"])])];

            public sealed record Card(int Id, string Title);
            public sealed record Column(int Id, string Name, List<Card> Cards);
        }
        """;

    [Fact]
    public void Generator_NestedForEachInComposable_BindsOuterParamOuterItemAndInnerItemToDistinctLocals()
    {
        var result = CompilationTestHost.RunGenerator(NestedForEachInComposableSource);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Two distinct loop variables must be emitted, one for the outer `columns` ForEach and one for
        // the inner `col.Cards` ForEach. If the generator ever collapsed these to a single shared local,
        // this would drop to 1 and fail here.
        var loopVars = Regex
            .Matches(generated, @"foreach \(var (__bcf_item_\d+) in ")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();
        Assert.Equal(2, loopVars.Length);

        // The innermost interpolation ($"{heading}:{col.Name}:{card.Title}") must resolve three distinct
        // locals: the composable arg local for `heading`, the outer item local for `col`, and the inner
        // item local for `card`. Extracting the actual identifiers used at that call site (rather than just
        // matching the member names anywhere in the file) proves the nested loops don't collide or shadow
        // one another.
        var interpolation = Regex.Match(
            generated,
            @"AddContent\(\d+,\s*\$""\{(__bcf_arg_\d+_\d+)\}:\{(__bcf_item_\d+)\.Name\}:\{(__bcf_item_\d+)\.Title\}""\);");
        Assert.True(interpolation.Success, $"Expected innermost interpolation not found in:\n{generated}");
        var outerItemLocal = interpolation.Groups[2].Value;
        var innerItemLocal = interpolation.Groups[3].Value;

        Assert.NotEqual(outerItemLocal, innerItemLocal);
        Assert.Contains(outerItemLocal, loopVars);
        Assert.Contains(innerItemLocal, loopVars);

        CompilationTestHost.AssertOutputCompiles(result);
        // No BCF3002 (every key references its own item).
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3002");
        // No BCF3003: the outer content root is now a Div, a keyable element frame.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3003");
    }

    [Fact]
    public void Generator_ForEachWithRegionRootedContent_ReportsBCF3003()
    {
        const string source = """
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;
            public partial class P : BlazorCodeFirst.BodyComponentBase
            {
                private readonly List<Group> _groups = new();
                protected override BlazorCodeFirst.View Body =>
                    ForEach(_groups, key: g => g.Id, content: g =>
                        ForEach(g.Items, key: i => i.Id, content: i => Span[i.Name]));
                private sealed record Item(int Id, string Name);
                private sealed record Group(int Id, List<Item> Items);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        // The outer content root is a bare nested ForEach (region-rooted), its key has no frame to
        // attach to, so BCF3003 fires and emission is suppressed.
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3003" && d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_RegionRootedContentComposableCalledTwice_ReportsBCF3003Once()
    {
        const string source = """
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;
            public partial class P : BlazorCodeFirst.BodyComponentBase
            {
                private readonly List<Group> _a = new();
                private readonly List<Group> _b = new();
                protected override BlazorCodeFirst.View Body =>
                    Div[Widget(_a), Widget(_b)];
                [BlazorCodeFirst.Composable]
                private static BlazorCodeFirst.View Widget(List<Group> gs) =>
                    ForEach(gs, key: g => g.Id, content: g =>
                        ForEach(g.Items, key: i => i.Id, content: i => Span[i.Name]));
                private sealed record Item(int Id, string Name);
                private sealed record Group(int Id, List<Item> Items);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        // Reported once (dedup across the two call sites), and both paths agree: BCF3003 fires AND emission
        // is suppressed (guards resolver/expander keyability from drifting apart).
        Assert.Single(result.Diagnostics, d => d.Id == "BCF3003");
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_ComponentForEachContentIsRegionRootedComposableCall_ReportsBCF3003()
    {
        // Case B: the bad ForEach is in the COMPONENT body; its content is a composable call whose body is
        // region-rooted. Transitive root-kind resolution (registry) must detect this from the component side.
        const string source = """
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;
            public partial class P : BlazorCodeFirst.BodyComponentBase
            {
                private readonly List<Group> _groups = new();
                protected override BlazorCodeFirst.View Body =>
                    ForEach(_groups, key: g => g.Id, content: g => Rows(g));
                [BlazorCodeFirst.Composable]
                private static BlazorCodeFirst.View Rows(Group g) =>
                    ForEach(g.Items, key: i => i.Id, content: i => Span[i.Name]);
                private sealed record Item(int Id, string Name);
                private sealed record Group(int Id, List<Item> Items);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3003" && d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_RegionRootedContentInUncalledComposable_StillReportsBCF3003()
    {
        const string source = """
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;
            public static class Widgets
            {
                [BlazorCodeFirst.Composable]
                public static BlazorCodeFirst.View Never(List<Group> gs) =>
                    ForEach(gs, key: g => g.Id, content: g =>
                        ForEach(g.Items, key: i => i.Id, content: i => Span[i.Name]));
                public sealed record Item(int Id, string Name);
                public sealed record Group(int Id, List<Item> Items);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Single(result.Diagnostics, d => d.Id == "BCF3003");
    }

    [Fact]
    public void Generator_ForEachAcceptsSimpleParenthesizedAndTypedLambdas_AllCompile()
    {
        const string source = """
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;

            public partial class P : BlazorCodeFirst.BodyComponentBase
            {
                private readonly List<int> _xs = new();
                protected override BlazorCodeFirst.View Body =>
                    Div[
                        ForEach(_xs, key: x => x, content: x => Span[x.ToString()]),
                        ForEach(_xs, key: (x) => x, content: (x) => Span[x.ToString()]),
                        ForEach(_xs, key: (int x) => x, content: (int x) => Span[x.ToString()])];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Single(result.GeneratedSources);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_ForEachConstantKey_ReportsBCF3002()
    {
        const string source = """
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;
            public partial class P : BlazorCodeFirst.BodyComponentBase
            {
                private readonly List<int> _xs = new();
                protected override BlazorCodeFirst.View Body =>
                    ForEach(_xs, key: _ => 0, content: x => Span[x.ToString()]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3002" && d.Severity == DiagnosticSeverity.Warning);
        // A warning must not suppress emission.
        Assert.Single(result.GeneratedSources);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_ForEachItemDerivedKey_DoesNotReportBCF3002()
    {
        const string source = """
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;
            public partial class P : BlazorCodeFirst.BodyComponentBase
            {
                private readonly List<Row> _xs = new();
                protected override BlazorCodeFirst.View Body =>
                    ForEach(_xs, key: r => r.Id, content: r => Span[r.Name]);
                private sealed record Row(int Id, string Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3002");
    }

    [Fact]
    public void Generator_NestedForEachInnerKeyReferencesOnlyOuterItem_ReportsBCF3002()
    {
        const string source = """
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;
            public partial class P : BlazorCodeFirst.BodyComponentBase
            {
                private readonly List<Group> _groups = new();
                protected override BlazorCodeFirst.View Body =>
                    ForEach(_groups, key: g => g.Id, content: g =>
                        Div[ForEach(g.Items, key: i => g.Id, content: i => Span[i.Name])]);
                private sealed record Item(int Id, string Name);
                private sealed record Group(int Id, List<Item> Items);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        // Exactly one BCF3002, the inner key references only the outer item, not its own.
        Assert.Single(result.Diagnostics, d => d.Id == "BCF3002");
    }

    [Fact]
    public void Generator_BadKeyForEachInComposableCalledTwice_ReportsBCF3002Once()
    {
        const string source = """
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;
            public partial class P : BlazorCodeFirst.BodyComponentBase
            {
                private readonly List<int> _xs = new();
                protected override BlazorCodeFirst.View Body => Div[Widget(_xs), Widget(_xs)];
                [BlazorCodeFirst.Composable]
                private static BlazorCodeFirst.View Widget(List<int> xs) =>
                    ForEach(xs, key: _ => 0, content: x => Span[x.ToString()]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Single(result.Diagnostics, d => d.Id == "BCF3002");
    }

    [Fact]
    public void Generator_BadKeyForEachInUnreachableComposable_StillReportsBCF3002()
    {
        const string source = """
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;
            public static class Widgets
            {
                [BlazorCodeFirst.Composable]
                public static BlazorCodeFirst.View Never(List<int> xs) =>
                    ForEach(xs, key: _ => 0, content: x => Span[x.ToString()]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        // Reported at definition-discovery time, independent of any call site.
        Assert.Single(result.Diagnostics, d => d.Id == "BCF3002");
    }

    [Fact]
    public void Generator_ForEachWithMethodGroupContent_ReportsBCF3004AndProducesNoSource()
    {
        const string source = """
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;
            public partial class P : BlazorCodeFirst.BodyComponentBase
            {
                private readonly List<int> _xs = new();
                protected override BlazorCodeFirst.View Body =>
                    ForEach(_xs, key: x => x, content: Render);
                private static BlazorCodeFirst.View Render(int x) => Span[x.ToString()];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        // Non-SSC content (method group, not an inline lambda): recognized ForEach, unanalyzable content.
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3004" && d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.GeneratedSources);
    }

    // -----------------------------------------------------------------------
    // Static composable call-site expansion
    // -----------------------------------------------------------------------

    [Fact]
    public void Generator_StaticComposable_ExpandsWithoutRuntimeMethodCall()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Label(string value) => Span[value];

                private string Compute() => "Count";

                protected override View Body =>
                    Div[Label(Compute()), Span["After"]];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("string __bcf_arg_1_0 = Compute();", generated);
        Assert.Contains("__builder.OpenElement(1, \"span\")", generated);
        Assert.Contains("__builder.AddContent(2, __bcf_arg_1_0)", generated);
        // The static sibling folds; it is here to show the expansion's width was accounted for, and the
        // folded frame's sequence number still says that.
        Assert.Contains("__builder.AddMarkupContent(3, \"<span>After</span>\")", generated);
        Assert.DoesNotContain("Label(", generated);
    }

    [Fact]
    public void Generator_ComposableCallArgument_NullForgiving_TranslatesInsteadOfFallingToBCF1003()
    {
        // Sibling of the design-time syntax path's FactoryArgumentBindingTests.
        // Div_NullForgivingChild_PreservesTheSuppressionInGeneratedSource: CreateInvocationArguments
        // used the same fragile `(argument.Syntax as ArgumentSyntax)?.Expression` cast the design-time
        // syntax path used to use. For a bare null-forgiving argument with nothing else to convert (Roslyn
        // elides the `!` from the operation tree), the cast failed, the method returned null, and the
        // call fell through to BCF1003 instead of translating.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private static string? NullText => null;

                [Composable]
                private static View Label(string value) => Span[value];

                protected override View Body => Label(NullText!);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1003");
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("NullText!", generated);
    }

    [Fact]
    public void Generator_SameComposableCalledTwice_UsesDistinctLocalsAndSequences()
    {
        // The arguments are property accesses, not literals, so they carry no compile-time constant and
        // the expansion locals stay live: a literal argument here would fold each call away entirely
        // (#140), leaving no locals for this test's actual point (distinct names/sequences per call site).
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private string _a => "A";
                private string _b => "B";

                [Composable]
                private static View Label(string value) => Span[value];

                protected override View Body =>
                    Div[Label(_a), Label(_b)];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Distinct locals from distinct logical preorder ordinals (Label calls are 1 and 4: the Span
        // element inside each Label body consumes an extra preorder ordinal for its own text-content
        // child, unlike the old single-node Text/Button/VStack templates).
        Assert.Contains("string __bcf_arg_1_0 = _a;", generated);
        Assert.Contains("string __bcf_arg_4_0 = _b;", generated);

        // Disjoint runtime sequence ranges for the two expanded Span nodes.
        Assert.Contains("__builder.AddContent(2, __bcf_arg_1_0)", generated);
        Assert.Contains("__builder.AddContent(4, __bcf_arg_4_0)", generated);

        Assert.DoesNotContain("Label(", generated);
    }

    [Fact]
    public void Generator_NamedArguments_EmitInSourceOrderMappedToParameterOrdinals()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Pair(string first, string second) => Span[first + second];

                private string A() => "a";
                private string B() => "b";

                protected override View Body =>
                    Pair(second: B(), first: A());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Locals are named by parameter ordinal (first=0, second=1) but are emitted in source
        // evaluation order (second is written first in source).
        AssertAppearsBefore(generated, "__bcf_arg_0_1 = B();", "__bcf_arg_0_0 = A();");

        // The body maps holes back to their parameter ordinals.
        Assert.Contains("__builder.AddContent(1, __bcf_arg_0_0 + __bcf_arg_0_1)", generated);
    }

    [Fact]
    public void Generator_UnusedSuppliedArgument_StillReceivesLocal()
    {
        // Property accesses, not literals: a constant argument here would fold the whole call away
        // (#140), and this test's point is that the unused argument still gets a local when the call
        // does not fold.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private string _keep => "keep";
                private string _drop => "drop";

                [Composable]
                private static View Ignore(string used, string unused) => Span[used];

                protected override View Body => Ignore(_keep, _drop);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("string __bcf_arg_0_0 = _keep;", generated);
        Assert.Contains("string __bcf_arg_0_1 = _drop;", generated);
        Assert.Contains("__builder.AddContent(1, __bcf_arg_0_0)", generated);
    }

    [Fact]
    public void Generator_GeneratedSource_DisablesCS0219AfterNullableDirective()
    {
        var result = CompilationTestHost.RunGenerator(PartialCounterSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The CS0219 pragma must follow the nullable directive and precede the generated declarations.
        AssertAppearsBefore(generated, "#nullable enable", "#pragma warning disable CS0219");
        AssertAppearsBefore(generated, "#pragma warning disable CS0219", "partial class Counter");
    }

    /// <summary>
    /// Both arguments are constant, including the unused one, so the whole expansion folds (#140): no
    /// locals are declared at all. Before constant propagation this call kept both locals, with the
    /// unused one relying on the file-level CS0219 pragma; folding away the declarations makes that
    /// reliance moot here rather than wrong (the mixed-constant case right below keeps it meaningful).
    /// </summary>
    [Fact]
    public void Generator_UnusedConstantComposableArgument_FoldsAway()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Ignore(string used, string unused) => Span[used];

                protected override View Body => Ignore("keep", "drop");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain("__bcf_arg_", generated);
        Assert.Contains("""__builder.AddMarkupContent(0, "<span>keep</span>");""", generated);
    }

    /// <summary>
    /// "used" is a property access (non-constant), so the expansion cannot fold as a whole and both
    /// locals stay declared; "unused" is a literal, so its local is never referenced. Roslyn's CS0219
    /// only fires for a local whose initializer is a compile-time constant (a non-constant one might
    /// have a side effect, so the compiler does not flag it as pointless to keep), which is exactly the
    /// shape this pins: without the file-level pragma, this would be CS0219 on "unused".
    /// </summary>
    [Fact]
    public void Generator_UnusedConstantArgumentBesideNonConstantOne_KeepsDeclarationWithoutCS0219Diagnostic()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private string _keep => "keep";

                [Composable]
                private static View Ignore(string used, string unused) => Span[used];

                protected override View Body => Ignore(_keep, "drop");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("string __bcf_arg_0_0 = _keep;", generated);
        Assert.Contains("string __bcf_arg_0_1 = \"drop\";", generated);
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Id == "CS0219");
    }

    [Fact]
    public void Generator_LambdaAndMethodGroupArguments_ReceiveDelegateParameterType()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Clickable(string label, System.Action onClick) => Button.OnClick(onClick)[label];

                private void Handle() { }

                protected override View Body =>
                    Div[
                        Clickable("lambda", () => Handle()),
                        Clickable("group", Handle)];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The typed local gives the lambda and method group their exact delegate target type. (Second
        // call's ordinal is 4, not 3: the Button element inside each Clickable body consumes an extra
        // preorder ordinal for its own text-content child, unlike the old single-node Button template.)
        Assert.Contains("global::System.Action __bcf_arg_1_1 = () => Handle();", generated);
        Assert.Contains("global::System.Action __bcf_arg_4_1 = Handle;", generated);
    }

    [Fact]
    public void Generator_NumericImplicitConversion_UsesDeclaredParameterType()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Sized(double width) => Span[width.ToString()];

                protected override View Body => Sized(5);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The declared parameter type (double) drives the implicit int-to-double conversion.
        Assert.Contains("double __bcf_arg_0_0 = 5;", generated);
        Assert.Contains("__builder.AddContent(1, __bcf_arg_0_0.ToString())", generated);
    }

    [Fact]
    public void Generator_ComposableCallInIfBranch_DeclaresLocalInsideBranchBraces()
    {
        // The then-branch argument is a property access, not a literal: a constant argument here would
        // fold the whole branch into one markup frame (#140), leaving no local to place inside the braces,
        // which is this test's actual point.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private bool _show = true;
                private string _label => "in-branch";

                [Composable]
                private static View Label(string value) => Span[value];

                protected override View Body =>
                    If(_show, () => Label(_label), () => Span["no"]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The expansion local must be declared inside the then branch, before the else branch.
        AssertAppearsBefore(generated, "if (_show)", "__bcf_arg_1_0 = _label;");
        AssertAppearsBefore(generated, "__bcf_arg_1_0 = _label;", "else");
        Assert.DoesNotContain("Label(", generated);
    }

    [Fact]
    public void Generator_RecursiveComposableCycle_ReportsBCF1002AndEmitsNoSource()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Ping() => Pong();

                [Composable]
                private static View Pong() => Ping();

                protected override View Body => Ping();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        Assert.Contains("cycle", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_CallToInvalidComposable_ReportsOnlyDeclarationDiagnostic()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                // Invalid declaration: a composable must be static. The call site must not add a
                // duplicate BCF1002.
                [Composable]
                private View Helper() => Span["x"];

                protected override View Body => Helper();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        Assert.Contains("must be static", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Empty(result.GeneratedSources);
    }

    // -----------------------------------------------------------------------
    // Cross-file and definition-context normalization
    // -----------------------------------------------------------------------

    [Fact]
    public void Generator_CrossFileComposable_UsesDefinitionSemanticModel()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public static class Widgets
                {
                    internal const string Prefix = "Value: ";

                    [Composable]
                    public static View Label(string value) =>
                        Span[Prefix + value];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => Widgets.Label("1");
                }
                """));

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("global::Widgets.Prefix", generated);
        Assert.DoesNotContain("Widgets.Label(", generated);
    }

    [Fact]
    public void Generator_PrivateCrossTypeMemberReference_ReportsBCF1002AtCall()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public static class Widgets
                {
                    private static string Secret() => "s";

                    [Composable]
                    public static View Label(string value) => Span[Secret() + value];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => Widgets.Label("x");
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Secret", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_ProtectedCrossTypeReference_IsValidatedAgainstInheritance()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public abstract partial class WidgetBase : BodyComponentBase
                {
                    protected static string Prefix() => "P:";

                    [Composable]
                    protected static View Label(string value) => Span[Prefix() + value];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : WidgetBase
                {
                    protected override View Body => Label("x");
                }
                """));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
        var counter = Assert.Single(
            result.GeneratedSources, static s => s.HintName.Contains("Counter"));
        var generated = counter.SourceText.ToString();
        Assert.Contains("global::WidgetBase.Prefix", generated);
        Assert.DoesNotContain("Label(", generated);
    }

    [Fact]
    public void Generator_ProtectedCrossTypeReferenceFromUnrelatedComponent_ReportsBCF1002AtCall()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public abstract class WidgetBase
                {
                    protected static string Prefix() => "P:";

                    [Composable]
                    public static View Label(string value) => Span[Prefix() + value];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => WidgetBase.Label("x");
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Prefix", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_ProtectedInternalCrossTypeReference_IsAccessibleThroughInternalHalf()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public abstract class WidgetBase
                {
                    protected internal static string Prefix() => "P:";

                    [Composable]
                    public static View Label(string value) => Span[Prefix() + value];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => WidgetBase.Label("x");
                }
                """));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("global::WidgetBase.Prefix", generated);
        Assert.DoesNotContain("Label(", generated);
    }

    [Fact]
    public void Generator_PrivateProtectedCrossTypeReferenceFromUnrelatedComponent_ReportsBCF1002AtCall()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public abstract class WidgetBase
                {
                    private protected static string Prefix() => "P:";

                    [Composable]
                    public static View Label(string value) => Span[Prefix() + value];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => WidgetBase.Label("x");
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Prefix", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_MetadataOnlyComposable_ReportsBCF1002AtCall()
    {
        var libraryReference = CompilationTestHost.CompileToMetadataReference("""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public static class ExternalWidgets
            {
                [Composable]
                public static View Badge(string text) => Span[text];
            }
            """,
            "ExternalWidgets");

        var compilation = CompilationTestHost.CreateCompilation(
            [
                ("Counter.cs", """
                    using BlazorCodeFirst;
                    using static BlazorCodeFirst.Html;

                    public partial class Counter : BodyComponentBase
                    {
                        protected override View Body => ExternalWidgets.Badge("hi");
                    }
                    """),
            ],
            libraryReference);

        var result = CompilationTestHost.RunGenerator(compilation);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        Assert.Contains("no source declaration", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_UnnameableParameterType_ReportsBCF1002AndEmitsNoSource()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            file class Hidden { }

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Show(Hidden h) => Span["x"];

                protected override View Body => Show(new Hidden());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        Assert.Contains("cannot be named", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_NestedComposableCalls_ExpandTransitively()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Inner(string value) => Span[value];

                [Composable]
                private static View Outer(string value) => Div[Inner(value), Span["tail"]];

                protected override View Body => Outer("hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain("Outer(", generated);
        Assert.DoesNotContain("Inner(", generated);
        // "hi" is constant, and the lone-hole substitution rule (#140) carries it through both levels of
        // expansion: Outer's own local, Inner's argument, and Inner's local are all constant, so nothing
        // is left to declare and the whole tree, both composable calls plus the trailing Span, folds into
        // one frame.
        Assert.DoesNotContain("__bcf_arg_", generated);
        Assert.Contains(
            """__builder.AddMarkupContent(0, "<div><span>hi</span><span>tail</span></div>");""",
            generated);
    }

    [Fact]
    public void Generator_DirectRecursion_ReportsSingleBCF1002()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Loop() => Loop();

                protected override View Body => Loop();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("cycle", message);
        Assert.Contains("Loop -> Loop", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_IndirectRecursion_ReportsSingleBCF1002WithCallChain()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Ping() => Pong();

                [Composable]
                private static View Pong() => Ping();

                protected override View Body => Ping();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("cycle", message);
        Assert.Contains("Ping -> Pong -> Ping", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_ParameterShadowingInsideNestedLambda_IsNotReplaced()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Echo(string value) =>
                    Span[((System.Func<string, string>)(value => value))(value)];

                protected override View Body => Echo("hi");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The shadowing lambda parameter keeps its source name ...
        Assert.Contains("value => value", generated);
        // ... while the composable parameter reference becomes the typed local.
        Assert.Contains("__bcf_arg_0_0", generated);
        Assert.DoesNotContain("Echo(", generated);
    }

    [Fact]
    public void Generator_NameofParameter_EmitsCompileTimeConstantString()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Named(string value) => Span[nameof(value)];

                protected override View Body => Named("x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The parameter 'value' does not exist after expansion, so nameof(value) must collapse to its
        // compile-time constant string rather than reference an out-of-scope name. Being a constant, it
        // folds into the markup, which is itself the strongest statement that it collapsed.
        Assert.Contains("__builder.AddMarkupContent(0, \"<span>value</span>\")", generated);
        Assert.DoesNotContain("nameof(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_NameofParameterMember_EmitsMemberNameConstant()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View NamedMember(string value) => Span[nameof(value.Length)];

                protected override View Body => NamedMember("x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // nameof(value.Length) evaluates to "Length"; the vanished parameter must not survive as text.
        Assert.Contains("__builder.AddMarkupContent(0, \"<span>Length</span>\")", generated);
        Assert.DoesNotContain("value", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_NameofNonParameter_CollapsesToCompileTimeConstantString()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View NamedType(string value) => Span[nameof(System.String) + value];

                protected override View Body => NamedType("x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Every nameof collapses to its compile-time constant string; nameof(System.String) is "String".
        Assert.Contains("\"String\" + __bcf_arg_0_0", generated);
        Assert.DoesNotContain("nameof(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_NameofUsingImportedType_CollapsesToConstantAndCompiles()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            using System.Text;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Named() => Span[nameof(StringBuilder)];

                protected override View Body => Named();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // 'StringBuilder' is only in scope through 'using System.Text;', which the generated file lacks;
        // the nameof must collapse to its constant string rather than reference an out-of-scope type.
        Assert.Contains("__builder.AddMarkupContent(0, \"<span>StringBuilder</span>\")", generated);
        Assert.DoesNotContain("nameof(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_NameofPrivateDefinitionMember_CollapsesToConstantAndCompiles()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public static class Widgets
                {
                    private static readonly string _secret = "s";

                    // Referenced so the private field is not reported as unused; the composable itself
                    // only names it through nameof.
                    public static string Reveal() => _secret;

                    [Composable]
                    public static View Show() => Span[nameof(_secret)];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => Widgets.Show();
                }
                """));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // '_secret' is a private field of Widgets and does not exist inside Counter's generated RenderView;
        // nameof(_secret) must collapse to its constant string so it never references the vanished member.
        Assert.Contains("__builder.AddMarkupContent(0, \"<span>_secret</span>\")", generated);
        Assert.DoesNotContain("nameof(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_InterpolatedParameterReference_IsReplaced()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Greet(string value) => Span[$"Hi {value}!"];

                protected override View Body => Greet("x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("$\"Hi {__bcf_arg_0_0}!\"", generated);
    }

    [Fact]
    public void Generator_OptionalEnumDefault_IsFullyQualified()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public enum Color { Red, Green }

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Swatch(Color c = Color.Green) => Span[c.ToString()];

                protected override View Body => Swatch();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("global::Color __bcf_arg_0_0 = (global::Color)1;", generated);
    }

    [Fact]
    public void Generator_OptionalValueTypeDefault_EmitsDefaultOfFullyQualifiedType()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public struct Box { public int V; }

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Show(Box b = default) => Span[b.V.ToString()];

                protected override View Body => Show();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("global::Box __bcf_arg_0_0 = default(global::Box);", generated);
    }

    // -----------------------------------------------------------------------
    // Fractional and special floating-point / decimal optional defaults
    // -----------------------------------------------------------------------

    [Fact]
    public void Generator_OptionalSingleDefault_EmitsFloatSuffixedLiteral()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Scaled(float scale = 1.5f) => Span[scale.ToString()];

                protected override View Body => Scaled();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // A bare 1.5 is a double literal and cannot initialize a float local (CS0664); the 'F' suffix
        // makes the constant round-trip into the declared parameter type.
        Assert.Contains("float __bcf_arg_0_0 = 1.5F;", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_OptionalDecimalDefault_EmitsDecimalSuffixedLiteral()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Priced(decimal amount = 1.5m) => Span[amount.ToString()];

                protected override View Body => Priced();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // A bare 1.5 is a double literal and cannot initialize a decimal local (CS0664); the 'M' suffix
        // makes the constant round-trip into the declared parameter type.
        Assert.Contains("decimal __bcf_arg_0_0 = 1.5M;", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_OptionalSpecialFloatingPointDefaults_EmitTypedConstants()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Special(
                    float f = float.NaN,
                    double d = double.PositiveInfinity,
                    double n = double.NegativeInfinity) =>
                    Span[f.ToString() + d.ToString() + n.ToString()];

                protected override View Body => Special();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("float __bcf_arg_0_0 = float.NaN;", generated);
        Assert.Contains("double __bcf_arg_0_1 = double.PositiveInfinity;", generated);
        Assert.Contains("double __bcf_arg_0_2 = double.NegativeInfinity;", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    // -----------------------------------------------------------------------
    // Accessibility of member-access (qualified) non-public references
    // -----------------------------------------------------------------------

    [Fact]
    public void Generator_PrivateMemberAccessReferenceFromUnrelatedComponent_ReportsBCF1002AtCall()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public static class Widgets
                {
                    private static string Secret() => "s";

                    // Qualified member access to a private member: legal here, but inaccessible once
                    // inlined into an unrelated component.
                    [Composable]
                    public static View Label(string value) => Span[Widgets.Secret() + value];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => Widgets.Label("x");
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Secret", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_ProtectedMemberAccessReferenceFromUnrelatedComponent_ReportsBCF1002AtCall()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public abstract class WidgetBase
                {
                    protected static string Prefix() => "P:";

                    [Composable]
                    public static View Label(string value) => Span[WidgetBase.Prefix() + value];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => WidgetBase.Label("x");
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Prefix", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_PrivateMemberAccessReferenceFromSameType_ExpandsAndCompiles()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Widget : BodyComponentBase
            {
                private static string Secret() => "s";

                [Composable]
                private static View Label(string value) => Span[Widget.Secret() + value];

                protected override View Body => Label("x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("global::Widget.Secret()", generated);
        Assert.DoesNotContain("Label(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_ProtectedMemberAccessReferenceFromDerivedComponent_ExpandsAndCompiles()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public abstract partial class WidgetBase : BodyComponentBase
                {
                    protected static string Prefix() => "P:";

                    [Composable]
                    protected static View Label(string value) => Span[WidgetBase.Prefix() + value];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : WidgetBase
                {
                    protected override View Body => Label("x");
                }
                """));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
        var counter = Assert.Single(
            result.GeneratedSources, static s => s.HintName.Contains("Counter"));
        var generated = counter.SourceText.ToString();
        Assert.Contains("global::WidgetBase.Prefix()", generated);
        Assert.DoesNotContain("Label(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    // -----------------------------------------------------------------------
    // Using-dependent qualification: generic type/static references and
    // extension methods invoked in instance syntax
    // -----------------------------------------------------------------------

    [Fact]
    public void Generator_ComposableGenericTypeAndExtensionMethod_QualifyInUsinglessOutput()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            using System.Collections.Generic;
            using System.Linq;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Head(IEnumerable<string> items) => Span[items.First()];

                protected override View Body => Head(new List<string> { "a" });
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The 'items.First()' extension call, only in scope through 'using System.Linq;', normalizes to a
        // fully qualified static call, and the generic 'List<string>' argument is fully qualified.
        Assert.Contains("global::System.Linq.Enumerable.First", generated);
        Assert.Contains(
            "global::System.Collections.Generic.IEnumerable<string> __bcf_arg_0_0 = " +
            "new global::System.Collections.Generic.List<string> { \"a\" };",
            generated);
        Assert.DoesNotContain(".First()", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_ComposableUnqualifiedGenericStaticMethodAndGenericType_QualifyInUsinglessOutput()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            using static Factory;
            using System.Collections.Generic;

            public static class Factory
            {
                public static string Make<T>() => typeof(T).Name;
            }

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Head() =>
                    Span[Make<string>() + new Dictionary<string, int>().Count.ToString()];

                protected override View Body => Head();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The unqualified generic static call and generic type reference, both in scope only through
        // 'using' directives the generated file lacks, are fully qualified while preserving type arguments.
        Assert.Contains("global::Factory.Make<string>()", generated);
        Assert.Contains("global::System.Collections.Generic.Dictionary<string, int>", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_ComponentBodyGenericTypeAndExtensionMethod_QualifyAndCompile()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            using System.Collections.Generic;
            using System.Linq;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Span[(new List<string> { "a" }).First()];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The shared analyzer also serves component Body expressions, so the same using-dependent generic
        // type and instance-syntax extension call must qualify and compile there too.
        Assert.Contains("global::System.Linq.Enumerable.First", generated);
        Assert.Contains("new global::System.Collections.Generic.List<string> { \"a\" }", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_ComposableExtensionMethodWithUnnameableTypeArgument_ReportsBCF1002()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            using System.Collections.Generic;
            using System.Linq;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Head(IEnumerable<string> items) =>
                    Span[items.Select(x => new { N = x }).First().N];

                protected override View Body => Head(new List<string> { "a" });
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        // The reduced 'Select' fixes an anonymous type argument that cannot be named in generated component
        // code, so normalization is not semantics-preserving and a precise BCF1002 is reported instead.
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("cannot be named", message);
        Assert.Empty(result.GeneratedSources);
    }

    // -----------------------------------------------------------------------
    // By-reference [Composable] parameters (ref / out / in / ref readonly)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("ref int value")]
    [InlineData("out int value")]
    [InlineData("in int value")]
    [InlineData("ref readonly int value")]
    public void Generator_ByReferenceComposableParameter_ReportsBCF1002AndEmitsNoInvalidOutput(string parameter)
    {
        var source = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Helper({{parameter}}) => Span["x"];

                protected override View Body => Span["Body"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        // A by-reference parameter rejects the declaration with exactly one BCF1002 and one reason.
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        Assert.Contains(
            "by-reference parameters are unsupported",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));

        // The rejected composable is never expanded, so the component's own valid Body still emits
        // while nothing references the invalid Helper: no broken generated output is produced.
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.DoesNotContain("Helper", generated);
        Assert.Contains("__builder.AddMarkupContent(0, \"<span>Body</span>\")", generated);
    }

    // -----------------------------------------------------------------------
    // Namespace-relative type / static references qualify across namespaces
    // -----------------------------------------------------------------------

    [Fact]
    public void Generator_CrossNamespaceRelativeTypeReference_QualifiesAndCompiles()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace Root.Models
                {
                    public sealed class Widget
                    {
                        public static string Label => "widget";
                    }

                    public sealed class Box<T>
                    {
                        public static string Describe => typeof(T).Name;
                    }
                }

                namespace Root.Features
                {
                    public static class Widgets
                    {
                        // 'Models.Widget' / 'Models.Box<int>' resolve lexically to 'Root.Models.*'
                        // through the shared 'Root' parent, but the using-less generated RenderView
                        // cannot resolve the relative 'Models' path, so it must be fully qualified on
                        // expansion while any written type arguments are preserved.
                        [Composable]
                        public static View Show() =>
                            Span[Models.Widget.Label + typeof(Models.Widget).Name + Models.Box<int>.Describe];
                    }
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;
                using Root.Features;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => Widgets.Show();
                }
                """));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The static-member access, the typeof type reference, and the generic type reference, all
        // written under the relative 'Models' path, are fully qualified to 'global::Root.Models.*',
        // and the generic type argument survives, so expansion into the global namespace compiles.
        Assert.Contains("global::Root.Models.Widget.Label", generated);
        Assert.Contains("typeof(global::Root.Models.Widget)", generated);
        Assert.Contains("global::Root.Models.Box<int>.Describe", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    // -----------------------------------------------------------------------
    // Component Body normalization diagnostics surface as BCF1002
    // -----------------------------------------------------------------------

    [Fact]
    public void Generator_ComponentBodyWithUnnormalizableExtensionReceiver_ReportsBCF1002AndEmitsNoSource()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            using System.Collections.Generic;
            using System.Linq;

            public partial class Counter : BodyComponentBase
            {
                private IEnumerable<string> _items = new List<string> { "a" };

                // A null-conditional extension receiver cannot be rewritten to a static call without
                // changing short-circuit semantics, so Body normalization reports BCF1002.
                protected override View Body => Span[_items?.First()];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        // The Body path surfaces exactly one BCF1002 (previously the diagnostic was discarded).
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        Assert.Contains("cannot be normalized", diagnostic.GetMessage(CultureInfo.InvariantCulture));

        // Emission is suppressed rather than producing a broken RenderView.
        Assert.Empty(result.GeneratedSources);

        // The only remaining consequence is the expected abstract-member gap (CS0534); there is no
        // secondary generator error from broken generated source.
        var error = Assert.Single(
            result.OutputCompilation.GetDiagnostics(),
            static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("CS0534", error.Id);
    }

    // -----------------------------------------------------------------------
    // Composables in generic containing types are rejected (would leak 'T')
    // -----------------------------------------------------------------------

    [Fact]
    public void Generator_ComposableInGenericContainingTypeWithTypeParameter_ReportsBCF1002AndEmitsNoSource()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public class Widgets<T>
                {
                    // A composable declared in a generic containing type would leak the unbound type
                    // parameter 'T', here through the 'T value' parameter, into the using-less
                    // generated component, so the declaration is rejected up front.
                    [Composable]
                    public static View Show(T value) => Span[value!.ToString()];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => Widgets<int>.Show(5);
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Show", message);
        Assert.Contains("containing type must be non-generic", message);

        // The rejected composable never expands, so no broken component source (which would leak the
        // unbound 'T') is produced.
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_ComposableInGenericContainingTypeReferencingTypeParameter_ReportsBCF1002AndEmitsNoSource()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public class Widgets<T>
                {
                    // Even a parameterless composable leaks 'T' through its body (typeof(T)); the
                    // generic containing type is rejected regardless of the parameter list.
                    [Composable]
                    public static View Show() => Span[typeof(T).Name];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => Widgets<int>.Show();
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Show", message);
        Assert.Contains("containing type must be non-generic", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_ComposableInGenericEnclosingType_ReportsBCF1002AndEmitsNoSource()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public class Outer<T>
                {
                    // The composable's immediate containing type is non-generic, but an *enclosing*
                    // containing type is generic, so 'T' would still leak: the declaration is rejected.
                    public static class Inner
                    {
                        [Composable]
                        public static View Show() => Span[typeof(T).Name];
                    }
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => Outer<int>.Inner.Show();
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Show", message);
        Assert.Contains("containing type must be non-generic", message);
        Assert.Empty(result.GeneratedSources);
    }

    // -----------------------------------------------------------------------
    // Access requirements are keyed on the referenced member's declaring type,
    // not the composable definition's containing type
    // -----------------------------------------------------------------------

    [Fact]
    public void Generator_ProtectedBaseMemberReferencedFromHelperType_ExpandsIntoDerivedComponent()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public abstract partial class WidgetBase : BodyComponentBase
                {
                    protected static string Prefix() => "P:";
                }
                """),
            ("Helper.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                // The composable lives in a *different* type than the one declaring the protected
                // member. It can name 'Prefix' because Helper derives from WidgetBase, so the access
                // requirement must be validated against the member's declaring type (WidgetBase), not
                // the composable's containing type (Helper).
                public abstract partial class Helper : WidgetBase
                {
                    [Composable]
                    public static View Label(string value) => Span[Prefix() + value];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : WidgetBase
                {
                    protected override View Body => Helper.Label("x");
                }
                """));

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
        var counter = Assert.Single(
            result.GeneratedSources, static s => s.HintName.Contains("Counter"));
        var generated = counter.SourceText.ToString();
        Assert.Contains("global::WidgetBase.Prefix()", generated);
        Assert.DoesNotContain("Label(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_ProtectedBaseMemberReferencedFromHelperTypeIntoUnrelatedComponent_ReportsBCF1002()
    {
        var result = CompilationTestHost.RunGenerator(
            ("WidgetBase.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public abstract partial class WidgetBase : BodyComponentBase
                {
                    protected static string Prefix() => "P:";
                }
                """),
            ("Helper.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public abstract partial class Helper : WidgetBase
                {
                    [Composable]
                    public static View Label(string value) => Span[Prefix() + value];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                // Counter does not derive from WidgetBase, so it cannot legally name the protected
                // 'Prefix'; the requirement (keyed on WidgetBase) is correctly unsatisfied here.
                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => Helper.Label("x");
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Prefix", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_SingleClassOnSpan_EmitsClassAttribute()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private string _badge => "badge";
                protected override View Body => Span.Class(_badge)["Hi"];
            }
            """);

        // Non-constant class, so the span is not folded away: the class attribute frame is the point.
        var source = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("__builder.OpenElement(0, \"span\");", source);
        Assert.Contains("__builder.AddAttribute(1, \"class\", _badge);", source);
        Assert.Contains("__builder.AddContent(2, \"Hi\");", source);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_ChainedClassesOnSpan_FoldIntoOneAttributeInSourceOrder()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private string _a => "a";
                private string _b => "b";
                protected override View Body => Span.Class(_a).Class(_b)["Hi"];
            }
            """);

        // Non-constant classes: what is pinned here is the concatenation expression the class channel emits,
        // and that expression exists only when the element is not folded.
        var source = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("__builder.AddAttribute(1, \"class\", (_a) + \" \" + (_b));", source);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_DynamicClassOnButton_EmitsClassBeforeOnclick()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private bool _on;
                protected override View Body => Button.OnClick(() => _on = !_on).Class(_on ? "on" : "off")["OK"];
            }
            """);

        var source = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("__builder.AddAttribute(1, \"class\", _on ? \"on\" : \"off\");", source);
        Assert.Contains("__builder.AddAttribute(2, \"onclick\", ", source);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_ClassOnForEachItemContent_SubstitutesLoopVariableHole()
    {
        var result = CompilationTestHost.RunGenerator("""
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private readonly List<string> _rows = new();
                protected override View Body =>
                    ForEach(_rows, key: r => r, content: r => Span.Class(r)[r]);
            }
            """);

        var source = Assert.Single(result.GeneratedSources).SourceText.ToString();
        // The class hole is substituted with the generated loop variable, not left unbound.
        Assert.Contains("\"class\", __bcf_item_", source);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_ClassOnComposableArgument_SubstitutesArgumentHole()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Div[Chip("hot")];

                [Composable]
                private static View Chip(string c) => Span.Class(c)["chip"];
            }
            """);

        var source = Assert.Single(result.GeneratedSources).SourceText.ToString();
        // "hot" is constant, so the class hole substitution (#140) carries it into the class attribute
        // and the whole call folds to one markup frame: an unbound hole would leave the class template
        // unable to serialize (StaticMarkupSerializer requires a constant), so a compiling, folded output
        // proves the composable-argument class hole was bound.
        Assert.Contains(
            """__builder.AddMarkupContent(0, "<div><span class=\"hot\">chip</span></div>");""",
            source);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_ClassOnComponent_IsRejectedByTypeSystem()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public sealed class Widget : ComponentBase { }

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Component<Widget>().Class("x");
            }
            """);

        // ComponentView<T> has no .Class, and extension resolution does not cross the user-defined
        // implicit conversion to View, so this is a plain C# error, not a generator diagnostic.
        Assert.Contains(result.OutputCompilation.GetDiagnostics(), d => d.Id == "CS1929");
    }

    [Fact]
    public void Generator_ClassOnRenderFragment_IsRejectedByTypeSystem()
    {
        // PR #50 deliberately added no diagnostic for decorating a RenderFragment, on the grounds that
        // C# does not apply a user-defined implicit conversion to an extension-method receiver, so
        // fragment.Class("x") does not compile in the first place. Nothing pinned that until now: if a
        // decoration were ever added as an instance member on View, this would silently start compiling
        // and reach the decoration branch instead.
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public partial class Card : BodyComponentBase
            {
                [Parameter]
                public RenderFragment? ChildContent { get; set; }

                protected override View Body => Div[ChildContent.Class("x")];
            }
            """);

        Assert.Contains(result.OutputCompilation.GetDiagnostics(), d => d.Id == "CS1929");
    }

    [Fact]
    public void Generator_GenericRenderFragmentAsChild_IsRejectedByTypeSystem()
    {
        // The implicit conversion to View is from the non-generic RenderFragment only. A
        // RenderFragment<T> is a template needing a context argument, so it must not silently become
        // content, the type system rejects it with CS1503 and no diagnostic of ours is needed.
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public partial class Card : BodyComponentBase
            {
                [Parameter]
                public RenderFragment<string>? Template { get; set; }

                protected override View Body => Div[Template];
            }
            """);

        Assert.Contains(result.OutputCompilation.GetDiagnostics(), d => d.Id == "CS1503");
    }

    [Fact]
    public void Generator_RenderFragmentParameterAsChild_EmitsAddContent()
    {
        const string source = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public partial class Card : BodyComponentBase
            {
                [Parameter] public RenderFragment? ChildContent { get; set; }
                protected override View Body => Div[ChildContent];
            }
            """;

        var generated = Assert.Single(CompilationTestHost.RunGenerator(source).GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"div\");", generated);
        Assert.Contains("__builder.AddContent(1, ChildContent);", generated);
    }

    [Fact]
    public void Generator_RenderFragmentReturningMethodCall_EmitsAddContent()
    {
        // Regression guard for branch placement: the RenderFragment check must run BEFORE the
        // `is not InvocationExpressionSyntax` guard, otherwise a method call returning RenderFragment
        // is neither design-time syntax nor a [Composable] call and falls through to BCF1003.
        const string source = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public partial class Host : BodyComponentBase
            {
                private RenderFragment MakeHeader() => b => b.AddContent(0, "h");
                protected override View Body => Div[MakeHeader()];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.AddContent(1, MakeHeader());", generated);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void Generator_RenderFragmentMixedWithText_EmitsBothAsContent()
    {
        const string source = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public partial class Mixed : BodyComponentBase
            {
                [Parameter] public RenderFragment? Slot { get; set; }
                protected override View Body => Div["before", Slot, "after"];
            }
            """;

        var generated = Assert.Single(CompilationTestHost.RunGenerator(source).GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.AddContent(1, \"before\");", generated);
        Assert.Contains("__builder.AddContent(2, Slot);", generated);
        Assert.Contains("__builder.AddContent(3, \"after\");", generated);
    }

    [Fact]
    public void Generator_RenderFragmentInIfBranch_EmitsAddContent()
    {
        const string source = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public partial class Conditional : BodyComponentBase
            {
                [Parameter] public RenderFragment? Slot { get; set; }
                private bool _show;
                protected override View Body => Div[If(_show, () => Slot)];
            }
            """;

        var generated = Assert.Single(CompilationTestHost.RunGenerator(source).GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.AddContent(2, Slot);", generated);
    }

    [Fact]
    public void Generator_RenderFragmentAsForEachContentRoot_ReportsBCF3003()
    {
        const string source = """
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public partial class Listing : BodyComponentBase
            {
                [Parameter] public RenderFragment? Slot { get; set; }
                private static readonly List<int> Items = [1, 2];
                protected override View Body => Div[ForEach(Items, key: i => i, content: i => Slot)];
            }
            """;

        var diagnostics = CompilationTestHost.RunGenerator(source).Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3003");
    }

    [Fact]
    public void Generator_RenderFragmentAsComposableParameter_SubstitutesHole()
    {
        const string source = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public partial class Panel : BodyComponentBase
            {
                [Composable]
                private static View Framed(RenderFragment? inner) => Div[inner];

                [Parameter] public RenderFragment? Slot { get; set; }
                protected override View Body => Framed(Slot);
            }
            """;

        var generated = Assert.Single(CompilationTestHost.RunGenerator(source).GeneratedSources).SourceText.ToString();

        Assert.Contains(
            "global::Microsoft.AspNetCore.Components.RenderFragment __bcf_arg_0_0 = Slot;",
            generated);
        Assert.Contains("__builder.OpenElement(0, \"div\");", generated);
        Assert.Contains("__builder.AddContent(1, __bcf_arg_0_0);", generated);
    }

    [Fact]
    public void Generator_TwoDeclarationsBothDeclaringBody_DoesNotKillTheGenerator()
    {
        // Two declarations that each carry a Body getter body is CS0102 (broken user code), but the
        // generator must still contribute source for every OTHER component in the compilation. Before
        // the declaration-election fix, both declarations produced hintName "Dup.g.cs", the second
        // AddSource threw ArgumentException, and Roslyn reported CS8785, at which point the generator
        // contributes NOTHING, so the unrelated and perfectly valid Healthy lost its RenderView too.
        // An exception from AddSource discards the generator's entire contribution for that run, which
        // is why the blast radius is every component in the compilation rather than just the colliding
        // one. The assertion that Healthy generates holds regardless of which declaration is processed
        // first, because Roslyn discards ALL sources on the throw (measured on main: GeneratedSources
        // is empty even when Healthy would have been emitted before the collision).
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Dup : BodyComponentBase
            {
                protected override View Body => Span["a"];
            }
            public partial class Dup : BodyComponentBase
            {
                protected override View Body => Span["b"];
            }
            public partial class Healthy : BodyComponentBase
            {
                protected override View Body => Span["h"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        // CS8785 is a generator-level failure, not a component diagnostic: it arrives in the driver's
        // own diagnostics as a Warning.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "CS8785");

        // The blast radius is what matters: Healthy is untouched by the user's mistake.
        Assert.Contains(result.GeneratedSources, g => g.HintName == "Healthy.g.cs");
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            d => d.Id == "CS0534" && d.GetMessage(CultureInfo.InvariantCulture).Contains("Healthy", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_TwoDeclarationsBothDeclaringBody_HealthyFirst_DoesNotKillTheGenerator()
    {
        // Same as the previous test but with Healthy declared before the colliding Dup pair, pinning
        // order-independence by construction. An exception from AddSource discards the generator's
        // entire contribution, so the outcome is identical regardless of which declaration is processed
        // first: no CS8785, and Healthy still generates.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Healthy : BodyComponentBase
            {
                protected override View Body => Span["h"];
            }
            public partial class Dup : BodyComponentBase
            {
                protected override View Body => Span["a"];
            }
            public partial class Dup : BodyComponentBase
            {
                protected override View Body => Span["b"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.GeneratedSources, g => g.HintName == "Healthy.g.cs");
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            d => d.Id == "CS0534" && d.GetMessage(CultureInfo.InvariantCulture).Contains("Healthy", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_HandWrittenRenderView_GeneratesNothingAndDoesNotConflict()
    {
        // Overriding RenderView by hand is legal and is the documented escape hatch for a body the SSC
        // path cannot express. The generator must then stay out of the way: emitting its own RenderView
        // next to the author's produces CS0111 from inside generated code, which the author cannot fix
        // without deleting their own method.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            using Microsoft.AspNetCore.Components.Rendering;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Span["Count"];

                protected override void RenderView(RenderTreeBuilder builder)
                    => builder.AddContent(0, "hand-written");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_RenderViewOverrideWithWrongParameterType_StillGenerates()
    {
        // Roslyn reports IsOverride=true even for an erroneous override, so a gate keyed only on
        // "declares an override named RenderView with one parameter" would be tripped by this shape,
        // suppress generation, and leave the author with CS0115 plus a bare CS0534, the dead end this
        // PR exists to remove. The generator must still emit, so the author's only error is the CS0115
        // that names their own mistake.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Span["Count"];

                protected override void RenderView(string builder) { }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Single(result.GeneratedSources);
        Assert.Contains(result.OutputCompilation.GetDiagnostics(), d => d.Id == "CS0115");
    }

    [Fact]
    public void Generator_GenericComponent_EmitsTypeParametersInTheClassHeader()
    {
        // The generated part must be the SAME type as the user's declaration. Emitting `partial class
        // Gen` for `Gen<T>` declares a different, non-generic type, so the user's class keeps the
        // abstract RenderView (CS0534) and the generated override has nothing to override (CS0115).
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Gen<TItem> : BodyComponentBase where TItem : notnull
            {
                protected override View Body => Span["generic"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("partial class Gen<TItem>", generated, StringComparison.Ordinal);
        // Constraints belong to the type parameter, not to the declaration: repeating them is optional
        // and getting them wrong (notnull, unmanaged, ordering) would break correct user code, so the
        // emitter deliberately omits them.
        Assert.DoesNotContain("where", generated, StringComparison.Ordinal);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_GenericComponentWithTwoTypeParameters_SeparatesThemWithCommas()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Pair<TKey, TValue> : BodyComponentBase
            {
                protected override View Body => Span["pair"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("partial class Pair<TKey, TValue>", generated, StringComparison.Ordinal);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_GenericComponentCallingComposable_ExpandsAndAccessesItsOwnMembers()
    {
        // A generic component must expand a [Composable] and still name its own private member from the
        // generated part, which only compiles because that part joins the same generic type. The composable
        // itself references nothing non-public, so this does not exercise ComposableExpander's
        // access-requirement path (`_label` is read from Body, not from the composable body); the non-generic
        // Generator_ProtectedBaseMemberReferencedFromHelperType_* tests cover that.
        //
        // The [Composable] lives on a separate non-generic static class on purpose: a [Composable]
        // declared INSIDE a generic type is rejected with BCF1002 ("containing type must be non-generic"),
        // measured against this generator. That limitation is out of scope here, a generic component
        // calling a composable from elsewhere is the supported combination.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Gen<TItem> : BodyComponentBase
            {
                private string _label = "label";

                protected override View Body => Div[Widgets.Label(_label)];
            }

            public static class Widgets
            {
                [Composable]
                public static View Label(string text) => Span[text];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1002");
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("partial class Gen<TItem>", generated, StringComparison.Ordinal);
        // The composable's body is expanded inline (no runtime dispatch), and `_label`, a private member
        // of the generic component, is read from inside the generated part, which only compiles because
        // the generated part joins the same generic type.
        Assert.Contains("_label", generated, StringComparison.Ordinal);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// Asserts that both <paramref name="first"/> and <paramref name="second"/> occur in
    /// <paramref name="source"/> and that <paramref name="first"/> appears before <paramref name="second"/>.
    /// </summary>
    private static void AssertAppearsBefore(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, System.StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, System.StringComparison.Ordinal);

        Assert.True(firstIndex >= 0, $"Expected to find '{first}' in generated source.");
        Assert.True(secondIndex >= 0, $"Expected to find '{second}' in generated source.");
        Assert.True(
            firstIndex < secondIndex,
            $"Expected '{first}' to appear before '{second}' in generated source.");
    }
}
