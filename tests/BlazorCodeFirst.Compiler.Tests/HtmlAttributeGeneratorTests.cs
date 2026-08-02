namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlAttributeGeneratorTests
{
    private static string Run(string body)
    {
        var source = $$"""
            using BlazorCodeFirst;
            public partial class C : BodyComponentBase
            {
                private string _url = "/x";
                protected override View Body => {{body}};
            }
            """;
        var result = CompilationTestHost.RunGenerator(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
        return Assert.Single(result.GeneratedSources).SourceText.ToString();
    }

    private static System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> Diags(string body)
    {
        var source = $$"""
            using BlazorCodeFirst;
            public partial class C : BodyComponentBase
            {
                protected override View Body => {{body}};
            }
            """;
        return CompilationTestHost.RunGenerator(source).Diagnostics;
    }

    [Fact]
    public void NamedShortcut_EmitsHrefAttribute()
    {
        var code = Run("""Html.A.Href("/home")["Home"]""");
        Assert.Contains("__builder.OpenElement(0, \"a\")", code);
        Assert.Contains("__builder.AddAttribute(1, \"href\", \"/home\")", code);
        Assert.Contains("__builder.AddContent(2, \"Home\")", code);
    }

    [Fact]
    public void GenericAttr_EmitsArbitraryAttribute()
    {
        var code = Run("""Html.Nav.Attr("aria-label", "main")""");
        Assert.Contains("__builder.AddAttribute(1, \"aria-label\", \"main\")", code);
    }

    [Fact]
    public void GenericOn_EmitsEventWithFullName()
    {
        var code = Run("""Html.Div.On("onmouseenter", () => { })""");
        Assert.Contains(
            "__builder.AddAttribute(1, \"onmouseenter\", global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => { }))",
            code);
    }

    [Fact]
    public void ClassThenAttrClass_BothFoldIntoSingleClassAttribute()
    {
        var code = Run("""Html.Div.Class("a").Attr("class", "b")[Html.Span["x"]]""");
        Assert.Contains("__builder.AddAttribute(1, \"class\", (\"a\") + \" \" + (\"b\"))", code);
        Assert.DoesNotContain("\"class\", \"b\"", code); // not a second class frame
    }

    [Fact]
    public void ValueHole_IsSubstituted()
    {
        var code = Run("""Html.A.Href(_url)["L"]""");
        Assert.Contains("__builder.AddAttribute(1, \"href\", _url)", code);
    }

    [Fact]
    public void DuplicateAttribute_ReportsBCF3010()
    {
        Assert.Contains(Diags("""Html.A.Id("a").Id("b")["L"]"""), d => d.Id == "BCF3010");
        Assert.Contains(Diags("""Html.A.Href("x").Attr("href", "y")["L"]"""), d => d.Id == "BCF3010");
    }

    [Fact]
    public void DuplicateEvent_ReportsBCF3010()
    {
        Assert.Contains(Diags("""Html.Button.OnClick(() => { }).On("onclick", () => { })["x"]"""), d => d.Id == "BCF3010");
    }

    [Fact]
    public void DistinctAttributesAndEvents_NoBCF3010()
    {
        Assert.DoesNotContain(Diags("""Html.A.Href("x").Id("i").Title("t")["L"]"""), d => d.Id == "BCF3010");
        Assert.DoesNotContain(Diags("""Html.Div.OnClick(() => { }).On("onmouseenter", () => { })"""), d => d.Id == "BCF3010");
    }

    [Fact]
    public void DuplicateAcrossAttributeAndEventChannels_ReportsBCF3010()
    {
        // Both channels emit AddAttribute frames under one name, so a name bound once through each is
        // the same dead duplicate as two bindings within a channel — in either decoration order.
        Assert.Contains(Diags("""Html.Div.Attr("onclick", "alert(1)").OnClick(() => { })"""), d => d.Id == "BCF3010");
        Assert.Contains(Diags("""Html.Div.OnClick(() => { }).Attr("onclick", "alert(1)")"""), d => d.Id == "BCF3010");
        Assert.Contains(Diags("""Html.Div.Attr("onclick", "alert(1)").On("onclick", () => { })"""), d => d.Id == "BCF3010");
        Assert.Contains(Diags("""Html.Div.On("onclick", () => { }).Attr("onclick", "alert(1)")"""), d => d.Id == "BCF3010");
    }

    [Fact]
    public void ClassIsExemptFromTheCrossChannelCheck()
    {
        // 'class' is the one repeatable attribute: every spelling folds into the class channel before
        // the duplicate check, in any order and any number of times.
        Assert.DoesNotContain(Diags("""Html.Div.Class("a").Attr("class", "b")"""), d => d.Id == "BCF3010");
        Assert.DoesNotContain(Diags("""Html.Div.Attr("class", "a").Class("b")"""), d => d.Id == "BCF3010");
        Assert.DoesNotContain(Diags("""Html.Div.Attr("class", "a").Attr("class", "b").Class("c")"""), d => d.Id == "BCF3010");
    }

    [Fact]
    public void NonConstantAttrName_ReportsBCF3011()
    {
        Assert.Contains(
            Diags("""Html.Div.Attr(System.Guid.NewGuid().ToString(), "v")"""),
            d => d.Id == "BCF3011");
    }

    [Fact]
    public void EmptyOnEventName_ReportsBCF3011()
    {
        Assert.Contains(Diags("""Html.Div.On("", () => { })"""), d => d.Id == "BCF3011");
    }
}
