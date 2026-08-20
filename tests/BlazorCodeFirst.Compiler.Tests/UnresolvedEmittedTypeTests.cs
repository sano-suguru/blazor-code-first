using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Analysis;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class UnresolvedEmittedTypeTests
{
    private sealed record ExpressionAnalysis(
        string Source,
        ExpressionSyntax Expression,
        ExpressionTemplate Template,
        ImmutableArray<DiagnosticInfo> Diagnostics,
        SemanticModel SemanticModel,
        INamedTypeSymbol ContainingType);

    [Fact]
    public void Roslyn_TypeofUnresolvedName_ExposesAnErrorType()
    {
        var result = AnalyzeValueExpression("typeof(Probe)");
        var name = result.Expression.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Single(static candidate => candidate.Identifier.ValueText == "Probe");

        var symbolType = result.SemanticModel.GetSymbolInfo(name).Symbol
            is ITypeSymbol type
                ? type
                : null;
        var inferredType = result.SemanticModel.GetTypeInfo(name).Type;

        Assert.True(
            symbolType?.TypeKind == TypeKind.Error || inferredType?.TypeKind == TypeKind.Error,
            "Roslyn exposed neither a symbol error type nor a type-info error type for Probe.");
    }

    [Fact]
    public void ParamTypeValue_UnresolvedType_ReportsBCF3015AndEmitsNoSource()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            namespace T;

            public sealed class Real : ComponentBase
            {
                [Parameter]
                public Type? Kind { get; set; }
            }

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Component<Real>().Param(r => r.Kind, typeof(Probe));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF3015");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id is "BCF3012" or "BCF1003");
        Assert.Empty(result.GeneratedSources);
        Assert.Equal("Probe", SourceText.From(source).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void IfCondition_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    If(typeof(Probe) == typeof(object), () => Div);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    [Fact]
    public void DeclarationPatternValue_UnresolvedType_ReportsBCF3015AndEmitsNoSource()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Class((new object() is Probe value).ToString());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF3015");

        Assert.Empty(result.GeneratedSources);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id is "BCF1003" or "BCF3012");
        Assert.Equal("Probe", SourceText.From(source).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void OutOfPositionNamedThenUnresolvedPositional_RemainsLanguageAndBCF1003Owned()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    If(then: () => Div, Has<Probe>());

                private static bool Has<T>() => true;
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.OutputCompilation.GetDiagnostics(), static d => d.Id == "CS8323");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    [Theory]
    [InlineData("""If(condition: typeof(Probe) == typeof(object), () => Div)""")]
    [InlineData("""If(condition: typeof(Probe) == typeof(object), then: () => Div)""")]
    [InlineData("""Element(tag: "section")[typeof(Probe).Name]""")]
    public void LegalNamedArgumentShapes_UnresolvedTypeStillReportsBCF3015(string body)
    {
        var source = $$"""
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body => {{body}};
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    [Fact]
    public void ForEachKey_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private readonly int[] _items = [1];

                protected override View Body =>
                    ForEach(_items, key: _ => typeof(Probe), content: i => Div[i.ToString()]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    [Fact]
    public void SplicedSelector_UnresolvedType_ReportsBCF3015()
    {
        // A spliced child is still a child, and BCF3015 is what keeps an unresolvable name out of the
        // generated file, which carries no using directives. Reported here or nowhere (#172).
        //
        // The same body written four ways was measured against this one input, and only the spliced one
        // used to disagree: a plain child, a collection-expression literal child, and the ForEach this
        // splice is sugar for all reported BCF3015, while the splice reported only BCF1003. The
        // unresolved name is what stops Select from binding, and every name under an unbound call was
        // being suppressed on the way out.
        const string source = """
            using System;
            using System.Linq;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private readonly int[] _items = [1];

                protected override View Body =>
                    Ul[[.. _items.Select(i => Li[typeof(Probe).Name])]];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    [Fact]
    public void SplicedSource_UnresolvedType_ReportsBCF3015()
    {
        // The splice's source, which needs no dedicated walk and is pinned here so that stays true. It is
        // measured, not assumed: adding and removing a walk over the source changed no input's
        // diagnostics, so ScanSplice covers the selector only. A source broken enough not to be reached
        // makes the whole element access an IInvalidOperation, and BCF1003 answers instead.
        const string source = """
            using System;
            using System.Linq;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private readonly int[] _items = [1];

                protected override View Body =>
                    Ul[[.. _items.Take(typeof(Probe).Name.Length).Select(i => Li[i.ToString()])]];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    [Fact]
    public void ReorderedForEachKey_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private readonly int[] _items = [1];

                protected override View Body =>
                    ForEach(content: i => Div[i.ToString()], source: _items, key: _ => typeof(Probe));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    [Fact]
    public void AttributeValue_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Attr("data-type", typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    [Fact]
    public void StaticDecorationValue_UnresolvedType_RemainsBCF1003Only()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Decorations.Attr(Div, "data-type", typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// The same static <c>Decorations.Attr(Div, ...)</c> spelling as
    /// <see cref="StaticDecorationValue_UnresolvedType_RemainsBCF1003Only"/>, but with a sibling unselected
    /// invocation so the body reaches this scanner's own failure-recovery walk rather than being reported
    /// through <c>RenderExpressionAnalyzer.Analyze</c>'s success path. On this shape, what keeps the
    /// argument unread is not <c>IsFluentExtensionInvocation</c>'s answer (see the equivalence note on that
    /// method) but <c>BoundArguments.TryBindFallback</c> binding against an offset that assumes the
    /// receiver is omitted, which a fully-written static call's argument count never satisfies: the bind
    /// fails before <c>ScanDecoration</c>'s gate is ever reached. Disabling either of
    /// <c>TryBindFallback</c>'s own arithmetic checks (<c>declaredCount</c>, <c>index + offset</c>) lets the
    /// mismatch through and crashes the generator instead of leaving the body at BCF1003.
    /// </summary>
    [Fact]
    public void StaticDecorationValueWithSiblingUnselectedInvocation_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Decorations.Attr(Div, "data-type", MissingMethod() + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    [Fact]
    public void NonElementDecorationValue_UnresolvedType_RemainsBCF3008Only()
    {
        // Decorations now bind ElementView, not View (Decorations.cs), so decorating Raw(...)'s View
        // result no longer resolves at all and BCF3008 is reported from the failure path by
        // RejectedDecorationScanner rather than from the analyzer's decoration arm. The report site moved,
        // but the claim did not: an unresolved type inside a rejected decoration's value must not ALSO
        // draw a BCF3015, the value sweep must not descend into a decoration that was already rejected.
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Raw("<b>x</b>").Class(typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3008");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void ReorderedAttrWithInvalidName_DoesNotReportValueTypeBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Attr(value: typeof(Probe).Name, name: typeof(string).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3011");
    }

    /// <summary>
    /// The statement-removal mutant on <c>TryResolveDecorationName</c>'s
    /// <c>RejectUnresolvedValueRecovery</c> call: without it, a rejected name leaves
    /// <c>ShouldRecoverUnresolvedValue</c> true, and <c>ScanDecoration</c>'s <c>Attr</c> arm falls to
    /// <c>ReportSelectedInvocationValues</c> on the value. <c>typeof(Probe).Name</c> alone (the sibling
    /// test above) has no nested invocation for that narrower scan to descend into, so it cannot tell the
    /// mutant apart; wrapping the same unresolved type as an argument to a real, non-surface method call
    /// gives the scan something to walk into.
    /// </summary>
    [Fact]
    public void AttrWithInvalidNameAndInvocationWrappedUnresolvedValue_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Attr(typeof(string).Name, string.Concat(typeof(Probe).Name));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3011");
    }

    [Fact]
    public void DuplicateAttribute_UnresolvedValueType_RemainsBCF3010Owned()
    {
        // A duplicate binding is rejected before its value is normalized, so the value never becomes
        // emitted code and its type is not the author's problem, the same ownership the event channel
        // and both BCF3011 paths already had.
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Id("first").Attr("id", typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3010");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void ViewPartArgument_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                [ViewPart]
                private static View Label(Type value) => Span[value.Name];

                protected override View Body => Label(typeof(Probe));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
    }

    /// <summary>
    /// The receiver of a fluently written <c>[ViewPart]</c> extension is scanned, the same as the
    /// arguments beside it: it is an expression the author wrote, and the sweep answers for what the author
    /// wrote rather than for what the call could have expanded to (#200).
    /// </summary>
    /// <remarks>
    /// The call itself never expands — #203 decided a view part is not an extension member, so the
    /// declaration is BCF1002, and the receiver the reduced invocation carries as argument 0 binds to no
    /// <c>ArgumentSyntax</c> of that call — so this body is an error either way, and what the scan decides
    /// is only whether the specific BCF3015 is reported in place of the generic BCF1003.
    /// </remarks>
    [Fact]
    public void ViewPartExtensionReceiver_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public static class Helpers
            {
                [ViewPart]
                public static View Label(this string value) => Span[value];
            }

            public partial class Host : BodyComponentBase
            {
                protected override View Body => typeof(Probe).Name.Label();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    [Fact]
    public void LayoutChrome_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : ChromeLayoutBase
            {
                protected override View Chrome =>
                    Div.Class(typeof(Probe).Name)[Body];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    [Fact]
    public void ViewPartBody_UnresolvedType_ReportsBCF3015Once()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                [ViewPart]
                private static View Card() => Div.Class(typeof(Probe).Name);

                protected override View Body => Card();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
    }

    [Fact]
    public void DirectComponentUnresolvedType_RemainsBCF3012Only()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body => Component<Probe>();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Single(result.Diagnostics, static d => d.Id == "BCF3012");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void TwoLocations_ReportTwoBCF3015Diagnostics()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Class(typeof(Probe).Name + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostics = result.Diagnostics.Where(static d => d.Id == "BCF3015").ToArray();

        Assert.Equal(2, diagnostics.Length);
        Assert.NotEqual(diagnostics[0].Location.SourceSpan, diagnostics[1].Location.SourceSpan);
    }

    [Theory]
    [InlineData("Div.Attr(typeof(Probe).Name, \"value\")")]
    [InlineData("Div.On(typeof(Probe).Name, () => { })")]
    [InlineData("Element(typeof(Probe).Name)")]
    public void CompileTimeOnlyFactoryArgument_UnresolvedType_DoesNotReportBCF3015(string body)
    {
        var source = $$"""
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body => {{body}};
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id is "BCF3009" or "BCF3011");
    }

    [Fact]
    public void ParamSelectorUnresolvedType_DoesNotReportBCF3015()
    {
        const string source = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            namespace T;

            public sealed class Real : ComponentBase
            {
                [Parameter]
                public string Name { get; set; } = "";
            }

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Component<Real>().Param((Probe r) => r.Name, "value");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id is "BCF1003" or "BCF3005");
    }

    [Theory]
    [InlineData(
        "Component<Real>().Param(r => _other.Kind, typeof(Probe))",
        "BCF3005")]
    [InlineData(
        "Component<Real>().Param(r => r.NotAParameter, typeof(Probe))",
        "BCF3006")]
    [InlineData(
        "Component<Real>().Param(r => r.Kind, typeof(string)).Param(r => r.Kind, typeof(Probe))",
        "BCF3007")]
    public void RejectedParamValue_UnresolvedType_RemainsOwnedByExistingDiagnostic(
        string body,
        string expectedDiagnostic)
    {
        var source = $$"""
            using System;
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            namespace T;

            public sealed class Real : ComponentBase
            {
                [Parameter]
                public Type? Kind { get; set; }

                public Type? NotAParameter { get; set; }
            }

            public partial class Host : BodyComponentBase
            {
                private readonly Real _other = new();

                protected override View Body => {{body}};
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, d => d.Id == expectedDiagnostic);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id is "BCF1003" or "BCF3015");
    }

    [Fact]
    public void AttrConstantNameValueSiblingOfUnselectedInvocation_UnresolvedType_ReportsBCF3015()
    {
        // AttributeValue_UnresolvedType_ReportsBCF3015 above already covers a plain typeof(Probe) value on
        // .Attr, and that shape is read by RenderExpressionAnalyzer's own decoration walk regardless of
        // this failure path, the same way ParamTypeValue's plain value is. The sibling shape used by
        // ValueSiblingOfUnselectedInvocation forces FactoryArguments.Bind to fail for the .Attr invocation
        // itself instead: Attr(string, string?) is not generic, so unlike ScalarParam's Param<TValue> no
        // explicit type argument is needed for recovery to name a single overload.
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Attr("data-type", MissingMethod() + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void AttrNonConstantNameSelectedInvocationSibling_UnresolvedType_ReportsBCF3015()
    {
        // The non-constant-name arm (ReportSelectedInvocationValues) needs a shape neither of the other
        // sibling-shape tests supply: a SELECTED invocation whose own argument carries the unresolved
        // name, sitting beside — not nested inside — the unselected one that breaks FactoryArguments.Bind.
        // Nesting it inside instead (Consume(typeof(Probe)) as MissingMethod's own argument, measured)
        // does not work: ReportValue's IsInsideUnselectedInvocation walks every ancestor of the name, not
        // just the nearest one, so it still finds the outer unselected MissingMethod() and suppresses the
        // report even though ReportSelectedInvocationValues correctly recognized the inner call as
        // selected. Writing Consume(typeof(Probe)) as MissingMethod's sibling keeps it off that ancestor
        // chain: its own ancestors run straight up through the binary expression to the .Attr invocation,
        // which resolves to a real candidate, so nothing along the way reads as unselected.
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Attr(GetName(), MissingMethod() + Consume(typeof(Probe)));

                private static string GetName() => "data-x";
                private static string Consume(Type t) => t.Name;
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// #451: the sibling above needs a SELECTED invocation in the value
    /// (<c>Consume(typeof(Probe))</c>) for <c>ReportSelectedInvocationValues</c> to walk into.
    /// <c>typeof(Probe).Name</c> alone is a member access, not an invocation, so that scanner finds
    /// nothing to report even though the name is exactly as non-constant. BCF3011 recovers the name
    /// half regardless of whether the value side has anything to report.
    /// </summary>
    [Fact]
    public void AttrNonConstantNameUnselectedInvocationValueSibling_UnresolvedType_ReportsBCF3011()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Attr(GetName(), MissingMethod() + typeof(Probe).Name);

                private static string GetName() => "data-x";
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        // BCF1003 is what #451 reported this shape as; asserting its absence is the issue's own complaint
        // ("left with only the generic BCF1003") stated as a test, not merely that BCF3011 was added beside it.
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3011");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    /// <summary>
    /// The sibling above, reached from the <c>[ViewPart]</c> host instead of <c>Body</c> (#100's lesson:
    /// a sweep wired through only one host silently degrades on the other).
    /// </summary>
    [Fact]
    public void AttrNonConstantNameUnselectedInvocationValueSibling_InsideAViewPartBody_ReportsBCF3011()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                [ViewPart]
                private static View Broken() => Div.Attr(GetName(), MissingMethod() + typeof(Probe).Name);

                private static string GetName() => "data-x";

                protected override View Body => Broken();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3011");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    [Fact]
    public void ElementBindSetter_UnresolvedType_ReportsBCF3015()
    {
        // Unlike the getter (which the normal walk must validate as an assignable expression and reports
        // through on its own), the setter of an element .Bind never gets that treatment from
        // RenderExpressionAnalyzer.ClassifyBind — measured, a plain unresolved type here needs none of the
        // sibling-invocation gymnastics the getter or a scalar .Param value would need. Only the
        // failure-path's ReportBindArguments, which walks the getter and everything written after it,
        // ever reaches the setter's own contents.
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private string _value = "";

                protected override View Body =>
                    Div.Bind("value", "onchange", () => _value, v => Consume(typeof(Probe)));

                private static void Consume(Type t) { }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// Two non-literal <c>params</c> children (not the single collection-expression-literal shape
    /// <c>FactoryArguments</c> handles), bound through <c>BoundArguments.TryBindFallback</c>'s syntactic
    /// route: a second broken child beside the reportable one is what makes <c>FactoryArguments.Bind</c>
    /// fail for the outer indexer too — a single such child on its own still lets <c>FactoryArguments</c>
    /// succeed, since the child invocation's own converted type (<c>ElementView</c>) resolves regardless
    /// of its broken argument (measured). Kills both the statement-removal mutant on the fallback binder's
    /// <c>paramsElements.Add(...)</c> call (the child is never added, so it is never scanned) and the
    /// boolean mutant flipping its <c>IsSpread</c> argument to <see langword="true"/> (the child would be
    /// routed to a splice scan instead, which finds no <c>.Select</c> projection here and reports nothing).
    /// </summary>
    [Fact]
    public void ParamsChildViaFallbackBinder_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div[Div.Class(MissingMethod() + typeof(Probe).Name), MissingMethod()];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// The statement-removal mutant on <c>ScanDecoration</c>'s <c>Bind</c> <c>return;</c>: without it,
    /// the tail <c>ReportValue(args.At(0))</c> reads the attribute name argument. <c>ReportBindArguments</c>
    /// only ever reads from the getter onward, so correct code never reports on either name argument.
    /// </summary>
    /// <remarks>
    /// The class remarks reason that a non-constant name is always <c>BCF3011</c>'s to report and clears
    /// <c>recoverOwnValue</c> first — true when the normal walk's own <c>FactoryArguments.Bind</c>
    /// succeeds and reaches that name check. It does not hold when the name argument is itself what
    /// breaks <c>FactoryArguments.Bind</c>: an unselected-invocation sibling in the name (the same shape
    /// used throughout this file) poisons the whole call's binding before the normal walk's constant check
    /// ever runs, so <c>ShouldRecoverUnresolvedValue</c> stays true and <c>ScanDecoration</c> reaches this
    /// branch with a non-constant, unresolved-carrying name — measured with a throwaway probe: with the
    /// getter and setter both clean, correct code reports only <c>BCF1003</c>, and the mutant additionally
    /// reports <c>BCF3015</c> on the name's own <c>Probe</c>.
    /// </remarks>
    [Fact]
    public void BindNameSiblingOfUnselectedInvocation_UnresolvedType_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private string _value = "";

                protected override View Body =>
                    Div.Bind(MissingMethod() + typeof(Probe).Name, "onchange", () => _value, v => { _value = v; });
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    [Fact]
    public void OnNonConstantNameHandlerLocalDeclaration_UnresolvedType_DoesNotReportBCF3015()
    {
        // The negate mutant on line 285 (dropping the `!`) would route this exact call to ReportValue's
        // raw name walk instead of ReportSelectedInvocationValues's narrower one. The two agree whenever
        // the handler's unresolved name sits inside a genuinely selected invocation's own arguments (the
        // test above), which is why that shape alone cannot tell the branches apart. A name that sits
        // outside any invocation at all — here, a local declaration's initializer — is where they diverge:
        // ReportSelectedInvocationValues never matches anything for it and reports nothing, while
        // ReportValue's raw walk would find and report it regardless.
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.On(MissingMethod() + "onclick", () => { var x = typeof(Probe); });
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void OnClickShortcutHandlerLocalDeclaration_UnresolvedType_ReportsBCF3015()
    {
        // The logical mutant on line 284 (`&&` to `||`) is invisible to every existing shortcut-handler
        // test, because a shortcut's CarriesEventName is false and EventNameIndex is the negative sentinel
        // (#48 in EventParameters), so args.At(EventNameIndex) is always null and
        // IsNonEmptyConstantString(null, ...) is always false: with `||`, `false || !false` is still true,
        // routing every shortcut call to ReportSelectedInvocationValues instead of the correct ReportValue.
        // That only becomes observable when the handler's unresolved name sits outside any selected
        // invocation's own arguments, the same local-declaration shape the sibling test above uses for the
        // negate mutant on line 285.
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.OnClick(() => { var x = typeof(Probe); });
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void OnNonConstantNameSelectedInvocationHandler_UnresolvedType_ReportsBCF3015()
    {
        // The event-name mirror of AttrNonConstantNameSelectedInvocationSibling above: TryResolveDecorationName
        // is shared between .Attr and .On, so a non-constant event name is BCF3011's to report on the normal
        // walk too, and the same FactoryArguments.Bind failure (from the unselected MissingMethod() sibling
        // in the name) is what keeps the normal walk from ever reaching that check. recoverOwnValue stays
        // true, IsNonEmptyConstantString correctly reads the name as non-constant, and the handler's own
        // selected invocation (Consume(typeof(Probe))) is what ReportSelectedInvocationValues finds.
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.On(MissingMethod() + "onclick", () => Consume(typeof(Probe)));

                private static void Consume(Type t) { }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// The statement-removal mutant on <c>ScanDecoration</c>'s <c>On</c>/<c>EventShortcut</c>
    /// <c>return;</c>: without it, control falls through the <c>Attr</c> and <c>Bind</c> checks to the
    /// tail <c>ReportValue(args.At(0))</c>, which for <c>.On</c> is the event name argument. Correct code
    /// never reports on the name directly; <c>ReportEventArguments</c> only ever reports the handler. A
    /// second, independently reportable unresolved name placed in the name argument (beside the
    /// <c>MissingMethod()</c> sibling that keeps <c>ReportEventArguments</c> alive) makes the fallthrough
    /// observable: the mutant reports it twice, at two different spans, where correct code reports the
    /// handler's occurrence once.
    /// </summary>
    [Fact]
    public void OnNonConstantNameCarriesUnresolvedType_ReportsBCF3015OnlyOnHandler()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.On(MissingMethod() + typeof(Probe).Name, () => Consume(typeof(Probe)));

                private static void Consume(Type t) { }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    /// <summary>
    /// The statement-removal mutant on <c>ScanDecoration</c>'s <c>Attr</c> <c>return;</c>: without it,
    /// control falls through the <c>Bind</c> check to the tail <c>ReportValue(args.At(0))</c>, the
    /// attribute name argument. Correct code never reports on the name directly in the non-constant-name
    /// arm; <c>ReportSelectedInvocationValues</c> only reports the value's own selected invocation. An
    /// unresolved name placed in the (non-constant) attribute name, beside the value's own reportable
    /// selected invocation, makes the fallthrough observable the same way as the <c>.On</c> case above.
    /// </summary>
    [Fact]
    public void AttrNonConstantNameCarriesUnresolvedType_ReportsBCF3015OnlyOnValue()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Attr(typeof(Probe).Name + GetName(), MissingMethod() + Consume(typeof(Probe)));

                private static string GetName() => "-x";
                private static string Consume(Type t) => t.Name;
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    [Fact]
    public void ScalarParamValueSiblingOfUnselectedInvocation_UnresolvedType_ReportsBCF3015()
    {
        // The sibling shape ValueSiblingOfUnselectedInvocation uses for a decoration value, adapted for a
        // ScalarParam's own value with one addition the decoration shape does not need: the explicit
        // <string?> type argument. Without it, TValue inference over an argument this broken (MissingMethod
        // has no symbol at all) fails outright — measured, GetSymbolInfo answers a synthesized conversion
        // operator with an empty candidate list, not the .Param overload group, so RenderExpressionAnalyzer
        // and the failure-path scanner both walk away with nothing recognized and the body reports bare
        // BCF1003. Naming <string?> narrows candidates to the one .Param<TValue> overload before inference
        // ever runs, which is what lets recovery name it: GetSymbolInfo resolves that single candidate,
        // RenderExpressionAnalyzer's Analyze filters it out at its own conversion-operator guard before
        // ClassifyComponentParameter runs (so RejectUnresolvedValueRecovery is never called and
        // recoverOwnValue stays true), and ClassifyComponentParameter's FactoryArguments.Bind fails on the
        // same broken operation, so only the failure path's syntactic fallback binder ever reaches the
        // value to report it.
        const string source = """
            using System;
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            namespace T;

            public sealed class Real : ComponentBase
            {
                [Parameter]
                public string? Kind { get; set; }
            }

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Component<Real>().Param<string?>(r => r.Kind, MissingMethod() + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// A component carrying the parameters the <c>.Bind</c> theory below selects against: the bound name,
    /// the <c>{name}Changed</c> the surface derives from it, and a slot to hang the failing sibling on.
    /// </summary>
    private const string CardSource = """
        using Microsoft.AspNetCore.Components;

        namespace T;

        public sealed class Card : ComponentBase
        {
            [Parameter] public string Title { get; set; } = "";
            [Parameter] public EventCallback<string> TitleChanged { get; set; }
            [Parameter] public RenderFragment? ChildContent { get; set; }
        }
        """;

    [Theory]
    [InlineData("""() => _title""")]
    [InlineData("""() => _title, v => _title = v""")]
    public void ComponentBindReceiver_UnresolvedType_ReportsBCF3015(string bindArguments)
    {
        // The sweep starts at the body's root and an unrecognized root returns before the receiver chain is
        // walked, so a ComponentView<T>.Bind sitting there silenced every value beneath it and the body
        // reported the bare BCF1003 with nothing naming the cause (#191). Both spellings are exercised
        // because they are separate overloads, and the recovery route that recognizes them reads the
        // overload the call site selected.
        var source = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private string _title = "";

                protected override View Body =>
                    Component<Card>()
                        .Param(c => c.ChildContent, Div.Class(MissingMethod() + typeof(Probe).Name)["x"])
                        .Bind(c => c.Title, {{bindArguments}});
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Host.cs", source), ("Card.cs", CardSource));

        AssertSingleBCF3015(result, source);
    }

    [Theory]
    [InlineData("""Component<Card>().Bind(c => c.Title, () => _titles[typeof(Probe).Name])["x"]""")]
    [InlineData("""Input.Bind("value", "onchange", () => _titles[typeof(Probe).Name])["x"]""")]
    [InlineData(
        """Input.Bind("value", "onchange", () => _titles["k"], v => _titles[typeof(Probe).Name] = v)["x"]""")]
    [InlineData(
        """Input.Bind(attributeName: "value", eventName: "onchange", get: () => _titles[typeof(Probe).Name])["x"]""")]
    // The culture is transplanted like the getter and the setter, so an unresolved type inside it is the
    // same defect in the same call. This one resolves — the unresolved name sits inside a constructor
    // argument, not in the overload's own arguments — so it tests what the arm reports rather than what
    // recovery selects (#307).
    [InlineData(
        """Input.Bind("value", "onchange", () => _titles["k"], new System.Globalization.CultureInfo(typeof(Probe).Name))["x"]""")]
    public void BindValueUnresolvedType_ReportsBCF3015(string body)
    {
        // The unresolved type is what makes the .Bind itself fail to bind, so recovery has to name the
        // method from a whole overload group rather than from a selected symbol (#197). The root is the
        // children indexer, which recovers through the receiver's type, so the sweep reaches the .Bind and
        // only the .Bind's own recovery is under test.
        AssertBodyOverCardReportsBCF3015(body);
    }

    [Theory]
    [InlineData(
        """Component<Card>().Param(c => c.ChildContent, Div.Class(typeof(Probe).Name)["x"]).Bind(c => c.Title)""")]
    [InlineData("""Div.Class(typeof(Probe).Name).Bind("value")""")]
    public void BindArgumentsMatchNoOverload_ReportsBCF3015OnTheReceiver(string body)
    {
        // Neither call fills any overload, so recovery cannot name one and its arguments go unread. Its
        // receiver does not: which overload was written says nothing about what the receiver is, so the
        // sweep walks it and the value below is still named (#197). Refusing the whole expression is what
        // #191 fixed for a selected .Bind, and an unselectable one deserves the same.
        AssertBodyOverCardReportsBCF3015(body);
    }

    /// <summary>
    /// Runs <paramref name="body"/> as the whole of a <c>Body</c> alongside <see cref="CardSource"/> and
    /// asserts it reports BCF3015 on <c>Probe</c> and nothing else. The <c>_titles</c> dictionary gives the
    /// <c>.Bind</c> theories an assignable <see langword="string"/> target, so that no body is turned away
    /// by BCF3017 or BCF3018 before the recovery under test runs; it is simply unused by the bodies that
    /// bind nothing.
    /// </summary>
    private static void AssertBodyOverCardReportsBCF3015(string body)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private readonly Dictionary<string, string> _titles = [];

                protected override View Body => {{body}};
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Host.cs", source), ("Card.cs", CardSource));

        AssertSingleBCF3015(result, source);
    }

    [Fact]
    public void ForEachParameterDeclarationUnresolvedType_DoesNotReportBCF3015()
    {
        const string source = """
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private readonly List<int> _items = [];

                protected override View Body =>
                    ForEach(_items, key: (Probe item) => item, content: item => Div);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    [Fact]
    public void LambdaParameterTypeInValue_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body => Div.Class((Probe value) => "x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    [Fact]
    public void PureOverloadFailure_UnresolvedType_DoesNotReportBCF3015()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private static View Pick<T>(int value) => Div;

                protected override View Body => Pick<Probe>("wrong");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    [Fact]
    public void UserDefinedForEachOverloadFailure_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using BlazorCodeFirst;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private readonly List<int> _items = [];

                private static View ForEach<T>(
                    int source,
                    Func<T, object?> key,
                    Func<T, View> content) => Html.Div;

                protected override View Body =>
                    ForEach(_items, key: _ => typeof(Probe), content: item => Html.Div);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    [Fact]
    public void QualifiedUserDefinedForEachOverloadFailure_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private readonly List<int> _items = [];

                protected override View Body =>
                    Helpers.ForEach(
                        _items,
                        key: _ => typeof(Probe),
                        content: item => Div);

                private static class Helpers
                {
                    public static View ForEach<T>(
                        int source,
                        Func<T, object?> key,
                        Func<T, View> content) => Div;
                }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    [Theory]
    [InlineData("nameof(Probe)")]
    [InlineData("typeof(global::Generated.Probe).Name")]
    public void SelfContainedValueUnderFailingOuterRoute_DoesNotReportBCF3015(string value)
    {
        var source = $$"""
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body => Div.Attr(typeof(string).Name, {{value}});
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3011");
    }

    [Fact]
    public void EscapedNameofMethod_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private static string @nameof(Type value) => value.Name;

                protected override View Body =>
                    Div.Attr("data-x", @nameof(typeof(Probe)));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void ValueSiblingOfUnselectedInvocation_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Class(MissingMethod() + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// A decoration chain link whose own overload is ambiguous, walked by
    /// <c>HasRejectedElementTag</c> while unwinding an indexer's receiver toward its <c>Element(tag)</c>.
    /// </summary>
    /// <remarks>
    /// <c>.Attr(string, string?)</c>, <c>.Attr(string, bool)</c> and <c>.Attr(string)</c> all apply to
    /// <c>Div.Attr("x", MissingMethod())</c> once the error-typed second argument is present, so
    /// <c>GetSymbolInfo</c> answers <c>Symbol=null</c>, <c>CandidateReason=OverloadResolutionFailure</c> —
    /// measured. The invocation's own <em>type</em> still resolves to <c>ElementView</c> regardless (every
    /// candidate returns it), which is what lets <c>.Class("c")</c> keep binding on top of it and the
    /// indexer recognize the whole chain. <c>HasRejectedElementTag</c> unwinds past <c>.Class</c> (an
    /// element decoration) and reaches this <c>.Attr</c> call next, where its own symbol resolution
    /// fails — the one shape that reaches the method-unresolved branch without the outer indexer losing
    /// recognition first.
    /// </remarks>
    [Fact]
    public void DecorationChainAmbiguousReceiver_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Attr("x", MissingMethod()).Class("c")[typeof(Probe).Name];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// A non-decoration call sitting in an indexer's receiver chain, ahead of a rejected
    /// <c>Element(tag)</c>. <c>HasRejectedElementTag</c> has to give up as soon as it meets a link that is
    /// neither <c>Element(tag)</c> nor one of the element decoration kinds, answering "not rejected"
    /// without climbing any further — the same fail-open rule the class remarks state for every route this
    /// gate cannot analyze.
    /// </summary>
    /// <remarks>
    /// <c>.Key</c> is such a link: it reaches generated code but is not itself a decoration this gate
    /// walks through. Putting a rejected tag <em>beneath</em> it (a non-constant <c>Element(_tag)</c>) is
    /// what tells the two mutants on this branch apart from the correct answer: giving up at <c>.Key</c>
    /// and climbing past it disagree only when what sits further up would flip the verdict, and a rejected
    /// tag is exactly that flip. The correct answer stays "not rejected" and the bracketed child is
    /// scanned; either mutant — negating the kind check or answering the wrong constant when it fires —
    /// climbs to the rejected tag instead and swallows the report.
    /// </remarks>
    [Fact]
    public void NonDecorationReceiverBeforeRejectedTag_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private readonly string _tag = "div";

                protected override View Body =>
                    Element(_tag).Key(1)[typeof(Probe).Name];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// A rejected tag behind a decoration chain that carries two different
    /// <see cref="SurfaceMethodKind"/>s, <c>.Id</c> (<c>AttributeShortcut</c>) and <c>.Attr</c>
    /// (<c>Attr</c>).
    /// </summary>
    /// <remarks>
    /// <c>IsElementDecoration</c>'s <c>or</c> chain has five joins across its six kinds (<c>Class</c>,
    /// <c>AttributeShortcut</c>, <c>EventShortcut</c>, <c>Attr</c>, <c>On</c>, <c>Bind</c>). A stryker
    /// mutant that flips one <c>or</c> to <c>and</c> collapses that join's two neighboring kinds into an
    /// unmatchable conjunction (C# pattern precedence binds <c>and</c> tighter than <c>or</c>), dropping
    /// both from the recognized set at once; the four survivors this test kills are the
    /// <c>Class</c>/<c>AttributeShortcut</c>, <c>AttributeShortcut</c>/<c>EventShortcut</c>,
    /// <c>EventShortcut</c>/<c>Attr</c>, and <c>Attr</c>/<c>On</c> joins. This chain's two kinds, <c>.Id</c>
    /// (<c>AttributeShortcut</c>) and <c>.Attr</c> (<c>Attr</c>), are chosen because at least one of the
    /// two sits in every one of those four joins, so whichever <c>or</c> a mutant drops,
    /// <c>HasRejectedElementTag</c>'s unwind meets an unrecognized kind at one of the two links and gives
    /// up early — answering "not rejected" and letting the bracketed child be scanned, which reports the
    /// BCF3015 this test asserts against.
    /// </remarks>
    [Fact]
    public void RejectedTagBehindMixedDecorationChain_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private readonly string _tag = "div";

                protected override View Body =>
                    Element(_tag).Id("x").Attr("k", "v")[typeof(Probe).Name];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3009");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// The handler argument of an event decoration is a value position on the failure path too, for both
    /// argument layouts: a named shortcut carries it at argument 0 and <c>.On</c> at argument 1.
    /// </summary>
    /// <remarks>
    /// The scanner used to write those two ordinals out, which made it a fourth reader of "which argument
    /// is the handler" — in the same file whose binding scan reads the positions off
    /// <c>KnownSymbols.TryGetBindParameters</c> and says why. It now asks
    /// <c>KnownSymbols.TryGetEventParameters</c>, and this is what holds the two layouts to what the readers
    /// on the success path resolve (#221). Nothing covered either ordinal before: the only event case here
    /// asked about the <em>name</em> argument, in the negative direction.
    /// </remarks>
    [Theory]
    [InlineData("""Div.OnClick(() => Consume(typeof(Probe)))""")]
    [InlineData("""Div.On("onclick", () => Consume(typeof(Probe)))""")]
    public void EventHandlerArgument_UnresolvedType_ReportsBCF3015(string body)
    {
        var source = $$"""
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private static void Consume(Type value) { }

                protected override View Body => {{body}};
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    [Theory]
    [InlineData("typeof(Wrapper<Missing>)")]
    [InlineData("typeof(Outer<Missing>.Inner)")]
    [InlineData("typeof(Missing[])")]
    [InlineData("typeof(Missing?)")]
    [InlineData("typeof((Missing, int))")]
    [InlineData("default(Missing)")]
    [InlineData("new Missing()")]
    [InlineData("new object() is Missing")]
    [InlineData("new object() as Missing")]
    [InlineData("new object() is Missing value")]
    [InlineData("new object() switch { Missing value => value, _ => null }")]
    [InlineData("new object() is Missing { }")]
    [InlineData("sizeof(Missing)")]
    [InlineData("stackalloc Missing[1]")]
    [InlineData("default(delegate*<Missing, void>)")]
    public void ValueExpression_UnresolvedType_ReportsOnceAtSmallestName(string expression)
    {
        var result = AnalyzeValueExpression(expression);
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF3015");

        Assert.Equal(
            "Missing",
            SourceText.From(result.Source).ToString(diagnostic.Span));
    }

    [Fact]
    public void GlobalQualifiedUnresolvedType_IsPreserved()
    {
        var result = AnalyzeValueExpression("typeof(global::Generated.Probe)");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Equal("typeof(global::Generated.Probe)", result.Template.ToCode());
    }

    [Fact]
    public void GlobalQualifiedOuter_DoesNotExemptUnqualifiedGenericArgument()
    {
        var result = AnalyzeValueExpression(
            "typeof(global::System.Collections.Generic.List<Probe>)");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Equal("Probe", SourceText.From(result.Source).ToString(diagnostic.Span));
    }

    [Theory]
    [InlineData("ActuallyMissingValue")]
    [InlineData("MissingMethod(1)")]
    public void UnresolvedValueOrOverloadFailure_DoesNotReportBCF3015(string expression)
    {
        var result = AnalyzeValueExpression(expression);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    [Theory]
    [InlineData("typeof(TValue)")]
    [InlineData("typeof(System.Text.StringBuilder)")]
    [InlineData("nameof(System.String)")]
    public void ResolvedOrTypeParameterExpression_DoesNotReportBCF3015(string expression)
    {
        var result = AnalyzeValueExpression(expression);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void ExtensionExplicitUnresolvedTypeArgument_ReportsBCF3015()
    {
        var result = AnalyzeValueExpression(
            "new object[] { 1 }.Cast<Probe>().FirstOrDefault()");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Equal("Probe", SourceText.From(result.Source).ToString(diagnostic.Span));
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
    }

    [Fact]
    public void SameContextAndLocation_ExtensionUnresolvedTypeArgumentReportsBCF3015Once()
    {
        var result = AnalyzeValueExpression(
            "new object[] { 1 }.Cast<Probe>().FirstOrDefault()");
        var context = new ViewPartBodyContext(
            result.SemanticModel,
            result.ContainingType,
            "ProbeExpression",
            KnownSymbols.TryCreate(result.SemanticModel.Compilation)!,
            ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
            isInlinedAtCallSites: false,
            default);

        var first = ExpressionTemplateFactory.Create(result.Expression, context);
        var second = ExpressionTemplateFactory.Create(result.Expression, context);

        Assert.Single(context.Diagnostics, static d => d.Id == "BCF3015");
        Assert.DoesNotContain(context.Diagnostics, static d => d.Id == "BCF1002");
        Assert.Equal(first.ToCode(), second.ToCode());
    }

    [Fact]
    public void SameContextAndLocation_ReportsBCF3015Once()
    {
        var result = AnalyzeValueExpression("typeof(Probe)");
        var context = new ViewPartBodyContext(
            result.SemanticModel,
            result.ContainingType,
            "ProbeExpression",
            KnownSymbols.TryCreate(result.SemanticModel.Compilation)!,
            ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
            isInlinedAtCallSites: false,
            default);

        _ = ExpressionTemplateFactory.Create(result.Expression, context);
        _ = ExpressionTemplateFactory.Create(result.Expression, context);

        Assert.Single(context.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// Three leading named arguments, each already in its declared position, followed by one trailing
    /// positional argument. <c>HasValidArgumentOrder</c>'s bookkeeping only has to track position for a
    /// reordered name, but a named argument that lands on its own natural slot still walks the same
    /// <c>nextPositional</c> increment, and a call this shape reaches the trailing positional argument's
    /// own out-of-position check with a value the increment left behind.
    /// </summary>
    [Fact]
    public void NamedArgumentsInPositionThenTrailingPositional_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private string _value = "";

                protected override View Body =>
                    Div.Bind(
                        attributeName: MissingMethod() + "x",
                        eventName: "onchange",
                        get: () => _value,
                        v => { _value = v; System.Console.WriteLine(typeof(Probe)); });
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    /// <summary>
    /// Two leading positional arguments, filling their declared slots without ever naming them, followed
    /// by a named argument for the next slot. <c>HasValidArgumentOrder</c>'s unconditional
    /// <c>nextPositional++</c> for an ordinary (non-<see langword="params"/>) positional argument keeps
    /// this bookkeeping in step for the positional case, the same way the named-match increment does for
    /// the named case above; dropping or reversing it leaves the later named argument compared against a
    /// stale position and misclassified as out of order.
    /// </summary>
    [Fact]
    public void PositionalArgumentsThenNamedArgument_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private string _value = "";

                protected override View Body =>
                    Div.Bind(
                        MissingMethod() + "x",
                        "onchange",
                        get: () => _value,
                        v => { _value = v; System.Console.WriteLine(typeof(Probe)); });
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    /// <summary>
    /// A named argument spelling that matches no parameter of any <c>.Attr</c> overload.
    /// <c>FindParameter</c>'s search loop stops at <c>parameters.Length</c> without finding one and
    /// returns -1; widening that bound to <c>parameters.Length</c> inclusive walks one ordinal past the
    /// array and throws, taking the whole generator down (<c>CS8785</c>) instead of degrading this one
    /// body to <c>BCF1003</c>.
    /// </summary>
    [Fact]
    public void MisnamedArgument_DoesNotCrashTheGenerator()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Attr(bogus: "x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "CS8785");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    /// <summary>
    /// Two written arguments against <c>PreventDefault</c>'s zero-parameter overload, one of the
    /// candidates <c>TrySelectCandidate</c> tries from the two-overload group. The bounds guard on
    /// <c>HasValidArgumentOrder</c>'s params check exists for exactly this candidate: with zero declared
    /// parameters, <c>nextPositional</c> reaches <c>parameterCount</c> on the very first written argument,
    /// and indexing <c>parameters[nextPositional + offset]</c> without the guard walks past the array
    /// (length 1, the receiver alone) and crashes the whole generator rather than letting this candidate
    /// fail to bind cleanly.
    /// </summary>
    [Fact]
    public void OverfilledZeroParameterCandidate_DoesNotCrashTheGenerator()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Attr("x", MissingMethod() + typeof(Probe).Name).PreventDefault(true, false);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "CS8785");
        AssertSingleBCF3015(result, source);
    }

    /// <summary>
    /// One written argument against two same-named <c>[ViewPart]</c> overloads, the one-parameter shape
    /// and a two-parameter shape whose second parameter is required. Resolution failure hands both back
    /// as candidates regardless of arity, so <c>FillsEveryParameter</c> is what excludes the two-parameter
    /// overload (its second parameter is unfilled) before <c>TrySelectCandidate</c> ever has to compare
    /// the two candidates' shapes — a decoration overload group can't isolate this the same way, because
    /// their shorter member (see <c>Div.Attr(string)</c>) always fills on the shared prefix and the group
    /// refuses on arity either way. Disabling this check (loop skipped, either boolean negated, or the
    /// rejection return flipped to accept) lets the two-parameter overload wrongly pass, and
    /// <c>AreInterchangeableOverloads</c>' own arity mismatch then refuses the whole group.
    /// </summary>
    [Fact]
    public void ViewPartOverloadMissingRequiredParameter_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                [ViewPart]
                private static View Label(string value) => Span[value];

                [ViewPart]
                private static View Label(string value, string extra) => Span[value];

                protected override View Body => Label(MissingMethod() + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    /// <summary>
    /// One written argument against two same-arity <c>[ViewPart]</c> overloads whose one parameter is
    /// named differently in each (<c>value</c> vs <c>count</c>) and typed differently to keep the pair a
    /// legal overload (parameter names alone do not distinguish a signature). Both fill under
    /// <c>FillsEveryParameter</c>, so <c>AreInterchangeableOverloads</c>' own name comparison is what has
    /// to refuse the pair; disabling it (the loop bound widened past the array, or the name/<c>IsParams</c>
    /// disjunction narrowed to a conjunction so a differing name alone no longer trips it) wrongly accepts
    /// the group and reports BCF3015 through whichever candidate <see cref="TrySelectCandidate"/> tried
    /// first, instead of leaving the call refused and the body at BCF1003.
    /// </summary>
    [Fact]
    public void ViewPartOverloadsWithDifferingParameterNames_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                [ViewPart]
                private static View Label(string value) => Span[value];

                [ViewPart]
                private static View Label(int count) => Span[count.ToString()];

                protected override View Body => Label(MissingMethod() + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    /// <summary>
    /// Two <c>using static</c> imports each bring a same-named, same-arity, one-string-parameter method into
    /// one bare call's candidate group: <c>Html.Raw</c> (<see cref="SurfaceMethodKind.Raw"/>) and a
    /// <c>[ViewPart]</c> declared on an unrelated helper type (<see cref="SurfaceMethodKind.None"/>). Both
    /// bind and fill the one written, poisoned argument, so <c>AreInterchangeableOverloads</c>' own kind
    /// comparison is what has to refuse the pair; disabling it (the branch answering <see langword="true"/>
    /// instead of refusing on a kind mismatch) wrongly accepts the group and reports BCF3015 through
    /// <c>Html.Raw</c>, the first candidate <see cref="TrySelectCandidate"/> tries, instead of leaving the
    /// call refused and the body at BCF1003.
    /// </summary>
    [Fact]
    public void SameNameFromTwoUsingStaticImportsWithDifferentKind_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            using static T.Helpers;

            namespace T;

            public static class Helpers
            {
                [ViewPart]
                public static View Raw(string value) => Span[value];
            }

            public partial class Host : BodyComponentBase
            {
                protected override View Body => Raw(MissingMethod() + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    /// <summary>
    /// A second <c>using static</c> brings a same-named, same-arity generic <c>ForEach&lt;TItem&gt;</c> into
    /// scope from a <c>[ViewPart]</c> declared on an unrelated helper type, exactly matching
    /// <c>Html.ForEach&lt;T&gt;</c>'s three parameters after substitution. Neither is more specific than the
    /// other, so the call's own <c>GetSymbolInfo</c> answers null with both as candidates -- the state
    /// <see cref="UnresolvedValueTypeScanner.IsHtmlForEachInScope"/> exists to recover from, reached before
    /// the ordinary candidate-gathering loop below it ever runs. Disabling that recovery (removing its
    /// call site's own <c>return</c>, or the inner loop's) leaves <c>candidates</c> built from
    /// <c>symbolInfo.CandidateSymbols</c> instead, where the two same-named methods carry different
    /// <c>SurfaceMethodKind</c>s and <c>AreInterchangeableOverloads</c> refuses the pair, reporting BCF1003
    /// through the whole call rather than BCF3015 through the unresolved <c>key</c> argument.
    /// <c>Helpers.ForEach</c> has to stay generic to keep the two candidates tied on specificity; declaring
    /// it as a <c>[ViewPart]</c> generic method is itself unsupported (BCF1002), which this test also
    /// expects rather than works around.
    /// </summary>
    [Fact]
    public void ForEachSameNameFromTwoUsingStaticImportsWithDifferentKind_ReportsBCF3015()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            using static T.Helpers;

            namespace T;

            public static class Helpers
            {
                [ViewPart]
                public static View ForEach<TItem>(
                    IEnumerable<TItem> source, Func<TItem, object?>? key, Func<TItem, View> content) => Span["x"];
            }

            public partial class Host : BodyComponentBase
            {
                private readonly int[] _items = [1];

                protected override View Body =>
                    ForEach(_items, key: _ => typeof(Probe), content: i => Div[i.ToString()]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1002");
        AssertSingleBCF3015(result, source);
    }

    /// <summary>
    /// One written argument against a single <c>[ViewPart]</c> overload with two required parameters, so
    /// the call underfills it. <c>AddRecognizedCandidate</c> is reached twice for this same method — once
    /// from the invocation's own <c>CandidateSymbols</c> and once from resolving the bare method-group
    /// reference (<c>invocation.Expression</c>) on its own — and its dedup loop is what collapses that pair
    /// back into a single candidate. With one candidate, <c>TrySelectCandidate</c>'s <c>candidates.Count
    /// == 1</c> fast path returns it "as it stands" without asking <c>FillsEveryParameter</c>, so the call
    /// still reports through its one bound argument. Disabling the dedup leaves both duplicate entries in
    /// the list, forcing the multi-candidate loop instead — which does ask
    /// <c>FillsEveryParameter</c>, finds the same underfilled method twice, and refuses both, leaving the
    /// body at BCF1003 instead of naming the type that could not be resolved.
    /// </summary>
    [Fact]
    public void DuplicateCandidateFromExpressionAndInvocationInfo_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                [ViewPart]
                private static View Label(string value, string extra) => Span[value];

                protected override View Body => Label(MissingMethod() + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    /// <summary>
    /// A content-taking <c>[ViewPart]</c>'s bracketed content, reached through this scanner's failure
    /// recovery once the part's own argument fails to resolve. <c>TryGetRecognizedIndexer</c> resolves the
    /// brackets to <c>SlotView</c>'s own indexer, which <c>IsRecognized</c> deliberately does not admit
    /// (class remarks on <see cref="IsRecognized(IPropertySymbol, KnownSymbols)"/>): the content slot is
    /// not a surface child list this scanner has an arm for, so the unresolved type inside it stays the
    /// generic BCF1003. Widening that pattern to admit anything but <c>ChildrenIndexerKind.Element</c>
    /// wrongly recognizes the content indexer too, and the resulting walk reports BCF3015 on a name this
    /// scanner was never written to reach.
    /// </summary>
    [Fact]
    public void ContentSlotIndexerWithSiblingUnselectedInvocation_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                [ViewPart]
                private static SlotView Card(string title) => Div.Class("card")[H2[title], Slot];

                protected override View Body =>
                    Card(MissingMethod() + "t")[Div[typeof(Probe).Name]];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// A name nested directly inside an unselected invocation's own argument list, rather than beside it.
    /// <c>IsInsideUnselectedInvocation</c>'s ancestor walk must find this suppression by climbing past the
    /// nearer, unrelated syntax between the name and the invocation (a <c>TypeOfExpressionSyntax</c>, its
    /// argument, the argument list), none of which is itself an invocation or element access.
    /// </summary>
    /// <remarks>
    /// Requiring every ancestor to answer the predicate, rather than only one, would fail on the first
    /// such node and stop suppressing every name this scanner is meant to leave alone -- <see
    /// cref="AttrNonConstantNameSelectedInvocationSibling_UnresolvedType_ReportsBCF3015"/>'s remarks
    /// record the same nesting as what keeps a <em>selected</em> sibling's report reachable; this test
    /// pins the complementary case, that an <em>unselected</em> parent's own suppression still reaches
    /// through the same climb.
    /// </remarks>
    [Fact]
    public void NestedInUnselectedInvocation_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Attr("data-type", MissingMethod(typeof(Probe)));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// An unresolved <c>.Select(...)</c> call, syntactically shaped like a spliced child list's
    /// projection but written where children are not read at all -- an <c>.Attr</c> value -- so its
    /// invocation is never a spread element's operand.
    /// </summary>
    /// <remarks>
    /// <c>IsSplicedSelect</c> exists to exempt exactly the shape <see cref="SpliceSyntax"/> matches from
    /// the ordinary unselected-invocation suppression, on the ground that the sweep deliberately walks
    /// into a genuine splice and must not suppress a value under it on the way out. Answering that
    /// exemption from either half of its check alone, rather than both together, wrongly exempts this
    /// call too: it matches <c>SpliceSyntax.IsProjection</c> by name and arity, but its parent is an
    /// <c>ArgumentSyntax</c>, not a <c>SpreadElementSyntax</c>, so it was never reached by a deliberate
    /// splice walk and the ordinary suppression is what is supposed to answer for it.
    /// </remarks>
    [Fact]
    public void UnspreadSelectShapedCall_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Div.Attr("x", MissingSource.Select(i => typeof(Probe)));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// A misnamed argument against a two-parameter <c>[ViewPart]</c> overload, single-candidate so
    /// <c>TrySelectCandidate</c>'s fast path never asks <c>FillsEveryParameter</c> -- validation runs
    /// only through <c>ScanRenderExpression</c>'s own <c>BindArguments</c> call, which is what
    /// <c>HasValidArgumentOrder</c> and <c>FindParameter</c> gate.
    /// </summary>
    /// <remarks>
    /// <c>FindParameter</c>'s not-found sentinel has to be negative for both of its readers'
    /// <c>&lt; 0</c> checks to catch it. Flipping the sign turns a name nothing declares into the
    /// ordinal one past the search's start: with a single-parameter overload that still overflows the
    /// bounds check <c>TryBindFallback</c> applies elsewhere and this scanner reports nothing either
    /// way, but a second parameter puts the wrong ordinal back in bounds, so the misnamed argument binds
    /// silently to <c>extra</c> instead of being refused, and its own unresolved name is walked and
    /// reported as if it had been written there correctly.
    /// </remarks>
    [Fact]
    public void MisnamedArgumentAgainstMultiParameterOverload_DoesNotReportBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                [ViewPart]
                private static View Label(string value, string extra = "") => Span[value + extra];

                protected override View Body =>
                    Label(bogus: MissingMethod() + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// A candidate that only fills by leaving an optional parameter unwritten, alongside a second,
    /// same-named overload that never fills at all (one written argument against three required
    /// parameters, always short by two regardless of this mutant).
    /// </summary>
    /// <remarks>
    /// <c>FillsEveryParameter</c>'s optional exemption is what lets the first overload pass; widening its
    /// guard to check <em>any</em> non-params parameter -- optional ones included -- for a written
    /// argument wrongly refuses that overload for the same reason <c>Html.If</c>'s own <c>otherwise</c>
    /// exists to be omittable (class remarks). With both candidates refused, <c>TrySelectCandidate</c>
    /// names no method and this scanner cannot recover the type that would resolve BCF3015, so the body
    /// is left with only the earlier BCF1003 that generic compile failures like a MissingMethod call
    /// already report.
    /// </remarks>
    [Fact]
    public void SecondViewPartOverloadUnderfillsWhileFirstOmitsOptional_UnresolvedType_ReportsBCF3015()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                [ViewPart]
                private static View Label(string value, string extra = "") => Span[value + extra];

                [ViewPart]
                private static View Label(string value1, string value2, string value3) =>
                    Span[value1 + value2 + value3];

                protected override View Body => Label(MissingMethod() + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBCF3015(result, source);
    }

    private static void AssertSingleBCF3015(
        GeneratorRunResult result,
        string source)
    {
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3012");
        Assert.Empty(result.GeneratedSources);
        Assert.Equal("Probe", SourceText.From(source).ToString(diagnostic.Location.SourceSpan));
    }

    private static ExpressionAnalysis AnalyzeValueExpression(string expression)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host<TValue> : BodyComponentBase
            {
                protected override View Body => Div["ok"];
                private object? ProbeExpression() => {{expression}};

                private sealed class Wrapper<T>
                {
                }

                private sealed class Outer<T>
                {
                    public sealed class Inner
                    {
                    }
                }
            }
            """;

        var compilation = CompilationTestHost.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var host = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(static declaration => declaration.Identifier.ValueText == "Host");
        var method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static declaration => declaration.Identifier.ValueText == "ProbeExpression");
        var containingType = (INamedTypeSymbol)model.GetDeclaredSymbol(host)!;
        var knownSymbols = KnownSymbols.TryCreate(compilation)!;
        var context = new ViewPartBodyContext(
            model,
            containingType,
            method.Identifier.ValueText,
            knownSymbols,
            ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
            isInlinedAtCallSites: false,
            default);
        var syntax = method.ExpressionBody!.Expression;
        var template = ExpressionTemplateFactory.Create(syntax, context);

        return new ExpressionAnalysis(
            source,
            syntax,
            template,
            context.Diagnostics.ToImmutable(),
            model,
            containingType);
    }
}
