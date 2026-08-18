using System;
using System.Collections.Generic;
using System.Linq;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class KnownSymbolsSyncTests
{
    // Structural Html members that are NOT curated element tags.
    private static readonly string[] StructuralHtml = ["Element", "If", "ForEach", "Component", "Fragment", "Raw"];

    /// <summary>
    /// Structural <c>Html</c> members that carry no <see cref="SurfaceMethodKind"/> either, because they are
    /// not methods. <c>Slot</c> is a property and a hole rather than a construct: the compiler recognizes it
    /// by symbol identity through <see cref="KnownSymbols.SlotProperty"/> and looks up the ordinal the
    /// enclosing <c>[ViewPart]</c> bound it at (#176), so there is no classification row to check it
    /// against. Kept separate from <see cref="StructuralHtml"/> so
    /// <c>StructuralHtmlMembers_AreClassified</c> is not asked for a row that cannot exist.
    /// </summary>
    private static readonly string[] StructuralHtmlProperties = ["Slot"];

    /// <summary>The number of curated element helpers <c>KnownSymbols</c> owns the table for.</summary>
    /// <remarks>
    /// Not the guard against a missing entry, despite reading like one: this constant is edited in the
    /// same act that transcribes the table, so an omission takes the count down with it and stays green.
    /// <see cref="ExpectedCuratedNames"/> is what catches that. This still earns its place for the reason
    /// the original comment gives, making a <em>deletion</em> from the runtime a failure.
    /// </remarks>
    internal const int CuratedTagCount = 100;

    /// <summary>
    /// Every curated element helper name: the conforming elements of the HTML Living Standard's
    /// "Index — Elements", minus the six exclusion groups recorded in <c>DESIGN.md</c> §4.1.
    /// </summary>
    /// <remarks>
    /// This list and <c>KnownSymbols.CuratedTags</c> are written by the same hand in the same sitting, so
    /// it does not prove the transcription complete. What it buys is a change of question: "are these 100
    /// dictionary rows right", which nobody can check by reading, becomes "is this sorted list the
    /// conforming element index minus the six groups", which is one comparison against a published
    /// document. That comparison is a review step, not an assumption. Ordinal-sorted so a failure diffs
    /// as a single missing or extra line.
    /// </remarks>
    private static readonly string[] ExpectedCuratedNames =
    [
        "A", "Abbr", "Address", "Area", "Article", "Aside", "Audio",
        "B", "Bdi", "Bdo", "Blockquote", "Br", "Button",
        "Canvas", "Caption", "Cite", "Code", "Col", "Colgroup",
        "Data", "Datalist", "Dd", "Del", "Details", "Dfn", "Dialog", "Div", "Dl", "Dt",
        "Em", "Embed",
        "Fieldset", "Figcaption", "Figure", "Footer", "Form",
        "H1", "H2", "H3", "H4", "H5", "H6", "Header", "Hgroup", "Hr",
        "I", "Iframe", "Img", "Input", "Ins",
        "Kbd",
        "Label", "Legend", "Li",
        "Main", "Map", "Mark", "Menu", "Meter",
        "Nav",
        "Ol", "Optgroup", "Option", "Output",
        "P", "Picture", "Pre", "Progress",
        "Q",
        "Rp", "Rt", "Ruby",
        "S", "Samp", "Search", "Section", "Select", "Selectedcontent", "Small", "Source", "Span",
        "Strong", "Sub", "Summary", "Sup",
        "Table", "Tbody", "Td", "Textarea", "Tfoot", "Th", "Thead", "Time", "Tr", "Track",
        "U", "Ul",
        "Var", "Video",
        "Wbr",
    ];

    /// <summary>The tags no curated helper may name, and why. Guards exclusion groups 1-4 and the two
    /// group-5 roots.</summary>
    /// <remarks>
    /// Keyed on the tag rather than the helper name because <c>Decorations.Title(string)</c> exists and a
    /// name-based check could not tell the element from the attribute shortcut. Group 5 covers whole
    /// element <em>indexes</em>, which a value list cannot express: this does not reject <c>circle</c> or
    /// <c>path</c>, and it cannot distinguish HTML <c>title</c> from SVG <c>title</c>. Group 6, the
    /// obsolete and non-conforming elements, is unenforceable here for the same reason: a row like
    /// <c>["marquee"] = "..."</c> would pass this guard undetected, and only <see cref="ExpectedCuratedNames"/>
    /// would catch it.
    /// </remarks>
    private static readonly Dictionary<string, string> ExcludedTags = new(System.StringComparer.Ordinal)
    {
        ["html"] = "group 1: document skeleton, silently inert in a Body",
        ["head"] = "group 1: document skeleton, silently inert in a Body",
        ["body"] = "group 1: resolves to LayoutComponentBase.Body inside a layout and compiles",
        ["title"] = "group 1: head-only; PageTitle is the Blazor route",
        ["base"] = "group 1: head-only; set once in index.html",
        ["meta"] = "group 1: head-only; reachable through Component<HeadContent>()",
        ["link"] = "group 1: head-only; reachable through Component<HeadContent>()",
        ["script"] = "group 2: raw text content model, not markup children",
        ["style"] = "group 2: raw text content model, not markup children",
        ["noscript"] = "group 2: raw text content model, not markup children",
        ["template"] = "group 3: appended children never reach content, only the parser fills it",
        ["slot"] = "group 3: Blazor creates no shadow root for a slot to fill",
        ["object"] = "group 4: CS0229 against the object keyword",
        ["svg"] = "group 5: foreign vocabulary; breaks the first-letter naming rule",
        ["math"] = "group 5: foreign vocabulary; Math collides with System.Math",
    };

    /// <summary>
    /// Every void element of the HTML Living Standard, ordinal-sorted, transcribed from "Index — Elements"
    /// independently of <c>KnownSymbols.VoidTagSet</c>.
    /// </summary>
    /// <remarks>
    /// The same change of question <see cref="ExpectedCuratedNames"/> buys, for the same reason: "are these
    /// thirteen hash-set entries right" cannot be checked by reading, while "is this sorted list the
    /// standard's void elements" is one comparison against a published document, and Appendix A's BCF3016 row
    /// writes the list out a third time for a reader who never opens this file. Unlike the curated set this
    /// one is closed in practice, the standard has not added a void element in over a decade, so a failure
    /// here is a transcription slip rather than news from the standard.
    /// </remarks>
    private static readonly string[] ExpectedVoidTags =
    [
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    ];

    [Fact]
    public void VoidTags_AreExactlyTheHtmlStandardsVoidElements()
    {
        var actual = KnownSymbols.VoidTags
            .OrderBy(static tag => tag, System.StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedVoidTags, actual);
    }

    /// <summary>
    /// Every void tag is a tag this surface can actually produce: a curated helper, or one of the three
    /// group-1 exclusions reachable through <c>Element</c>.
    /// </summary>
    /// <remarks>
    /// This is the direction <see cref="ExpectedVoidTags"/> cannot cover, because both lists are written by
    /// the same hand: it holds the void set against the two tables the rest of this file already fixes
    /// against the standard, so a tag misspelled identically in both transcriptions ("imge") still fails,
    /// being neither curated nor excluded. It is also what notices a void tag going missing from the
    /// curated table without the count in
    /// <see cref="ElementTags_CoverEveryCuratedHtmlHelper_AndNothingStructural"/> moving, since a rename
    /// keeps the count.
    /// </remarks>
    [Fact]
    public void EveryVoidTag_IsEitherACuratedHelperOrAGroup1Exclusion()
    {
        var (symbols, _) = ResolveHtml();
        var curated = symbols.ElementTags.Values.ToHashSet(System.StringComparer.Ordinal);

        foreach (var tag in KnownSymbols.VoidTags)
        {
            Assert.True(
                curated.Contains(tag) || ExcludedTags.ContainsKey(tag),
                $"'{tag}' is registered as a void element but is neither a curated helper nor an " +
                $"excluded tag, so BCF3016 can never fire for it. Fix the spelling, or record why the " +
                $"tag exists in neither table.");
        }
    }

    /// <summary>
    /// Both event-modifier decorations are classified, and both overloads of each land on one row.
    /// </summary>
    /// <remarks>
    /// The pair is registered by name in the decoration switch, as <c>.Key</c> and <c>.Ref</c> are, so a
    /// rename on either side leaves the decoration arm reaching a classification nothing produces and the
    /// modifier silently ignored. The overload count is asserted alongside because the valueless and the
    /// <see langword="bool"/> spelling are two symbols standing for one behaviour: losing one to a
    /// mis-typed switch case would leave half the surface unclassified with the other half green (#368).
    /// </remarks>
    [Fact]
    public void EventModifierDecorations_AreClassified()
    {
        var (symbols, _) = ResolveHtml();

        // Duplicates kept rather than distinct: the row and the overload count are one fact, and asserting
        // the multiplicity is what would notice a mis-typed switch case leaving one spelling unclassified.
        List<SurfaceMethodKind> RowsFor(string name) =>
            [.. symbols.SurfaceMethods.Where(entry => entry.Key.Name == name).Select(entry => entry.Value)];

        Assert.Equal(
            [SurfaceMethodKind.PreventDefault, SurfaceMethodKind.PreventDefault],
            RowsFor("PreventDefault"));
        Assert.Equal(
            [SurfaceMethodKind.StopPropagation, SurfaceMethodKind.StopPropagation],
            RowsFor("StopPropagation"));
    }

    /// <summary>
    /// The three void tags with no curated helper are exactly <c>base</c>, <c>link</c> and <c>meta</c>.
    /// </summary>
    /// <remarks>
    /// Pinned as a set rather than a count so the pair of tables cannot drift into agreement by swapping a
    /// member: this is the statement <c>DESIGN.md</c> §4.1 makes, that the group-1 exclusions are void too
    /// and are checked when reached through <c>Element</c>, and it is the one sentence in that paragraph a
    /// reader cannot verify from either table alone.
    /// </remarks>
    [Fact]
    public void VoidTagsWithoutACuratedHelper_AreTheThreeHeadOnlyElements()
    {
        var (symbols, _) = ResolveHtml();
        var curated = symbols.ElementTags.Values.ToHashSet(System.StringComparer.Ordinal);

        var uncurated = KnownSymbols.VoidTags
            .Where(tag => !curated.Contains(tag))
            .OrderBy(static tag => tag, System.StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["base", "link", "meta"], uncurated);
    }

    [Fact]
    public void ElementTags_AreExactlyTheCuratedSet()
    {
        var (symbols, _) = ResolveHtml();

        var actual = symbols.ElementTags.Keys
            .Select(static key => key.Name)
            .OrderBy(static name => name, System.StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedCuratedNames, actual);
    }

    [Fact]
    public void EveryCuratedTag_IsItsHelperNameWithALowercasedFirstLetter()
    {
        var (symbols, _) = ResolveHtml();

        // The rule DESIGN.md §4.1 states, asserted rather than trusted: it is what makes a one-sided
        // transcription slip (["Textarea"] = "textrea") a failure, which ExpectedCuratedNames cannot see
        // because it holds helper names and not tags.
        foreach (var entry in symbols.ElementTags)
        {
            var name = entry.Key.Name;
            Assert.Equal(char.ToLowerInvariant(name[0]) + name[1..], entry.Value);
        }
    }

    [Fact]
    public void CuratedTags_ExcludeDocumentScriptingForeignAndAmbiguousElements()
    {
        var (symbols, _) = ResolveHtml();

        foreach (var tag in symbols.ElementTags.Values)
        {
            Assert.False(
                ExcludedTags.TryGetValue(tag, out var reason),
                $"'{tag}' is a curated element helper but is excluded by DESIGN.md §4.1 ({reason}). " +
                $"Element(\"{tag}\") remains available for it.");
        }
    }

    [Fact]
    public void ElementTags_CoverEveryCuratedHtmlHelper_AndNothingStructural()
    {
        var (symbols, html) = ResolveHtml();
        var tagged = symbols.ElementTags.Keys
            .Select(static key => key.Name).ToHashSet(System.StringComparer.Ordinal);

        // Both member kinds are enumerated on purpose, and the split is not accidental: the structural
        // members (Element, If, ForEach, Component, Fragment, Raw) are methods, while every curated tag is
        // a property returning ElementView. Filtering to IMethodSymbol would make `tagged` empty, leave
        // every remaining ordinary Html method looking structural, and so never run the Assert.Contains
        // arm, the guard would go vacuous while staying green.
        foreach (var member in html.GetMembers())
        {
            var name = member switch
            {
                IMethodSymbol { MethodKind: MethodKind.Ordinary } method => method.Name,
                IPropertySymbol { IsIndexer: false } property => property.Name,
                _ => null,
            };

            if (name is null)
                continue;

            bool structural = System.Array.IndexOf(StructuralHtml, name) >= 0
                || System.Array.IndexOf(StructuralHtmlProperties, name) >= 0;
            if (structural)
                Assert.DoesNotContain(name, tagged);
            else
                Assert.Contains(name, tagged); // every non-structural Html helper is a curated tag
        }

        // The count is what makes deleting a curated helper from the runtime a failure. The loop above only
        // checks the helpers that exist, so on its own it would pass a surface that had lost one.
        Assert.Equal(CuratedTagCount, symbols.ElementTags.Count);
    }

    [Fact]
    public void DecorationMaps_CoverAttributeAndEventShortcuts()
    {
        var (symbols, _) = ResolveHtml();
        var attrNames = symbols.AttributeShortcuts.Values.ToHashSet(System.StringComparer.Ordinal);
        foreach (var expected in new[] { "href", "src", "alt", "id", "type", "title", "role" })
            Assert.Contains(expected, attrNames);
        Assert.Contains("onclick", symbols.EventShortcuts.Values);
        Assert.NotEmpty(SurfaceMethodsOfKind(symbols, SurfaceMethodKind.Attr));
        Assert.NotEmpty(SurfaceMethodsOfKind(symbols, SurfaceMethodKind.On));
    }

    /// <summary>
    /// Every registered shortcut is (receiver, one value). The values check above asserts over
    /// <c>AttributeShortcuts.Values</c>/<c>EventShortcuts.Values</c> alone, so an extra overload mapping
    /// to an already-present name would not move it; this checks the key side, an
    /// <see cref="IMethodSymbol"/> per registration, so a future overload of a shortcut name with a
    /// different arity would fail here instead of being silently registered.
    /// </summary>
    [Fact]
    public void DecorationShortcuts_AreAllTwoParameterOverloads()
    {
        var (symbols, _) = ResolveHtml();
        foreach (var method in symbols.AttributeShortcuts.Keys.Concat(symbols.EventShortcuts.Keys))
            Assert.Equal(2, Assert.IsAssignableFrom<IMethodSymbol>(method).Parameters.Length);
    }

    /// <summary>
    /// The methods <c>KnownSymbols</c> classified as <paramref name="kind"/>, which is the only place the
    /// compiler now records what a surface method is.
    /// </summary>
    private static List<ISymbol> SurfaceMethodsOfKind(KnownSymbols symbols, SurfaceMethodKind kind) =>
        [.. symbols.SurfaceMethods.Where(entry => entry.Value == kind).Select(entry => entry.Key)];

    /// <summary>
    /// Every decoration <c>KnownSymbols</c> captured is an extension on <c>ElementView</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sets are built by matching the method <em>name</em>, but what makes a method an element
    /// decoration is its <em>receiver</em>. <c>KnownSymbols</c>'s constructor already filters on that
    /// receiver when <c>ElementViewType</c> resolves, so neither half of the failure mode on its own
    /// makes this test fail today: adding a future <c>Attr(this ComponentView&lt;T&gt;, string, string)</c>
    /// to <c>Decorations</c> is excluded by that very filter before it reaches <c>captured</c>, and removing
    /// the filter with no such overload declared yet has nothing new to admit, since every current
    /// <c>Decorations</c> member already takes <c>this ElementView</c>. It fails only when both happen
    /// together, the filter is gone and a decoration on another receiver exists, at which point
    /// <c>UnresolvedValueTypeScanner.IsDecorationMethod</c>, pure set membership, would treat it as an
    /// element decoration with nothing else to notice. <c>ClassMethod</c> showed the same defect more
    /// loudly before the filter existed, being a single slot that took whichever two-parameter overload
    /// <c>GetMembers</c> returned last.
    /// </para>
    /// <para>
    /// Asserted over every captured symbol rather than over a count, because the failure is one specific
    /// wrong entry rather than a missing one.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCapturedDecoration_ExtendsElementView()
    {
        var (symbols, html) = ResolveHtml();
        var elementView = ResolveElementView(html);

        var captured = symbols.SurfaceMethods
            .Where(entry => entry.Value is SurfaceMethodKind.Class
                or SurfaceMethodKind.AttributeShortcut
                or SurfaceMethodKind.EventShortcut
                or SurfaceMethodKind.Attr
                or SurfaceMethodKind.On
                or SurfaceMethodKind.Bind)
            .Select(entry => entry.Key)
            .ToList();

        Assert.NotEmpty(captured);
        foreach (var symbol in captured)
        {
            var method = Assert.IsAssignableFrom<IMethodSymbol>(symbol);
            Assert.True(
                method.Parameters.Length > 0
                    && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, elementView),
                $"'{method.Name}' was captured as an element decoration but its receiver is " +
                $"'{(method.Parameters.Length > 0 ? method.Parameters[0].Type.ToDisplayString() : "none")}'.");
        }
    }

    /// <summary>
    /// Every decoration <c>Decorations</c> declares is a name <c>DeclaresDecorationNamed</c> answers to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>RejectedDecorationScanner</c> decides whether a failed call is a misplaced decoration partly by
    /// that name test, and the set behind it is a function of what the constructor's member switch
    /// <em>captures</em>, not of what <c>Decorations</c> <em>declares</em>. A runtime that added, say, a
    /// <c>.Style(…)</c> shortcut without also adding it to <c>AttributeShortcutNames</c> would leave it out
    /// of the set, and a misplaced <c>.Style(…)</c> would be reported as BCF1003, "not statically
    /// analyzable", instead of BCF3008. That fails in the safe direction, which is exactly why nothing else
    /// would notice.
    /// </para>
    /// <para>
    /// Name by name rather than by count alone: the failure mode is one <em>specific</em> name going
    /// missing, and a count would also be satisfied by a swap. The count is asserted as well, for the same
    /// reason <see cref="CuratedTagCount"/> is, the loop only visits the decorations that exist, so on its
    /// own it would pass a runtime that had dropped one. The converse direction needs no assertion: every
    /// name in the set is read off a symbol resolved out of <c>Decorations</c>, so the set cannot hold a name
    /// that type does not declare.
    /// </para>
    /// </remarks>
    [Fact]
    public void DecorationNames_CoverEveryDecorationTheRuntimeDeclares()
    {
        var (symbols, _) = ResolveHtml();
        var declared = DeclaredDecorationNames();

        foreach (var name in declared)
        {
            Assert.True(
                symbols.DeclaresDecorationNamed(name),
                $"Decorations declares '{name}', but KnownSymbols does not capture it, so a misplaced " +
                $".{name}(…) falls through to BCF1003 instead of BCF3008.");
        }

        Assert.Equal(DecorationNameCount, declared.Count);
    }

    /// <summary>The number of distinct decoration names <c>BlazorCodeFirst.Decorations</c> declares.</summary>
    private const int DecorationNameCount = 16;

    /// <summary>
    /// The distinct names of the public static extension methods <c>BlazorCodeFirst.Decorations</c> declares,
    /// in a stable order so a failure names the same decoration every run.
    /// </summary>
    private static List<string> DeclaredDecorationNames()
    {
        var compilation = CompilationTestHost.CreateCompilation("");
        var decorations = compilation.GetTypeByMetadataName("BlazorCodeFirst.Decorations");
        Assert.NotNull(decorations);

        var names = decorations!.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static method =>
                method is { IsExtensionMethod: true, IsStatic: true, DeclaredAccessibility: Accessibility.Public })
            .Select(static method => method.Name)
            .Distinct(System.StringComparer.Ordinal)
            .OrderBy(static name => name, System.StringComparer.Ordinal)
            .ToList();

        // A resolution failure would otherwise make the whole guard vacuous rather than red.
        Assert.NotEmpty(names);
        return names;
    }

    /// <summary>
    /// Every decoration shaped like an event shortcut is registered as one, with an event name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape carries the rule and the table supplies only what the shape cannot: an extension on
    /// <c>ElementView</c> whose sole non-receiver parameter is a delegate takes a handler and no event
    /// name, so the event it stands for has to come from somewhere, and that somewhere is
    /// <c>EventShortcutNames</c>. The rule therefore needs no name knowledge of its own and cannot be
    /// satisfied by transcribing the table it checks.
    /// </para>
    /// <para>
    /// That shape is read off <see cref="KnownSymbols.TryGetEventParameters"/> rather than spelled here as
    /// <c>(ElementView, delegate)</c>. This test used to be the third convention answering "which
    /// argument is the handler", alongside BCF3001's exemption and the decoration arm, and being a
    /// consumer of the one answer is what keeps it from drifting from them (#221). It is also what widens
    /// it: the transcribed shape matched a two-parameter overload only, so it said nothing about
    /// <c>.On</c> in either direction — see <see cref="EveryEventDecoration_ResolvesItsHandlerArgument"/>.
    /// </para>
    /// <para>
    /// It is not the net that catches a missing row. <see cref="DecorationNames_CoverEveryDecorationTheRuntimeDeclares"/>
    /// already does, in both directions and for every decoration group, because a member the constructor's
    /// switch does not classify never reaches <c>_decorationNames</c> either — measured by adding
    /// <c>.OnInput(Action)</c> to the runtime, which fails that test by name and by count. What this adds is
    /// narrower: that an event shortcut is classified as <see cref="SurfaceMethodKind.EventShortcut"/>
    /// specifically and not, say, mis-filed under <c>AttributeShortcutNames</c>, that it carries an event
    /// name at all, and that the name is spelled as an event.
    /// </para>
    /// <para>
    /// That prediction came true. <c>.Ref(this ElementView, Action&lt;ElementReference&gt;)</c> (#309) is an
    /// extension on <c>ElementView</c> whose sole non-receiver parameter is a delegate, matches the shape
    /// above exactly, and stands for no event. The answer was the one this rule's earlier wording called
    /// for: a classification of its own (<see cref="SurfaceMethodKind.Ref"/>) and this revision, not an
    /// <c>EventShortcutNames</c> row.
    /// </para>
    /// <para>
    /// The revision is the argument test below. A handler either takes nothing (<c>Action</c>,
    /// <c>Func&lt;Task&gt;</c>) or takes an argument the event delivers, which is a
    /// <see cref="System.EventArgs"/> — that is the same constraint the surface declares on
    /// <c>.On&lt;TArgs&gt;</c> and BCF3028 enforces, so this narrowing costs no new knowledge. A delegate
    /// taking anything else is carrying something other than an event's argument to somewhere other than
    /// an event, and that is what makes it a different channel. The shape stays load-bearing: a real event
    /// shortcut added without a row still lands here.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryEventShortcutShapedDecoration_IsRegisteredWithAnEventName()
    {
        var (symbols, html) = ResolveHtml();
        var elementView = ResolveElementView(html);
        var decorations = html.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.Decorations");
        Assert.NotNull(decorations);

        var eventArgs = symbols.EventArgsType;
        Assert.NotNull(eventArgs);

        var shortcuts = decorations!.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method is { IsExtensionMethod: true, Parameters.Length: > 0 }
                && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, elementView)
                && KnownSymbols.TryGetEventParameters(method, out var eventParameters)
                && !eventParameters.CarriesEventName
                // The last parameter, not Parameters[HandlerIndex]: that index is in argument space, which
                // excludes the extension receiver, while these symbols are unreduced. TryGetEventParameters
                // answers only for a handler written last, so the two agree here by its own contract.
                && CarriesAnEventsArgument(method.Parameters[method.Parameters.Length - 1].Type, eventArgs!))
            .ToList();

        // A resolution or shape-rule failure would otherwise make the whole guard vacuous rather than red.
        Assert.NotEmpty(shortcuts);

        foreach (var method in shortcuts)
        {
            var key = KnownSymbols.Normalize(method);
            Assert.Equal(SurfaceMethodKind.EventShortcut, symbols.ClassifySurfaceMethod(method));
            Assert.True(
                symbols.EventShortcuts.TryGetValue(key, out var eventName),
                $"'.{method.Name}({method.Parameters[method.Parameters.Length - 1].Type.Name})' extends " +
                $"ElementView and takes a handler and no event name, but KnownSymbols registered no " +
                $"event name for it. Either it stands for an event and needs an EventShortcutNames row, " +
                $"or it stands for something else and needs a classification of its own plus a revision " +
                $"of this rule.");

            // The same rule BCF3019 holds a hand-written .Bind event name to. Nothing derives the name from
            // the method's, deliberately: an event whose HTML spelling is not the member name minus "On"
            // (ondblclick, say) is a table row, not a special case.
            Assert.StartsWith("on", eventName, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Whether <paramref name="handler"/> is a delegate an event could invoke: one taking no argument, or
    /// one taking an argument the event delivers, which is an <see cref="System.EventArgs"/>.
    /// </summary>
    /// <remarks>
    /// A delegate taking anything else belongs to another channel. <c>.Ref</c> is the case that forced the
    /// question: its <c>Action&lt;ElementReference&gt;</c> is a handler in shape and receives an element,
    /// not an event. Nothing is asserted about the return type, since both <c>Action</c> and
    /// <c>Func&lt;Task&gt;</c> are handlers.
    /// </remarks>
    private static bool CarriesAnEventsArgument(ITypeSymbol handler, INamedTypeSymbol eventArgs)
    {
        if (handler is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke })
            return false;

        return invoke.Parameters.Length == 0
            || TypeSymbolFacts.IsAssignableTo(invoke.Parameters[0].Type, eventArgs);
    }

    /// <summary>
    /// Every event decoration the runtime declares has a handler argument this compiler can name, and the
    /// two argument layouts are the ones its readers index into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The coverage <c>(ElementView, delegate)</c> never had. That transcription walked the
    /// two-parameter shortcut shape only, so an <c>.On</c> overload was outside it in both directions: a
    /// new one whose handler did not sit where <c>RenderExpressionAnalyzer</c> and BCF3001's exemption
    /// assumed was not caught, and neither was one this rule could not read at all (#221).
    /// </para>
    /// <para>
    /// Indices rather than a re-derivation of the shape rule, because the indices are what the readers
    /// actually use: the exemption compares an argument's normalized position against
    /// <c>HandlerIndex</c>, and the decoration arm reads its handler expression out of the argument list
    /// at it. A layout that moved would be a silent change of meaning at both.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryEventDecoration_ResolvesItsHandlerArgument()
    {
        var (symbols, _) = ResolveHtml();

        var events = symbols.SurfaceMethods
            .Where(entry => entry.Value is SurfaceMethodKind.EventShortcut or SurfaceMethodKind.On)
            .ToList();

        // Both kinds, so neither half of the assertion below can pass by there being nothing of that kind.
        Assert.Contains(events, entry => entry.Value == SurfaceMethodKind.EventShortcut);
        Assert.Contains(events, entry => entry.Value == SurfaceMethodKind.On);

        foreach (var entry in events)
        {
            var method = (IMethodSymbol)entry.Key;
            Assert.True(
                KnownSymbols.TryGetEventParameters(method, out var eventParameters),
                $"'.{method.Name}' is classified {entry.Value}, but KnownSymbols cannot say which of its " +
                $"arguments is the handler, so BCF3001's exemption and the decoration arm have no answer " +
                $"to share. Its parameters are ({string.Join(", ", method.Parameters.Select(p => p.Type.Name))}). " +
                $"A second delegate parameter is the expected cause: decide whether a mutation in it is " +
                $"deferred, then widen TryGetEventParameters to say so.");

            // .On takes the event's name as its first argument and the handler after it; a named shortcut
            // stands for the name itself and takes the handler alone.
            var (expectedHandler, expectedEventName) =
                entry.Value == SurfaceMethodKind.On ? (1, 0) : (0, -1);
            Assert.Equal(expectedHandler, eventParameters.HandlerIndex);
            Assert.Equal(expectedEventName, eventParameters.EventNameIndex);
        }
    }

    [Fact]
    public void OnAndOnClick_RegisterAllOverloads()
    {
        var (symbols, _) = ResolveHtml();
        // .On(string,Action), .On(string,Func<Task>), .On<TArgs>(string,Action<TArgs>) and
        // .On<TArgs>(string,Func<TArgs,Task>) => 4 classified as On. The generic pair keys correctly
        // because Normalize takes OriginalDefinition, so a constructed call site lands on the open
        // definition.
        Assert.Equal(4, SurfaceMethodsOfKind(symbols, SurfaceMethodKind.On).Count);
        // .OnClick(Action) and .OnClick(Func<Task>) both map to "onclick" => 2 EventShortcuts entries.
        Assert.Equal(2, symbols.EventShortcuts.Count(kvp => kvp.Value == "onclick"));
    }

    /// <summary>
    /// <c>.Attr(string,string?)</c>, <c>.Attr(string,bool)</c> and <c>.Attr(string)</c> are all captured. The
    /// registration is by name, so it takes every overload without a change here; what this pins is that the
    /// runtime still declares all three, since dropping one would silently narrow the surface — back to
    /// strings, or back to a written <see langword="bool"/> — while every other test kept passing.
    /// </summary>
    /// <remarks>
    /// The count also guards the shape #178 chose. The bare spelling is an overload of its own rather than a
    /// default on the <see langword="bool"/> one, and a default is RS0027 while a fourth overload could steal
    /// <c>.Attr("disabled")</c> from the arity-2 form silently. Adding one fails here first.
    /// </remarks>
    [Fact]
    public void Attr_RegistersEveryOverload()
    {
        var (symbols, _) = ResolveHtml();

        Assert.Equal(3, SurfaceMethodsOfKind(symbols, SurfaceMethodKind.Attr).Count);
    }

    /// <summary>
    /// Every structural <c>Html</c> member is classified. The classification is the only place the
    /// compiler records that a method is surface syntax, so a runtime that renamed one, or changed its
    /// shape past the constructor's guards, would leave that member falling through to BCF1003 with
    /// nothing else to notice.
    /// </summary>
    /// <remarks>
    /// One <see cref="FactAttribute"/> over a local table rather than a <see cref="TheoryAttribute"/>
    /// taking the kind: <c>SurfaceMethodKind</c> is internal, and a public test method cannot declare an
    /// internal parameter (CS0051).
    /// </remarks>
    [Fact]
    public void StructuralHtmlMembers_AreClassified()
    {
        var (symbols, _) = ResolveHtml();

        var expected = new (string Name, SurfaceMethodKind Kind)[]
        {
            ("Element", SurfaceMethodKind.Element),
            ("If", SurfaceMethodKind.If),
            ("ForEach", SurfaceMethodKind.ForEach),
            ("Component", SurfaceMethodKind.Component),
            ("Raw", SurfaceMethodKind.Raw),
            ("Fragment", SurfaceMethodKind.Fragment),
        };

        // Held against the list the tag tests already read structural membership off, so a member added
        // to one and not the other cannot pass here.
        Assert.Equal(StructuralHtml.Length, expected.Length);

        foreach (var (name, kind) in expected)
        {
            var classified = Assert.Single(SurfaceMethodsOfKind(symbols, kind));
            Assert.Equal(name, classified.Name);
            Assert.Contains(name, StructuralHtml);
        }
    }

    [Fact]
    public void Element_ResolvesTheSingleTagOverload()
    {
        var (symbols, _) = ResolveHtml();

        var element = Assert.IsAssignableFrom<IMethodSymbol>(
            Assert.Single(SurfaceMethodsOfKind(symbols, SurfaceMethodKind.Element)));
        Assert.Single(element.Parameters);
        Assert.Equal(SpecialType.System_String, element.Parameters[0].Type.SpecialType);
    }

    [Fact]
    public void Component_ResolvesTheParameterlessFactory_AndTheChildrenIndexer()
    {
        var (symbols, _) = ResolveHtml();

        Assert.NotNull(symbols.HtmlComponent);
        Assert.Empty(symbols.HtmlComponent!.Parameters);

        // Children arrive through the indexer now, not an overload.
        Assert.NotNull(symbols.ComponentIndexer);
        Assert.NotNull(symbols.ElementIndexer);
        Assert.NotNull(symbols.ElementViewType);
    }

    /// <summary>
    /// The content-slot surface resolves whole (#34, #176). All three are guarded because each degrades
    /// silently on its own: without <c>SlotViewType</c> a content-taking declaration is rejected as
    /// returning the wrong type, without <c>ContentIndexer</c> every <c>Card("t")[…]</c> falls through to
    /// BCF1003, and without <c>SlotProperty</c> no declaration binds a slot ordinal, so every correct
    /// <c>Slot</c> is reported as BCF3025 instead.
    /// </summary>
    [Fact]
    public void ContentSlotSurface_ResolvesTheMarkerTypeItsIndexerAndTheSlotHole()
    {
        var (symbols, _) = ResolveHtml();

        Assert.NotNull(symbols.SlotViewType);
        Assert.NotNull(symbols.ContentIndexer);
        Assert.NotNull(symbols.SlotProperty);

        // The hole is View-typed, which is what keeps it disjoint from the element helpers (they return
        // ElementView) and out of ElementTags.
        Assert.True(SymbolEqualityComparer.Default.Equals(symbols.SlotProperty!.Type, symbols.ViewType));
        Assert.DoesNotContain(
            KnownSymbols.Normalize(symbols.SlotProperty!),
            symbols.ElementTags.Keys);

        // The content channel is the same one ElementView and ComponentView<T> declare: params
        // ReadOnlySpan<View>. A differently shaped indexer would not be found by FindChildrenIndexer at all,
        // so this asserts the shape the compiler matched rather than restating the declaration.
        var contentIndexerParameter = Assert.Single(symbols.ContentIndexer!.Parameters);
        Assert.True(contentIndexerParameter.IsParams);
    }

    [Fact]
    public void ComponentParameterMethods_AllFourStructuralShapesAreResolvedSeparately()
    {
        var (symbols, _) = ResolveHtml();
        Assert.NotNull(symbols.ComponentViewType);

        var kinds = symbols.ComponentViewType!.GetMembers()
            .OfType<IMethodSymbol>()
            .Select(symbols.ClassifySurfaceMethod)
            .Where(static kind => kind is SurfaceMethodKind.ScalarParam
                or SurfaceMethodKind.FragmentParam
                or SurfaceMethodKind.GenericTemplateIgnored
                or SurfaceMethodKind.GenericTemplateContextual)
            .OrderBy(static kind => kind)
            .ToArray();

        Assert.Equal(
            [
                SurfaceMethodKind.ScalarParam,
                SurfaceMethodKind.FragmentParam,
                SurfaceMethodKind.GenericTemplateIgnored,
                SurfaceMethodKind.GenericTemplateContextual,
            ],
            kinds);
    }

    /// <summary>
    /// Every <c>ComponentView&lt;T&gt;.Bind</c> overload is classified, since the arm that reads a
    /// component binding is reached by the classification alone.
    /// </summary>
    [Fact]
    public void ComponentBind_RegistersAllThreeOverloads()
    {
        var (symbols, _) = ResolveHtml();

        Assert.Equal(3, SurfaceMethodsOfKind(symbols, SurfaceMethodKind.ComponentBind).Count);
    }

    /// <summary>
    /// The tag-value view added for the #140 fold predicate has to agree with the name-keyed table it is
    /// derived from, in both count and content. The content direction reuses the rule
    /// <see cref="EveryCuratedTag_IsItsHelperNameWithALowercasedFirstLetter"/> already establishes, so a
    /// second hand-written transcription of 100 tags is not needed here.
    /// </summary>
    [Fact]
    public void CuratedElementTags_AreExactlyTheCuratedTableValues()
    {
        var expected = ExpectedCuratedNames
            .Select(static name => char.ToLowerInvariant(name[0]) + name.Substring(1))
            .OrderBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();

        var actual = KnownSymbols.CuratedElementTags
            .OrderBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(CuratedTagCount, actual.Length);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsCuratedTag_IsOrdinal()
    {
        Assert.True(KnownSymbols.IsCuratedTag("div"));
        Assert.False(KnownSymbols.IsCuratedTag("DIV"));
        Assert.False(KnownSymbols.IsCuratedTag("marquee"));
    }

    /// <summary>
    /// Every member the inert types declare is one <c>KnownSymbols.IsDesignTimeApiMember</c> recognizes, or a
    /// conversion operator, which is not a member an author writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The direction the other guards in this file leave open. They hold the <c>Html</c> and
    /// <c>Decorations</c> halves of the design-time API against what the runtime declares, so a member added
    /// there under an unrecognized name is red here first. The inert types' own members had no such guard:
    /// <c>ComponentParameterMethods_AllFourStructuralShapesAreResolvedSeparately</c> filters to four known
    /// kinds, so an unclassified member answers <c>None</c>, drops out of the filter, and leaves the asserted
    /// array equal.
    /// </para>
    /// <para>
    /// What that costs is quiet and one-sided, which is why it needs a test rather than care. An
    /// <c>ElementView.Key(…)</c> or <c>ComponentView&lt;T&gt;.Ref(…)</c> returning an inert type would pass
    /// BCF3029's type test and fail its member test, so the chain walk would step over it and anchor the
    /// report on an inner helper instead of the whole expression — contradicting Appendix A's stated
    /// <c>位置は最も外側の設計時式の全体</c> in the direction where nothing throws and no test looks.
    /// </para>
    /// <para>
    /// Conversion operators are excluded rather than classified. They are how an inert value reaches a
    /// <c>View</c>-typed position and are never written by name, so the analyzer meets them as
    /// <c>IConversionOperation</c> and not as a reference to a member.
    /// </para>
    /// </remarks>
    [Fact]
    public void InertTypeMembers_AreAllRecognizedAsDesignTimeApi()
    {
        var (symbols, html) = ResolveHtml();
        var assembly = html.ContainingAssembly;

        string[] inertTypeNames =
        [
            "BlazorCodeFirst.View",
            "BlazorCodeFirst.ElementView",
            "BlazorCodeFirst.SlotView",
            "BlazorCodeFirst.ComponentView`1",
        ];

        var unrecognized = new List<string>();

        foreach (var typeName in inertTypeNames)
        {
            var type = assembly.GetTypeByMetadataName(typeName);
            Assert.NotNull(type);

            foreach (var member in type!.GetMembers())
            {
                if (member.DeclaredAccessibility != Accessibility.Public || member.IsImplicitlyDeclared)
                    continue;

                // A conversion operator is not a member an author names; see the remarks.
                if (member is IMethodSymbol { MethodKind: MethodKind.Conversion })
                    continue;

                // The indexer's own getter arrives as a separate member and is reached through the property.
                if (member is IMethodSymbol { MethodKind: MethodKind.PropertyGet })
                    continue;

                if (!symbols.IsDesignTimeApiMember(member))
                    unrecognized.Add($"{type.Name}.{member.Name}");
            }
        }

        Assert.Empty(unrecognized);
    }

    [Fact]
    public void EnumerableSelect_ResolvesTheProjectionOverloadAndNotTheIndexedOne()
    {
        // The splice folds `source.Select(item => …)` (#172). Select's other overload takes
        // Func<TSource, int, TResult>, whose two-parameter lambda has no single iteration variable for a
        // content template to bind. Both overloads declare two parameters, so the discriminator has to be
        // the selector's own arity, and this is what pins that.
        var (symbols, _) = ResolveHtml();

        Assert.NotNull(symbols.EnumerableSelect);
        Assert.Equal("Select", symbols.EnumerableSelect!.Name);
        Assert.Equal(
            "System.Linq.Enumerable",
            symbols.EnumerableSelect.ContainingType.ToDisplayString());

        var selector = Assert.IsAssignableFrom<INamedTypeSymbol>(
            symbols.EnumerableSelect.Parameters[1].Type);
        Assert.Equal(2, selector.TypeArguments.Length);
    }

    /// <summary>
    /// <c>BlazorCodeFirst.ElementView</c>, resolved out of the runtime assembly directly rather than read
    /// off <c>KnownSymbols.ElementViewType</c>.
    /// </summary>
    /// <remarks>
    /// The two guards that call this exist to check <c>KnownSymbols</c>'s receiver filter, and that filter is
    /// only meaningful while the type it filters on is resolved independently; sourcing it from
    /// <c>KnownSymbols</c> would have the guard compare that object against itself.
    /// </remarks>
    private static INamedTypeSymbol ResolveElementView(INamedTypeSymbol html)
    {
        var elementView = html.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.ElementView");
        Assert.NotNull(elementView);
        return elementView!;
    }

    private static (KnownSymbols, INamedTypeSymbol) ResolveHtml()
    {
        var compilation = CompilationTestHost.CreateCompilation("");
        var symbols = KnownSymbols.TryCreate(compilation);
        Assert.NotNull(symbols);
        var html = compilation.GetTypeByMetadataName("BlazorCodeFirst.Html")!;
        return (symbols!, html);
    }
}
