using System.Collections.Immutable;

namespace BlazorCompose.DiagnosticTests;

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
        new("BC1001", FixtureKind.GeneratorViaProjectReference, "error", "Bc1001Bc1005.cs", "Bc1001NonPartial"),
        new("BC1002", FixtureKind.GeneratorViaProjectReference, "error", "Bc1002Bc1003Bc1004.cs", "Helper"),
        new(
            "BC1003",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bc1002Bc1003Bc1004.cs",
            "Make()",
            Note: "Points at the innermost expression that failed to classify, not at the whole Body " +
                "(#77). Anchoring on the call rather than the property is the contract: a file with " +
                "several components and a deep Body is exactly where a coarser location stops helping."),
        new("BC1004", FixtureKind.GeneratorViaProjectReference, "error", "Bc1002Bc1003Bc1004.cs", "Body"),
        new("BC1005", FixtureKind.GeneratorViaProjectReference, "error", "Bc1001Bc1005.cs", "Body"),
        new("BC3001", FixtureKind.AnalyzerViaProjectReference, "error", "Mutating.cs", "_count++"),
        new("BC3002", FixtureKind.GeneratorViaProjectReference, "warning", "Bc3002Bc3003Bc3004.cs", "item => 0"),
        new(
            "BC3003",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bc3002Bc3003Bc3004.cs",
            "ForEach(_items, item => item, item => Fragment(Div[item]))"),
        new(
            "BC3004",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bc3002Bc3003Bc3004.cs",
            "ForEach(_items, item => item, Render)"),
        new("BC3005", FixtureKind.GeneratorViaProjectReference, "error", "Bc3005ToBc3008.cs", "w => w.Label!.ToUpperInvariant()"),
        new("BC3006", FixtureKind.GeneratorViaProjectReference, "error", "Bc3005ToBc3008.cs", "w => w.NotAParameter"),
        // Two identical selectors on the line; the duplicate is the second, and that is the one to blame.
        new("BC3007", FixtureKind.GeneratorViaProjectReference, "error", "Bc3005ToBc3008.cs", "w => w.Label", AnchorOccurrence.Last),
        new("BC3008", FixtureKind.GeneratorViaProjectReference, "error", "Bc3005ToBc3008.cs", "Class"),
        new("BC3009", FixtureKind.GeneratorViaProjectReference, "error", "Bc3009ToBc3011.cs", "\"\""),
        // Likewise: the second .Id is the duplicate.
        new("BC3010", FixtureKind.GeneratorViaProjectReference, "error", "Bc3009ToBc3011.cs", "Id", AnchorOccurrence.Last),
        new("BC3011", FixtureKind.GeneratorViaProjectReference, "error", "Bc3009ToBc3011.cs", "_name"),
        new("BC3012", FixtureKind.GeneratorViaProjectReference, "error", "Bc3012ToBc3015.cs", "MissingWidget"),
        new(
            "BC3013",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bc3012ToBc3015.cs",
            "Component<ChildlessWidget>()[Span[\"bc3013\"]]"),
        new("BC3014", FixtureKind.GeneratorViaProjectReference, "error", "Bc3012ToBc3015.cs", "Span[\"bc3014\"]"),
        new("BC3015", FixtureKind.GeneratorViaProjectReference, "error", "Bc3012ToBc3015.cs", "MissingProbe"),
    ];

    /// <summary>
    /// Descriptors deliberately not exercised end-to-end, each with the reason.  Empty on purpose:
    /// every declared diagnostic is currently proven to reach a real build, and an entry added here
    /// should be an argued exception, not a way to make the coverage guard quiet.
    /// </summary>
    public static ImmutableArray<(string Id, string Reason)> Excluded { get; } = [];

    /// <summary>
    /// IDs that <c>ARCHITECTURE.md</c> 付録A documents on purpose while no <c>DiagnosticDescriptor</c>
    /// declares them yet, each with the reason.  This is the opposite axis from <see cref="Excluded"/>:
    /// those are declared diagnostics not proven against a real build, these are specified diagnostics
    /// not yet implemented.  Without the list, a deliberate row is indistinguishable from a row that
    /// outlived its descriptor.
    /// </summary>
    public static ImmutableArray<(string Id, string Reason)> DocumentedWithoutDescriptor { get; } =
    [
        ("BC2001",
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
