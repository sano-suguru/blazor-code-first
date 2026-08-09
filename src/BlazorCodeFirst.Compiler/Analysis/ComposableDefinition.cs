namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>A single parameter of a composable definition, captured as symbol-free value data.</summary>
/// <remarks>
/// Only what expansion reads. <c>ComposableExpander</c> needs the ordinal to place the substituted
/// argument and the type name to declare the local that holds it; a parameter's source name, its default
/// value, and whether it had one are all resolved at the <em>call</em> site instead
/// (<c>RenderExpressionAnalyzer.CreateInvocationArguments</c> recomputes the default from the callee's
/// own symbol), so carrying them here paid for a second copy that nothing consulted.
/// </remarks>
internal sealed record ComposableParameter(int Ordinal, string TypeName);

/// <summary>Classifies why a referenced member forces an accessibility requirement on the caller.</summary>
internal enum ComposableAccessRequirementKind
{
    /// <summary>The member is only reachable from code in the same containing type.</summary>
    SameContainingType,

    /// <summary>The member is only reachable from code in the containing type or a derived type.</summary>
    DerivedContainingType,
}

/// <summary>
/// Records that the expanded body references a member whose accessibility constrains where the body
/// can legally be inlined. <see cref="RequiredContainingTypeKey"/> is the fully qualified key of the
/// type that <em>declares</em> the referenced member (not the composable's own containing type), so a
/// composable defined in one type that references an inherited protected member is validated against the
/// member's declaring type.
/// </summary>
internal sealed record ComposableAccessRequirement(
    ComposableAccessRequirementKind Kind,
    string RequiredContainingTypeKey,
    string SymbolDisplayName);

/// <summary>
/// The symbol-free, value-equal model of a valid composable definition: the parameters it accepts, the
/// accessibility it requires at expansion sites, and its normalized body.
/// </summary>
/// <remarks>
/// Identity lives on <see cref="ComposableDefinitionEntry"/>, not here. A definition is only ever reached
/// through its entry, which is what the registry keys and what every reader (the expander's cycle chain,
/// <c>KeyabilityResolver</c>) takes the method key and display name from, so a second copy on the
/// definition could only ever disagree with the one in use. Note also that the containing-type key it used
/// to carry was not the one expansion validates against: that is
/// <see cref="ComposableAccessRequirement.RequiredContainingTypeKey"/>, the <em>declaring</em> type of each
/// referenced member.
/// </remarks>
internal sealed record ComposableDefinition(
    EquatableArray<ComposableParameter> Parameters,
    EquatableArray<ComposableAccessRequirement> AccessRequirements,
    RenderTemplateNode Body);

/// <summary>
/// A registry slot for one source-declared composable. Invalid declarations remain present with
/// <see cref="Definition"/> set to <see langword="null"/> and <see cref="DeclarationDiagnosticReported"/>
/// set to <see langword="true"/> so expansion can distinguish an already-diagnosed source declaration
/// from a metadata-only method that must report BCF1002 at the call site.
/// </summary>
internal sealed record ComposableDefinitionEntry(
    string MethodKey,
    string DisplayName,
    ComposableDefinition? Definition,
    bool DeclarationDiagnosticReported);
