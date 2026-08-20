using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Validates a discovered <c>[ViewPart]</c> method against the supported static-expansion contract and,
/// when valid, builds its symbol-free <see cref="ViewPartDefinition"/>. Invalid source declarations
/// still yield a registry <see cref="ViewPartDefinitionEntry"/> (with a null definition) plus a single
/// value-equal BCF1002 diagnostic so expansion can distinguish an already-diagnosed source declaration
/// from a metadata-only method.
/// </summary>
internal static class ViewPartDefinitionFactory
{
    /// <summary>
    /// The type name a parameter's expansion local is declared with. <c>FullyQualifiedFormat</c> on its own
    /// carries no nullable modifier, which made a <c>string?</c> parameter's local be declared
    /// non-nullable and warn CS8600 the moment a null-bearing argument was assigned to it — a build
    /// failure under <c>TreatWarningsAsErrors</c>, in a file the call site's author does not write (#235).
    /// </summary>
    /// <remarks>
    /// Added to the format's options rather than replacing them: <c>FullyQualifiedFormat</c>'s own
    /// <see cref="SymbolDisplayMiscellaneousOptions.UseSpecialTypes"/> and
    /// <see cref="SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers"/> are both load-bearing here,
    /// and <c>WithMiscellaneousOptions</c> would drop them.
    /// </remarks>
    private static readonly SymbolDisplayFormat LocalDeclarationFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static ViewPartDiscoveryResult Create(
        GeneratorAttributeSyntaxContext attributeContext,
        KnownSymbols? knownSymbols,
        CancellationToken cancellationToken)
    {
        var method = (IMethodSymbol)attributeContext.TargetSymbol;
        var declaration = (MethodDeclarationSyntax)attributeContext.TargetNode;

        var methodKey = MethodKey.Create(method);
        var displayName = method.Name;

        // The body's shape is read once, here, and the answer travels: reading it again inside the build
        // would walk the whole block a second time on every keystroke to reach the same two values.
        var bodyAccepted = TryReadBody(declaration, out var bodyExpression, out var bodyStatements);

        var invalidReason = ValidateDeclaration(method, bodyAccepted, knownSymbols);
        if (invalidReason is not null)
            return Invalid(methodKey, displayName, declaration, invalidReason);

        var definition = TryBuildDefinition(
            attributeContext,
            method,
            declaration,
            bodyExpression,
            bodyStatements,
            knownSymbols!,
            cancellationToken,
            out var bodyDiagnostics);

        if (definition is null)
        {
            return new ViewPartDiscoveryResult(
                new ViewPartDefinitionEntry(
                    methodKey, displayName, declaration.SyntaxTree.FilePath,
                    Definition: null, DeclarationDiagnosticReported: true),
                bodyDiagnostics);
        }

        return new ViewPartDiscoveryResult(
            new ViewPartDefinitionEntry(
                methodKey, displayName, declaration.SyntaxTree.FilePath,
                definition, DeclarationDiagnosticReported: false),
            bodyDiagnostics);
    }

