using System.Linq;
using BlazorCompose.Compiler.Analysis;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Shape-level coverage of the bracket surface: that the compiler recognizes an element written as a
/// property reference or an element access at all.  The proof that it produces the <em>same</em> code as the
/// method surface is <see cref="BracketSurfaceBaselineTests"/>; these tests exist to localize a failure to
/// the dispatch head before a whole-file comparison is consulted.
/// </summary>
public sealed class BracketSurfaceGeneratorTests
{
    private static (string Path, string Source)[] Host(string body, string members = "") =>
    [
        ("Host.cs", $$"""
            using System.Collections.Generic;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                {{members}}
                protected override View Body => {{body}};
            }
            """),
    ];

    [Fact]
    public void ElementWithChildren_WrittenAsAnElementAccess_OpensTheElement()
    {
        var result = BracketSurfaceShim.RunGenerator(Host("""Div["a"]"""));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.AddContent(1, \"a\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ChildlessElement_WrittenAsABarePropertyReference_OpensTheElement()
    {
        var result = BracketSurfaceShim.RunGenerator(Host("""Img"""));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"img\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ChildlessElement_WrittenQualified_OpensTheElement()
    {
        // The qualified escape hatch is a MemberAccessExpressionSyntax rather than the IdentifierNameSyntax
        // the unqualified form produces. Dispatching on the resolved symbol is what makes one arm serve both.
        var result = BracketSurfaceShim.RunGenerator(Host("""Html.Img"""));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"img\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void DecoratedElementWithChildren_FoldsTheDecorationIntoTheElement()
    {
        var result = BracketSurfaceShim.RunGenerator(Host("""Div.Class("card")["a"]"""));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.AddAttribute(1, \"class\", \"card\")", generated);
        Assert.Contains("__builder.AddContent(2, \"a\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void CuratedTags_ResolveFromPropertiesOnTheBracketSurface()
    {
        // Runs against the shim rather than the shipped runtime, which KnownSymbolsSyncTests already covers.
        // Kept separate only until the shim goes: this pins that the property arm reads a surface the
        // compiler has never seen the source of.
        var compilation = CompilationTestHost.CreateCompilationWithoutRuntime(
            ("Empty.cs", ""), BracketSurfaceShim.ShimFile);

        var symbols = KnownSymbols.TryCreate(compilation);

        Assert.NotNull(symbols);
        Assert.Equal(KnownSymbolsSyncTests.CuratedTagCount, symbols!.ElementTags.Count);
        Assert.DoesNotContain(symbols.ElementTags.Keys, static key => key is IPropertySymbol { IsIndexer: true });
        Assert.All(symbols.ElementTags.Keys, static key => Assert.IsAssignableFrom<IPropertySymbol>(key));
    }

    /// <summary>
    /// A surface whose members have the right names and shapes but the wrong declared types must not be
    /// recognized.  This is the only test that exercises the type guards: the real shim declares everything
    /// correctly, so it can only prove the positive side.
    /// </summary>
    [Fact]
    public void MembersWithTheWrongDeclaredTypes_AreNotRecognized()
    {
        var compilation = CompilationTestHost.CreateCompilationWithoutRuntime(("Surface.cs", """
            namespace BlazorCompose;

            public readonly struct View { }

            public readonly struct ElementBuilder
            {
                // A single params indexer, but over string rather than View: the shape matches and the
                // channel does not, so reading its arguments as children would be wrong.
                public View this[params System.ReadOnlySpan<string> items] => default;
            }

            public static class Html
            {
                // A curated name that is not an element factory.
                public static int Div => 0;

                // A curated name that is one.
                public static ElementBuilder Span => default;
            }
            """));

        var symbols = KnownSymbols.TryCreate(compilation);

        Assert.NotNull(symbols);
        Assert.Null(symbols!.ElementIndexer);

        // Declared as a local, not spelled inline in the call: a collection expression has no target type
        // in an Assert.Equal argument position. This is the pattern AssertRetiredIntoCS1929 already uses.
        string[] expected = ["Span"];
        Assert.Equal(expected, symbols.ElementTags.Keys.Select(static key => key.Name).ToList());
    }
}
