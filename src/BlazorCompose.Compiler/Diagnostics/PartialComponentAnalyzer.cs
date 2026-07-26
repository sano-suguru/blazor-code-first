using System.Collections.Immutable;
using BlazorCompose.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorCompose.Compiler.Diagnostics;

/// <summary>
/// Reports BC1001 when a class that inherits from a Compose base (<c>ComposeComponentBase</c> or
/// <c>ComposeLayoutBase</c>) is not declared <c>partial</c>.
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
                composeBase.Name));
        }
    }
}
