using System.Linq;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Shape-level coverage of the bracket surface: that the compiler recognizes an element written as a
/// property reference or an element access at all. The proof that it produces the <em>same</em> code as the
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
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
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
    /// recognized. This is the only test that exercises the type guards: the shipped runtime declares
    /// everything correctly, so every other test can only prove the positive side. Hence the compilation
    /// built without it, the wrong surface has to be the only <c>BlazorCodeFirst</c> in scope.
    /// </summary>
    [Fact]
    public void MembersWithTheWrongDeclaredTypes_AreNotRecognized()
    {
        var compilation = CompilationTestHost.CreateCompilationWithoutRuntime(("Surface.cs", """
            namespace BlazorCodeFirst;

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

    [Fact]
    public void EveryCuratedHelper_CompilesUnqualifiedAndOpensItsOwnTag()
    {
        // The sync tests read symbol tables. This is the only check that runs a curated name through the
        // whole path: `using static Html` import, generator dispatch, emitted tag string, and a
        // compilation of the generated output. It is derived from ElementTags on purpose, so it proves
        // every row works rather than re-pinning which rows exist; ExpectedCuratedNames does the latter.
        var symbols = KnownSymbols.TryCreate(CompilationTestHost.CreateCompilation(""));
        Assert.NotNull(symbols);

        var tags = symbols!.ElementTags
            .OrderBy(static entry => entry.Key.Name, System.StringComparer.Ordinal)
            .Select(static entry => (Name: entry.Key.Name, Tag: entry.Value))
            .ToArray();
        Assert.Equal(KnownSymbolsSyncTests.CuratedTagCount, tags.Length);

        // Childless bare property references: each opens and closes one element, and none of them emits
        // an attribute or a text node, so the only quoted string in the output is a tag name.
        var body = $"Div[{string.Join(", ", tags.Select(static t => t.Name))}]";
        var result = CompilationTestHost.RunGenerator(Host(body));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        foreach (var (name, tag) in tags)
        {
            Assert.True(
                generated.Contains($", \"{tag}\");", System.StringComparison.Ordinal),
                $"Html.{name} produced no OpenElement for \"{tag}\".");
        }

        // Proves the 100 runtime properties exist and are reachable unqualified through `using static`.
        CompilationTestHost.AssertOutputCompiles(result);
    }
}
