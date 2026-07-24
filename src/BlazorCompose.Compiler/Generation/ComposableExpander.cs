using System;
using System.Collections.Immutable;
using System.Text;
using BlazorCompose.Compiler.Analysis;
using BlazorCompose.Compiler.Diagnostics;

namespace BlazorCompose.Compiler.Generation;

/// <summary>
/// The value-equal outcome of expanding a component <c>Body</c> template: the final
/// <see cref="RenderNode"/> tree (or <see langword="null"/> when expansion failed) plus the call-site
/// BC1002 <see cref="Diagnostics"/> captured as symbol-free data.
/// </summary>
internal readonly record struct ExpansionResult(
    RenderNode? Node,
    ImmutableArray<DiagnosticInfo> Diagnostics);

/// <summary>
/// Statically expands <c>[Composable]</c> call template nodes into the emittable <see cref="RenderNode"/>
/// tree.  Every template node — including composable call nodes that consume no render sequence — is
/// assigned a global logical preorder ordinal used only to name the typed argument locals, so repeated
/// helpers at different depths cannot collide.  Expansion is a pure function of the template tree, the
/// registry, and the generated component's containing-type key; it performs no rendering and evaluates no
/// runtime composable calls.
/// </summary>
internal static class ComposableExpander
{
    internal static ExpansionResult Expand(
        RenderTemplateNode root,
        ComposableRegistry registry,
        ImmutableArray<string> generatedTypeInheritanceKeys)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var nextLogicalPreorderOrdinal = 0;

        var node = ExpandNode(
            root,
            [],
            ref nextLogicalPreorderOrdinal,
            [],
            registry,
            generatedTypeInheritanceKeys,
            diagnostics);

