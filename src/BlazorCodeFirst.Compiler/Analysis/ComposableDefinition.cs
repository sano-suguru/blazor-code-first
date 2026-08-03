namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>Distinguishes value parameters from view-subtree parameters of a composable method.</summary>
internal enum ComposableParameterKind
{
    /// <summary>An ordinary value parameter substituted into the expanded body.</summary>
    Value,

    /// <summary>A parameter that supplies a nested view subtree (not yet supported for expansion).</summary>
    ViewSubtree,
}

/// <summary>A single parameter of a composable definition, captured as symbol-free value data.</summary>
internal sealed record ComposableParameter(
    int Ordinal,
    string Name,
    string TypeName,
    ComposableParameterKind Kind,
    bool HasExplicitDefaultValue,
    ExpressionTemplate? DefaultValue);

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
/// The symbol-free, value-equal model of a valid composable definition: its stable identity, the
/// parameters it accepts, the accessibility it requires at expansion sites, and its normalized body.
/// </summary>
internal sealed record ComposableDefinition(
    string MethodKey,
    string DisplayName,
    string ContainingTypeKey,
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