    private static string? ValidateDeclaration(
        IMethodSymbol method,
        bool bodyAccepted,
        KnownSymbols? knownSymbols)
    {
        // A view part is never an extension member (DESIGN.md §4.3, #203). Rejected ahead of the static
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

        // A view part declared in a generic containing type (or nested inside one) would leak the
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

        if (!bodyAccepted)
            return "must reach one return, with only local declarations and expression statements ahead of it";

        var viewType = knownSymbols?.ViewType;
        if (viewType is null)
            return "must return BlazorCodeFirst.View";

        // Two return types, and the choice is the whole of how a part declares whether it takes content
        // (#176). View is the part that does not, and is called bare; SlotView is the part that does, and
        // is called with brackets. Nothing else is a view part.
        var takesContent = knownSymbols!.TakesContent(method);

        if (!takesContent && !SymbolEqualityComparer.Default.Equals(method.ReturnType, viewType))
            return "must return BlazorCodeFirst.View, or BlazorCodeFirst.SlotView to take content";

        foreach (var parameter in method.Parameters)
        {
            if (parameter.IsParams)
                return "params parameters are unsupported";

            // A by-reference parameter (ref, out, in, or ref readonly) cannot be reproduced by the
            // static-expansion contract, which lowers each argument to a plain typed local passed by
            // value. Reject every RefKind other than None with a single reason.
            if (parameter.RefKind != RefKind.None)
                return "by-reference parameters are unsupported";

            if (knownSymbols.IsContentType(parameter.Type))
            {
                // A View parameter is an additional content slot (#34), and only a part that already takes
                // content may have one. Allowing it on a View-returning part would readmit the positional
                // spelling that #176 decided against -- Card("Profile", P["本文"]) -- as a second way to write
                // what brackets write, which is the one thing that decision rules out.
                if (!takesContent)
                {
                    return "View parameters are content slots and require a SlotView return type; "
                        + "a part returning View takes no content";
                }

                // An omitted optional would have to mean "no content", and default(View) is not that: it is
                // the inert marker, which the expander has no subtree for. Requiring the argument keeps the
                // question from arising, and C# then reports a forgotten slot at the call site.
                if (parameter.IsOptional)
                    return $"View parameter '{parameter.Name}' must not be optional";

                continue;
            }

            // ElementView is rejected symmetrically to the *old* View rejection: a childless element is an
            // ElementView rather than a View, so admitting it would give content a second parameter type
            // and, with it, a second spelling. A caller passes Div as content by writing Div[…] or
            // Fragment(Div), both of which are Views.
            if (knownSymbols.ElementViewType is { } elementViewType
                && SymbolEqualityComparer.Default.Equals(parameter.Type, elementViewType))
            {
                return "ElementView parameters are unsupported";
            }
        }

        return null;
    }

    /// <summary>
    /// The expression a view part body returns, and the statements written ahead of that return. Both body
    /// forms reach here: <c>=&gt; e</c>, and the block a design-time expression getter and a <c>ForEach</c>
    /// content lambda accept, which <see cref="RenderExpressionAnalyzer.TryReadTransplantableBlock"/>
    /// reads (ARCHITECTURE.md §2.3 Transplantable). Returns <see langword="false"/> for a body outside
    /// both, which earns BCF1002 at the declaration.
    /// </summary>
    private static bool TryReadBody(
        MethodDeclarationSyntax declaration,
        out ExpressionSyntax expression,
        out ImmutableArray<StatementSyntax> statements)
    {
        statements = [];

        if (declaration.ExpressionBody is { Expression: var expressionBody })
        {
            expression = expressionBody;
            return true;
        }

        if (declaration.Body is { } block
            && RenderExpressionAnalyzer.TryReadTransplantableBlock(block, out var leading, out var returned))
        {
            expression = returned;
            statements = leading;
            return true;
        }

        expression = null!;
        return false;
    }

    private static ViewPartDefinition? TryBuildDefinition(
        GeneratorAttributeSyntaxContext attributeContext,
        IMethodSymbol method,
        MethodDeclarationSyntax declaration,
        ExpressionSyntax bodyExpression,
        ImmutableArray<StatementSyntax> bodyStatements,
        KnownSymbols knownSymbols,
        CancellationToken cancellationToken,
        out ImmutableArray<DiagnosticInfo> diagnostics)
    {
        var ordinals = ImmutableDictionary.CreateBuilder<ISymbol, int>(SymbolEqualityComparer.Default);
        var contentOrdinals = ImmutableHashSet.CreateBuilder<int>();
        var parameters = ImmutableArray.CreateBuilder<ViewPartParameter>(method.Parameters.Length);
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

            var isContent = knownSymbols.IsContentType(parameter.Type);

            ordinals[parameter] = parameter.Ordinal;
            if (isContent)
                contentOrdinals.Add(parameter.Ordinal);

            parameters.Add(new ViewPartParameter(
                parameter.Ordinal,
                parameter.Type.ToDisplayString(LocalDeclarationFormat),
                isContent));
        }

