using System.Collections.Immutable;
using System.Text;
using BlazorCodeFirst.Compiler.Analysis;
using BlazorCodeFirst.Compiler.Diagnostics;

namespace BlazorCodeFirst.Compiler.Generation;

/// <summary>
/// The value-equal outcome of expanding a component's design-time expression (<c>Body</c> or
/// <c>Chrome</c>) template: the final <see cref="RenderNode"/> tree (or <see langword="null"/> when
/// expansion failed) plus the call-site BCF1002 <see cref="Diagnostics"/> captured as symbol-free data.
/// </summary>
internal readonly record struct ExpansionResult(
    RenderNode? Node,
    ImmutableArray<DiagnosticInfo> Diagnostics);

/// <summary>
/// Statically expands <c>[ViewPart]</c> call template nodes into the emittable <see cref="RenderNode"/>
/// tree. Every template node, including view part call nodes that consume no render sequence, is
/// assigned a global logical preorder ordinal used to name typed argument locals and contextual-fragment
/// parameters, so repeated helpers and nested templates at different depths cannot collide. Expansion is
/// a pure function of the template tree, the registry, and the generated component's containing-type key;
/// it performs no rendering and evaluates no runtime view part calls.
/// </summary>
internal static class ViewPartExpander
{
    /// <summary>
    /// Everything one call to <see cref="Expand"/> carries unchanged through the whole recursion:
    /// invariant across every node, unlike <c>substitution</c>/<c>activeMethodStack</c>/
    /// <c>currentScope</c>, which vary per call as the traversal descends. Bundled into one type so a
    /// future cross-cutting input (the way <c>cssScopes</c> itself joined <c>registry</c> and
    /// <c>generatedTypeInheritanceKeys</c> here) is one field added to this record rather than another
    /// parameter threaded through every recursive call site in this file.
    /// </summary>
    private sealed record ExpansionEnvironment(
        ViewPartRegistry Registry,
        ImmutableArray<string> GeneratedTypeInheritanceKeys,
        CssScopeRegistry CssScopes,
        ImmutableArray<DiagnosticInfo>.Builder Diagnostics);

    internal static ExpansionResult Expand(
        RenderNode root,
        ViewPartRegistry registry,
        ImmutableArray<string> generatedTypeInheritanceKeys,
        CssScopeRegistry cssScopes,
        string? hostCssScope)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var nextLogicalPreorderOrdinal = 0;
        var environment = new ExpansionEnvironment(registry, generatedTypeInheritanceKeys, cssScopes, diagnostics);

        var node = ExpandNode(
            root,
            [],
            ref nextLogicalPreorderOrdinal,
            [],
            hostCssScope,
            environment);

