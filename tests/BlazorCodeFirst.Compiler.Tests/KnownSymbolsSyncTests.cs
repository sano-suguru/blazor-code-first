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
    /// standard's void elements" is one comparison against a published document, and 付録A's BCF3016 row
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
        // a property returning ElementBuilder. Filtering to IMethodSymbol would make `tagged` empty, leave
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

            bool structural = System.Array.IndexOf(StructuralHtml, name) >= 0;
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
    /// The methods <c>KnownSymbols</c> classified as <paramref name="kind"/>, which is the only place the
    /// compiler now records what a surface method is.
    /// </summary>
    private static List<ISymbol> SurfaceMethodsOfKind(KnownSymbols symbols, SurfaceMethodKind kind) =>
        [.. symbols.SurfaceMethods.Where(entry => entry.Value == kind).Select(entry => entry.Key)];

    /// <summary>
    /// Every decoration <c>KnownSymbols</c> captured is an extension on <c>ElementBuilder</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sets are built by matching the method <em>name</em>, but what makes a method an element
    /// decoration is its <em>receiver</em>. <c>KnownSymbols</c>'s constructor already filters on that
    /// receiver when <c>ElementBuilderType</c> resolves, so neither half of the failure mode on its own
    /// makes this test fail today: adding a future <c>Attr(this ComponentView&lt;T&gt;, string, string)</c>
    /// to <c>Decorations</c> is excluded by that very filter before it reaches <c>captured</c>, and removing
    /// the filter with no such overload declared yet has nothing new to admit, since every current
    /// <c>Decorations</c> member already takes <c>this ElementBuilder</c>. It fails only when both happen
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
    public void EveryCapturedDecoration_ExtendsElementBuilder()
    {
        var (symbols, html) = ResolveHtml();
        var elementBuilder = html.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.ElementBuilder");
        Assert.NotNull(elementBuilder);

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
                    && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, elementBuilder),
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
    private const int DecorationNameCount = 12;

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
    /// <c>.Attr(string,string)</c> and <c>.Attr(string,bool)</c> are both captured. The registration is
    /// by name, so it takes every overload without a change here; what this pins is that the runtime
    /// still declares both, since dropping one would silently narrow the surface back to strings while
    /// every other test kept passing.
    /// </summary>
    [Fact]
    public void Attr_RegistersBothOverloads()
    {
        var (symbols, _) = ResolveHtml();

        Assert.Equal(2, SurfaceMethodsOfKind(symbols, SurfaceMethodKind.Attr).Count);
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
        Assert.NotNull(symbols.ElementBuilderType);
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

    private static (KnownSymbols, INamedTypeSymbol) ResolveHtml()
    {
        var compilation = CompilationTestHost.CreateCompilation("");
        var symbols = KnownSymbols.TryCreate(compilation);
        Assert.NotNull(symbols);
        var html = compilation.GetTypeByMetadataName("BlazorCodeFirst.Html")!;
        return (symbols!, html);
    }
}
