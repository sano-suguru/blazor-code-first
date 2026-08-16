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
            "_cached",
            Note: "Points at the innermost expression that failed to classify, not at the whole Body " +
                "(#77). Anchoring on the call rather than the property is the contract: a file with " +
                "several components and a deep Body is exactly where a coarser location stops helping."),
        new("BCF1004", FixtureKind.GeneratorViaProjectReference, "error", "Bcf1002Bcf1003Bcf1004.cs", "Body"),
        new("BCF1005", FixtureKind.GeneratorViaProjectReference, "error", "Bcf1001Bcf1005.cs", "Body"),
        // SARIF names the Info level "note".
        new("BCF2001", FixtureKind.GeneratorViaProjectReference, "note", "Bcf2001Bcf3030.cs", "Wrap()"),
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
            "ForEach(_items, item => item, new Func<string, View>(Render))"),
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
            "Bcf3017Bcf3018.cs",
            "() => { return _name; }",
            Note: "Anchors the whole getter argument rather than its body. What is wrong is the shape of " +
                "the lambda, not anything inside it, and the fix rewrites the argument."),
        new(
            "BCF3018",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3017Bcf3018.cs",
            "Name",
            Note: "Anchors the getter's body, which is the expression the message quotes and the one " +
                "that has to change."),
        new(
            "BCF3020",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3020.cs",
            "w => w.Label",
            Note: "Anchors the selector, which is what the two derived names come from: the fix is to " +
                "select a parameter the component can write back, or to bind this one one-way with " +
                ".Param."),
        new(
            "BCF3019",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3019.cs",
            "\"click\"",
            Note: "Anchors the event-name argument, the one that has to gain the \"on\" prefix. .On and " +
                ".Bind both report this id; the fixture exercises only .On, since a real build needs one " +
                "reachable route, not both."),
        new(
            "BCF3022",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3022.cs",
            "Render",
            Note: "Anchors the complete contextual-template content argument; the argument shape is what " +
                "must be rewritten."),
        new(
            "BCF3023",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3023.cs",
            "true",
            Note: "Anchors the value argument rather than the name. Either half could change, but the " +
                "author who reached this wanted a class conditionally applied, and that is written on " +
                "the value side as a string expression; renaming the attribute would abandon the " +
                "intent instead of expressing it. The fixture writes the value out, so this anchor is " +
                "the written argument. The spelling that writes none, .Attr(\"class\"), is anchored at " +
                "the decoration name instead, there being nothing else to point at, and is covered " +
                "in-process by HtmlAttributeGeneratorTests."),
        new(
            "BCF3024",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3024.cs",
            "Bind",
            Note: "Anchors the decoration the check runs on, as BCF3010 does. Which of the two that " +
                "makes depends on the order they were written, and the rule holds either way; the " +
                "alternative is a location on a decoration the author has already passed."),
        new(
            "BCF3025",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3025.cs",
            "Slot",
            Note: "Anchors the Slot itself, which is the misplacement half of the rule. The arity half " +
                "(a SlotView part naming its slot zero times or twice) reports at the declaration " +
                "identifier instead, because the count is a property of the declaration and not of any " +
                "one Slot; that half is covered in-process."),
        new(
            "BCF3026",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3026.cs",
            "Clas",
            Note: "The delivery claim is the whole point of this fixture. CS1061 is what would name the " +
                "misspelling, and it is never computed, because the class carries CS0534 and csc stops " +
                "after the declaration stage. Only a real build shows that, so the in-process assertion " +
                "in BracketSurfaceDiagnosticTests cannot stand in for it. The diagnostic's other shape, " +
                "a bound extension method on ElementView that the runtime does not declare, is covered " +
                "in-process: an id gets one fixture occurrence, and that shape raises no C# error for " +
                "the cutoff to suppress, so it has no delivery claim of its own to make."),
        new(
            "BCF3027",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3027.cs",
            "Data",
            Note: "Anchors the shadowed receiver, which is what has to be qualified as Html.Data. The " +
                "delivery claim is again the point: CS1503 is what C# says about the index argument, and " +
                "the class carries CS0534, so csc stops before it binds the body and the author sees " +
                "neither. The id fires on four shapes since #266 — a member, a type, a namespace, and a " +
                "method taking the name — and the member is the one here. What the fixture proves is " +
                "that the report reaches a real build at the right anchor, and the cutoff that keeps " +
                "C#'s own error away is one mechanism shared by all four (a declaration error stops csc " +
                "before any body binds), so a second shape would re-prove it rather than prove anything " +
                "new. It cannot state the other half — that CS0119 does not arrive — because these tests " +
                "assert the BCF report's presence and never a C# error's absence. The member shape is " +
                "kept because #99 made those names routine. The other three are pinned in-process by " +
                "BracketSurfaceDiagnosticTests."),
        new(
            "BCF3028",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3028.cs",
            "(int x) => { }",
            Note: "Of the two shapes this id fires on, the one whose delivery is in question: a TArgs " +
                "outside the where TArgs : System.EventArgs constraint binds to nothing, so CS0311 is the " +
                "C# error that would name it and the class carries CS0534, which stops csc before it binds " +
                "the body. Measured on #155, the author's whole list was CS0534 and a BCF1003 saying the " +
                "expression is not statically analyzable. The other shape, a handler whose type merely " +
                "disagrees with the event's [EventHandler] mapping, binds and raises no C# error at all, " +
                "so it has no delivery claim of its own; it is covered in-process, against both tables " +
                "the mapping is read from. Anchors the handler, because the parameter type written there " +
                "is what has to change."),
        new(
            "BCF3029",
            FixtureKind.AnalyzerViaProjectReference,
            "error",
            "Bcf3029.cs",
            "Div.Class(\"card\")[Span[\"bcf3029\"]]",
            Note: "The second diagnostic in the analyzer fixture, and it belongs there rather than in the " +
                "generator one for the reason BCF3001 does: the shape compiles, so no declaration error " +
                "stops the analyzer driver, and a fixture that carries one would suppress it. There is no " +
                "C# error here at all for the cutoff to hide — that is the whole complaint — so what this " +
                "fixture proves is delivery of an analyzer-reported id other than BCF3001, which nothing " +
                "else did. Anchors the whole chain, because what is wrong is where the expression is " +
                "written rather than anything inside it."),
        new(
            "BCF3030",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf2001Bcf3030.cs",
            "Card(\"bcf3030\")",
            Note: "Anchors the call, not the callee's declaration: the declaration is legal C# that the " +
                "author may never have meant to expand, and the call is where the missing output shows. " +
                "Beside BCF2001 in one fixture because the two split the same classification — the callee " +
                "there builds no design-time syntax, this one does — and a reader comparing them needs " +
                "both in view."),
        new(
            "BCF3031",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3031.cs",
            "\"D4\"",
            Note: "Anchors the format argument, which is what the author drops or rewrites. The value " +
                "type is the other half of the rule, but it is not what went wrong: binding an int is " +
                "legitimate, and only the format written beside it is not."),
        new(
            "BCF3032",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3032.cs",
            "ForEach(_items, item => item, item => Div.Key(item)[item])",
            Note: "Anchors the whole loop, as BCF3003 does from the same walk: the defect is the pair, and "
                + "either half is a legitimate way to write a key on its own."),
        new(
            "BCF3033",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3033.cs",
            "Key",
            AnchorOccurrence.Last,
            "Anchors the second decoration's name, which is the one to delete. Last rather than first "
                + "for the reason BCF3007 gives: on a line carrying both, the duplicate is the later one."),
        new(
            "BCF3034",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3034.cs",
            "RenderMode",
            Note: "Anchors the decoration's name, which is what the author deletes. The attribute on the "
                + "component is the other half of the rule and is not the mistake: fixing a component's "
                + "render mode on its declaration is exactly what that attribute is for."),
        new(
            "BCF3035",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3035.cs",
            "PreventDefault",
            Note: "Anchors the decoration's name, which is what the author moves or deletes. The event it "
                + "was meant for is not necessarily missing — it may be written later in the chain, and "
                + "moving the modifier after it is the fix, so the location cannot name an event."),
        new(
            "BCF3036",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3036.cs",
            "PreventDefault",
            AnchorOccurrence.Last,
            "Anchors the second decoration, which is the one to delete. Last rather than first for the "
                + "reason BCF3033 gives: on a chain carrying both, the duplicate is the later one."),
        new(
            "BCF3038",
            FixtureKind.GeneratorViaProjectReference,
            "error",
            "Bcf3038.cs",
            "StopPropagation",
            Note: "The only shape this diagnostic has, rather than one of several: every arm reads an "
                + "[EventHandler] registration. The one here is a framework registration, which is why "
                + "this fixture project references Microsoft.AspNetCore.Components.Web; since #396 a "
                + "registration the compilation declares itself is read without it, and that path is "
                + "pinned in-process rather than by a second fixture."),
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
    public static ImmutableArray<(string Id, string Reason)> DocumentedWithoutDescriptor { get; } = [];

    /// <summary>
    /// IDs that were implemented and then withdrawn. The number is retired, not freed: a reader who hits
    /// the old error in a preview build and searches for it must not find a different rule wearing the
    /// same name. <c>CONTRIBUTING.md</c>'s prohibition covers IDs listed in
    /// <c>AnalyzerReleases.Shipped.md</c>, which is empty, so this is what enforces the decision.
    /// 付録B records the withdrawal itself.
    /// </summary>
    public static ImmutableArray<(string Id, string Reason)> RetiredIds { get; } =
    [
        ("BCF3021", "One binding per element. Withdrawn in #162; the justification was false and no break stood behind the rule."),
        ("BCF3037",
            "An event modifier after a .Bind. Retired in #370, and for the opposite reason to BCF3021: the "
                + "rule was right for as long as it stood, and it is gone because the surface now does what "
                + "it refused. Nothing it reported is a defect any more, so no rule replaces it."),
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
