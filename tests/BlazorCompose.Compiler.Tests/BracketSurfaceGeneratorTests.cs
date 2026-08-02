using System.Linq;
using BlazorCompose.Compiler.Analysis;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Shape-level coverage of the bracket surface: that the compiler recognizes an element written as a
/// property reference or an element access at all.  The proof that it produces the <em>same</em> code as the
/// method surface that preceded it is <see cref="SnapshotCorpusTests"/>, whose baselines were captured before
/// the migration; these tests exist to localize a failure to the dispatch head before a whole-file comparison
/// is consulted.
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
        var result = CompilationTestHost.RunGenerator(Host("""Div["a"]"""));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.AddContent(1, \"a\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ChildlessElement_WrittenAsABarePropertyReference_OpensTheElement()
    {
        var result = CompilationTestHost.RunGenerator(Host("""Img"""));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"img\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ChildlessElement_WrittenQualified_OpensTheElement()
    {
        // The qualified escape hatch is a MemberAccessExpressionSyntax rather than the IdentifierNameSyntax
        // the unqualified form produces. Dispatching on the resolved symbol is what makes one arm serve both.
        var result = CompilationTestHost.RunGenerator(Host("""Html.Img"""));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"img\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void DecoratedElementWithChildren_FoldsTheDecorationIntoTheElement()
    {
        var result = CompilationTestHost.RunGenerator(Host("""Div.Class("card")["a"]"""));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.AddAttribute(1, \"class\", \"card\")", generated);
        Assert.Contains("__builder.AddContent(2, \"a\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void CuratedTags_ResolveFromPropertiesOnTheBracketSurface()
    {
        // KnownSymbolsSyncTests pins which names the table holds; this pins the member *kind* every key has.
        // Nothing there would fail if a curated tag resolved to a method or to an indexer, and the dispatch
        // head reads the table through the property arm alone.
        var compilation = CompilationTestHost.CreateCompilation("");

        var symbols = KnownSymbols.TryCreate(compilation);

        Assert.NotNull(symbols);
        Assert.Equal(KnownSymbolsSyncTests.CuratedTagCount, symbols!.ElementTags.Count);
        Assert.DoesNotContain(symbols.ElementTags.Keys, static key => key is IPropertySymbol { IsIndexer: true });
        Assert.All(symbols.ElementTags.Keys, static key => Assert.IsAssignableFrom<IPropertySymbol>(key));
    }

    [Fact]
    public void ChildlessElement_AsAChildOfAnotherElement_OpensBothElements()
    {
        // element-childless covers a bare property reference at the body root only. As a child it converts
        // to View through the implicit operator inside a collection expression, which is a different
        // binding.
        var result = CompilationTestHost.RunGenerator(Host("""Div[Img]"""));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.OpenElement(1, \"img\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// A surface whose members have the right names and shapes but the wrong declared types must not be
    /// recognized.  This is the only test that exercises the type guards: the shipped runtime declares
    /// everything correctly, so every other test can only prove the positive side.  Hence the compilation
    /// built without it — the wrong surface has to be the only <c>BlazorCompose</c> in scope.
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
                // A curated name that is not an element helper.
                public static int Div => 0;

                // A curated name that is one.
                public static ElementBuilder Span => default;
            }
            """));

        var symbols = KnownSymbols.TryCreate(compilation);

        Assert.NotNull(symbols);
        Assert.Null(symbols!.ElementIndexer);

        // Declared as a local, not spelled inline in the call: a collection expression has no target type
        // in an Assert.Equal argument position. BracketSurfaceDiagnosticTests uses the same pattern.
        string[] expected = ["Span"];
        Assert.Equal(expected, symbols.ElementTags.Keys.Select(static key => key.Name).ToList());
    }
}
