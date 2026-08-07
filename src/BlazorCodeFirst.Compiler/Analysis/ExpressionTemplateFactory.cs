using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Normalizes a definition-side expression into a symbol-free <see cref="ExpressionTemplate"/> so it can
/// be inlined at any expansion site. The generated <c>RenderView</c> carries no <c>using</c> directives,
/// so every name that would otherwise depend on an import must be made self-contained. Replacement
/// decisions use Roslyn symbol identity, never textual substitution, so that:
/// <list type="bullet">
/// <item>identifiers bound to composable parameters become <see cref="ParameterHoleExpressionSegment"/>;</item>
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
/// <item>references to source-local constructs (local functions or locals from an enclosing scope)
/// that cannot exist in generated code report a single declaration BCF1002;</item>
/// <item>local, lambda, and unrecognized identifiers plus all trivia are preserved as literal text.</item>
/// </list>
/// </summary>
internal static class ExpressionTemplateFactory
{
    // Fully qualified type name without its type-argument list. Used to qualify only the identifier token
    // of a generic name so the written type-argument syntax (including nullable annotations) survives and
    // each type argument is qualified independently by the traversal.
    private static readonly SymbolDisplayFormat QualifiedNameWithoutTypeArguments =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None);

    public static ExpressionTemplate Create(ExpressionSyntax expression, ComposableBodyContext context)
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

                if (TryCreateExtensionMethodCall(invocation, extensionMethod, context, out var extensionSegments))
                    replacements.Add(new Replacement(invocation.Span, extensionSegments));

                // Whether normalized or rejected (a BCF1002 was recorded inside), the invocation is fully
                // handled here; record its span so the second pass leaves its inner names untouched.
                replacedSpans.Add(invocation.Span);
            }
        }

        // Second pass: normalize simple names into parameter holes, fully qualified references, or recorded
        // accessibility requirements.
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (node is not SimpleNameSyntax name)
                continue;

            // A receiver, method, or type-argument name inside an already-rewritten invocation (an
            // extension call, or a collapsed nameof) is owned by that whole-span replacement.
            if (IsNestedInReplaced(name.Span, replacedSpans))
                continue;

            if (TryReportUnresolvedType(name, context))
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
                if (TryQualifyNamespaceQualifiedType(name, context, replacements, replacedSpans))
                    continue;

                RecordMemberAccessRequirement(name, context);
                continue;
            }

            var symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
            if (symbol is null)
                continue;

            if (IsUnsupportedSourceLocalReference(symbol, expression, out var unsupportedReason))
            {
                context.ReportUnsupportedReference(name.GetLocation(), unsupportedReason);
                continue;
            }

            if (name is IdentifierNameSyntax
                && context.TryGetParameterOrdinal(symbol, out var ordinal))
            {
                AddReplacement(replacements, replacedSpans, name.Span,
                    new ParameterHoleExpressionSegment(ordinal));
                continue;
            }

            // A type reference, including a generic one such as List<string>, is fully qualified. A
            // generic name qualifies only its identifier token so the written type-argument list (with any
            // nullable annotations) survives and each type argument is qualified independently below.
            if (symbol is INamedTypeSymbol typeSymbol && name is IdentifierNameSyntax or GenericNameSyntax)
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
                && !IsQualifiedReference(name))
            {
                RecordAccessRequirement(symbol, context);
                var span = IdentifierSpan(name);
                var containing = symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                AddReplacement(replacements, replacedSpans, span,
                    new LiteralExpressionSegment($"{containing}.{symbol.Name}"));
            }
        }

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
        ExpressionSyntax expression,
        ComposableBodyContext context)
    {
        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
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

    internal static bool TryReportUnresolvedType(
        SimpleNameSyntax name,
        ComposableBodyContext context)
    {
        var alias = context.SemanticModel.GetAliasInfo(name, context.CancellationToken);
        var type = GetReferencedType(name, context);
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
        ComposableBodyContext context)
    {
        if (context.SemanticModel.GetAliasInfo(name, context.CancellationToken)
            is { Target: ITypeSymbol aliasType })
        {
            return aliasType;
        }

        if (context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol
            is ITypeSymbol symbolType)
        {
            return symbolType;
        }

        if (FindTypeOnlySyntax(name) is not { } typeSyntax)
            return null;

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
            QualifiedNameSyntax qualified =>
                qualified.Left == child || qualified.Right == child,
            AliasQualifiedNameSyntax alias =>
                alias.Alias == child || alias.Name == child,
            _ => false,
        };

    private static void AddReplacement(
        List<Replacement> replacements,
        List<TextSpan> replacedSpans,
        TextSpan span,
        ExpressionSegment segment)
    {
        replacements.Add(new Replacement(span, [segment]));
        replacedSpans.Add(span);
    }

    private static ExpressionTemplate Splice(
        ExpressionSyntax expression,
        List<Replacement> replacements,
        ConstantInfo? constant)
    {
        replacements.Sort(static (left, right) => left.Span.Start.CompareTo(right.Span.Start));

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
        ExpressionSyntax root,
        out string reason)
    {
        reason = string.Empty;

        var kindLabel = symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.LocalFunction } => "local function",
            ILocalSymbol => "local",
            IRangeVariableSymbol => "range variable",
            ILabelSymbol => "label",
            _ => null,
        };

        if (kindLabel is null)
            return false;

        foreach (var declaration in symbol.DeclaringSyntaxReferences)
        {
            if (root.FullSpan.Contains(declaration.Span))
                return false;
        }

        reason = $"references {kindLabel} '{symbol.Name}' that cannot exist in generated component code";
        return true;
    }

    private static void RecordAccessRequirement(ISymbol symbol, ComposableBodyContext context)
    {
        var kind = symbol.DeclaredAccessibility switch
        {
            Accessibility.Private => (ComposableAccessRequirementKind?)ComposableAccessRequirementKind.SameContainingType,
            Accessibility.Protected => ComposableAccessRequirementKind.DerivedContainingType,
            Accessibility.ProtectedAndInternal => ComposableAccessRequirementKind.DerivedContainingType,
            _ => null,
        };

        if (kind is null)
            return;

        // The requirement is keyed on the type that declares the referenced member so expansion checks
        // that type, not the composable's own containing type, against the component's inheritance
        // chain. A private/protected member always has a containing type; guard defensively regardless.
        var requiredContainingTypeKey = symbol.ContainingType is { } containingType
            ? containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : string.Empty;

        context.AddAccessRequirement(new ComposableAccessRequirement(
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
    private static void RecordMemberAccessRequirement(SimpleNameSyntax name, ComposableBodyContext context)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
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
        ComposableBodyContext context,
        List<Replacement> replacements,
        List<TextSpan> replacedSpans)
    {
        if (context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol
            is not INamedTypeSymbol typeSymbol)
        {
            return false;
        }

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
            default:
                return false;
        }

        // Only a namespace-qualified left needs whole-path rewriting. A type-qualified left (an enclosing
        // type naming a nested type) is already handled by qualifying that left identifier on its own, and
        // a value receiver is a genuine member access that keeps its unqualified text.
        if (context.SemanticModel.GetSymbolInfo(leftSide, context.CancellationToken).Symbol
            is not INamespaceSymbol)
        {
            return false;
        }

        RecordAccessRequirement(typeSymbol, context);

        // Replace from the start of the whole path through the type identifier token; a generic name's
        // trailing type-argument list stays in place so its arguments are qualified independently.
        var span = TextSpan.FromBounds(qualifiedNode.SpanStart, IdentifierSpan(name).End);
        var qualifiedText = name is GenericNameSyntax
            ? typeSymbol.ToDisplayString(QualifiedNameWithoutTypeArguments)
            : typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
        ComposableBodyContext context)
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
        ComposableBodyContext context)
    {
        var generic = invocation.Expression switch
        {
            GenericNameSyntax direct => direct,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax member } => member,
            _ => null,
        };

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
        ComposableBodyContext context,
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
            if (!IsNameableType(typeArgument))
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
        foreach (var segment in Create(memberAccess.Expression, context).Segments)
            builder.Add(segment);

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            builder.Add(new LiteralExpressionSegment(", " + LeadingArgumentText(argument)));
            foreach (var segment in Create(argument.Expression, context).Segments)
                builder.Add(segment);
        }

        builder.Add(new LiteralExpressionSegment(")"));

        // The declaring type and the method itself must be accessible from the expansion site.
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
        return offset > 0 ? argument.ToString().Substring(0, offset) : string.Empty;
    }

    /// <summary>
    /// Determines whether <paramref name="type"/> can be written as a fully qualified type name in a
    /// generated file with no <c>using</c> directives. Anonymous types, pointer types, open type
    /// parameters, file-local types, and otherwise unnameable types cannot, so an extension method that
    /// fixes such a type argument cannot be normalized in a semantics-preserving way.
    /// </summary>
    private static bool IsNameableType(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsNameableType(array.ElementType);

            case IPointerTypeSymbol:
                return false;

            case ITypeParameterSymbol:
                return false;

            case IDynamicTypeSymbol:
                return true;

            case INamedTypeSymbol named:
                if (named.IsAnonymousType || named.IsFileLocal || !named.CanBeReferencedByName)
                    return false;

                for (var containing = named.ContainingType; containing is not null; containing = containing.ContainingType)
                {
                    if (containing.IsFileLocal || !containing.CanBeReferencedByName)
                        return false;
                }

                foreach (var argument in named.TypeArguments)
                {
                    if (!IsNameableType(argument))
                        return false;
                }

                return true;

            default:
                return false;
        }
    }

    private static TextSpan IdentifierSpan(SimpleNameSyntax name) =>
        name is GenericNameSyntax generic ? generic.Identifier.Span : name.Span;

    private static bool IsMemberAccessName(SimpleNameSyntax name) =>
        name.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name == name,
            QualifiedNameSyntax qualified => qualified.Right == name,
            MemberBindingExpressionSyntax binding => binding.Name == name,
            _ => false,
        };

    private static bool IsQualifiedReference(SimpleNameSyntax name) =>
        name.Parent is MemberAccessExpressionSyntax or QualifiedNameSyntax or MemberBindingExpressionSyntax;

    private static bool IsInsideNameof(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList: var arguments,
                }
                && arguments.Span.Contains(node.Span))
            {
                return true;
            }
        }

        return false;
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

    private readonly record struct Replacement(
        TextSpan Span,
        ImmutableArray<ExpressionSegment> Segments);
}
