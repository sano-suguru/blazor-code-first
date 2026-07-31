using System.Collections.Immutable;
using System.Linq;
using BlazorCompose.Compiler.Analysis;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCompose.Compiler.Tests;

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
    public void ParamTypeValue_UnresolvedType_ReportsBC3015AndEmitsNoSource()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using Microsoft.AspNetCore.Components;
            using static BlazorCompose.Html;

            namespace T;

            public sealed class Real : ComponentBase
            {
                [Parameter]
                public Type? Kind { get; set; }
            }

            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Component<Real>().Param(r => r.Kind, typeof(Probe));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BC3015");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id is "BC3012" or "BC1003");
        Assert.Empty(result.GeneratedSources);
        Assert.Equal("Probe", SourceText.From(source).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void IfCondition_UnresolvedType_ReportsBC3015()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    If(typeof(Probe) == typeof(object), () => Div());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBC3015(result, source);
    }

    [Fact]
    public void DeclarationPatternValue_UnresolvedType_ReportsBC3015AndEmitsNoSource()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Div().Class((new object() is Probe value).ToString());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BC3015");

        Assert.Empty(result.GeneratedSources);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id is "BC1003" or "BC3012");
        Assert.Equal("Probe", SourceText.From(source).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void OutOfPositionNamedThenUnresolvedPositional_RemainsLanguageAndBC1003Owned()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    If(then: () => Div(), Has<Probe>());

                private static bool Has<T>() => true;
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.OutputCompilation.GetDiagnostics(), static d => d.Id == "CS8323");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BC1003");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
    }

    [Theory]
    [InlineData("""If(condition: typeof(Probe) == typeof(object), () => Div())""")]
    [InlineData("""If(condition: typeof(Probe) == typeof(object), then: () => Div())""")]
    [InlineData("""Element(tag: "section", typeof(Probe).Name)""")]
    public void LegalNamedArgumentShapes_UnresolvedTypeStillReportsBC3015(string body)
    {
        var source = $$"""
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body => {{body}};
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBC3015(result, source);
    }

    [Fact]
    public void ForEachKey_UnresolvedType_ReportsBC3015()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                private readonly int[] _items = [1];

                protected override View Body =>
                    ForEach(_items, key: _ => typeof(Probe), content: i => Div(i.ToString()));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBC3015(result, source);
    }

    [Fact]
    public void ReorderedForEachKey_UnresolvedType_ReportsBC3015()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                private readonly int[] _items = [1];

                protected override View Body =>
                    ForEach(content: i => Div(i.ToString()), source: _items, key: _ => typeof(Probe));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBC3015(result, source);
    }

    [Fact]
    public void AttributeValue_UnresolvedType_ReportsBC3015()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Div().Attr("data-type", typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBC3015(result, source);
    }

    [Fact]
    public void StaticDecorationValue_UnresolvedType_RemainsBC1003Only()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Decorations.Attr(Div(), "data-type", typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, static d => d.Id == "BC1003");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void RejectedDecorationValue_UnresolvedType_RemainsBC3008Owned()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Raw("<b>x</b>").Class(typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, static d => d.Id == "BC3008");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id is "BC1003" or "BC3015");
    }

    [Fact]
    public void ReorderedAttrWithInvalidName_DoesNotReportValueTypeBC3015()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Div().Attr(value: typeof(Probe).Name, name: typeof(string).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BC3011");
    }

    [Fact]
    public void DuplicateAttribute_UnresolvedValueType_RemainsBC3010Owned()
    {
        // A duplicate binding is rejected before its value is normalized, so the value never becomes
        // emitted code and its type is not the author's problem — the same ownership the event channel
        // and both BC3011 paths already had.
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Div().Id("first").Attr("id", typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BC3010");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void ComposableArgument_UnresolvedType_ReportsBC3015()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                [Composable]
                private static View Label(Type value) => Span(value.Name);

                protected override View Body => Label(typeof(Probe));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBC3015(result, source);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC1002");
    }

    [Fact]
    public void LayoutChrome_UnresolvedType_ReportsBC3015()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeLayoutBase
            {
                protected override View Chrome =>
                    Div(Body).Class(typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBC3015(result, source);
    }

    [Fact]
    public void ComposableBody_UnresolvedType_ReportsBC3015Once()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                [Composable]
                private static View Card() => Div().Class(typeof(Probe).Name);

                protected override View Body => Card();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        AssertSingleBC3015(result, source);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC1002");
    }

    [Fact]
    public void DirectComponentUnresolvedType_RemainsBC3012Only()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Probe>();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Single(result.Diagnostics, static d => d.Id == "BC3012");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void TwoLocations_ReportTwoBC3015Diagnostics()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Div().Class(typeof(Probe).Name + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostics = result.Diagnostics.Where(static d => d.Id == "BC3015").ToArray();

        Assert.Equal(2, diagnostics.Length);
        Assert.NotEqual(diagnostics[0].Location.SourceSpan, diagnostics[1].Location.SourceSpan);
    }

    [Theory]
    [InlineData("Div().Attr(typeof(Probe).Name, \"value\")")]
    [InlineData("Div().On(typeof(Probe).Name, () => { })")]
    [InlineData("Element(typeof(Probe).Name)")]
    public void CompileTimeOnlyFactoryArgument_UnresolvedType_DoesNotReportBC3015(string body)
    {
        var source = $$"""
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body => {{body}};
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.Contains(result.Diagnostics, static d => d.Id is "BC3009" or "BC3011");
    }

    [Fact]
    public void ParamSelectorUnresolvedType_DoesNotReportBC3015()
    {
        const string source = """
            using BlazorCompose;
            using Microsoft.AspNetCore.Components;
            using static BlazorCompose.Html;

            namespace T;

            public sealed class Real : ComponentBase
            {
                [Parameter]
                public string Name { get; set; } = "";
            }

            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Component<Real>().Param((Probe r) => r.Name, "value");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.Contains(result.Diagnostics, static d => d.Id is "BC1003" or "BC3005");
    }

    [Theory]
    [InlineData(
        "Component<Real>().Param(r => _other.Kind, typeof(Probe))",
        "BC3005")]
    [InlineData(
        "Component<Real>().Param(r => r.NotAParameter, typeof(Probe))",
        "BC3006")]
    [InlineData(
        "Component<Real>().Param(r => r.Kind, typeof(string)).Param(r => r.Kind, typeof(Probe))",
        "BC3007")]
    public void RejectedParamValue_UnresolvedType_RemainsOwnedByExistingDiagnostic(
        string body,
        string expectedDiagnostic)
    {
        var source = $$"""
            using System;
            using BlazorCompose;
            using Microsoft.AspNetCore.Components;
            using static BlazorCompose.Html;

            namespace T;

            public sealed class Real : ComponentBase
            {
                [Parameter]
                public Type? Kind { get; set; }

                public Type? NotAParameter { get; set; }
            }

            public partial class Host : ComposeComponentBase
            {
                private readonly Real _other = new();

                protected override View Body => {{body}};
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, d => d.Id == expectedDiagnostic);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id is "BC1003" or "BC3015");
    }

    [Fact]
    public void ForEachParameterDeclarationUnresolvedType_DoesNotReportBC3015()
    {
        const string source = """
            using System.Collections.Generic;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                private readonly List<int> _items = [];

                protected override View Body =>
                    ForEach(_items, key: (Probe item) => item, content: item => Div());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BC1003");
    }

    [Fact]
    public void LambdaParameterTypeInValue_DoesNotReportBC3015()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Div().Class((Probe value) => "x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BC1003");
    }

    [Fact]
    public void PureOverloadFailure_UnresolvedType_DoesNotReportBC3015()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                private static View Pick<T>(int value) => Div();

                protected override View Body => Pick<Probe>("wrong");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BC1003");
    }

    [Fact]
    public void UserDefinedForEachOverloadFailure_DoesNotReportBC3015()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using BlazorCompose;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                private readonly List<int> _items = [];

                private static View ForEach<T>(
                    int source,
                    Func<T, object?> key,
                    Func<T, View> content) => Html.Div();

                protected override View Body =>
                    ForEach(_items, key: _ => typeof(Probe), content: item => Html.Div());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BC1003");
    }

    [Fact]
    public void QualifiedUserDefinedForEachOverloadFailure_DoesNotReportBC3015()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                private readonly List<int> _items = [];

                protected override View Body =>
                    Helpers.ForEach(
                        _items,
                        key: _ => typeof(Probe),
                        content: item => Div());

                private static class Helpers
                {
                    public static View ForEach<T>(
                        int source,
                        Func<T, object?> key,
                        Func<T, View> content) => Div();
                }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BC1003");
    }

    [Theory]
    [InlineData("nameof(Probe)")]
    [InlineData("typeof(global::Generated.Probe).Name")]
    public void SelfContainedValueUnderFailingOuterRoute_DoesNotReportBC3015(string value)
    {
        var source = $$"""
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Div().Attr(typeof(string).Name, {{value}});
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BC3011");
    }

    [Fact]
    public void EscapedNameofMethod_UnresolvedType_ReportsBC3015()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                private static string @nameof(Type value) => value.Name;

                protected override View Body =>
                    Div().Attr("data-x", @nameof(typeof(Probe)));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void ValueSiblingOfUnselectedInvocation_UnresolvedType_ReportsBC3015()
    {
        const string source = """
            using System;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                protected override View Body =>
                    Div().Class(MissingMethod() + typeof(Probe).Name);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BC3015");
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
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BC3015");

        Assert.Equal(
            "Missing",
            SourceText.From(result.Source).ToString(diagnostic.Span));
    }

    [Fact]
    public void GlobalQualifiedUnresolvedType_IsPreserved()
    {
        var result = AnalyzeValueExpression("typeof(global::Generated.Probe)");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.Equal("typeof(global::Generated.Probe)", result.Template.ToCode());
    }

    [Fact]
    public void GlobalQualifiedOuter_DoesNotExemptUnqualifiedGenericArgument()
    {
        var result = AnalyzeValueExpression(
            "typeof(global::System.Collections.Generic.List<Probe>)");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.Equal("Probe", SourceText.From(result.Source).ToString(diagnostic.Span));
    }

    [Theory]
    [InlineData("ActuallyMissingValue")]
    [InlineData("MissingMethod(1)")]
    public void UnresolvedValueOrOverloadFailure_DoesNotReportBC3015(string expression)
    {
        var result = AnalyzeValueExpression(expression);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
    }

    [Theory]
    [InlineData("typeof(TValue)")]
    [InlineData("typeof(System.Text.StringBuilder)")]
    [InlineData("nameof(System.String)")]
    public void ResolvedOrTypeParameterExpression_DoesNotReportBC3015(string expression)
    {
        var result = AnalyzeValueExpression(expression);
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void ExtensionExplicitUnresolvedTypeArgument_ReportsBC3015()
    {
        var result = AnalyzeValueExpression(
            "new object[] { 1 }.Cast<Probe>().FirstOrDefault()");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.Equal("Probe", SourceText.From(result.Source).ToString(diagnostic.Span));
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC1002");
    }

    [Fact]
    public void SameContextAndLocation_ExtensionUnresolvedTypeArgumentReportsBC3015Once()
    {
        var result = AnalyzeValueExpression(
            "new object[] { 1 }.Cast<Probe>().FirstOrDefault()");
        var context = new ComposableBodyContext(
            result.SemanticModel,
            result.ContainingType,
            "ProbeExpression",
            KnownSymbols.TryCreate(result.SemanticModel.Compilation)!,
            ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
            default);

        var first = ExpressionTemplateFactory.Create(result.Expression, context);
        var second = ExpressionTemplateFactory.Create(result.Expression, context);

        Assert.Single(context.Diagnostics, static d => d.Id == "BC3015");
        Assert.DoesNotContain(context.Diagnostics, static d => d.Id == "BC1002");
        Assert.Equal(first.ToCode(), second.ToCode());
    }

    [Fact]
    public void SameContextAndLocation_ReportsBC3015Once()
    {
        var result = AnalyzeValueExpression("typeof(Probe)");
        var context = new ComposableBodyContext(
            result.SemanticModel,
            result.ContainingType,
            "ProbeExpression",
            KnownSymbols.TryCreate(result.SemanticModel.Compilation)!,
            ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
            default);

        _ = ExpressionTemplateFactory.Create(result.Expression, context);
        _ = ExpressionTemplateFactory.Create(result.Expression, context);

        Assert.Single(context.Diagnostics, static d => d.Id == "BC3015");
    }

    private static void AssertSingleBC3015(
        GeneratorRunResult result,
        string source)
    {
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3012");
        Assert.Empty(result.GeneratedSources);
        Assert.Equal("Probe", SourceText.From(source).ToString(diagnostic.Location.SourceSpan));
    }

    private static ExpressionAnalysis AnalyzeValueExpression(string expression)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host<TValue> : ComposeComponentBase
            {
                protected override View Body => Div("ok");
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
        var context = new ComposableBodyContext(
            model,
            containingType,
            method.Identifier.ValueText,
            knownSymbols,
            ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
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