        // The slot takes the ordinal after the last parameter, and is registered in the same map so that
        // ViewPartBodyContext.PushRenderVariable's ordinal arithmetic (_parameterOrdinals.Count plus the
        // render-variable depth) accounts for it without being told about it. The ordinal space therefore
        // reads: parameters, then the slot, then the scoped render variables. What separates a content
        // ordinal from a value ordinal is the set below, not which map it lives in.
        var hasSlot = knownSymbols.TakesContent(method) && knownSymbols.SlotProperty is not null;
        if (hasSlot)
        {
            var slotOrdinal = method.Parameters.Length;
            ordinals[knownSymbols.SlotProperty!] = slotOrdinal;
            contentOrdinals.Add(slotOrdinal);

            // Counted from syntax rather than from the classified body, which would only see the slots that
            // reached a translatable position. Asked only when there is a slot to count: a part returning View
            // cannot have one, and a Slot written in its body is reported at the reference by ClassifySlot.
            // The leading statements are counted too: a Slot named there is written into the expansion just
            // as one in the returned expression is, so leaving them out would let `var v = Slot;` pass as
            // "never named" and then place the content twice.
            var slotReferences = CountSlotReferences(
                bodyExpression, attributeContext.SemanticModel, knownSymbols, cancellationToken);

            foreach (var statement in bodyStatements)
            {
                slotReferences += CountSlotReferences(
                    statement, attributeContext.SemanticModel, knownSymbols, cancellationToken);
            }

            if (slotReferences != 1)
            {
                diagnostics = [DiagnosticInfo.Create(
                    DiagnosticDescriptors.BCF3025,
                    declaration.Identifier.GetLocation(),
                    [slotReferences == 0
                        ? $"is never named in '{method.Name}', whose SlotView return type requires it"
                        : $"is named {slotReferences} times in '{method.Name}'"])];
                return null;
            }
        }

        var context = new ViewPartBodyContext(
            attributeContext.SemanticModel,
            method.ContainingType,
            method.Name,
            knownSymbols,
            ordinals.ToImmutable(),
            isInlinedAtCallSites: true,
            cancellationToken,
            contentOrdinals.ToImmutable());

        var body = RenderExpressionAnalyzer.Analyze(bodyStatements, bodyExpression, context);
        if (body is null)
        {
            // The same failure-path sweeps the component host runs, from the one list both share: report
            // the specific cause rather than falling through to the generic "not statically sequenceable"
            // text. See FailurePathScanners.
            FailurePathScanners.ReportAll(bodyExpression, context);

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
        return new ViewPartDefinition(
            parameters.ToImmutable(),
            context.AccessRequirements.ToImmutable(),
            body,
            hasSlot);
    }

    /// <summary>
    /// How many times <paramref name="body"/> names <c>Html.Slot</c>, counting both the unqualified
    /// spelling under <c>using static</c> and the qualified escape hatch, and counting references inside
    /// nested lambdas (an <c>If</c> branch) as well.
    /// </summary>
    /// <remarks>
    /// Prefiltered on the name so the semantic query is asked only of candidates: a body of any size holds
    /// far more identifiers than it holds slots. The symbol comparison is what decides — a member of the
    /// author's own named <c>Slot</c> is not this hole, which is the shadowing case #127 records for element
    /// helpers.
    /// </remarks>
    private static int CountSlotReferences(
        SyntaxNode body,
        SemanticModel semanticModel,
        KnownSymbols knownSymbols,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var name in body.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
            if (name.Identifier.ValueText != "Slot")
                continue;

            // The qualified form is a MemberAccessExpressionSyntax whose Name is this node; asking for the
            // symbol of the name itself answers for both spellings and counts neither twice.
            if (semanticModel.GetSymbolInfo(name, cancellationToken).Symbol is IPropertySymbol resolved
                && knownSymbols.IsSlot(resolved))
            {
                count++;
            }
        }

        return count;
    }

    private static ViewPartDiscoveryResult Invalid(
        string methodKey,
        string displayName,
        MethodDeclarationSyntax declaration,
        string reason)
    {
        var diagnostic = BuildDiagnostic(declaration, displayName, reason);
        return new ViewPartDiscoveryResult(
            new ViewPartDefinitionEntry(
                methodKey, displayName, declaration.SyntaxTree.FilePath,
                Definition: null, DeclarationDiagnosticReported: true),
            [diagnostic]);
    }

    private static DiagnosticInfo BuildDiagnostic(
        MethodDeclarationSyntax declaration,
        string displayName,
        string reason) =>
        DiagnosticInfo.Create(
            DiagnosticDescriptors.BCF1002,
            declaration.Identifier.GetLocation(),
            [DiagnosticDescriptors.ViewPartSubject(displayName), reason]);

}
