using System;
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
    internal static ExpansionResult Expand(
        RenderTemplateNode root,
        ViewPartRegistry registry,
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
    /// The arguments bound to the enclosing view part's parameter holes, code plus constant; empty at the
    /// component's design-time expression root (<c>Body</c> or <c>Chrome</c>), where no holes exist.
    /// </param>
    /// <param name="activeMethodStack">
    /// The method keys currently being expanded along this path, used for cycle detection. Sibling calls
    /// to the same view part are not cycles because each branch receives an independent immutable stack.
    /// </param>
    private static RenderNode? ExpandNode(
        RenderTemplateNode node,
        ImmutableArray<SubstitutedArgument> substitution,
        ref int nextLogicalPreorderOrdinal,
        ImmutableArray<string> activeMethodStack,
        ViewPartRegistry registry,
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
                    var loopVariableName = $"__bcf_item_{ordinal}";
                    var source = forEach.Source.Substitute(substitution);
                    var extended = substitution.Add(new SubstitutedArgument(loopVariableName, Constant: null));
                    var key = forEach.Key?.Substitute(extended);

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
                    // content (a bare If/ForEach, or a view part whose expanded body is region-rooted)
                    // has no keyable frame. BCF3003 is reported by KeyabilityResolver (reachability-
                    // independent, deduped per definition/component); suppress emission here so no SetKey
                    // lands on a region. A declined key attaches nothing, so the gate has nothing to
                    // protect and every root is admitted (#172).
                    if (key is not null && !IsKeyableRoot(content))
                        return null;

                    return new ForEachNode(source, key, content, loopVariableName);
                }

            case ComponentTemplateNode component:
                {
                    var parameters = ImmutableArray.CreateBuilder<ComponentParameter>(component.Parameters.Length);
                    foreach (var parameter in component.Parameters)
                        parameters.Add(new ComponentParameter(parameter.Name, parameter.Value.Substitute(substitution)));

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
                            registry,
                            generatedTypeInheritanceKeys,
                            diagnostics);
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
                        component.TypeName, parameters.ToImmutable(), slots.ToImmutable())
                    {
                        Key = component.Key?.Substitute(substitution),
                    };
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

                    var attributes = ImmutableArray.CreateBuilder<AttributeTemplate>(element.Attributes.Length);
                    foreach (var a in element.Attributes.AsImmutableArray())
                        attributes.Add(new AttributeTemplate(a.Name, a.Value.Substitute(substitution)));

                    var events = ImmutableArray.CreateBuilder<EventTemplate>(element.Events.Length);
                    foreach (var e in element.Events.AsImmutableArray())
                        events.Add(new EventTemplate(e.Name, e.Handler.Substitute(substitution)));

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
                        });
                    }

                    return new ElementNode(
                        element.Tag,
                        SubstituteClasses(element.Classes, substitution),
                        attributes.ToImmutable(),
                        events.ToImmutable(),
                        children.ToImmutable())
                    {
                        Bindings = bindings.ToImmutable(),
                        Key = element.Key?.Substitute(substitution),
                    };
                }

            case RawMarkupTemplateNode raw:
                return new RawMarkupNode(raw.Content.Substitute(substitution));

            case RenderFragmentContentTemplateNode fragmentContent:
                return new RenderFragmentContentNode(fragmentContent.Content.Substitute(substitution));

            case OpaqueViewTemplateNode opaque:
                return new OpaqueViewNode(opaque.Call.Substitute(substitution));

            case TransplantedBlockTemplateNode transplanted:
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
                        registry,
                        generatedTypeInheritanceKeys,
                        diagnostics);
                    if (content is null)
                        return null;

                    // A body that declares through its expression alone reaches here to have its names
                    // minted and carries no statements to write, so the substitution above is the whole of
                    // its job and the wrapper is dropped rather than emitted empty (#343).
                    var statements = transplanted.Statements.Substitute(blockSubstitution);

                    return statements.Segments.Length == 0
                        ? content
                        : new TransplantedBlockNode(statements, content);
                }

            case FragmentTemplateNode fragment:
                {
                    var children = ImmutableArray.CreateBuilder<RenderNode>(fragment.Children.Length);
                    foreach (var child in fragment.Children.AsImmutableArray())
                    {
                        var expanded = ExpandNode(
                            child, substitution, ref nextLogicalPreorderOrdinal,
                            activeMethodStack, registry, generatedTypeInheritanceKeys, diagnostics);
                        if (expanded is null)
                            return null;
                        children.Add(expanded);
                    }
                    return new FragmentNode(children.ToImmutable());
                }

            case ViewPartCallTemplateNode call:
                return ExpandCall(
                    call,
                    ordinal,
                    substitution,
                    ref nextLogicalPreorderOrdinal,
                    activeMethodStack,
                    registry,
                    generatedTypeInheritanceKeys,
                    diagnostics);

            case ContentHoleTemplateNode hole:
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
                        registry,
                        generatedTypeInheritanceKeys,
                        diagnostics);
                }

            default:
                throw new NotSupportedException(
                    $"Unknown RenderTemplateNode type '{node.GetType().Name}'; add an ExpandNode case for it.");
        }
    }

    private static ExpansionNode? ExpandCall(
        ViewPartCallTemplateNode call,
        int callPreorderOrdinal,
        ImmutableArray<SubstitutedArgument> substitution,
        ref int nextLogicalPreorderOrdinal,
        ImmutableArray<string> activeMethodStack,
        ViewPartRegistry registry,
        ImmutableArray<string> generatedTypeInheritanceKeys,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
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
            if (!SatisfiesAccess(requirement, generatedTypeInheritanceKeys))
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
                new ContentArgument(contentArgument.Content, substitution, activeMethodStack));
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

    /// <summary>
    /// Determines whether an expanded content node's root frame is a single element or component (and so
    /// can carry a <c>SetKey</c>). <see cref="ExpansionNode"/> is transparent, its view part body's root
    /// is the real frame, so it is unwrapped. Element/component-rooted nodes (<see cref="ComponentNode"/>,
    /// <see cref="ElementNode"/>) are keyable; region-rooted nodes (<see cref="IfNode"/>,
    /// <see cref="ForEachNode"/>, <see cref="TextContentNode"/>) and wrapper-less nodes
    /// (<see cref="FragmentNode"/>, <see cref="RawMarkupNode"/>, <see cref="RenderFragmentContentNode"/>)
    /// are not.
    /// </summary>
    private static bool IsKeyableRoot(RenderNode node) => node switch
    {
        ExpansionNode expansion => IsKeyableRoot(expansion.Body),
        TransplantedBlockNode transplanted => IsKeyableRoot(transplanted.Content),
        // A root that keys itself is not keyable by the loop: the second SetKey would overwrite the first.
        // BCF3032 refuses that pair at the template layer, so reaching here with one means the two layers
        // have drifted, and this is the layer whose default is to answer no.
        ComponentNode component => component.Key is null,
        ElementNode element => element.Key is null,
        _ => false,
    };

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

    private static string CreateLocalName(int callPreorderOrdinal, int parameterOrdinal) =>
        $"__bcf_arg_{callPreorderOrdinal}_{parameterOrdinal}";

    private static DiagnosticInfo CreateDiagnostic(ViewPartCallTemplateNode call, string reason) =>
        DiagnosticInfo.Create(
            DiagnosticDescriptors.BCF1002,
            call.Location.ToLocation(),
            [DiagnosticDescriptors.ViewPartSubject(call.DisplayName), reason]);
}