        return new ExpansionResult(node, diagnostics.ToImmutable());
    }

    /// <param name="node">The node being expanded.</param>
    /// <param name="substitution">
    /// The arguments bound to the enclosing view part's parameter holes, code plus constant; empty at the
    /// component's design-time expression root (<c>Body</c> or <c>Chrome</c>), where no holes exist.
    /// </param>
    /// <param name="nextLogicalPreorderOrdinal">The next logical preorder ordinal to assign, advanced by one for this node before its subtree is visited.</param>
    /// <param name="activeMethodStack">
    /// The method keys currently being expanded along this path, used for cycle detection. Sibling calls
    /// to the same view part are not cycles because each branch receives an independent immutable stack.
    /// </param>
    /// <param name="currentScope">The <c>.cs.css</c> scope currently in effect, threaded through view part calls.</param>
    /// <param name="environment">The registry, inheritance keys, CSS scopes, and diagnostics shared across the whole expansion.</param>
    private static RenderNode? ExpandNode(
        RenderNode node,
        ImmutableArray<SubstitutedArgument> substitution,
        ref int nextLogicalPreorderOrdinal,
        ImmutableArray<string> activeMethodStack,
        string? currentScope,
        ExpansionEnvironment environment)
    {
        // Every node consumes one logical preorder ordinal, assigned before its subtree is visited.
        var ordinal = nextLogicalPreorderOrdinal++;

        switch (node)
        {
            case IfNode ifNode:
                return ExpandBranches(
                    ifNode.Then,
                    ifNode.Otherwise,
                    substitution,
                    ref nextLogicalPreorderOrdinal,
                    activeMethodStack,
                    currentScope,
                    environment,
                    (then, otherwise) => new IfNode(ifNode.ConditionExpression.Substitute(substitution), then, otherwise));

            case TransplantedIfNode transplantedIf:
                return ExpandBranches(
                    transplantedIf.Then,
                    transplantedIf.Otherwise,
                    substitution,
                    ref nextLogicalPreorderOrdinal,
                    activeMethodStack,
                    currentScope,
                    environment,
                    (then, otherwise) => new TransplantedIfNode(
                        transplantedIf.Condition.Substitute(substitution), then, otherwise));

            case ForEachNode forEach:
                {
                    // The preorder `ordinal` (assigned at the top of ExpandNode) names a loop variable
                    // unique across the whole component. Source is bound in the outer scope; key/content
                    // are bound in the extended scope whose last slot is this loop variable.
                    var loopVariableName = $"__bcf_item_{ordinal}";
                    var source = forEach.Source.Substitute(substitution);
                    var extended = substitution.Add(new SubstitutedArgument(loopVariableName, Constant: null));
                    var key = forEach.Key?.Substitute(extended);

                    var content = ExpandNode(
                        forEach.Content,
                        extended,
                        ref nextLogicalPreorderOrdinal,
                        activeMethodStack,
                        currentScope,
                        environment);
                    if (content is null)
                        return null;

                    // The key is applied to the content's root element/component frame. Region-rooted
                    // content, or a root already carrying its own key, has nowhere a second SetKey can
                    // land. BCF3003/BCF3032 report this at the template layer; suppress emission here so
                    // nothing lands on a frame that cannot take it. A declined key attaches nothing, so
                    // every root is admitted (#172).
                    if (key is not null)
                    {
                        var root = Analysis.KeyabilityResolver.ResolveRoot(content, environment.Registry);
                        if (root.Kind != Analysis.ContentRootKind.Element || root.IsKeyed)
                            return null;
                    }

                    // Location is a template-phase field (BCF3002/BCF3003 blame it); an expanded node
                    // carries no absolute TextSpan (see RenderNode's remarks).
                    return new ForEachNode(source, key, content, Location: null, LoopVariableName: loopVariableName);
                }

            case ComponentNode component:
                {
                    var parameters = ImmutableArray.CreateBuilder<ComponentParameter>(component.Parameters.Length);
                    // A `with`: expansion substitutes holes in the value and changes nothing else, and a
                    // constructor call here names the channels that existed when it was written. The value
                    // type this parameter carries would have been dropped exactly that way, silently.
                    foreach (var parameter in component.Parameters)
                        parameters.Add(parameter with { Value = parameter.Value.Substitute(substitution) });

                    var slots = ImmutableArray.CreateBuilder<ComponentSlotNode>(component.Slots.Length);
                    foreach (var slot in component.Slots)
                    {
                        string? contextVariableName = null;
                        var slotSubstitution = substitution;
                        if (slot.Kind == ComponentSlotKind.GenericContextual)
                        {
                            contextVariableName = $"__bcf_context_{nextLogicalPreorderOrdinal}";
                            slotSubstitution = substitution.Add(
                                new SubstitutedArgument(contextVariableName, Constant: null));
                        }

                        // Slot content is a real subtree: it consumes preorder ordinals and may itself
                        // contain ForEach/[ViewPart] calls, so it expands through the same recursion.
                        var content = ExpandNode(
                            slot.Content,
                            slotSubstitution,
                            ref nextLogicalPreorderOrdinal,
                            activeMethodStack,
                            currentScope,
                            environment);
                        if (content is null)
                            return null;

                        slots.Add(new ComponentSlotNode(slot.Name, content)
                        {
                            Kind = slot.Kind,
                            ContextTypeName = slot.ContextTypeName,
                            ContextVariableName = contextVariableName,
                        });
                    }

                    return new ComponentNode(
                        component.TypeName,
                        parameters.ToImmutable(),
                        slots.ToImmutable(),
                        SubstituteAttributes(component.Attributes, substitution))
                    {
                        Key = component.Key?.Substitute(substitution),
                        RenderMode = component.RenderMode?.Substitute(substitution),
                        Ref = component.Ref?.Substitute(substitution),
                    };
                }

            case TextContentNode text:
                return new TextContentNode(text.Content.Substitute(substitution));

            case ElementNode element:
                {
                    if (ExpandChildren(
                            element.Children, substitution, ref nextLogicalPreorderOrdinal,
                            activeMethodStack, currentScope, environment)
                        is not { } children)
                    {
                        return null;
                    }

                    var events = ImmutableArray.CreateBuilder<EventTemplate>(element.Events.Length);
                    foreach (var e in element.Events.AsImmutableArray())
                    {
                        // Every expression channel of the event, written as a `with` for the reason the
                        // bindings below give: a channel added to EventTemplate is carried through rather
                        // than silently dropped. The modifiers travel because a [ViewPart] may take one as
                        // a parameter, and a dropped one would emit nothing with no diagnostic (#368).
                        events.Add(e with
                        {
                            Handler = e.Handler.Substitute(substitution),
                            PreventDefault = e.PreventDefault?.Substitute(substitution),
                            StopPropagation = e.StopPropagation?.Substitute(substitution),
                        });
                    }

                    var bindings = ImmutableArray.CreateBuilder<BindTemplate>(element.Bindings.Length);
                    foreach (var b in element.Bindings.AsImmutableArray())
                    {
                        // Every expression channel of the binding, and only those: the rest of the record
                        // is resolved facts. Written as a `with` so that a channel added to BindTemplate
                        // is carried through rather than silently dropped here — though carried through
                        // is not substituted, which is why #307's culture and format are named here as
                        // well as added there.
                        bindings.Add(b with
                        {
                            Value = b.Value.Substitute(substitution),
                            Setter = b.Setter?.Substitute(substitution),
                            Culture = b.Culture?.Substitute(substitution),
                            Format = b.Format?.Substitute(substitution),
                            PreventDefault = b.PreventDefault?.Substitute(substitution),
                            StopPropagation = b.StopPropagation?.Substitute(substitution),
                        });
                    }

                    return new ElementNode(
                        element.Tag,
                        SubstituteClasses(element.Classes, substitution),
                        SubstituteAttributes(element.Attributes, substitution),
                        events.ToImmutable(),
                        children)
                    {
                        Bindings = bindings.ToImmutable(),
                        Key = element.Key?.Substitute(substitution),
                        Ref = element.Ref?.Substitute(substitution),
                        FormName = element.FormName?.Substitute(substitution),
                        AttributesSplat = element.AttributesSplat?.Substitute(substitution),
                        CssScope = currentScope,
                    };
                }

            case RawMarkupNode raw:
                return new RawMarkupNode(raw.Content.Substitute(substitution));

            case RenderFragmentContentNode fragmentContent:
                return new RenderFragmentContentNode(fragmentContent.Content.Substitute(substitution));

            case OpaqueViewNode opaque:
                return new OpaqueViewNode(opaque.Call.Substitute(substitution));

            case TransplantedBlockNode transplanted:
                {
                    // One minted name per authored local the block declares, appended in the order analysis
                    // pushed them, so the ordinals the holes carry index this array (#336). The preorder
                    // `ordinal` names them apart from every other expansion of the same block, which is the
                    // collision this exists for.
                    var blockSubstitution = substitution;
                    for (var local = 0; local < transplanted.LocalCount; local++)
                    {
                        blockSubstitution = blockSubstitution.Add(
                            new SubstitutedArgument($"__bcf_local_{ordinal}_{local}", Constant: null));
                    }

                    var content = ExpandNode(
                        transplanted.Content,
                        blockSubstitution,
                        ref nextLogicalPreorderOrdinal,
                        activeMethodStack,
                        currentScope,
                        environment);
                    if (content is null)
                        return null;

                    // A body that declares through its expression alone reaches here to have its names
                    // minted and carries no statements to write, so the substitution above is the whole of
                    // its job and the wrapper is dropped rather than emitted empty (#343).
                    var statements = transplanted.Statements.Substitute(blockSubstitution);

                    return statements.Segments.Length == 0
                        ? content
                        : new TransplantedBlockNode(statements, content, transplanted.LocalCount);
                }

            case FragmentNode fragment:
                {
                    if (ExpandChildren(
                            fragment.Children, substitution, ref nextLogicalPreorderOrdinal,
                            activeMethodStack, currentScope, environment)
                        is not { } children)
                    {
                        return null;
                    }
                    return new FragmentNode(children);
                }

            case ViewPartCallNode call:
                return ExpandCall(
                    call,
                    ordinal,
                    substitution,
                    ref nextLogicalPreorderOrdinal,
                    activeMethodStack,
                    currentScope,
                    environment);

            case ContentHoleNode hole:
                {
                    // Lazy substitution: the caller's subtree is expanded here, where the callee names the
                    // hole, rather than at the call site. That is what makes zero, one, and many references
                    // each work -- every reference expands the argument again and consumes fresh preorder
                    // ordinals, so no two expansions can name the same local or loop variable.
                    var content = hole.ParameterOrdinal < substitution.Length
                        ? substitution[hole.ParameterOrdinal].Content
                        : null;

                    if (content is null)
                    {
                        // Unreachable through source: a slot ordinal is bound by every call that can be
                        // written (SlotView has no conversion to View, so the brackets are mandatory) and a
                        // View parameter cannot be optional. Failing to expand rather than asserting keeps a
                        // model defect from emitting a body with the hole silently dropped.
                        return null;
                    }

                    // Expanded under the *caller's* substitution and cycle stack, because the argument is an
                    // expression written there. Carrying the caller's stack is what keeps
                    // Wrap()[Wrap()[…]] from being reported as recursion: at the moment the inner call is
                    // expanded, the outer one is not on its own path.
                    return ExpandNode(
                        content.Template,
                        content.Substitution,
                        ref nextLogicalPreorderOrdinal,
                        content.ActiveMethodStack,
                        content.CssScope,
                        environment);
                }

            case ExpansionNode:
                throw new NotSupportedException(
                    "An ExpansionNode reached ExpandNode; it is a render-phase-only node produced by "
                        + "this method and never a valid input to it.");

            default:
                throw new NotSupportedException(
                    $"Unknown RenderNode type '{node.GetType().Name}'; add an ExpandNode case for it.");
        }
    }

    /// <summary>
    /// Expands a two-branch node's <c>then</c>/<c>otherwise</c>, shared by <see cref="IfNode"/>
    /// and <see cref="TransplantedIfNode"/>: the two differ only in which condition-carrying node
    /// type <paramref name="build"/> constructs from the expanded branches, not in how the branches
    /// themselves expand.
    /// </summary>
    private static RenderNode? ExpandBranches(
        RenderNode then,
        RenderNode? otherwise,
        ImmutableArray<SubstitutedArgument> substitution,
        ref int nextLogicalPreorderOrdinal,
        ImmutableArray<string> activeMethodStack,
        string? currentScope,
        ExpansionEnvironment environment,
        Func<RenderNode, RenderNode?, RenderNode> build)
    {
        var thenNode = ExpandNode(
            then, substitution, ref nextLogicalPreorderOrdinal, activeMethodStack, currentScope, environment);
        if (thenNode is null)
            return null;

        RenderNode? otherwiseNode = null;
        if (otherwise is not null)
        {
            otherwiseNode = ExpandNode(
                otherwise, substitution, ref nextLogicalPreorderOrdinal, activeMethodStack, currentScope,
                environment);
            if (otherwiseNode is null)
                return null;
        }

        return build(thenNode, otherwiseNode);
    }

    /// <summary>Expands each of a node's children in order, or null if any one fails. Shared by
    /// <see cref="ElementNode"/> and <see cref="FragmentNode"/>, the two shapes that
    /// carry a bare children list.</summary>
    private static ImmutableArray<RenderNode>? ExpandChildren(
        EquatableArray<RenderNode> children,
        ImmutableArray<SubstitutedArgument> substitution,
        ref int nextLogicalPreorderOrdinal,
        ImmutableArray<string> activeMethodStack,
        string? currentScope,
        ExpansionEnvironment environment)
    {
        var expanded = ImmutableArray.CreateBuilder<RenderNode>(children.Length);
        foreach (var child in children.AsImmutableArray())
        {
            var expandedChild = ExpandNode(
                child, substitution, ref nextLogicalPreorderOrdinal,
                activeMethodStack, currentScope, environment);
            if (expandedChild is null)
                return null;
            expanded.Add(expandedChild);
        }
        return expanded.ToImmutable();
    }

    private static ExpansionNode? ExpandCall(
        ViewPartCallNode call,
        int callPreorderOrdinal,
        ImmutableArray<SubstitutedArgument> substitution,
        ref int nextLogicalPreorderOrdinal,
        ImmutableArray<string> activeMethodStack,
        string? currentScope,
        ExpansionEnvironment environment)
    {
        var registry = environment.Registry;
        var diagnostics = environment.Diagnostics;
        var methodKey = call.MethodKey;

        if (!registry.TryGet(methodKey, out var entry))
        {
            // A [ViewPart] method with no source declaration in this compilation (metadata-only) cannot
            // be inlined; report a call-site BCF1002.
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
                    $"recursive view part expansion forms a cycle: {BuildCycleChain(activeMethodStack, call.DisplayName, registry)}"));
                return null;
            }
        }

        if (entry.Definition is null)
        {
            // The declaration is present but invalid; BCF1002 was already reported at the declaration, so
            // the call site suppresses a duplicate diagnostic and simply fails to expand.
            return null;
        }

        var definition = entry.Definition;

        foreach (var requirement in definition.AccessRequirements)
        {
            if (!SatisfiesAccess(requirement, environment.GeneratedTypeInheritanceKeys))
            {
                diagnostics.Add(CreateDiagnostic(
                    call,
                    $"references '{requirement.SymbolDisplayName}' which is not accessible from the expansion site"));
                return null;
            }
        }

        var parameters = definition.Parameters;

        // The substitution the body is expanded under: one slot per parameter, plus one for the slot ordinal
        // when the definition declares one. That ordering is the definition's own (SlotOrdinal is always
        // Parameters.Length), so the array is indexed by exactly the ordinals the holes carry.
        var innerArguments = new SubstitutedArgument[parameters.Length + (definition.HasSlot ? 1 : 0)];

        // One typed local per value parameter, named from the call's logical preorder ordinal and the
        // parameter ordinal so names are unique across the whole component. The argument's constant
        // travels with the name so a pass-through body (Span[title]) keeps its constant and can fold.
        // A content parameter gets no local: content is a subtree, not a value, so there is nothing to bind.
        foreach (var parameter in parameters)
        {
            if (parameter.IsContent)
                continue;

            innerArguments[parameter.Ordinal] = new SubstitutedArgument(
                CreateLocalName(callPreorderOrdinal, parameter.Ordinal),
                Constant: null);
        }

        // Every content channel captures the caller's substitution and cycle stack, because the argument is an
        // expression written here. The bracket content is one of these entries, at the slot ordinal, so the
        // two kinds need no reconciling.
        foreach (var contentArgument in call.ContentArguments)
        {
            // The only mismatch left to guard: a call built against a definition whose shape the registry
            // reports differently. Unreachable from source, and an out-of-range write would be an
            // IndexOutOfRangeException escaping the generator rather than a failed expansion.
            if (contentArgument.ParameterOrdinal >= innerArguments.Length)
                return null;

            innerArguments[contentArgument.ParameterOrdinal] = SubstitutedArgument.ForContent(
                new ContentArgument(contentArgument.Content, substitution, activeMethodStack, currentScope));
        }

        // Emit the locals in source evaluation order (supplied arguments by source position, then implicit
        // defaults) while binding each to its parameter ordinal. Argument initializers reference the
        // caller's scope, so they are substituted with the outer names.
        var ordered = call.Arguments.ToArray();
        Array.Sort(ordered, static (left, right) => left.SourceOrder.CompareTo(right.SourceOrder));

        var locals = ImmutableArray.CreateBuilder<LocalBinding>(ordered.Length);
        foreach (var argument in ordered)
        {
            var parameter = parameters[argument.ParameterOrdinal];
            var initializer = argument.Value.Substitute(substitution);
            locals.Add(new LocalBinding(
                parameter.TypeName,
                innerArguments[argument.ParameterOrdinal].Code,
                initializer));

            // Gated on the parameter's declared type being exactly string, not the argument expression's
            // own type: the local is declared with the parameter's type, so a parameter of a type with an
            // implicit conversion from string and a custom ToString (e.g. MyType m) would diverge between
            // the local's value (converted, then re-stringified through that custom ToString) and the bare
            // literal substituted in the body's hole (never converted at all). Every serializable slot in
            // the surface is string-typed today (Decorations.cs), but that is a fact about the surface, not
            // this file, so the gate is written here rather than assumed from elsewhere.
            //
            // Both spellings of that one type. The name carries the nullable annotation since #235, and a
            // string? parameter is the same type for this gate's purpose: what it guards against is a
            // conversion on the way into the local, and there is none between string and string?.
            if (parameter.TypeName is "string" or "string?")
            {
                innerArguments[argument.ParameterOrdinal] =
                    innerArguments[argument.ParameterOrdinal] with { Constant = initializer.Constant };
            }
        }

        var innerSubstitution = ImmutableArray.Create(innerArguments);

        var calleeScope = environment.CssScopes.GetScopeOrDefault(entry.FilePath);

        var body = ExpandNode(
            definition.Body,
            innerSubstitution,
            ref nextLogicalPreorderOrdinal,
            activeMethodStack.Add(methodKey),
            calleeScope,
            environment);
        if (body is null)
            return null;

        return new ExpansionNode(locals.ToImmutable(), body);
    }

    /// <summary>
    /// Determines whether an inlined body may legally name the referenced non-public member from the
    /// generated component type. Because the value model is symbol-free, access is decided by comparing
    /// normalized type keys against the type that <em>declares</em> the referenced member
    /// (<see cref="ViewPartAccessRequirement.RequiredContainingTypeKey"/>), not the view part's own
    /// containing type: a <see cref="ViewPartAccessRequirementKind.SameContainingType"/> (private)
    /// member is legal only when the generated component *is* the declaring type, while a
    /// <see cref="ViewPartAccessRequirementKind.DerivedContainingType"/> (protected/private-protected)
    /// member is legal when the declaring type appears anywhere in the generated component's inheritance
    /// chain (the component itself or any base type).
    /// </summary>
    private static bool SatisfiesAccess(
        ViewPartAccessRequirement requirement,
        ImmutableArray<string> generatedTypeInheritanceKeys)
    {
        if (generatedTypeInheritanceKeys.IsDefaultOrEmpty)
            return false;

        return requirement.Kind switch
        {
            ViewPartAccessRequirementKind.SameContainingType =>
                string.Equals(
                    requirement.RequiredContainingTypeKey,
                    generatedTypeInheritanceKeys[0],
                    StringComparison.Ordinal),
            ViewPartAccessRequirementKind.DerivedContainingType =>
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
    /// chain so a cycle diagnostic names every view part involved. Display names are resolved from the
    /// registry so the chain reads in source terms rather than mangled method keys.
    /// </summary>
    private static string BuildCycleChain(
        ImmutableArray<string> activeMethodStack,
        string closingDisplayName,
        ViewPartRegistry registry)
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

    private static EquatableArray<ExpressionTemplate> SubstituteClasses(
        EquatableArray<ExpressionTemplate> classes, ImmutableArray<SubstitutedArgument> substitution)
    {
        if (classes.Length == 0)
            return classes;

        var builder = ImmutableArray.CreateBuilder<ExpressionTemplate>(classes.Length);
        foreach (var @class in classes)
            builder.Add(@class.Substitute(substitution));
        return builder.ToImmutable();
    }

    /// <summary>Substitutes an element's or a component's <c>.Attr</c>/<c>.Class</c> attribute list.</summary>
    private static EquatableArray<AttributeTemplate> SubstituteAttributes(
        EquatableArray<AttributeTemplate> attributes, ImmutableArray<SubstitutedArgument> substitution)
    {
        if (attributes.Length == 0)
            return attributes;

        var builder = ImmutableArray.CreateBuilder<AttributeTemplate>(attributes.Length);
        foreach (var attribute in attributes)
            builder.Add(new AttributeTemplate(attribute.Name, attribute.Value.Substitute(substitution)));
        return builder.ToImmutable();
    }

    private static string CreateLocalName(int callPreorderOrdinal, int parameterOrdinal) =>
        $"__bcf_arg_{callPreorderOrdinal}_{parameterOrdinal}";

    private static DiagnosticInfo CreateDiagnostic(ViewPartCallNode call, string reason) =>
        DiagnosticInfo.Create(
            DiagnosticDescriptors.BCF1002,
            call.Location.ToLocation(),
            [DiagnosticDescriptors.ViewPartSubject(call.DisplayName), reason]);
}
