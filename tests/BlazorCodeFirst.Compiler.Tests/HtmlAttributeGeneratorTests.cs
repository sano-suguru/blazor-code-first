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
                private string _a => "a";
                private string _b => "b";
                private bool _flag => true;
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
        // Static throughout, so it folds; the resolved attribute name is what this test is about and the
        // markup states it, with the value quoted as an attribute value.
        var code = Run("""Html.A.Href("/home")["Home"]""");
        Assert.Contains("""__builder.AddMarkupContent(0, "<a href=\"/home\">Home</a>");""", code);
    }

    [Fact]
    public void GenericAttr_EmitsArbitraryAttribute()
    {
        // Folded, for the reason NamedShortcut_EmitsHrefAttribute gives: the arbitrary name is written
        // through to the markup unchanged.
        var code = Run("""Html.Nav.Attr("aria-label", "main")""");
        Assert.Contains("""__builder.AddMarkupContent(0, "<nav aria-label=\"main\"></nav>");""", code);
    }

    /// <summary>
    /// The <see langword="bool"/> overload (#158) folded: a constant <see langword="true"/> is written as
    /// an empty attribute value, which parses to the same DOM <c>AddAttribute</c> produces for it.
    /// </summary>
    [Fact]
    public void ConstantTrueAttr_FoldsToAnEmptyAttributeValue()
    {
        var code = Run("""Html.Input.Attr("disabled", true)""");
        Assert.Contains("""__builder.AddMarkupContent(0, "<input disabled=\"\">");""", code);
    }

    /// <summary>
    /// A constant <see langword="false"/> is written by omitting the attribute, which is Blazor's
    /// conditional-attribute behaviour. The second attribute is what makes the run worth folding (one
    /// absorbed frame is left on the element path), so the omission is pinned inside a real fold.
    /// </summary>
    [Fact]
    public void ConstantFalseAttr_FoldsToNoAttributeAtAll()
    {
        var code = Run("""Html.Input.Attr("disabled", false).Attr("id", "x")""");
        Assert.Contains("""__builder.AddMarkupContent(0, "<input id=\"x\">");""", code);
        Assert.DoesNotContain("disabled", code);
    }

    /// <summary>
    /// A non-constant <see langword="bool"/> cannot fold, and reaches <c>AddAttribute</c>'s
    /// <see langword="bool"/> overload as written — which is the whole point of the overload: that
    /// overload is what omits the attribute when the value is <see langword="false"/> at render time.
    /// <c>Run</c> compiles the generated source, so this also pins that the emitted call binds.
    /// </summary>
    [Fact]
    public void RuntimeBooleanAttr_PassesTheBoolThroughToAddAttribute()
    {
        var code = Run("""Html.Input.Attr("disabled", _flag)""");
        Assert.Contains("""__builder.AddAttribute(1, "disabled", _flag);""", code);
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
        // Non-constant class values: what is pinned here is the concatenation expression the class channel
        // emits into one frame, and that expression exists only in the frame form.
        var code = Run("""Html.Div.Class(_a).Attr("class", _b)[Html.Span["x"]]""");
        Assert.Contains("__builder.AddAttribute(1, \"class\", (_a) + \" \" + (_b))", code);
        Assert.DoesNotContain("\"class\", _b)", code); // not a second class frame
    }

    /// <summary>
    /// The <see langword="bool"/> overload written on the class channel is BCF3023. Both counts are
    /// asserted because the count is what used to decide the meaning (#159): one class decoration reached
    /// <c>AddAttribute(int, string, bool)</c> and emptied the class list, two or more concatenated the
    /// <see langword="bool"/> into the joined value and rendered <c>class="a True"</c>.
    /// </summary>
    [Fact]
    public void BooleanValueOnClassChannel_ReportsBCF3023()
    {
        Assert.Contains(Diags("""Html.Div.Attr("class", true)["x"]"""), d => d.Id == "BCF3023");
        Assert.Contains(Diags("""Html.Div.Class("a").Attr("class", true)["x"]"""), d => d.Id == "BCF3023");
    }

    /// <summary>
    /// The rule is about the class channel, not about the <see langword="bool"/> overload:
    /// <c>.Attr("class", …)</c> with a string still folds. The other half — that the overload keeps
    /// working under every other name — needs no assertion here, because
    /// <see cref="ConstantTrueAttr_FoldsToAnEmptyAttributeValue"/> compiles that exact body through
    /// <c>Run</c>, which rejects any error diagnostic.
    /// </summary>
    [Fact]
    public void StringValueOnClassChannel_ReportsNothing()
    {
        Assert.DoesNotContain(Diags("""Html.Div.Attr("class", "a")["x"]"""), d => d.Id == "BCF3023");
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
        // the same dead duplicate as two bindings within a channel, in either decoration order.
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
