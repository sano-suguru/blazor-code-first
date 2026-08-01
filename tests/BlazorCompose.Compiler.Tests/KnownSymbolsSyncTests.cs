using System.Linq;
using BlazorCompose.Compiler.Analysis;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class KnownSymbolsSyncTests
{
    // Structural Html members that are NOT curated element tags.
    private static readonly string[] StructuralHtml = ["Element", "If", "ForEach", "Component", "Fragment", "Raw"];

    /// <summary>The number of curated element helpers <c>KnownSymbols</c> owns the table for.</summary>
    internal const int CuratedTagCount = 22;

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
        // arm — the guard would go vacuous while staying green.
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
        var html = compilation.GetTypeByMetadataName("BlazorCompose.Html")!;
        return (symbols!, html);
    }
}
