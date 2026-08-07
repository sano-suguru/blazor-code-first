using System.Collections.Immutable;

namespace BlazorCodeFirst.DiagnosticTests;

/// <summary>Which fixture project a diagnostic is exercised in.</summary>
public enum FixtureKind
{
    AnalyzerViaProjectReference,
    GeneratorViaProjectReference,
    AnalyzerViaPackage,
    GeneratorViaPackage,
}

/// <summary>Which occurrence of the anchor text on its line the diagnostic must point at.</summary>
public enum AnchorOccurrence
{
    First,
    Last,
}

/// <summary>
/// What one diagnostic must look like when a real build reports it: where it comes from, how severe
/// it is, and the exact source text it squiggles.
/// </summary>
/// <param name="Anchor">
/// The source text the reported span must cover, or <see langword="null"/> for a diagnostic that is
/// deliberately pinned as location-less.
/// </param>
public sealed record DiagnosticExpectation(
    string Id,
    FixtureKind Fixture,
    string Severity,
    string FileName,
    string? Anchor,
    AnchorOccurrence Occurrence = AnchorOccurrence.First,
    string? Note = null);

/// <summary>
/// The single source of truth for both the delivery tests and the coverage guard: every declared
/// descriptor must appear here or in <see cref="Excluded"/>, so a diagnostic cannot be added without
/// someone deciding whether it survives a real build.
/// </summary>
public static class DiagnosticExpectations
{
    public static ImmutableArray<DiagnosticExpectation> All { get; } =
    [
        new("BCF1001", FixtureKind.GeneratorViaProjectReference, "error", "Bcf1001Bcf1005.cs", "Bcf1001NonPartial"),
        new("BCF1002", FixtureKind.GeneratorViaProjectReference, "error", "Bcf1002Bcf1003Bcf1004.cs", "Helper"),
        new(
            "BCF1003",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf1002Bcf1003Bcf1004.cs",
            "Make()",
            Note: "Points at the innermost expression that failed to classify, not at the whole Body " +
                "(#77). Anchoring on the call rather than the property is the contract: a file with " +
                "several components and a deep Body is exactly where a coarser location stops helping."),
        new("BCF1004", FixtureKind.GeneratorViaProjectReference, "error", "Bcf1002Bcf1003Bcf1004.cs", "Body"),
        new("BCF1005", FixtureKind.GeneratorViaProjectReference, "error", "Bcf1001Bcf1005.cs", "Body"),
        new("BCF3001", FixtureKind.AnalyzerViaProjectReference, "error", "Mutating.cs", "_count++"),
        new("BCF3002", FixtureKind.GeneratorViaProjectReference, "warning", "Bcf3002Bcf3003Bcf3004.cs", "item => 0"),
        new(
            "BCF3003",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3002Bcf3003Bcf3004.cs",
            "ForEach(_items, item => item, item => Fragment(Div[item]))"),
        new(
            "BCF3004",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3002Bcf3003Bcf3004.cs",
            "ForEach(_items, item => item, Render)"),
        new("BCF3005", FixtureKind.GeneratorViaProjectReference, "error", "Bcf3005ToBcf3008.cs", "w => w.Label!.ToUpperInvariant()"),
        new("BCF3006", FixtureKind.GeneratorViaProjectReference, "error", "Bcf3005ToBcf3008.cs", "w => w.NotAParameter"),
        // Two identical selectors on the line; the duplicate is the second, and that is the one to blame.
        new("BCF3007", FixtureKind.GeneratorViaProjectReference, "error", "Bcf3005ToBcf3008.cs", "w => w.Label", AnchorOccurrence.Last),
        new("BCF3008", FixtureKind.GeneratorViaProjectReference, "error", "Bcf3005ToBcf3008.cs", "Class"),
        new("BCF3009", FixtureKind.GeneratorViaProjectReference, "error", "Bcf3009ToBcf3011.cs", "\"\""),
        // Likewise: the second .Id is the duplicate.
        new("BCF3010", FixtureKind.GeneratorViaProjectReference, "error", "Bcf3009ToBcf3011.cs", "Id", AnchorOccurrence.Last),
        new("BCF3011", FixtureKind.GeneratorViaProjectReference, "error", "Bcf3009ToBcf3011.cs", "_name"),
        new("BCF3012", FixtureKind.GeneratorViaProjectReference, "error", "Bcf3012ToBcf3015.cs", "MissingWidget"),
        new(
            "BCF3013",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3012ToBcf3015.cs",
            "Component<ChildlessWidget>()[Span[\"bcf3013\"]]"),
        new("BCF3014", FixtureKind.GeneratorViaProjectReference, "error", "Bcf3012ToBcf3015.cs", "Span[\"bcf3014\"]"),
        new("BCF3015", FixtureKind.GeneratorViaProjectReference, "error", "Bcf3012ToBcf3015.cs", "MissingProbe"),
        new(
            "BCF3016",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3016.cs",
            "Img.Src(\"/bcf3016.png\")[\"bcf3016\"]",
            Note: "Anchors the whole element access, as BCF3013 does, not the argument list. The " +
                "diagnostic is about a void tag and a child list written together and either half can be " +
                "the one to change, so the report does not presuppose which. The decoration is inside the " +
                "anchor because it is part of that access; only Element(\"img\")[\"x\"], asserted " +
                "in-process, has no receiver chain."),
        new(
            "BCF3017",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3017Bcf3018Bcf3021.cs",
            "() => { return _name; }",
            Note: "Anchors the whole getter argument rather than its body. What is wrong is the shape of " +
                "the lambda, not anything inside it, and the fix rewrites the argument."),
        new(
            "BCF3018",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3017Bcf3018Bcf3021.cs",
            "Name",
            Note: "Anchors the getter's body, which is the expression the message quotes and the one " +
                "that has to change."),
        // Two .Bind on the line; the second is the one that would take the resynchronization away.
        new(
            "BCF3021",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3017Bcf3018Bcf3021.cs",
            "Bind",
            AnchorOccurrence.Last),
    ];

    /// <summary>
    /// Descriptors deliberately not exercised end-to-end, each with the reason. Empty on purpose:
    /// every declared diagnostic is currently proven to reach a real build, and an entry added here
    /// should be an argued exception, not a way to make the coverage guard quiet.
    /// </summary>
    public static ImmutableArray<(string Id, string Reason)> Excluded { get; } = [];

    /// <summary>
    /// IDs that <c>ARCHITECTURE.md</c> 付録A documents on purpose while no <c>DiagnosticDescriptor</c>
    /// declares them yet, each with the reason. This is the opposite axis from <see cref="Excluded"/>:
    /// those are declared diagnostics not proven against a real build, these are specified diagnostics
    /// not yet implemented. Without the list, a deliberate row is indistinguishable from a row that
    /// outlived its descriptor.
    /// </summary>
    public static ImmutableArray<(string Id, string Reason)> DocumentedWithoutDescriptor { get; } =
    [
        ("BCF2001",
            "The 付録A row is intentional: the Opaque path is specified but unimplemented, so the " +
            "descriptor lands with it (#57)."),
    ];

    public static TheoryData<string> Ids
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var expectation in All)
                data.Add(expectation.Id);

            return data;
        }
    }

    public static DiagnosticExpectation For(string id) =>
        All.Single(expectation => string.Equals(expectation.Id, id, StringComparison.Ordinal));
}