        return new ExpansionResult(node, diagnostics.ToImmutable());
    }

    /// <param name="substitution">
    /// The local names bound to the enclosing composable's parameter holes; empty at the component
    /// <c>Body</c> root, where no holes exist.
    /// </param>
    /// <param name="activeMethodStack">
    /// The method keys currently being expanded along this path, used for cycle detection.  Sibling calls
    /// to the same composable are not cycles because each branch receives an independent immutable stack.
    /// </param>
    private static RenderNode? ExpandNode(
        RenderTemplateNode node,
        ImmutableArray<string> substitution,
        ref int nextLogicalPreorderOrdinal,
        ImmutableArray<string> activeMethodStack,
        ComposableRegistry registry,
        ImmutableArray<string> generatedTypeInheritanceKeys,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        // Every node consumes one logical preorder ordinal, assigned before its subtree is visited.
        var ordinal = nextLogicalPreorderOrdinal++;

        switch (node)
        {
            case IfTemplateNode ifNode:
                {
                    var thenNode = ExpandNode(
                        ifNode.Then,
                        substitution,
                        ref nextLogicalPreorderOrdinal,
                        activeMethodStack,
                        registry,
                        generatedTypeInheritanceKeys,
                        diagnostics);
                    if (thenNode is null)
                        return null;

                    RenderNode? otherwiseNode = null;
                    if (ifNode.Otherwise is not null)
                    {
                        otherwiseNode = ExpandNode(
                            ifNode.Otherwise,
                            substitution,
                            ref nextLogicalPreorderOrdinal,
                            activeMethodStack,
                            registry,
                            generatedTypeInheritanceKeys,
                            diagnostics);
                        if (otherwiseNode is null)
                            return null;
                    }

                    return new IfNode(ifNode.Condition.Substitute(substitution), thenNode, otherwiseNode);
                }

            case ForEachTemplateNode forEach:
                {
                    // The preorder `ordinal` (assigned at the top of ExpandNode) names a loop variable
                    // unique across the whole component. Source is bound in the outer scope; key/content
                    // are bound in the extended scope whose last slot is this loop variable.
                    var loopVariableName = $"__bc_item_{ordinal}";
                    var source = forEach.Source.Substitute(substitution);
                    var extended = substitution.Add(loopVariableName);
                    var key = forEach.Key.Substitute(extended);

                    var content = ExpandNode(
                        forEach.Content,
                        extended,
                        ref nextLogicalPreorderOrdinal,
                        activeMethodStack,
                        registry,
                        generatedTypeInheritanceKeys,
                        diagnostics);
                    if (content is null)
                        return null;

                    // The key is applied to the content's root element/component frame. Region-rooted
                    // content (a bare If/ForEach, or a composable whose expanded body is region-rooted)
                    // has no keyable frame. BC3003 is reported by KeyabilityResolver (reachability-
                    // independent, deduped per definition/component); suppress emission here so no SetKey
                    // lands on a region.
                    if (!IsKeyableRoot(content))
                        return null;

                    return new ForEachNode(source, key, content, loopVariableName);
                }

            case ComponentTemplateNode component:
                {
                    var parameters = ImmutableArray.CreateBuilder<ComponentParameter>(component.Parameters.Length);
                    foreach (var parameter in component.Parameters)
                        parameters.Add(new ComponentParameter(parameter.Name, parameter.Value.Substitute(substitution)));
                    return new ComponentNode(component.TypeName, parameters.ToImmutable());
                }

            case TextContentTemplateNode text:
                return new TextContentNode(text.Content.Substitute(substitution));

            case ElementTemplateNode element:
                {
                    var children = ImmutableArray.CreateBuilder<RenderNode>(element.Children.Length);
                    foreach (var child in element.Children.AsImmutableArray())
                    {
                        var expanded = ExpandNode(
                            child, substitution, ref nextLogicalPreorderOrdinal,
                            activeMethodStack, registry, generatedTypeInheritanceKeys, diagnostics);
                        if (expanded is null)
                            return null;
                        children.Add(expanded);
                    }

                    var events = ImmutableArray.CreateBuilder<EventTemplate>(element.Events.Length);
                    foreach (var e in element.Events.AsImmutableArray())
                        events.Add(new EventTemplate(
                            e.AttributeName.Substitute(substitution),
                            e.Handler.Substitute(substitution)));

                    return new ElementNode(
                        element.Tag,
                        SubstituteClasses(element.Classes, substitution),
                        events.ToImmutable(),
                        children.ToImmutable());
                }

            case ComposableCallTemplateNode call:
                return ExpandCall(
                    call,
                    ordinal,
                    substitution,
                    ref nextLogicalPreorderOrdinal,
                    activeMethodStack,
                    registry,
                    generatedTypeInheritanceKeys,
                    diagnostics);

            default:
                return null;
        }
    }

    private static ExpansionNode? ExpandCall(
        ComposableCallTemplateNode call,
        int callPreorderOrdinal,
        ImmutableArray<string> substitution,
        ref int nextLogicalPreorderOrdinal,
        ImmutableArray<string> activeMethodStack,
        ComposableRegistry registry,
        ImmutableArray<string> generatedTypeInheritanceKeys,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var methodKey = call.MethodKey;

        if (!registry.TryGet(methodKey, out var entry))
        {
            // A [Composable] method with no source declaration in this compilation (metadata-only) cannot
            // be inlined; report a call-site BC1002.
            diagnostics.Add(CreateDiagnostic(
                call,
                "no source declaration is available to expand at the call site"));
            return null;
        }

        foreach (var active in activeMethodStack)
        {
            if (string.Equals(active, methodKey, StringComparison.Ordinal))
            {
                diagnostics.Add(CreateDiagnostic(
                    call,
                    $"recursive composable expansion forms a cycle: {BuildCycleChain(activeMethodStack, call.DisplayName, registry)}"));
                return null;
            }
        }

        if (entry.Definition is null)
        {
            // The declaration is present but invalid; BC1002 was already reported at the declaration, so
            // the call site suppresses a duplicate diagnostic and simply fails to expand.
            return null;
        }

        var definition = entry.Definition;

        foreach (var requirement in definition.AccessRequirements)
        {
            if (!SatisfiesAccess(requirement, generatedTypeInheritanceKeys))
            {
                diagnostics.Add(CreateDiagnostic(
                    call,
                    $"references '{requirement.SymbolDisplayName}' which is not accessible from the expansion site"));
                return null;
            }
        }

        var parameters = definition.Parameters;

        // One typed local per parameter, named from the call's logical preorder ordinal and the
        // parameter ordinal so names are unique across the whole component.
        var innerNames = new string[parameters.Length];
        foreach (var parameter in parameters)
            innerNames[parameter.Ordinal] = CreateLocalName(callPreorderOrdinal, parameter.Ordinal);
        var innerSubstitution = ImmutableArray.Create(innerNames);

        // Emit the locals in source evaluation order (supplied arguments by source position, then implicit
        // defaults) while binding each to its parameter ordinal.  Argument initializers reference the
        // caller's scope, so they are substituted with the outer names.
        var ordered = call.Arguments.ToArray();
        Array.Sort(ordered, static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));

        var locals = ImmutableArray.CreateBuilder<LocalBinding>(ordered.Length);
        foreach (var argument in ordered)
        {
            var parameter = parameters[argument.ParameterOrdinal];
            locals.Add(new LocalBinding(
                parameter.TypeName,
                innerNames[argument.ParameterOrdinal],
                argument.Value.Substitute(substitution)));
        }

        var body = ExpandNode(
            definition.Body,
            innerSubstitution,
            ref nextLogicalPreorderOrdinal,
            activeMethodStack.Add(methodKey),
            registry,
            generatedTypeInheritanceKeys,
            diagnostics);
        if (body is null)
            return null;

        return new ExpansionNode(locals.ToImmutable(), body);
    }

    /// <summary>
    /// Determines whether an inlined body may legally name the referenced non-public member from the
    /// generated component type.  Because the value model is symbol-free, access is decided by comparing
    /// normalized type keys against the type that <em>declares</em> the referenced member
    /// (<see cref="ComposableAccessRequirement.RequiredContainingTypeKey"/>), not the composable's own
    /// containing type: a <see cref="ComposableAccessRequirementKind.SameContainingType"/> (private)
    /// member is legal only when the generated component *is* the declaring type, while a
    /// <see cref="ComposableAccessRequirementKind.DerivedContainingType"/> (protected/private-protected)
    /// member is legal when the declaring type appears anywhere in the generated component's inheritance
    /// chain (the component itself or any base type).
    /// </summary>
    private static bool SatisfiesAccess(
        ComposableAccessRequirement requirement,
        ImmutableArray<string> generatedTypeInheritanceKeys)
    {
        if (generatedTypeInheritanceKeys.IsDefaultOrEmpty)
            return false;

        return requirement.Kind switch
        {
            ComposableAccessRequirementKind.SameContainingType =>
                string.Equals(
                    requirement.RequiredContainingTypeKey,
                    generatedTypeInheritanceKeys[0],
                    StringComparison.Ordinal),
            ComposableAccessRequirementKind.DerivedContainingType =>
                InheritanceChainContains(generatedTypeInheritanceKeys, requirement.RequiredContainingTypeKey),
            _ => false,
        };
    }

    private static bool InheritanceChainContains(ImmutableArray<string> inheritanceKeys, string typeKey)
    {
        foreach (var key in inheritanceKeys)
        {
            if (string.Equals(key, typeKey, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Renders the active expansion stack plus the closing call into a readable <c>A -&gt; B -&gt; A</c>
    /// chain so a cycle diagnostic names every composable involved.  Display names are resolved from the
    /// registry so the chain reads in source terms rather than mangled method keys.
    /// </summary>
    private static string BuildCycleChain(
        ImmutableArray<string> activeMethodStack,
        string closingDisplayName,
        ComposableRegistry registry)
    {
        var builder = new StringBuilder();
        foreach (var methodKey in activeMethodStack)
        {
            var display = registry.TryGet(methodKey, out var entry) ? entry.DisplayName : methodKey;
            builder.Append(display).Append(" -> ");
        }

        builder.Append(closingDisplayName);
        return builder.ToString();
    }

    /// <summary>
    /// Determines whether an expanded content node's root frame is a single element or component (and so
    /// can carry a <c>SetKey</c>). <see cref="ExpansionNode"/> is transparent — its composable body's root
    /// is the real frame — so it is unwrapped. Element/component-rooted nodes (<see cref="ComponentNode"/>,
    /// <see cref="ElementNode"/>) are keyable; region-rooted nodes (<see cref="IfNode"/>,
    /// <see cref="ForEachNode"/>, <see cref="TextContentNode"/>) are not.
    /// </summary>
    private static bool IsKeyableRoot(RenderNode node) => node switch
    {
        ExpansionNode expansion => IsKeyableRoot(expansion.Body),
        ComponentNode or ElementNode => true,
        _ => false,
    };

    private static EquatableArray<ExpressionTemplate> SubstituteClasses(
        EquatableArray<ExpressionTemplate> classes, ImmutableArray<string> substitution)
    {
        if (classes.Length == 0)
            return classes;

        var builder = ImmutableArray.CreateBuilder<ExpressionTemplate>(classes.Length);
        foreach (var @class in classes)
            builder.Add(@class.Substitute(substitution));
        return builder.ToImmutable();
    }

    private static string CreateLocalName(int callPreorderOrdinal, int parameterOrdinal) =>
        $"__bc_arg_{callPreorderOrdinal}_{parameterOrdinal}";

    private static DiagnosticInfo CreateDiagnostic(ComposableCallTemplateNode call, string reason) =>
        DiagnosticInfo.Create(
            DiagnosticDescriptors.BC1002,
            call.Location.ToLocation(),
            [call.DisplayName, reason]);
}
