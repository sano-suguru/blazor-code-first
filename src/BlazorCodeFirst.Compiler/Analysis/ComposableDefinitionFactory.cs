using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Validates a discovered <c>[Composable]</c> method against the supported static-expansion contract and,
/// when valid, builds its symbol-free <see cref="ComposableDefinition"/>. Invalid source declarations
/// still yield a registry <see cref="ComposableDefinitionEntry"/> (with a null definition) plus a single
/// value-equal BCF1002 diagnostic so expansion can distinguish an already-diagnosed source declaration
/// from a metadata-only method.
/// </summary>
internal static class ComposableDefinitionFactory
{
    public static ComposableDiscoveryResult Create(
        GeneratorAttributeSyntaxContext attributeContext,
        KnownSymbols? knownSymbols,
        CancellationToken cancellationToken)
    {
        var method = (IMethodSymbol)attributeContext.TargetSymbol;
        var declaration = (MethodDeclarationSyntax)attributeContext.TargetNode;

        var methodKey = MethodKey.Create(method);
        var displayName = method.Name;

        var invalidReason = ValidateDeclaration(method, declaration, knownSymbols);
        if (invalidReason is not null)
            return Invalid(methodKey, displayName, declaration, invalidReason);

        var definition = TryBuildDefinition(
            attributeContext,
            method,
            declaration,
            knownSymbols!,
            cancellationToken,
            out var bodyDiagnostics);

        if (definition is null)
        {
            return new ComposableDiscoveryResult(
                new ComposableDefinitionEntry(methodKey, displayName, Definition: null, DeclarationDiagnosticReported: true),
                bodyDiagnostics);
        }

        return new ComposableDiscoveryResult(
            new ComposableDefinitionEntry(methodKey, displayName, definition, DeclarationDiagnosticReported: false),
            bodyDiagnostics);
    }

    private static string? ValidateDeclaration(
        IMethodSymbol method,
        MethodDeclarationSyntax declaration,
        KnownSymbols? knownSymbols)
    {
        // A composable is never an extension member (DESIGN.md §4.3, #203). Rejected ahead of the static
        // test so both spellings answer with the reason that is true of them rather than the instance form
        // answering "must be static". The disjunction is one question the language splits in two: Roslyn
        // answers the classic 'this' parameter with IsExtensionMethod and the C# 14 extension block with
        // ContainingType.IsExtension, and neither answers for the other.
        if (method.IsExtensionMethod || method.ContainingType.IsExtension)
            return "must not be an extension member";

        if (!method.IsStatic)
            return "must be static";

        if (method.Arity > 0)
            return "must be non-generic";

        // A composable declared in a generic containing type (or nested inside one) would leak the
        // enclosing unbound type parameter, through a parameter type such as 'T value' or a body
        // reference such as 'typeof(T)', into the using-less generated component, where that parameter
        // is not in scope. Reject the declaration up front rather than emit uncompilable expansion.
        for (var containingType = method.ContainingType;
             containingType is not null;
             containingType = containingType.ContainingType)
        {
            if (containingType.Arity > 0)
                return "containing type must be non-generic";
        }

        if (declaration.ExpressionBody is null)
            return "must be expression-bodied";

        var viewType = knownSymbols?.ViewType;
        if (viewType is null || !SymbolEqualityComparer.Default.Equals(method.ReturnType, viewType))
            return "must return BlazorCodeFirst.View";

        foreach (var parameter in method.Parameters)
        {
            if (parameter.IsParams)
                return "params parameters are unsupported";

            // A by-reference parameter (ref, out, in, or ref readonly) cannot be reproduced by the
            // static-expansion contract, which lowers each argument to a plain typed local passed by
            // value. Reject every RefKind other than None with a single reason.
            if (parameter.RefKind != RefKind.None)
                return "by-reference parameters are unsupported";

            if (SymbolEqualityComparer.Default.Equals(parameter.Type, viewType))
                return "View parameters are unsupported";

            // ElementBuilder is rejected symmetrically: a childless element is an ElementBuilder rather than
            // a View, so accepting it would readmit exactly the case the View rejection exists for. Guarded
            // on the type resolving, because it is absent from a runtime without the bracket surface and
            // SymbolEqualityComparer.Default.Equals(x, null) answers true for a null x.
            if (knownSymbols?.ElementBuilderType is { } elementBuilderType
                && SymbolEqualityComparer.Default.Equals(parameter.Type, elementBuilderType))
            {
                return "ElementBuilder parameters are unsupported";
            }
        }

        return null;
    }

