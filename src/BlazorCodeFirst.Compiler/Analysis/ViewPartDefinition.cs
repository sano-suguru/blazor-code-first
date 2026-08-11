namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>A single parameter of a view part definition, captured as symbol-free value data.</summary>
/// <remarks>
/// Only what expansion reads. <c>ViewPartExpander</c> needs the ordinal to place the substituted
/// argument and the type name to declare the local that holds it; a parameter's source name, its default
/// value, and whether it had one are all resolved at the <em>call</em> site instead
/// (<c>RenderExpressionAnalyzer.CreateInvocationArguments</c> recomputes the default from the callee's
/// own symbol), so carrying them here paid for a second copy that nothing consulted.
/// </remarks>
/// <param name="IsContent">
/// Whether this parameter is a <c>View</c>-typed content slot (#34) rather than a value. Expansion declares
/// no local for one — content is a node subtree and there is nothing to bind — so the flag is carried here
/// rather than inferred from <paramref name="TypeName"/>, which would make the decision a string comparison
/// against a display format.
/// </param>
internal sealed record ViewPartParameter(int Ordinal, string TypeName, bool IsContent = false);

/// <summary>Classifies why a referenced member forces an accessibility requirement on the caller.</summary>
internal enum ViewPartAccessRequirementKind
{
    /// <summary>The member is only reachable from code in the same containing type.</summary>
    SameContainingType,

    /// <summary>The member is only reachable from code in the containing type or a derived type.</summary>
    DerivedContainingType,
}

/// <summary>
/// Records that the expanded body references a member whose accessibility constrains where the body
/// can legally be inlined. <see cref="RequiredContainingTypeKey"/> is the fully qualified key of the
/// type that <em>declares</em> the referenced member (not the view part's own containing type), so a
/// view part defined in one type that references an inherited protected member is validated against the
/// member's declaring type.
/// </summary>
internal sealed record ViewPartAccessRequirement(
    ViewPartAccessRequirementKind Kind,
    string RequiredContainingTypeKey,
    string SymbolDisplayName);

/// <summary>
/// The symbol-free, value-equal model of a valid view part definition: the parameters it accepts, the
/// accessibility it requires at expansion sites, and its normalized body.
/// </summary>
/// <remarks>
/// Identity lives on <see cref="ViewPartDefinitionEntry"/>, not here. A definition is only ever reached
/// through its entry, which is what the registry keys and what every reader (the expander's cycle chain,
/// <c>KeyabilityResolver</c>) takes the method key and display name from, so a second copy on the
/// definition could only ever disagree with the one in use. Note also that the containing-type key it used
/// to carry was not the one expansion validates against: that is
/// <see cref="ViewPartAccessRequirement.RequiredContainingTypeKey"/>, the <em>declaring</em> type of each
/// referenced member.
/// </remarks>
/// <param name="HasSlot">
/// Whether this definition names <c>Html.Slot</c>, which is exactly whether it returns <c>SlotView</c>
/// (#176). The ordinal that slot is bound at is <see cref="SlotOrdinal"/>, derived rather than stored: it is
/// always the ordinal after the last parameter, so storing it would put a second copy of that invariant into
/// a value-equal model that the incremental generator caches on.
/// </param>
internal sealed record ViewPartDefinition(
    EquatableArray<ViewPartParameter> Parameters,
    EquatableArray<ViewPartAccessRequirement> AccessRequirements,
    RenderTemplateNode Body,
    bool HasSlot = false)
{
    /// <summary>
    /// The substitution ordinal the slot is bound at, or <c>-1</c> when there is none. The ordinal space reads
    /// parameters, then the slot, then the scoped render variables
    /// <c>ViewPartBodyContext.PushRenderVariable</c> appends.
    /// </summary>
    public int SlotOrdinal => HasSlot ? Parameters.Length : -1;
}

/// <summary>
/// A registry slot for one source-declared view part. Invalid declarations remain present with
/// <see cref="Definition"/> set to <see langword="null"/> and <see cref="DeclarationDiagnosticReported"/>
/// set to <see langword="true"/> so expansion can distinguish an already-diagnosed source declaration
/// from a metadata-only method that must report BCF1002 at the call site.
/// </summary>
internal sealed record ViewPartDefinitionEntry(
    string MethodKey,
    string DisplayName,
    ViewPartDefinition? Definition,
    bool DeclarationDiagnosticReported);
