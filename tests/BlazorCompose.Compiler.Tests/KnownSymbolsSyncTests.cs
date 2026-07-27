using System.Linq;
using BlazorCompose.Compiler.Analysis;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class KnownSymbolsSyncTests
{
    // Structural Html members that are NOT curated element tags.
    private static readonly string[] StructuralHtml = ["Element", "If", "ForEach", "Component", "Fragment", "Raw"];

    [Fact]
    public void ElementTags_CoverEveryCuratedHtmlHelper_AndNothingStructural()
    {
        var (symbols, html) = ResolveHtml();
        var tagged = symbols.ElementTags.Keys.OfType<IMethodSymbol>()
            .Select(m => m.Name).ToHashSet(System.StringComparer.Ordinal);

        foreach (var member in html.GetMembers().OfType<IMethodSymbol>()
                     .Where(m => m.MethodKind == MethodKind.Ordinary))
        {
            bool structural = System.Array.IndexOf(StructuralHtml, member.Name) >= 0;
            if (structural)
                Assert.DoesNotContain(member.Name, tagged);
            else
                Assert.Contains(member.Name, tagged); // every non-structural Html helper is a curated tag
        }
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