    private static ComposableDefinition? TryBuildDefinition(
        GeneratorAttributeSyntaxContext attributeContext,
        IMethodSymbol method,
        MethodDeclarationSyntax declaration,
        KnownSymbols knownSymbols,
        CancellationToken cancellationToken,
        out ImmutableArray<DiagnosticInfo> diagnostics)
    {
        var ordinals = ImmutableDictionary.CreateBuilder<ISymbol, int>(SymbolEqualityComparer.Default);
        var parameters = ImmutableArray.CreateBuilder<ComposableParameter>(method.Parameters.Length);
        foreach (var parameter in method.Parameters)
        {
            // A parameter (or optional-default) type that cannot be named from another file, a file-local
            // type or one otherwise unnameable, would produce invalid generated C# at the expansion site,
            // so reject the declaration with BCF1002 instead.
            if (!TypeSymbolFacts.IsNameableInGeneratedCode(parameter.Type))
            {
                diagnostics = [BuildDiagnostic(
                    declaration,
                    method.Name,
                    $"parameter '{parameter.Name}' has a type that cannot be named in generated component code")];
                return null;
            }

            ordinals[parameter] = parameter.Ordinal;
            parameters.Add(new ComposableParameter(
                parameter.Ordinal,
                parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        var context = new ComposableBodyContext(
            attributeContext.SemanticModel,
            method.ContainingType,
            method.Name,
            knownSymbols,
            ordinals.ToImmutable(),
            cancellationToken);

        var body = RenderExpressionAnalyzer.Analyze(declaration.ExpressionBody!.Expression, context);
        if (body is null)
        {
            // The same failure-path sweeps the component host runs, from the one list both share: report
            // the specific cause rather than falling through to the generic "not statically sequenceable"
            // text. See FailurePathScanners.
            FailurePathScanners.ReportAll(declaration.ExpressionBody!.Expression, context);

            // Prefer a specific recorded unsupported-reference diagnostic (for example a referenced local
            // that cannot exist in generated code) over the generic non-SSC message.
            diagnostics = context.Diagnostics.Count > 0
                ? context.Diagnostics.ToImmutable()
                : [BuildDiagnostic(
                    declaration,
                    method.Name,
                    "body must be a statically sequenceable expression")];
            return null;
        }

        if (context.Diagnostics.Any(static d => d.IsError))
        {
            diagnostics = context.Diagnostics.ToImmutable();
            return null;
        }

        // Only non-error diagnostics (for example BCF3002) remain; the definition is valid and its
        // warnings are still surfaced.
        diagnostics = context.Diagnostics.ToImmutable();
        return new ComposableDefinition(
            parameters.ToImmutable(),
            context.AccessRequirements.ToImmutable(),
            body);
    }

    private static ComposableDiscoveryResult Invalid(
        string methodKey,
        string displayName,
        MethodDeclarationSyntax declaration,
        string reason)
    {
        var diagnostic = BuildDiagnostic(declaration, displayName, reason);
        return new ComposableDiscoveryResult(
            new ComposableDefinitionEntry(methodKey, displayName, Definition: null, DeclarationDiagnosticReported: true),
            [diagnostic]);
    }

    private static DiagnosticInfo BuildDiagnostic(
        MethodDeclarationSyntax declaration,
        string displayName,
        string reason) =>
        DiagnosticInfo.Create(
            DiagnosticDescriptors.BCF1002,
            declaration.Identifier.GetLocation(),
            [displayName, reason]);

}
