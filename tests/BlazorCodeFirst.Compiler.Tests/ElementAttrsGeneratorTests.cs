namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ElementAttrsGeneratorTests
{
    [Fact]
    public void Attrs_EmitsAddMultipleAttributes_BeforeTheClassChannel()
    {
        var result = CompilationTestHost.RunGenerator(
            """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class TestComponent : BodyComponentBase
            {
                public System.Collections.Generic.IReadOnlyDictionary<string, object>? Extra { get; set; }

                protected override View Body =>
                    Div.Attrs(Extra).Class("card")["text"];
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // AddMultipleAttributes must appear before the class channel's AddAttribute, and at the
        // sequence immediately following OpenElement (0 here, since there is no .Key on this
        // element) — i.e. sequence 1, with the class channel taking sequence 2.
        Assert.Contains(
            "__builder.AddMultipleAttributes(1, "
                + "(global::System.Collections.Generic.IReadOnlyDictionary<string, object>?)(Extra));",
            generated);
        var splatIndex = generated.IndexOf("AddMultipleAttributes", StringComparison.Ordinal);
        var classIndex = generated.IndexOf("AddAttribute(2, \"class\"", StringComparison.Ordinal);
        Assert.True(splatIndex >= 0 && classIndex >= 0 && splatIndex < classIndex);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Attrs_WithNoOtherAttributes_StillReservesExactlyOneSequenceNumber()
    {
        var result = CompilationTestHost.RunGenerator(
            """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class TestComponent : BodyComponentBase
            {
                public System.Collections.Generic.IReadOnlyDictionary<string, object>? Extra { get; set; }

                protected override View Body => Div.Attrs(Extra)["text"];
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"div\");", generated);
        Assert.Contains(
            "__builder.AddMultipleAttributes(1, "
                + "(global::System.Collections.Generic.IReadOnlyDictionary<string, object>?)(Extra));",
            generated);
        Assert.Contains("__builder.AddContent(2, \"text\");", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void SecondAttrsCall_IsBCF3033()
    {
        var result = CompilationTestHost.RunGenerator(
            """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class TestComponent : BodyComponentBase
            {
                public System.Collections.Generic.IReadOnlyDictionary<string, object>? A { get; set; }
                public System.Collections.Generic.IReadOnlyDictionary<string, object>? B { get; set; }

                protected override View Body => Div.Attrs(A).Attrs(B)["text"];
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3033");
    }

    [Fact]
    public void ElementWithoutAttrs_GeneratesIdenticalSequencingToToday()
    {
        // A non-constant class (matches HtmlDecorationGeneratorTests.ClassOnDivSource's own
        // reasoning): a fully-constant Div.Class("card")["text"] folds whole into one
        // AddMarkupContent frame (§2.7(D)) and never reaches AddAttribute at all, which would make
        // this assertion vacuous rather than a check on ordinary (unfolded) sequencing.
        var result = CompilationTestHost.RunGenerator(
            """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class TestComponent : BodyComponentBase
            {
                private string _cls => "card";
                protected override View Body => Div.Class(_cls)["text"];
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain("AddMultipleAttributes", generated);
        Assert.Contains("__builder.AddAttribute(1, \"class\", _cls);", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Attrs_DisqualifiesTheElementFromStaticFolding()
    {
        // Every other part of this element is constant, so without an explicit exclusion for
        // AttributesSplat, StaticMarkupSerializer.IsFoldableElement would fold the whole element
        // into one AddMarkupContent frame — silently dropping the runtime dictionary the author
        // wrote .Attrs for. This pins that IsFoldableElement excludes it, the same way it already
        // excludes Key/Ref/FormName.
        var result = CompilationTestHost.RunGenerator(
            """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class TestComponent : BodyComponentBase
            {
                public System.Collections.Generic.IReadOnlyDictionary<string, object>? Extra { get; set; }

                protected override View Body => Div.Attrs(Extra).Class("card")["text"];
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain("AddMarkupContent", generated);
        Assert.Contains("__builder.OpenElement(0, \"div\");", generated);
        Assert.Contains("__builder.AddMultipleAttributes(1, ", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}
