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

        // Both spellings are enumerated on purpose. A curated helper is an ordinary method on the current
        // surface and a property returning ElementBuilder on the bracket surface (#87); filtering either
        // side to IMethodSymbol makes `tagged` empty after the flip, leaves every remaining ordinary Html
        // method looking structural, and so never runs the Assert.Contains arm — the guard would go vacuous
        // while staying green.
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
    public void Component_BothOverloads_AreResolvedSeparately()
    {
        var (symbols, _) = ResolveHtml();

        Assert.NotNull(symbols.HtmlComponent);
        Assert.NotNull(symbols.HtmlComponentWithChildren);
        Assert.Empty(symbols.HtmlComponent!.Parameters);
        Assert.Single(symbols.HtmlComponentWithChildren!.Parameters);
        Assert.True(symbols.HtmlComponentWithChildren.Parameters[0].IsParams);
    }

    [Fact]
    public void Element_ResolvesOnlyTheChildrenOverload_OnTheShippedRuntime()
    {
        var (symbols, _) = ResolveHtml();

        // Pinned from the opposite side to the bracket-surface assertion in
        // BracketSurfaceGeneratorTests: one field per arity, so a runtime declaring both cannot let
        // GetMembers order decide which one analysis recognizes.
        Assert.Null(symbols.HtmlElement);
        Assert.NotNull(symbols.HtmlElementWithChildren);
        Assert.Equal(2, symbols.HtmlElementWithChildren!.Parameters.Length);
        Assert.True(symbols.HtmlElementWithChildren.Parameters[1].IsParams);
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
