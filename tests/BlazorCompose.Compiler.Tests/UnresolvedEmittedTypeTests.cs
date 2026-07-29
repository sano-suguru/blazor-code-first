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
