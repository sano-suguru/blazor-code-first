using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Normalizes a definition-side expression into a symbol-free <see cref="ExpressionTemplate"/> so it can
/// be inlined at any expansion site. The generated <c>RenderView</c> carries no <c>using</c> directives,
/// so every name that would otherwise depend on an import must be made self-contained. Replacement
/// decisions use Roslyn symbol identity, never textual substitution, so that:
/// <list type="bullet">
/// <item>identifiers bound to view part parameters become <see cref="ParameterHoleExpressionSegment"/>;</item>
/// <item>every <c>nameof(...)</c> collapses to its compile-time constant string, because the entity it
/// names (a parameter replaced by a typed local, a private definition member, or a type in scope only
/// through a using) generally does not exist at the expansion site;</item>
/// <item>unqualified type and static-member references, including generic ones such as
/// <c>List&lt;string&gt;</c> or <c>Make&lt;string&gt;</c>, are fully qualified while their written type
/// arguments are preserved and independently qualified;</item>
/// <item>an extension method invoked in instance syntax (<c>items.First()</c>) is normalized to a fully
/// qualified static call, or reported as BCF1002 when that rewrite cannot be made semantics-preserving;</item>
/// <item>references to non-public members, whether unqualified or accessed through a receiver, record an
/// accessibility requirement;</item>
/// <item>unqualified containing-instance members whose names overlap the generated contextual-variable
/// prefix gain an explicit <c>this.</c> receiver so the generated lambda parameter cannot shadow them;</item>
/// <item>references to source-local constructs (local functions or locals from an enclosing scope)
/// that cannot exist in generated code report a single declaration BCF1002;</item>
/// <item>an interpolation hole any of the above rewrote is parenthesized, because a hole's expression
/// cannot hold the <c>::</c> or the <c>,</c> that a rewrite introduces at its top level;</item>
/// <item>local and lambda identifiers plus all trivia are preserved as literal text, except authored
/// declarations that could capture a generated contextual-fragment parameter after hole substitution;
/// those declarations and their symbol-bound references receive a deterministic collision-free name.</item>
/// <item>a local declared by a transplanted statement and registered as a render variable carries a hole
/// at its own declaring identifier, so expansion names the declaration and its references alike (#336).</item>
/// </list>
/// </summary>
internal static class ExpressionTemplateFactory
{
    // Fully qualified type name without its type-argument list. Used to qualify only the identifier token
    // of a generic name so the written type-argument syntax (including nullable annotations) survives and
    // each type argument is qualified independently by the traversal.
    private static readonly SymbolDisplayFormat QualifiedNameWithoutTypeArguments =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None);

    /// <summary>What <see cref="CreateForStatements"/> writes between two transplanted statements.</summary>
    private static readonly LiteralExpressionSegment StatementSeparator = new("\n");

    public static ExpressionTemplate Create(ExpressionSyntax expression, ViewPartBodyContext context) =>
        CreateCore(expression, context, AuthoredContextNameHygiene.Create(expression, context));

    /// <summary>
    /// The statements transplanted ahead of the content they lead into, as one template. Every position
    /// that accepts a transplantable block reaches here: a <c>ForEach</c> content lambda, a design-time
    /// expression getter, and a <c>[ViewPart]</c> body.
    /// </summary>
    /// <remarks>
    /// An <see cref="ExpressionTemplate"/> holds code text with holes in it, and a statement list is that
    /// too: it takes the same hole substitution, the same value equality, and the same place in the
    /// incremental cache. The name says expression because that was the only shape until now; a second
    /// type carrying the identical three properties would be a second thing to keep in step.
    /// <para>
    /// Each statement is normalized under its own rename plan, which is safe only because the caller
    /// refuses a block declaring a generator-reserved name — the one thing a plan renames. Locals crossing
    /// from one statement to the next are admitted by
    /// <see cref="ViewPartBodyContext.IsInsideTransplantedScope"/>, which the caller has already opened
    /// over the whole block.
    /// </para>
    /// </remarks>
    public static ExpressionTemplate CreateForStatements(
        ImmutableArray<StatementSyntax> statements, ViewPartBodyContext context)
    {
        var segments = ImmutableArray.CreateBuilder<ExpressionSegment>();

        foreach (var statement in statements)
        {
            // Between statements, not after each: a trailing separator would have to be stripped back off
            // by whoever emits the text, putting one convention in two files. Adjacent literals still
            // coalesce in ExpressionTemplate's constructor, so the canonical form is unchanged.
            if (segments.Count > 0)
                segments.Add(StatementSeparator);

            var template = CreateCore(
                statement, context, AuthoredContextNameHygiene.Create(statement, context));

            segments.AddRange(template.Segments.AsImmutableArray());
        }

        return ExpressionTemplate.Create(segments.ToImmutable());
    }

    /// <summary>
    /// The template for a <see langword="bool"/> literal the author did not write, which is what an omitted
    /// optional value argument means: <c>.Attr("disabled")</c> is <c>.Attr("disabled", true)</c> with the
    /// default supplied (#178).
    /// </summary>
    /// <remarks>
    /// Here rather than at the one call site that needs it, so "a value becomes a template" stays one rule.
    /// The code text and the constant have to agree — the emitter reads the text, the fold reads the
    /// constant — and this is the file where that agreement is otherwise established.
    /// </remarks>
    public static ExpressionTemplate ForBooleanConstant(bool value) =>
        value ? ExpressionTemplate.TrueLiteral : ExpressionTemplate.FalseLiteral;

    private static ExpressionTemplate CreateCore(
        SyntaxNode expression,
        ViewPartBodyContext context,
        AuthoredContextNameHygiene authoredNameHygiene)
    {
        var replacements = new List<Replacement>();
        var replacedSpans = new List<TextSpan>();

        // First pass: whole-invocation rewrites that must run before per-name normalization.
        //  * every nameof(...) collapses to its compile-time constant string;
        //  * an extension method invoked in instance syntax normalizes to a fully qualified static call.
        // Both record the whole invocation span so the second pass never rewrites the receiver, method, or
        // argument names inside them a second time.
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (node is not InvocationExpressionSyntax invocation)
                continue;

            if (IsNestedInReplaced(invocation.Span, replacedSpans))
                continue;

            var nameofSegment = TryCreateNameofConstant(invocation, context);
            if (nameofSegment is not null)
            {
                replacements.Add(new Replacement(
                    invocation.Span,
                    [nameofSegment]));
                replacedSpans.Add(invocation.Span);
                continue;
            }

            if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is IMethodSymbol { MethodKind: MethodKind.ReducedExtension } extensionMethod)
            {
                if (ReportUnresolvedExtensionTypeArguments(invocation, context))
                {
                    replacedSpans.Add(invocation.Span);
                    continue;
                }

                if (TryCreateExtensionMethodCall(
                    invocation,
                    extensionMethod,
                    context,
                    authoredNameHygiene,
                    out var extensionSegments))
                {
                    replacements.Add(new Replacement(invocation.Span, extensionSegments));
                }

                // Whether normalized or rejected (a BCF1002 was recorded inside), the invocation is fully
                // handled here; record its span so the second pass leaves its inner names untouched.
                replacedSpans.Add(invocation.Span);
            }
        }

        // Declaration identifiers are tokens rather than SimpleNameSyntax nodes, so splice their safe
        // names explicitly. A declaration inside a whole-invocation rewrite is handled by that rewrite's
        // recursive CreateCore call with the same symbol-aware plan.
        foreach (var declaration in authoredNameHygiene.Declarations)
        {
            if (expression.Span.Contains(declaration.Span)
                && !IsNestedInReplaced(declaration.Span, replacedSpans))
            {
                AddReplacement(
                    replacements,
                    replacedSpans,
                    declaration.Span,
                    new LiteralExpressionSegment(declaration.Name));
            }
        }

        // Only a body that is inlined at call sites registers a local as a render variable, so only there
        // can the arm below produce anything. Read once: it gates a semantic query per declaration, and
        // this loop runs over every identifier of every component body.
        var mintsTransplantedLocals = context.IsInlinedAtCallSites;

        // Second pass: normalize simple names into parameter holes, fully qualified references, or recorded
        // accessibility requirements.
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // A declaration whose local was registered as a render variable carries the hole at its own
            // identifier, so one ordinal names the declaration and every reference to it, and expansion
            // mints the name (#336). The statement around it stays literal text: its written type travels
            // with it, so nothing here has to reproduce the local's type. `var` is a type reference like
            // any other and is resolved below, with the one exception recorded there. Handled in this pass
            // rather than a walk of its own, because a declaration and a reference are the same rewrite
            // over the same traversal, and the spans cannot overlap: a declaring identifier is a token,
            // never a name.
            if (mintsTransplantedLocals && TryGetDeclaredLocalIdentifier(node, out var declaredIdentifier))
            {
                if (!IsNestedInReplaced(declaredIdentifier.Span, replacedSpans)
                    && context.SemanticModel.GetDeclaredSymbol(node, context.CancellationToken)
                        is { } declaredSymbol
                    && context.ResolveHole(declaredSymbol, out var declaredOrdinal) == BodyHoleKind.Value)
                {
                    AddReplacement(
                        replacements,
                        replacedSpans,
                        declaredIdentifier.Span,
                        new ParameterHoleExpressionSegment(declaredOrdinal));
                }

                continue;
            }

            if (node is not SimpleNameSyntax name)
                continue;

            // A receiver, method, or type-argument name inside an already-rewritten invocation (an
            // extension call, or a collapsed nameof) is owned by that whole-span replacement.
            if (IsNestedInReplaced(name.Span, replacedSpans))
                continue;

            // The type of a deconstruction declaration. Every other type reference is qualified below,
            // `var` included, because the generated file carries no using directives and a written type has
            // to stand on its own. This is the one position where no written form would be legal: a
            // parenthesized designation takes one type per element or none at all, never one ahead of the
            // whole list, so the inferred tuple type written there declared nothing (#342). Ahead of the
            // semantic queries because it needs neither of their answers.
            if (IsDeconstructionDeclarationType(name))
                continue;

            // The semantic model is asked about this name exactly once, here. Everything below wants the
            // same two answers, and when each helper fetched its own, a member-access name cost five
            // semantic queries to answer two questions. Semantic queries dominate this transform's cost and
            // it runs over every identifier of every component and every view part body.
            var alias = context.SemanticModel.GetAliasInfo(name, context.CancellationToken);
            var symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;

            if (TryReportUnresolvedType(name, alias, symbol, context))
                continue;

            // A name inside a nameof(...) belongs to an invocation already collapsed above; it must never
            // be rewritten on its own.
            if (IsInsideNameof(name))
                continue;

            // A member accessed through a receiver keeps its unqualified text (the receiver qualifies it),
            // but a non-public member still constrains where the body may be inlined, so its accessibility
            // requirement is recorded even though no text is rewritten.
            if (IsMemberAccessName(name))
            {
                // A type written under a relative namespace path (for example 'Models.Widget' inside
                // 'namespace Root.Features' where it binds to 'Root.Models.Widget') must have the whole
                // path fully qualified, because the generated file has no using/namespace context to
                // resolve the left-hand namespace. When the name is not such a reference this is a no-op
                // and the accessibility requirement is recorded as before.
                if (TryQualifyNamespaceQualifiedType(name, symbol, context, replacements, replacedSpans))
                    continue;

                RecordMemberAccessRequirement(symbol, context);
                continue;
            }

            if (symbol is null)
                continue;

            // This branch precedes source-local rejection because a recursively normalized subexpression
            // may reference a declaration owned by the outer expression. The shared plan proves that the
            // declaration travels with the complete expression and supplies its deterministic safe name.
            if (authoredNameHygiene.TryGetName(symbol, out var authoredName))
            {
                AddReplacement(
                    replacements,
                    replacedSpans,
                    IdentifierSpan(name),
                    new LiteralExpressionSegment(authoredName));
                continue;
            }

            if (IsUnsupportedSourceLocalReference(symbol, expression, context, out var unsupportedReason))
            {
                context.ReportUnsupportedReference(name.GetLocation(), unsupportedReason);
                continue;
            }

            if (name is IdentifierNameSyntax)
            {
                switch (context.ResolveHole(symbol, out var ordinal))
                {
                    case BodyHoleKind.Value:
                        AddReplacement(replacements, replacedSpans, name.Span,
                            new ParameterHoleExpressionSegment(ordinal));
                        continue;

                    // Caller content in a value position: Div.Attr("x", Describe(header)),
                    // ForEach(xs, x => Slot, …). There is no expression text to substitute -- content is a
                    // node subtree spliced by ViewPartExpander -- so a hole minted here would be
                    // unsubstitutable. Reported as the unsupported reference it is, rather than left to fail
                    // during expansion, where it would surface as a generator crash with no location.
                    case BodyHoleKind.Content:
                        context.ReportUnsupportedReference(
                            name.GetLocation(),
                            $"'{name.Identifier.ValueText}' is caller-supplied content, which has no value; "
                                + "content can only be placed as a child");
                        continue;
                }
            }

            if (NeedsGeneratedContextCollisionQualification(name, symbol, context))
            {
                RecordAccessRequirement(symbol, context);
                AddReplacement(
                    replacements,
                    replacedSpans,
                    IdentifierSpan(name),
                    new LiteralExpressionSegment($"this.{name.Identifier.ValueText}"));
                continue;
            }

            // A type reference, including a generic one such as List<string>, is fully qualified. A
            // generic name qualifies only its identifier token so the written type-argument list (with any
            // nullable annotations) survives and each type argument is qualified independently below. A
            // name under an alias qualification (global::Data) is skipped: it is fully qualified by
            // construction already, and a second qualification is not even legal syntax (#392).
            if (symbol is INamedTypeSymbol typeSymbol
                && name is IdentifierNameSyntax or GenericNameSyntax
                && name.Parent is not AliasQualifiedNameSyntax)
            {
                RecordAccessRequirement(typeSymbol, context);
                var span = IdentifierSpan(name);
                var qualified = name is GenericNameSyntax
                    ? typeSymbol.ToDisplayString(QualifiedNameWithoutTypeArguments)
                    : typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                AddReplacement(replacements, replacedSpans, span, new LiteralExpressionSegment(qualified));
                continue;
            }

            // An unqualified static member, including a generic static method such as Make<string>, is
            // qualified with its declaring type; a generic name again keeps its written type arguments.
            if ((name is IdentifierNameSyntax or GenericNameSyntax)
                && symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol
                && symbol.IsStatic
                && !IsMemberAccessName(name))
            {
                RecordAccessRequirement(symbol, context);
                var span = IdentifierSpan(name);
                var containing = symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                AddReplacement(replacements, replacedSpans, span,
                    new LiteralExpressionSegment($"{containing}.{symbol.Name}"));
            }
        }

        // Third pass: restore the top level of every interpolation hole the passes above rewrote, which
        // the rewritten text can no longer occupy as it stands.
        AddInterpolationHoleParentheses(expression, replacements);

        // The constant value is a property of the whole expression, independent of the name-level
        // rewrites above: a rewrite qualifies or collapses source text and never changes what the
        // expression evaluates to. Captured here because the emitter sees only source text, and folding
        // needs the value (ARCHITECTURE.md §2.7).
        var constant = ReadConstant(expression, context);

        return replacements.Count == 0
            ? ExpressionTemplate.Create(
                [new LiteralExpressionSegment(expression.ToString())], constant)
            : Splice(expression, replacements, constant);
    }

    /// <summary>
    /// Reads <paramref name="expression"/>'s compile-time constant value off the semantic model and
    /// classifies it into one of <see cref="ConstantInfo"/>'s cases. Returns <see langword="null"/> when
    /// it is not a constant.
    /// </summary>
    /// <remarks>
    /// A constant <see langword="null"/> arrives here with no type of its own to read — a null constant
    /// string and a null constant of any other type are the same <see cref="Optional{T}"/> — which costs
    /// nothing: both mean <c>AddAttribute</c> omits the attribute, so both are
    /// <see cref="NullConstant"/>.
    /// </remarks>
    private static ConstantInfo? ReadConstant(
        SyntaxNode expression,
        ViewPartBodyContext context)
    {
        // A statement has no value, so nothing to fold. Only the expression roots reach the model.
        if (expression is not ExpressionSyntax valueExpression)
            return null;

        var constant = context.SemanticModel.GetConstantValue(valueExpression, context.CancellationToken);
        if (!constant.HasValue)
            return null;

        return constant.Value switch
        {
            null => new NullConstant(),
            string text => new StringConstant(text),
            bool value => new BooleanConstant(value),
            _ => new RuntimeFormattedConstant(),
        };
    }

    /// <summary>
    /// As <see cref="TryReportUnresolvedType(SimpleNameSyntax, IAliasSymbol?, ISymbol?, ViewPartBodyContext)"/>,
    /// for the two callers that reach a name outside the normalization loop and so hold neither answer yet.
    /// </summary>
    internal static bool TryReportUnresolvedType(
        SimpleNameSyntax name,
        ViewPartBodyContext context) =>
        TryReportUnresolvedType(
            name,
            context.SemanticModel.GetAliasInfo(name, context.CancellationToken),
            context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol,
            context);

    /// <param name="name">The identifier to check for an unresolved type.</param>
    /// <param name="alias">
    /// <paramref name="name"/>'s alias info, already read by the caller.
    /// </param>
    /// <param name="symbol">
    /// The symbol <paramref name="name"/> binds to, already read by the caller.
    /// </param>
    /// <param name="context">The body context to report BCF3015 against.</param>
    private static bool TryReportUnresolvedType(
        SimpleNameSyntax name,
        IAliasSymbol? alias,
        ISymbol? symbol,
        ViewPartBodyContext context)
    {
        var type = GetReferencedType(name, alias, symbol, context);
        if (type is null
            || !TypeSymbolFacts.ContainsUnresolvedType(type)
            || (alias is null && type.TypeKind != TypeKind.Error)
            || IsGlobalQualifiedTypeReference(name))
        {
            return false;
        }

        context.ReportUnresolvedType(name.Identifier.GetLocation(), name.Identifier.ValueText);
        return true;
    }

    private static ITypeSymbol? GetReferencedType(
        SimpleNameSyntax name,
        IAliasSymbol? alias,
        ISymbol? symbol,
        ViewPartBodyContext context)
    {
        if (alias is { Target: ITypeSymbol aliasType })
            return aliasType;

        if (symbol is ITypeSymbol symbolType)
            return symbolType;

        if (FindTypeOnlySyntax(name) is not { } typeSyntax)
            return null;

        // Swapping which side of the ?? runs first is a stryker survivor, measured equivalent rather than
        // assumed: hand-applying the swap and running BlazorCodeFirst.Compiler.Tests, including probes
        // built the way ObjectCreationExpressionType_UnresolvedType_ReportsBCF3015's own "new Missing()"
        // row is (a bare unresolved constructor call) and a namespace-qualified variant
        // ("new System.MissingConcreteType()"), left every test passing unchanged with identical
        // diagnostics and identical spliced code in both orders. Reading why: when the right operand is
        // non-null (typeSyntax.Parent is the creation being constructed), GetTypeInfo(name) and
        // GetTypeInfo(creation) answer from the same failed bind and so carry the same (error) type either
        // way; when it is null (every other FindTypeOnlySyntax position — a cast, a pattern, an is/as
        // right-hand side — none of which is a creation), the swap only reorders `a ?? null` against
        // `null ?? a`, which return the same `a` regardless of order.
        return context.SemanticModel.GetTypeInfo(name, context.CancellationToken).Type
            ?? (typeSyntax.Parent is ObjectCreationExpressionSyntax creation
                ? context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type
                : null);
    }

    private static TypeSyntax? FindTypeOnlySyntax(SimpleNameSyntax name)
    {
        TypeSyntax current = name;
        while (current.Parent is TypeSyntax parent)
            current = parent;

        return current.Parent switch
        {
            TypeArgumentListSyntax arguments when arguments.Arguments.Contains(current) => current,
            TypeOfExpressionSyntax typeOf when typeOf.Type == current => current,
            SizeOfExpressionSyntax sizeOf when sizeOf.Type == current => current,
            DefaultExpressionSyntax defaultExpression when defaultExpression.Type == current => current,
            CastExpressionSyntax cast when cast.Type == current => current,
            ObjectCreationExpressionSyntax creation when creation.Type == current => current,
            ArrayCreationExpressionSyntax arrayCreation when arrayCreation.Type == current => current,
            StackAllocArrayCreationExpressionSyntax stackAlloc when stackAlloc.Type == current => current,
            BinaryExpressionSyntax binary
                when binary.Right == current
                    && (binary.IsKind(SyntaxKind.IsExpression)
                        || binary.IsKind(SyntaxKind.AsExpression)) => current,
            DeclarationPatternSyntax declarationPattern when declarationPattern.Type == current => current,
            RecursivePatternSyntax recursivePattern when recursivePattern.Type == current => current,
            // Flipping this to != is an unreached stryker mutant, reasoned rather than measured: no
            // BlazorCodeFirst.Compiler.Tests probe reaches it at all (its own report is NoCoverage), and
            // reading why shows none can. A bare name only parses as a TypePatternSyntax when Roslyn's own
            // pattern grammar successfully binds it as a type; an unresolvable name is reinterpreted as a
            // ConstantPatternSyntax instead (confirmed by hand: 'new object() is System.Missing' parses as
            // the classic BinaryExpressionSyntax is-expression above, not this arm, and a bare 'Missing' in
            // a pattern position binds as ConstantPatternSyntax). A name that DOES reach this arm is
            // therefore always already resolved, which GetReferencedType's own symbol-based branch above
            // (`symbol is ITypeSymbol symbolType`) already returns before ever calling this method — so no
            // name reported by TryReportUnresolvedType (which requires an unresolved type) can be the one
            // this arm is asked to classify.
            TypePatternSyntax typePattern when typePattern.Type == current => current,
            TupleElementSyntax element when element.Type == current => current,
            ParameterSyntax parameter when parameter.Type == current => current,
            FunctionPointerParameterSyntax functionPointerParameter
                when functionPointerParameter.Type == current => current,
            VariableDeclarationSyntax declaration when declaration.Type == current => current,
            _ => null,
        };
    }

    private static bool IsGlobalQualifiedTypeReference(SimpleNameSyntax name)
    {
        NameSyntax current = name;
        while (current.Parent is NameSyntax parent && OwnsName(parent, current))
            current = parent;
        while (current is QualifiedNameSyntax qualified)
            current = qualified.Left;
        return current is AliasQualifiedNameSyntax alias
            && alias.Alias.Identifier.ValueText == "global";
    }

    private static bool OwnsName(NameSyntax parent, NameSyntax child) =>
        parent switch
        {
            // Flipping the Left half to != is a stryker survivor, measured equivalent rather than assumed:
            // hand-applying it and running the full BlazorCodeFirst.Compiler.Tests suite, plus a probe
            // built around a global-qualified reference with a genuinely unresolvable middle component
            // ('typeof(global::System.MissingNamespace.Deeper)'), left every test passing unchanged.
            // Reading why: IsGlobalQualifiedTypeReference's own second loop re-descends through every
            // QualifiedNameSyntax.Left independently of OwnsName, starting from wherever the first loop's
            // ascent stopped. Stopping that ascent one step early through a false Left comparison still
            // leaves `current` inside the same qualified-name spine, so the second loop's unconditional
            // `.Left` walk reaches the identical alias-qualified root either way; only diverting `current`
            // to a node outside that spine would change the final answer, and a false OwnsName result
            // never does that — it only ever halts the climb, never redirects it.
            QualifiedNameSyntax qualified =>
                qualified.Left == child || qualified.Right == child,
            // Flipping the Alias half to != is a stryker survivor, measured equivalent rather than assumed:
            // hand-applying it and running the full suite left every test passing unchanged. Reading why:
            // this branch is asked about `child` == the alias identifier itself (e.g. the 'global' token
            // in 'global::T') only when `child` is where the first loop's ascent *starts*, since ascent
            // only ever moves to a larger enclosing node afterward, never back down to an alias's own
            // sub-node. That starting `child` is `name` itself, but 'global' — a contextual keyword with no
            // value or type of its own — never carries a non-null GetTypeInfo, so GetReferencedType returns
            // null and TryReportUnresolvedType's own `type is null` guard returns before ever constructing
            // this call. This branch cannot observe a `child` that is the case it is being asked to widen.
            AliasQualifiedNameSyntax alias =>
                alias.Alias == child || alias.Name == child,
            // Flipping this to true is an unreached stryker mutant, reasoned rather than measured: no
            // BlazorCodeFirst.Compiler.Tests probe reaches it (its own report is NoCoverage), and reading
            // why shows none can. The only caller passes `current.Parent` as `parent`, so `parent` is
            // already known to structurally contain `current` as a direct child before this method is ever
            // asked. NameSyntax has exactly four shapes: QualifiedNameSyntax and AliasQualifiedNameSyntax
            // are handled above; IdentifierNameSyntax has no NameSyntax child at all, and GenericNameSyntax's
            // only NameSyntax-shaped descendants sit inside its TypeArgumentListSyntax, one level below a
            // direct child. Neither can be the `parent` that put `current` here, so this arm is asked about
            // a combination the caller cannot construct.
            _ => false,
        };

    private static void AddReplacement(
        List<Replacement> replacements,
        List<TextSpan> replacedSpans,
        TextSpan span,
        ExpressionSegment segment)
    {
        // Dropping this call is a stryker survivor, measured equivalent rather than assumed: hand-applying
        // the removal and running the full BlazorCodeFirst.Compiler.Tests suite and
        // BlazorCodeFirst.DiagnosticTests left every test passing unchanged. Reading why: every span this
        // helper's eight callers record is either a leaf token (a declaration, an identifier, a generic
        // name's own IdentifierSpan deliberately excluding its type arguments) that nothing else can be
        // nested inside, or the wide namespace-qualified-type span TryQualifyNamespaceQualifiedType builds
        // — which covers only names DescendantNodesAndSelf's pre-order walk has already visited (the
        // qualifying left side, visited before the right-side name that triggers the wide replacement),
        // never one still to come. IsNestedInReplaced's callers all check a *later* node's span against
        // spans recorded so far, so a span this helper stops recording is never the one protecting the
        // still-unvisited work that check exists for.
        replacements.Add(new Replacement(span, [segment]));
        replacedSpans.Add(span);
    }

    /// <summary>
    /// Parenthesizes the expression of every interpolation hole inside <paramref name="expression"/> that
    /// the normalization passes rewrote, by recording an empty-span replacement at each end of it.
    /// </summary>
    /// <remarks>
    /// A hole's expression ends at the first <c>,</c> or <c>:</c> the interpolation parser reads at the
    /// hole's top level, because those begin its alignment and its format specifier. The qualification
    /// this file exists to apply therefore cannot be written into a hole as it stands:
    /// <c>$"{global::Ns.Type.Member}"</c> parses as the expression <c>global</c> formatted with
    /// <c>Ns.Type.Member</c>, and the generated file fails to compile with CS0103 (#273). The same holds
    /// for an extension call's explicit type-argument list. Authored text never has the problem, since it
    /// had to parse where it was written; only substituted text does, so a hole is parenthesized exactly
    /// when something was substituted into it. Parentheses restore the top level for the whole hole and
    /// leave the alignment and the format specifier outside.
    /// <para>
    /// A parameter hole is the one substitution that cannot break a hole, so it does not call for
    /// parentheses: <c>ViewPartExpander</c> binds every value argument to a local it names itself, and
    /// what reaches the hole is that name. <c>Generator_InterpolatedHoleFromViewPartArgument_Compiles</c>
    /// holds that, by passing a qualified argument to a parameter an interpolation hole reads.
    /// </para>
    /// </remarks>
    private static void AddInterpolationHoleParentheses(
        SyntaxNode expression,
        List<Replacement> replacements)
    {
        // Nothing was rewritten, so no hole can need restoring. Checked before the walk because the walk
        // would otherwise run over every expression in every body, most of which are rewritten nowhere.
        if (replacements.Count == 0)
            return;

        // The scan below reads only what was recorded before the walk, so a parenthesis added here never
        // counts as the rewrite that puts parentheses around an enclosing hole.
        var rewriteCount = replacements.Count;

        foreach (var node in expression.DescendantNodes())
        {
            // An author who already parenthesized the hole -- a conditional expression has to be --
            // left no top level inside it for a rewrite to reach.
            if (node is not InterpolationSyntax { Expression: { } hole and not ParenthesizedExpressionSyntax }
                || !ContainsSubstitutedText(hole.Span, replacements, rewriteCount))
            {
                continue;
            }

            replacements.Add(new Replacement(
                new TextSpan(hole.SpanStart, 0),
                [new LiteralExpressionSegment("(")]));
            replacements.Add(new Replacement(
                new TextSpan(hole.Span.End, 0),
                [new LiteralExpressionSegment(")")]));
        }
    }

    /// <summary>
    /// Whether any of the first <paramref name="count"/> replacements inside <paramref name="span"/>
    /// writes text other than a parameter hole's substituted name.
    /// </summary>
    private static bool ContainsSubstitutedText(TextSpan span, List<Replacement> replacements, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var replacement = replacements[index];
            if (span.Contains(replacement.Span) && !IsParameterHole(replacement))
                return true;
        }

        return false;
    }

    private static bool IsParameterHole(Replacement replacement) =>
        replacement.Segments.Length == 1
            && replacement.Segments[0] is ParameterHoleExpressionSegment;

    private static ExpressionTemplate Splice(
        SyntaxNode expression,
        List<Replacement> replacements,
        ConstantInfo? constant)
    {
        // TextSpan orders by start and then by length, and the length is what an empty-span parenthesis
        // needs: it lands outside a replacement that begins where it does, which is what a hole rewritten
        // from its first character looks like.
        replacements.Sort(static (left, right) => left.Span.CompareTo(right.Span));

        var baseText = expression.ToString();
        var baseStart = expression.Span.Start;

        var segments = ImmutableArray.CreateBuilder<ExpressionSegment>();
        var cursor = 0;

        foreach (var replacement in replacements)
        {
            var relativeStart = replacement.Span.Start - baseStart;
            if (relativeStart > cursor)
                segments.Add(new LiteralExpressionSegment(baseText.Substring(cursor, relativeStart - cursor)));

            foreach (var segment in replacement.Segments)
                segments.Add(segment);

            cursor = relativeStart + replacement.Span.Length;
        }

        if (cursor < baseText.Length)
            segments.Add(new LiteralExpressionSegment(baseText.Substring(cursor)));

        return ExpressionTemplate.Create(segments.ToImmutable(), constant);
    }

    /// <summary>
    /// Determines whether <paramref name="symbol"/> is a source-local construct (a local function,
    /// local variable, range variable, or label) that is referenced from outside its declaration and
    /// therefore cannot be reproduced in generated component code. A source-local declared inside the
    /// spliced <paramref name="root"/> travels with the literal text and remains legal; one declared in
    /// an enclosing scope (for example an <c>out var</c> from a sibling argument) does not.
    /// </summary>
    private static bool IsUnsupportedSourceLocalReference(
        ISymbol symbol,
        SyntaxNode root,
        ViewPartBodyContext context,
        out string reason)
    {
        // Changing this initial value is a stryker survivor, measured equivalent rather than assumed:
        // hand-applying it and running the full suite left every test passing unchanged. Reading why:
        // every `false` return below leaves `reason` unread by its caller (IsUnsupportedSourceLocalReference
        // reports nothing on a `false` result), and the one `true` return overwrites it first with the
        // actual message. No return path reads this initial value.
        reason = string.Empty;

        var kindLabel = symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.LocalFunction } => "local function",
            ILocalSymbol => "local",
            IRangeVariableSymbol => "range variable",
            // Emptying this string is an unreached stryker mutant, reasoned rather than measured: no
            // BlazorCodeFirst.Compiler.Tests probe reaches it (its own report is NoCoverage), and reading
            // why shows none can, for a reason the local-function and range-variable arms above do not
            // share. Both of those can be captured into an extension-call argument, which this file
            // re-analyzes under that argument's own narrower root — a root the enclosing declaration falls
            // outside of, which is exactly how RangeVariableAsExtensionArgument_ReportsBCF1002 and
            // LocalFunctionAsExtensionArgument_ReportsBCF1002 reach this method's `true` return. A label has
            // no expression form at all — its one reference site, `goto`, is itself a statement, never an
            // argument — so a label reference can only ever sit in the same CreateForStatements-processed
            // statement, the same lambda block, or the same transplanted scope as its own declaration. Every
            // path that reaches this method already holds a `root` or a transplanted-scope span covering
            // that whole unit, so the declaration is always found and this arm's `true` return is never
            // taken for a label.
            ILabelSymbol => "label",
            _ => null,
        };

        if (kindLabel is null)
            return false;

        foreach (var declaration in symbol.DeclaringSyntaxReferences)
        {
            // Either the declaration travels with this template, or it sits in a block being transplanted
            // whole, which puts it in the generated code beside this reference (ARCHITECTURE.md §2.3).
            if (root.FullSpan.Contains(declaration.Span)
                || context.IsInsideTransplantedScope(declaration.Span))
            {
                return false;
            }
        }

        reason = $"references {kindLabel} '{symbol.Name}' that cannot exist in generated component code";
        return true;
    }

    /// <summary>
    /// Records what the expansion site has to be able to reach for <paramref name="symbol"/> to be
    /// nameable there, so a body naming a non-public member is refused with BCF1002 rather than emitted
    /// into a type that cannot see it. Internal for the one caller outside this file:
    /// <see cref="BindTargetResolver"/> registers the setter a getter-only <c>.Bind</c> derives, which the
    /// walk above cannot reach it through because the author's syntax does not contain it (#391).
    /// </summary>
    internal static void RecordAccessRequirement(ISymbol symbol, ViewPartBodyContext context)
    {
        var kind = symbol.DeclaredAccessibility switch
        {
            Accessibility.Private => (ViewPartAccessRequirementKind?)ViewPartAccessRequirementKind.SameContainingType,
            Accessibility.Protected => ViewPartAccessRequirementKind.DerivedContainingType,
            Accessibility.ProtectedAndInternal => ViewPartAccessRequirementKind.DerivedContainingType,
            _ => null,
        };

        if (kind is null)
            return;

        // The requirement is keyed on the type that declares the referenced member so expansion checks
        // that type, not the view part's own containing type, against the component's inheritance
        // chain. A private/protected member always has a containing type; guard defensively regardless.
        // Changing this fallback's text is an unreached stryker mutant, reasoned rather than measured: no
        // BlazorCodeFirst.Compiler.Tests probe reaches it (its own report is NoCoverage), and the comment
        // right above already names why: `kind` is non-null here only for Private, Protected, or
        // ProtectedAndInternal accessibility, and every symbol Roslyn can assign one of those to is a
        // member — a member always has a containing type.
        var requiredContainingTypeKey = symbol.ContainingType is { } containingType
            ? containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : string.Empty;

        context.AddAccessRequirement(new ViewPartAccessRequirement(
            kind.Value,
            requiredContainingTypeKey,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
    }

    /// <summary>
    /// Records the accessibility requirement for a member named through a receiver (<c>receiver.Member</c>)
    /// when that member is a non-public field, property, method, or event. The member text stays
    /// unqualified because the receiver already qualifies it, but a private or protected member still
    /// constrains where the inlined body may legally be placed, so without this the expansion site would
    /// emit CS0122 instead of the intended BCF1002.
    /// </summary>
    private static void RecordMemberAccessRequirement(ISymbol? symbol, ViewPartBodyContext context)
    {
        if (symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol)
            RecordAccessRequirement(symbol, context);
    }

    /// <summary>
    /// Fully qualifies a type reference written as the right-hand identifier of a namespace-qualified path
    /// (<c>Models.Widget</c> where <c>Models</c> binds to a namespace), replacing the whole path so the
    /// using-less generated file can resolve it (<c>global::Root.Models.Widget</c>). A generic name keeps
    /// its written type-argument list (only the identifier token is rewritten) so each argument is
    /// qualified independently. Returns <see langword="false"/> when <paramref name="name"/> is not the
    /// right side of a namespace-qualified type reference, for example a member accessed through a value
    /// receiver, or a nested type named through an enclosing type (whose left identifier is qualified on
    /// its own), leaving the caller to record the ordinary member-access requirement.
    /// </summary>
    private static bool TryQualifyNamespaceQualifiedType(
        SimpleNameSyntax name,
        ISymbol? symbol,
        ViewPartBodyContext context,
        List<Replacement> replacements,
        List<TextSpan> replacedSpans)
    {
        if (symbol is not INamedTypeSymbol typeSymbol)
            return false;

        SyntaxNode qualifiedNode;
        ExpressionSyntax leftSide;
        switch (name.Parent)
        {
            case QualifiedNameSyntax qualified when qualified.Right == name:
                qualifiedNode = qualified;
                leftSide = qualified.Left;
                break;
            case MemberAccessExpressionSyntax memberAccess when memberAccess.Name == name:
                qualifiedNode = memberAccess;
                leftSide = memberAccess.Expression;
                break;
            // Flipping this to true is an unreached stryker mutant, reasoned rather than measured: no
            // BlazorCodeFirst.Compiler.Tests probe reaches it (its own report is NoCoverage). Same reason as
            // the leftSide check below — see there.
            default:
                return false;
        }

        // Only a namespace-qualified left needs whole-path rewriting. A type-qualified left (an enclosing
        // type naming a nested type) is already handled by qualifying that left identifier on its own, and
        // a value receiver is a genuine member access that keeps its unqualified text.
        // Flipping this to true is a stryker survivor, measured equivalent rather than assumed: hand-
        // applying the flip and running the full BlazorCodeFirst.Compiler.Tests suite left every test
        // passing unchanged. Reading why: this line is reached only when `symbol` is an INamedTypeSymbol
        // (the guard above already refused any other kind), so the caller's fallback on a `false` return —
        // RecordMemberAccessRequirement — is asked about a type symbol every time. That method only ever
        // acts on IFieldSymbol, IPropertySymbol, IMethodSymbol, or IEventSymbol; a type symbol is a no-op
        // there regardless. Returning `true` instead skips that same no-op call, which changes nothing
        // observable: neither branch records a requirement or adds a replacement for a name that reaches
        // this specific check.
        if (context.SemanticModel.GetSymbolInfo(leftSide, context.CancellationToken).Symbol
            is not INamespaceSymbol)
        {
            return false;
        }

        // Dropping this call is a stryker survivor, measured equivalent rather than assumed: hand-applying
        // the removal and running the full suite left every test passing unchanged. Reading why: this
        // whole path (leftSide resolves to an INamespaceSymbol) only ever reaches a type declared directly
        // in a namespace — the sibling branch above already owns a type-qualified left (an enclosing type
        // naming a nested type), which is the only way `typeSymbol` here could otherwise be non-public. A
        // namespace-scoped type's DeclaredAccessibility is Public or Internal, and RecordAccessRequirement's
        // own kind switch maps both to `null`, recording nothing for either. The call is reached, but never
        // with a symbol it would act on.
        RecordAccessRequirement(typeSymbol, context);

        // Replace from the start of the whole path through the type identifier token; a generic name's
        // trailing type-argument list stays in place so its arguments are qualified independently.
        var span = TextSpan.FromBounds(qualifiedNode.SpanStart, IdentifierSpan(name).End);
        // Forcing this ternary to its true branch is a stryker survivor, measured equivalent rather than
        // assumed: hand-applying it and running the full suite left every test passing unchanged. Reading
        // why: QualifiedNameWithoutTypeArguments and FullyQualifiedFormat differ only in how a type
        // argument list renders, and the false branch is taken only when `name` is not a GenericNameSyntax
        // — a plain identifier, which can only name a type Roslyn resolved with no type arguments at all.
        // With nothing for WithGenericsOptions(None) to hide, the two formats produce identical text for
        // that symbol — the same reasoning that applies to this file's other GenericNameSyntax check against
        // QualifiedNameWithoutTypeArguments.
        var qualifiedText = name is GenericNameSyntax
            ? typeSymbol.ToDisplayString(QualifiedNameWithoutTypeArguments)
            : typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        // Flipping this to false is a stryker survivor, measured equivalent rather than assumed: hand-
        // applying it and running the full suite left every test passing unchanged. Reading why: the
        // replacement was already added, on the line above, regardless of what this method returns — a
        // `false` return only makes IsMemberAccessName's caller fall through to
        // RecordMemberAccessRequirement, and `symbol` here is the INamedTypeSymbol this whole method
        // requires from its opening guard, which that call always no-ops on (it only acts on
        // IFieldSymbol, IPropertySymbol, IMethodSymbol, or IEventSymbol). Both branches end in the same
        // `continue`, over the same already-spliced text.
        AddReplacement(replacements, replacedSpans, span, new LiteralExpressionSegment(qualifiedText));
        return true;
    }

    /// <summary>
    /// Detects a <c>nameof(...)</c> operator and returns a literal segment carrying its compile-time
    /// constant string. Because the entity a nameof names (a parameter replaced by a typed local, a
    /// private definition member, or a type in scope only through a using) generally does not exist at the
    /// expansion site, the operator cannot survive as written and its constant value is emitted instead,
    /// which is exactly what the C# compiler would have produced. A method literally named <c>nameof</c>
    /// is not a constant, so the constant value doubles as a reliable operator check.
    /// </summary>
    internal static LiteralExpressionSegment? TryCreateNameofConstant(
        InvocationExpressionSyntax invocation,
        ViewPartBodyContext context)
    {
        if (invocation.Expression is not IdentifierNameSyntax { Identifier.Text: "nameof" })
            return null;

        var constant = context.SemanticModel.GetConstantValue(invocation, context.CancellationToken);
        if (!constant.HasValue || constant.Value is not string value)
            return null;

        return new LiteralExpressionSegment(SymbolDisplay.FormatLiteral(value, quote: true));
    }

    private static bool ReportUnresolvedExtensionTypeArguments(
        InvocationExpressionSyntax invocation,
        ViewPartBodyContext context)
    {
        var generic = TypeSymbolFacts.TryGetInvokedGenericName(invocation);

        if (generic is null)
            return false;

        var found = false;
        foreach (var argument in generic.TypeArgumentList.Arguments)
        {
            foreach (var name in argument.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
            {
                if (TryReportUnresolvedType(name, context))
                    found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Normalizes an extension method invoked in instance syntax (<c>receiver.Method(args)</c>) into a
    /// fully qualified static call (<c>global::Ns.Type.Method&lt;T&gt;(receiver, args)</c>), because the
    /// generated file has no <c>using</c> directive to bring the method into scope. The reduced receiver
    /// becomes the first argument, carrying the original <c>this</c> parameter's ref kind, and the inferred
    /// type arguments are emitted so the same instantiation is fixed. Returns <see langword="false"/> and
    /// reports BCF1002 when the rewrite cannot be made semantics-preserving, a null-conditional receiver or
    /// a type argument that cannot be named in generated component code.
    /// </summary>
    private static bool TryCreateExtensionMethodCall(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ViewPartBodyContext context,
        AuthoredContextNameHygiene authoredNameHygiene,
        out ImmutableArray<ExpressionSegment> segments)
    {
        segments = [];

        // Only 'receiver.Method(...)' can become a static call; a null-conditional 'receiver?.Method(...)'
        // would change short-circuit semantics, so it is reported rather than silently rewritten.
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            context.ReportUnsupportedReference(
                invocation.GetLocation(),
                $"invokes extension method '{method.Name}' in a form that cannot be normalized to a static call in generated component code");
            return false;
        }

        foreach (var typeArgument in method.TypeArguments)
        {
            if (!TypeSymbolFacts.IsNameableInGeneratedCode(typeArgument))
            {
                context.ReportUnsupportedReference(
                    invocation.GetLocation(),
                    $"invokes extension method '{method.Name}' whose inferred type argument '{typeArgument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' cannot be named in generated component code");
                return false;
            }
        }

        var builder = ImmutableArray.CreateBuilder<ExpressionSegment>();

        var prefix = new StringBuilder();
        prefix.Append(method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        prefix.Append('.');
        prefix.Append(method.Name);
        AppendTypeArguments(prefix, method.TypeArguments);
        prefix.Append('(');
        prefix.Append(ReceiverRefKindPrefix(method));
        builder.Add(new LiteralExpressionSegment(prefix.ToString()));

        // The reduced receiver becomes the first argument; supplied arguments keep their original order.
        foreach (var segment in CreateCore(memberAccess.Expression, context, authoredNameHygiene).Segments)
            builder.Add(segment);

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            builder.Add(new LiteralExpressionSegment(", " + LeadingArgumentText(argument)));
            foreach (var segment in CreateCore(argument.Expression, context, authoredNameHygiene).Segments)
                builder.Add(segment);
        }

        builder.Add(new LiteralExpressionSegment(")"));

        // The declaring type and the method itself must be accessible from the expansion site.
        // Dropping this call is a stryker survivor, measured equivalent in every case this file's test
        // suite can currently construct, though not proven the way RecordAccessRequirement(method, ...)
        // just below is: hand-applying the removal and running the full BlazorCodeFirst.Compiler.Tests
        // suite left every test passing unchanged. Reading why: an extension method's declaring type has
        // to be a type its own extension methods can be found through, and every extension class this
        // scanner's own instance-syntax resolution can reach in a ViewPart or design-time expression body
        // is one this compiler's RenderExpressionAnalyzer classification recognizes only when it is a
        // top-level (non-nested) class — never private, since only a nested class can be. A nested static
        // class carrying the extension method (the one shape that would give this call a private or
        // protected `method.ContainingType` to act on) was tried directly and left the ViewPart's own body
        // refused as "not a statically sequenceable expression" before this normalization ever runs, for a
        // reason unrelated to this mutation. No probe built within the currently-supported surface reaches
        // a non-public extension-method containing type, so whether this call ever fires for one is an
        // open question rather than a proven equivalence.
        RecordAccessRequirement(method.ContainingType, context);
        RecordAccessRequirement(method, context);

        segments = builder.ToImmutable();
        return true;
    }

    private static void AppendTypeArguments(StringBuilder builder, ImmutableArray<ITypeSymbol> typeArguments)
    {
        if (typeArguments.Length == 0)
            return;

        builder.Append('<');
        for (var index = 0; index < typeArguments.Length; index++)
        {
            if (index > 0)
                builder.Append(", ");
            builder.Append(typeArguments[index].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        builder.Append('>');
    }

    /// <summary>
    /// Returns the keyword (<c>ref </c> / <c>in </c>) required to pass the reduced receiver as the first
    /// argument of the static call, matching the extension's original <c>this</c> parameter ref kind so a
    /// by-reference receiver is not silently copied. Ordinary by-value receivers need no keyword.
    /// </summary>
    private static string ReceiverRefKindPrefix(IMethodSymbol method)
    {
        // Changing this fallback's text is an unreached stryker mutant, reasoned rather than measured: no
        // BlazorCodeFirst.Compiler.Tests probe reaches it (its own report is NoCoverage), and reading why
        // shows none can. This method's one caller (TryCreateExtensionMethodCall) is reached only once
        // CreateCore's own caller has already matched `method.MethodKind: MethodKind.ReducedExtension`, and
        // Roslyn only assigns that kind to a method obtained by reducing an unreduced extension method
        // against a receiver — ReducedFrom is that unreduced original, always non-null with the receiver as
        // its first declared parameter, for every ReducedExtension symbol Roslyn can produce.
        if (method.ReducedFrom is not { Parameters.Length: > 0 } original)
            return string.Empty;

        return original.Parameters[0].RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.In => "in ",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Returns the leading tokens of an argument that precede its expression (a <c>ref</c>/<c>in</c>/
    /// <c>out</c> keyword or a <c>name:</c> label) so they are preserved when the expression is rebuilt.
    /// A plain positional value argument has none.
    /// </summary>
    private static string LeadingArgumentText(ArgumentSyntax argument)
    {
        var offset = argument.Expression.SpanStart - argument.SpanStart;
        // Forcing this ternary to its true branch, and widening the guard to offset >= 0, are both stryker
        // survivors, measured equivalent rather than assumed: hand-applying each and running
        // ExtensionCallRewrite_TwoTypeArgumentsAndANamedArgument_ProducesExactCode (which exercises both a
        // plain positional argument, offset 0, and a named one, offset > 0) left it passing unchanged for
        // both mutants. Reading why: offset can never be negative — Expression is a child of argument, so
        // its span cannot start before argument's own — so the branch these mutations force is exactly the
        // one already reachable, taken with the one input (offset == 0) where the guard's own truth value
        // flips: Substring(0, 0) returns "", the same value string.Empty already carries.
        return offset > 0 ? argument.ToString().Substring(0, offset) : string.Empty;
    }

    private static TextSpan IdentifierSpan(SimpleNameSyntax name) =>
        name is GenericNameSyntax generic ? generic.Identifier.Span : name.Span;

    /// <summary>
    /// Whether <paramref name="name"/> is the member a receiver names, and so is already spelled by
    /// whatever stands to its left. A receiver is not one of these: it is the name that carries the
    /// reference into scope, so it is the name a using-less file still has to have qualified (#392).
    /// </summary>
    private static bool IsMemberAccessName(SimpleNameSyntax name) =>
        name.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name == name,
            QualifiedNameSyntax qualified => qualified.Right == name,
            MemberBindingExpressionSyntax binding => binding.Name == name,
            _ => false,
        };

    /// <summary>
    /// Whether an unqualified author member would be shadowed by a generated contextual-fragment lambda
    /// parameter. The operation's instance kind distinguishes the component's implicit <c>this</c> from
    /// another implicit receiver, notably the left side of an object initializer, where inserting
    /// <c>this.</c> would be invalid.
    /// </summary>
    private static bool NeedsGeneratedContextCollisionQualification(
        SimpleNameSyntax name,
        ISymbol symbol,
        ViewPartBodyContext context)
    {
        if (!name.Identifier.ValueText.StartsWith("__bcf_context_", System.StringComparison.Ordinal)
            || symbol.IsStatic
            || symbol is not (IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol)
            || IsMemberAccessName(name))
        {
            return false;
        }

        if (context.SemanticModel.GetOperation(name, context.CancellationToken)
            is IMemberReferenceOperation
            {
                Instance: IInstanceReferenceOperation
                {
                    ReferenceKind: InstanceReferenceKind.ContainingTypeInstance,
                },
            })
        {
            return true;
        }

        return name.Parent is InvocationExpressionSyntax invocation
            && context.SemanticModel.GetOperation(invocation, context.CancellationToken)
                is IInvocationOperation
            {
                Instance: IInstanceReferenceOperation
                {
                    ReferenceKind: InstanceReferenceKind.ContainingTypeInstance,
                },
            };
    }

    /// <summary>
    /// Whether <paramref name="name"/> is the type written ahead of a parenthesized designation list, the
    /// <c>var</c> of <c>var (a, b) = e</c>. Read from the designation rather than from the spelling: the
    /// same node type carries a single designation in <c>out var x</c>, where the inferred type is legal
    /// and is written (#342). The parent settles it, because a declaration expression's other child is a
    /// designation and no designation is a name.
    /// </summary>
    private static bool IsDeconstructionDeclarationType(SimpleNameSyntax name) =>
        name.Parent is DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax };

    private static bool IsInsideNameof(SyntaxNode node)
    {
        // Flipping this to `current is null` — which turns the loop's body unreachable, since node.Parent is
        // non-null for anything this file walks — is a stryker survivor, measured equivalent rather than
        // assumed: hand-applying it and running the full suite (BlazorCodeFirst.Compiler.Tests and
        // BlazorCodeFirst.DiagnosticTests) left every test passing unchanged. Reading why: this method's
        // whole reason to exist is defensive redundancy against the first pass's own nameof collapse (see
        // TryCreateNameofConstant) — every nameof(...) is a compile-time constant unconditionally, so the
        // first pass always collapses it and records its span, and IsNestedInReplaced already refuses to
        // reprocess anything inside that span before this method's own caller is ever reached. No input
        // exists where a name is genuinely inside an uncollapsed nameof for this loop to still be needed.
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList: var arguments,
                }
                && arguments.Span.Contains(node.Span))
            {
                // Flipping this to false is an unreached stryker mutant for the same reason the loop
                // condition above is: nothing reaches this line with the loop's body live.
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The identifier a local-declaring node binds: a declarator (<c>var x = e</c>) or a designation
    /// (<c>e is T x</c>). The two forms a transplanted statement can declare a local through, which is why
    /// this is narrower than <c>AuthoredContextNameHygiene.TryGetDeclaredIdentifier</c> — that one answers
    /// for every declaring form, including a lambda's own parameter, whose declaration is not the
    /// generator's to rename.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="RenderExpressionAnalyzer"/>, which registers these locals as render
    /// variables. One list of forms for both: a form registered there but not recognized here would leave
    /// the declaration under the author's name while its references became holes.
    /// </remarks>
    internal static bool TryGetDeclaredLocalIdentifier(SyntaxNode node, out SyntaxToken identifier)
    {
        identifier = node switch
        {
            VariableDeclaratorSyntax variable => variable.Identifier,
            SingleVariableDesignationSyntax designation => designation.Identifier,
            _ => default,
        };

        return identifier.RawKind != 0;
    }

    private static bool IsNestedInReplaced(TextSpan span, List<TextSpan> replacedSpans)
    {
        foreach (var replaced in replacedSpans)
        {
            if (replaced.Contains(span))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a transient symbol-aware rename plan for authored declarations whose source names could
    /// equal a generated contextual-fragment parameter at an expansion site. Only rewritten strings flow
    /// into <see cref="ExpressionTemplate"/>; symbols and spans remain confined to this analysis call.
    /// </summary>
    /// <remarks>
    /// What reaches it is narrower than what it can rename. Every position that transplants under the
    /// author's names refuses a reserved name outright rather than renaming it
    /// (<c>RenderExpressionAnalyzer.DeclaresReservedName</c>), and that scan covers the declarations at
    /// such a position's own level. What is left to rename here is what the scan does not reach: a
    /// declaration inside a nested lambda, and a lambda's own parameter, which no scan sees because the
    /// generator does not transplant a parameter's declaration.
    /// </remarks>
    private sealed class AuthoredContextNameHygiene
    {
        private const string GeneratedContextPrefix = "__bcf_context_";
        private const string AuthoredContextPrefix = "__bcf_authored_context_";

        private readonly Dictionary<ISymbol, string> _names;

        private AuthoredContextNameHygiene(
            Dictionary<ISymbol, string> names,
            ImmutableArray<AuthoredDeclarationRename> declarations)
        {
            _names = names;
            Declarations = declarations;
        }

        public ImmutableArray<AuthoredDeclarationRename> Declarations { get; }

        public static AuthoredContextNameHygiene Create(
            SyntaxNode expression,
            ViewPartBodyContext context)
        {
            // Built on the first rename, not up front. Its only reader is the disambiguation loop below,
            // which runs only for an authored declaration literally spelled __bcf_context_<digits> — the
            // collision this class exists for, and one almost no expression contains. Eagerly, every
            // expression in every body paid a full DescendantTokens() walk plus a string hash per
            // identifier to fill a set nothing went on to read.
            HashSet<string>? usedNames = null;

            var names = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
            var declarations = ImmutableArray.CreateBuilder<AuthoredDeclarationRename>();
            var renameOrdinal = 0;

            foreach (var node in expression.DescendantNodesAndSelf())
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if (!TryGetDeclaredIdentifier(node, out var identifier)
                    || !IsGeneratedContextName(identifier.ValueText)
                    || context.SemanticModel.GetDeclaredSymbol(node, context.CancellationToken)
                        is not { } symbol
                    || names.ContainsKey(symbol))
                {
                    continue;
                }

                usedNames ??= CollectIdentifierNames(expression);

                var baseName = $"{AuthoredContextPrefix}{renameOrdinal++}";
                var name = baseName;
                var disambiguator = 0;
                while (!usedNames.Add(name))
                    name = $"{baseName}_{++disambiguator}";

                names.Add(symbol, name);
                declarations.Add(new AuthoredDeclarationRename(identifier.Span, name));
            }

            return new AuthoredContextNameHygiene(names, declarations.ToImmutable());
        }

        public bool TryGetName(ISymbol symbol, out string name) =>
            _names.TryGetValue(symbol, out name!);

        /// <summary>Every identifier spelled anywhere in <paramref name="expression"/>.</summary>
        private static HashSet<string> CollectIdentifierNames(SyntaxNode expression)
        {
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var token in expression.DescendantTokens())
            {
                if (token.IsKind(SyntaxKind.IdentifierToken))
                    names.Add(token.ValueText);
            }

            return names;
        }

        private static bool IsGeneratedContextName(string name)
        {
            if (!name.StartsWith(GeneratedContextPrefix, System.StringComparison.Ordinal)
                || name.Length == GeneratedContextPrefix.Length)
            {
                return false;
            }

            for (var index = GeneratedContextPrefix.Length; index < name.Length; index++)
            {
                if (name[index] is < '0' or > '9')
                    return false;
            }

            return true;
        }

        private static bool TryGetDeclaredIdentifier(SyntaxNode node, out SyntaxToken identifier)
        {
            identifier = node switch
            {
                ParameterSyntax parameter => parameter.Identifier,
                VariableDeclaratorSyntax variable => variable.Identifier,
                SingleVariableDesignationSyntax designation => designation.Identifier,
                ForEachStatementSyntax forEach => forEach.Identifier,
                CatchDeclarationSyntax catchDeclaration => catchDeclaration.Identifier,
                LocalFunctionStatementSyntax localFunction => localFunction.Identifier,
                FromClauseSyntax fromClause => fromClause.Identifier,
                LetClauseSyntax letClause => letClause.Identifier,
                JoinClauseSyntax joinClause => joinClause.Identifier,
                JoinIntoClauseSyntax joinIntoClause => joinIntoClause.Identifier,
                QueryContinuationSyntax continuation => continuation.Identifier,
                _ => default,
            };

            return identifier.RawKind != 0;
        }
    }

    private readonly record struct AuthoredDeclarationRename(TextSpan Span, string Name);

    private readonly record struct Replacement(
        TextSpan Span,
        ImmutableArray<ExpressionSegment> Segments);
}
