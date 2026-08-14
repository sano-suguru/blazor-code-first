using System.Globalization;

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
                private string? _maybe => null;
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
        // Non-constant class values: what is pinned here is the join expression the class channel emits
        // into one frame, and that expression exists only in the frame form.
        var code = Run("""Html.Div.Class(_a).Attr("class", _b)[Html.Span["x"]]""");
        Assert.Contains("__builder.AddAttribute(1, \"class\", __BlazorCodeFirstJoinClasses((_a), (_b)))", code);
        Assert.DoesNotContain("\"class\", _b)", code); // not a second class frame
    }

    /// <summary>
    /// The <see langword="bool"/> overload written on the class channel is BCF3023. Both counts are
    /// asserted because the count is what used to decide the meaning (#159): one class decoration reached
    /// <c>AddAttribute(int, string, bool)</c> and emptied the class list, two or more concatenated the
    /// <see langword="bool"/> into the joined value and rendered <c>class="a True"</c>.
    /// </summary>
    /// <remarks>
    /// The message is asserted to name the type it refused rather than to assume it. The rule reads on the
    /// channel's requirement — anything but a <see cref="string"/> — and <see langword="bool"/> is only the
    /// overload that makes it reachable today (#223). An <c>.Attr</c> overload added by #171 or #178 trips
    /// the same gate without the analyzer being touched, and would otherwise be reported as a
    /// <see langword="bool"/>.
    /// </remarks>
    [Fact]
    public void BooleanValueOnClassChannel_ReportsBCF3023_NamingTheValueType()
    {
        var single = Assert.Single(Diags("""Html.Div.Attr("class", true)["x"]"""), d => d.Id == "BCF3023");
        Assert.Contains(
            "a value of type 'bool'",
            single.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

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

    /// <summary>
    /// A conditional attribute value, which is what the nullable widening exists for (#171). Measured in
    /// Chromium: <c>AddAttribute</c> appends no frame for a null string, static SSR and prerender write no
    /// attribute, and a re-render that turns the value null emits <c>RemoveAttribute</c>. The empty string
    /// is a different value at every one of those stages, so the two spellings stay distinguishable here.
    /// </summary>
    [Fact]
    public void NullableAttributeValue_IsPassedThroughToAddAttribute()
    {
        var code = Run("""Html.Span.Attr("title", _flag ? _a : null)["x"]""");
        Assert.Contains("""__builder.AddAttribute(1, "title", _flag ? _a : null);""", code);
    }

    /// <summary>
    /// The widening reaches the curated shortcuts too. Validity is not this surface's question
    /// (<c>DESIGN.md</c> §4.1): an <c>img</c> with no <c>src</c> is written as the author wrote it.
    /// </summary>
    [Fact]
    public void NullableShortcutValue_IsPassedThroughToAddAttribute()
    {
        var code = Run("""Html.Img.Src(_maybe)""");
        Assert.Contains("""__builder.AddAttribute(1, "src", _maybe);""", code);
    }

    /// <summary>
    /// The class channel takes a nullable term through a join that drops it (#236). A term that is null
    /// at render time contributes neither text nor a separator, so the value reaching
    /// <c>AddAttribute</c> is what the surviving terms spell and nothing more, and a join whose every
    /// term is null is a null — which omits the attribute, exactly as a lone null does.
    /// </summary>
    [Fact]
    public void NullableClassTerm_JoinsThroughTheHelperThatDropsIt()
    {
        var code = Run("""Html.Div.Class("card").Class(_maybe)[Html.Span["x"]]""");
        Assert.Contains(
            """__builder.AddAttribute(1, "class", __BlazorCodeFirstJoinClasses(("card"), (_maybe)));""",
            code);
        Assert.Contains(
            """private static string? __BlazorCodeFirstJoinClasses(string? a0, string? a1) => a0 is null ? a1 : a1 is null ? a0 : string.Concat(a0, " ", a1);""",
            code);
    }

    /// <summary>
    /// Every decoration that takes one value, written with a null-bearing argument and a bare
    /// <see langword="null"/>, asserted to warn about nothing (#171). This is the assertion the widening is
    /// answerable to: the three above pin what the generator emits, and the generator emitted that before
    /// the parameters were widened — a <c>string?</c> reaching a <see langword="string"/> parameter is
    /// CS8604 and a <see langword="null"/> literal is CS8625, both warnings, so nothing that asks only for
    /// errors can tell the two surfaces apart. Under this repository's <c>TreatWarningsAsErrors</c> those
    /// warnings are what an author actually hits.
    /// </summary>
    [Fact]
    public void EveryValueDecoration_AcceptsANullableValueWithoutWarning()
    {
        // The nullable context is opted into here rather than in CompilationTestHost, because the test
        // compilation leaves it off by default and this is the one test that is about nullability. A real
        // project has it on — this repository's own Directory.Build.props does — so this is the setting an
        // author writing the spellings below actually compiles under.
        var result = CompilationTestHost.RunGenerator("""
            #nullable enable
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                private string? _maybe => null;

                protected override View Body =>
                    Html.Div
                        .Class(_maybe)
                        .Href(_maybe)
                        .Src(_maybe)
                        .Alt(_maybe)
                        .Id(_maybe)
                        .Type(_maybe)
                        .Title(_maybe)
                        .Role(_maybe)
                        .Attr("data-a", _maybe)
                        .Attr("data-b", null)[Html.Span[_maybe ?? "x"]];
            }
            """);

        CompilationTestHost.AssertNoNullableWarnings(result);
        CompilationTestHost.AssertOutputCompiles(result);
        CompilationTestHost.AssertNoDiagnostics(result);
    }

    /// <summary>
    /// The bare spelling of a valueless attribute, which is how HTML writes it (#178). A default on the
    /// existing <see langword="bool"/> overload rather than a one-argument sibling, so the two spellings
    /// travel one path and cannot emit different frames — the acceptance criterion becomes structural
    /// rather than something to test. Asserted anyway, because the structure is only as good as the value
    /// the analyzer synthesizes for the omitted argument, and comparing whole generated files is what
    /// would catch a difference anywhere in it.
    /// </summary>
    [Fact]
    public void BareAttr_EmitsTheSameSourceAsAnExplicitTrue()
    {
        var bare = Run("""Html.Button.Attr("disabled")["Save"]""");
        var explicitTrue = Run("""Html.Button.Attr("disabled", true)["Save"]""");

        Assert.Equal(explicitTrue, bare);
    }

    /// <summary>
    /// The one name the bare form cannot take. <c>class</c> folds into a channel that joins its decorations
    /// as text, and a presence has no text — the same rule BCF3023 already applies to the
    /// <see langword="bool"/> overload, reached here without the author writing a <see langword="bool"/>
    /// anywhere. The location is the decoration's name, there being no value argument to point at.
    /// </summary>
    /// <remarks>
    /// The message is asserted too, because this is the one shape whose refused value the author did not
    /// write: the <see langword="bool"/> it carries is synthesized for a presence (#178), so a message
    /// naming that type would describe a step of the compiler's rather than the source (#223).
    /// </remarks>
    [Fact]
    public void BareAttrOnClassChannel_ReportsBCF3023AtTheDecorationName()
    {
        // The source is spelled out here rather than taken from Diags, because the span has to be read back
        // against it: DiagnosticInfo reconstructs its location from a file path and a span and holds no
        // SyntaxTree, which is how the incremental model avoids rooting one, so Location.SourceTree is null.
        const string source = """
            using BlazorCodeFirst;
            public partial class C : BodyComponentBase
            {
                protected override View Body => Html.Div.Attr("class")["x"];
            }
            """;

        var diagnostic = Assert.Single(
            CompilationTestHost.RunGenerator(source).Diagnostics,
            d => d.Id == "BCF3023");

        var span = diagnostic.Location.SourceSpan;
        Assert.Equal("Attr", source.Substring(span.Start, span.Length));

        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("the bare .Attr(name) spelling", message, StringComparison.Ordinal);
        Assert.DoesNotContain("bool", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bare form leaves the other channels alone. No event overload has an optional handler, so a
    /// missing one is a call this compiler was not written against and must keep reaching BCF1003 rather
    /// than picking up a synthesized <see langword="true"/> — which would emit
    /// <c>AddAttribute(seq, "onclick", true)</c>, an attribute whose handler never fires. The synthesis
    /// lives at the attribute call site for exactly this reason, not in the name resolver both channels
    /// share.
    /// </summary>
    [Fact]
    public void EventDecorationWithNoHandler_FallsToBCF1003WithoutSynthesizingAValue()
    {
        var diagnostics = Diags("""Html.Div.On("onclick")["x"]""");

        Assert.Contains(diagnostics, d => d.Id == "BCF1003");
        Assert.DoesNotContain(diagnostics, d => d.Id == "BCF3023");
    }

    /// <summary>
    /// A constant null attribute value on an element the fold cannot take. Measured (#234):
    /// <c>AddAttribute</c>'s value position is overloaded, so a bare <see langword="null"/> is CS0121
    /// between the <see langword="string"/> and <c>MulticastDelegate</c> overloads. The fold hides that —
    /// a foldable element writes no attribute at all — so the element here carries an event to keep it on
    /// the element path, which is the only path that reaches the emitted call.
    /// </summary>
    [Fact]
    public void ConstantNullAttributeValue_OnAnUnfoldableElement_EmitsCodeThatCompiles()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                private int _n;
                protected override View Body =>
                    Html.Span.Attr("title", null!).OnClick(() => _n++)["a"];
            }
            """);

        // The compile comes first: it is the defect, and the cast's exact spelling is how this fixes it.
        CompilationTestHost.AssertOutputCompiles(result);
        Assert.Contains(
            """__builder.AddAttribute(1, "title", (global::System.String?)(null!));""",
            Assert.Single(result.GeneratedSources).SourceText.ToString());
    }

    /// <summary>
    /// A constant <see langword="null"/> is dropped from the class channel, so a lone one leaves the
    /// channel with nothing to join and it spells its own absent value (#240) — into the same overloaded
    /// position, needing the same cast (#234). The author's <c>null!</c> does not survive that, and the
    /// defect never was the spelling. Two or more decorations concatenate, and a concatenation is a
    /// <see langword="string"/> whatever its terms are, so only this case is at risk.
    /// </summary>
    [Fact]
    public void ConstantNullClassValue_OnAnUnfoldableElement_EmitsCodeThatCompiles()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                private int _n;
                protected override View Body =>
                    Html.Span.Class(null!).OnClick(() => _n++)["b"];
            }
            """);

        // The compile comes first: it is the defect, and the cast's exact spelling is how this fixes it.
        CompilationTestHost.AssertOutputCompiles(result);
        Assert.Contains(
            """__builder.AddAttribute(1, "class", (global::System.String?)(null));""",
            Assert.Single(result.GeneratedSources).SourceText.ToString());
    }
}
