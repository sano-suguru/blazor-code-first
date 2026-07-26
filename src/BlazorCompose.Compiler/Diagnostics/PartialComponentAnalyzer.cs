using System.Collections.Immutable;
using BlazorCompose.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorCompose.Compiler.Diagnostics;

/// <summary>
/// Reports BC1001 when a class that declares the design-time expression override (<c>Body</c> on
/// <c>ComposeComponentBase</c>, <c>Chrome</c> on <c>ComposeLayoutBase</c>) is not declared <c>partial</c>.
/// A class that only inherits a Compose base without declaring the override is not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PartialComponentAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.BC1001];

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var symbol = (INamedTypeSymbol)context.Symbol;

        if (symbol.TypeKind != TypeKind.Class)
            return;

        if (ComposeComponentBaseFacts.FindComposeBase(symbol) is not { } composeBase)
            return;

        // BC1001 exists so the generator can emit RenderView into this class. A type that inherits a
        // Compose base but declares no design-time expression override of its own has nothing generated
        // into it and therefore needs no partial modifier: an intermediate abstract base, a leaf whose
        // base already declares the override, or a re-abstraction (`abstract override View Body { get; }`,
        // which is legal and has no getter body). Narrowing here is what keeps those from being told to
        // add a modifier that would change nothing.
        if (ComposeComponentBaseFacts.FindDesignTimeExpressionName(symbol) is not { } expressionName ||
            !DeclaresDesignTimeExpression(symbol, expressionName))
        {
            return;
        }

        ClassDeclarationSyntax? firstDeclaration = null;

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(context.CancellationToken) is not ClassDeclarationSyntax classDecl)
                continue;

            if (classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                return;

            firstDeclaration ??= classDecl;
        }

        if (firstDeclaration is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.BC1001,
                firstDeclaration.Identifier.GetLocation(),
                symbol.Name,
                expressionName,
                composeBase.Name));
        }
    }

    /// <summary>
    /// True when <paramref name="symbol"/> itself declares the design-time expression override with a
    /// concrete getter — the only case where the generator emits a <c>RenderView</c> into this class.
    /// Resolved from symbols rather than syntax so a class split across several partial declarations is
    /// judged once, and re-abstraction (<c>abstract override</c>) is excluded because it declares no
    /// getter to translate.
    /// </summary>
    private static bool DeclaresDesignTimeExpression(INamedTypeSymbol symbol, string expressionName)
    {
        foreach (var member in symbol.GetMembers(expressionName))
        {
            if (member is IPropertySymbol { IsOverride: true, IsAbstract: false })
                return true;
        }

        return false;
    }
}
