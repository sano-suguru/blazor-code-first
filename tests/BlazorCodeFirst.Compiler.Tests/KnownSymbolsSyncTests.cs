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
        Assert.NotEmpty(symbols.AttrMethods);
        Assert.NotEmpty(symbols.OnMethods);
    }

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

        var captured = new List<ISymbol>();
        if (symbols.ClassMethod is not null)
            captured.Add(KnownSymbols.Normalize(symbols.ClassMethod));
        captured.AddRange(symbols.AttributeShortcuts.Keys);
        captured.AddRange(symbols.EventShortcuts.Keys);
        captured.AddRange(symbols.AttrMethods);
        captured.AddRange(symbols.OnMethods);

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
    private const int DecorationNameCount = 11;

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
        // .On(string,Action) and .On(string,Func<Task>) => 2 OnMethods.
        Assert.Equal(2, symbols.OnMethods.Count);
        // .OnClick(Action) and .OnClick(Func<Task>) both map to "onclick" => 2 EventShortcuts entries.
        Assert.Equal(2, symbols.EventShortcuts.Count(kvp => kvp.Value == "onclick"));
    }

    [Fact]
    public void Raw_IsResolved()
    {
        var (symbols, _) = ResolveHtml();
        Assert.NotNull(symbols.HtmlRaw);
    }

    [Fact]
    public void Fragment_IsResolved()
    {
        var (symbols, _) = ResolveHtml();
        Assert.NotNull(symbols.HtmlFragment);
    }

    [Fact]
    public void Element_ResolvesTheSingleTagOverload()
    {
        var (symbols, _) = ResolveHtml();

        Assert.NotNull(symbols.HtmlElement);
        Assert.Single(symbols.HtmlElement!.Parameters);
        Assert.Equal(SpecialType.System_String, symbols.HtmlElement.Parameters[0].Type.SpecialType);
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
    public void Param_BothOverloads_AreResolvedSeparately()
    {
        var (symbols, _) = ResolveHtml();

        Assert.NotNull(symbols.ParamMethod);
        Assert.NotNull(symbols.FragmentParamMethod);

        // The generic overload has a type parameter; the fragment overload does not.
        Assert.Equal(1, symbols.ParamMethod!.Arity);
        Assert.Equal(0, symbols.FragmentParamMethod!.Arity);
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
