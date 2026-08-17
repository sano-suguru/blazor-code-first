namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlBindDiagnosticTests
{
    private static System.Collections.Immutable.ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> Diags(
        string body)
    {
        var source = $$"""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                {{body}}
            }
            """;

        return CompilationTestHost.RunGenerator(source).Diagnostics;
    }

    private static void AssertDiagnostic(string body, string id) =>
        Assert.Contains(Diags(body), d => d.Id == id);

    /// <summary>
    /// Asserts the shape translates with no error at all, not merely without one particular id: these
    /// cases exist to show that a rule stops where it is meant to, and a body that failed translation
    /// for an unrelated reason would satisfy a narrower assertion while proving nothing.
    /// </summary>
    private static void AssertNoDiagnostics(string body) =>
        Assert.DoesNotContain(
            Diags(body), d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

    [Fact]
    public void Bind_BlockBodiedGetter_ReportsBcf3017()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => { return _name; });
            """;

        AssertDiagnostic(body, "BCF3017");
    }

    [Fact]
    public void Bind_MethodGroupGetter_ReportsBcf3017()
    {
        const string body = """
            private string _name = "";
            private string GetName() => _name;
            protected override View Body =>
                Html.Input.Bind("value", "oninput", GetName);
            """;

        AssertDiagnostic(body, "BCF3017");
    }

    [Fact]
    public void Bind_MethodCallGetter_ReportsBcf3018()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _name.ToUpperInvariant());
            """;

        AssertDiagnostic(body, "BCF3018");
    }

    [Fact]
    public void Bind_GetOnlyPropertyGetter_ReportsBcf3018()
    {
        const string body = """
            private string Name => "x";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => Name);
            """;

        AssertDiagnostic(body, "BCF3018");
    }

    [Fact]
    public void Bind_InitOnlyPropertyGetter_ReportsBcf3018()
    {
        // A setter exists and the derived one still cannot call it: C# admits an init accessor only in an
        // object initializer, a constructor, or another init accessor, and the derived setter is the
        // lambda `__value => Name = __value`. Unchecked, this reached the author as a CS8852 inside
        // C.g.cs naming a property they did write (#391).
        const string body = """
            public string Name { get; init; } = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => Name);
            """;

        AssertDiagnostic(body, "BCF3018");
    }

    [Fact]
    public void Bind_SetterInaccessibleFromTheComponent_ReportsBcf3018()
    {
        // The setter is private to Model and RenderView is emitted into C, so the derived assignment is
        // CS0272. Enclosing a type grants no access to its private members, which is what makes this
        // differ from the private setter declared on the component itself.
        const string body = """
            private sealed class Model { public string Name { get; private set; } = ""; }
            private readonly Model _model = new();
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _model.Name);
            """;

        AssertDiagnostic(body, "BCF3018");
    }

    [Fact]
    public void Bind_IndexerSetterInaccessibleFromTheComponent_ReportsBcf3018()
    {
        // The element-access arm asks the same question of an indexer's setter, so it rejects what the
        // member arm rejects and keeps accepting _dict["k"], whose setter is public.
        const string body = """
            private sealed class Bag { public string this[int i] { get => ""; private set { } } }
            private readonly Bag _bag = new();
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _bag[0]);
            """;

        AssertDiagnostic(body, "BCF3018");
    }

    [Fact]
    public void Bind_ViewPartDerivingAnInaccessibleSetter_ReportsBcf1002()
    {
        // A view part body is written into call sites the analysis has not seen, so the setter's
        // accessibility is recorded as a requirement on the site rather than decided at the binding, and
        // expansion refuses a site that cannot reach it. The same route every non-public member a view
        // part names outright already takes; this one is not named anywhere in the authored syntax.
        const string body = """
            private sealed class Model { public string Name { get; private set; } = ""; }
            private readonly Model _model = new();

            [ViewPart]
            private static View Field(Model model) =>
                Html.Input.Bind("value", "oninput", () => model.Name);

            protected override View Body => Html.Div[Field(_model)];
            """;

        AssertDiagnostic(body, "BCF1002");
    }

    [Fact]
    public void Bind_IterationVariableItself_ReportsBcf3018()
    {
        const string body = """
            private readonly System.Collections.Generic.List<string> _items = new();
            protected override View Body =>
                Html.Div[Html.ForEach(_items, key: s => s,
                    content: s => Html.Input.Bind("value", "oninput", () => s))];
            """;

        AssertDiagnostic(body, "BCF3018");
    }

    [Fact]
    public void Bind_ViewPartParameterItself_ReportsBcf3018()
    {
        // Expansion replaces the parameter with a local holding a copy of the caller's argument, so an
        // inverted setter would assign to that copy and the caller's own field would never see it. The
        // rule is the same one the iteration variable falls under, reached through the same arm.
        const string body = """
            private string _name = "";

            [ViewPart]
            private static View Field(string current) =>
                Html.Input.Bind("value", "oninput", () => current);

            protected override View Body => Html.Div[Field(_name)];
            """;

        AssertDiagnostic(body, "BCF3018");
    }

    [Fact]
    public void Bind_MemberOfIterationVariable_IsAccepted()
    {
        const string body = """
            private sealed class Row { public string Title { get; set; } = ""; public int Id { get; set; } }
            private readonly System.Collections.Generic.List<Row> _rows = new();
            protected override View Body =>
                Html.Div[Html.ForEach(_rows, key: r => r.Id,
                    content: r => Html.Input.Bind("value", "oninput", () => r.Title))];
            """;

        AssertNoDiagnostics(body);
    }

    [Fact]
    public void Bind_ExplicitSetterWithNonAssignableGetter_IsAccepted()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _name.Trim(), v => _name = v);
            """;

        AssertNoDiagnostics(body);
    }

    [Fact]
    public void Bind_TwiceOnOneElement_ReportsNothing()
    {
        // BCF3021 rejected this until #162. SetUpdatesAttributeName records the resynchronized name on
        // the event's own frame, so two bindings never collided; nothing broke, so nothing is reported.
        const string body = """
            private string _a = "";
            private string _b = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _a).Bind("data-x", "onfocus", () => _b);
            """;

        AssertNoDiagnostics(body);
    }

    [Fact]
    public void Bind_TwiceOnOneAttributeName_ReportsBcf3010()
    {
        const string body = """
            private string _a = "";
            private string _b = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _a).Bind("value", "onchange", () => _b);
            """;

        AssertDiagnostic(body, "BCF3010");
    }

    [Fact]
    public void Bind_TwiceOnOneEventName_ReportsBcf3010()
    {
        // The event channel half. Two bindings sharing an event name emit two frames under one name, of
        // which the second wins, so the first binding's write-back is dead — the same duplicate BCF3010
        // reports between an .Attr and an .On.
        const string body = """
            private string _a = "";
            private string _b = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _a).Bind("data-x", "oninput", () => _b);
            """;

        AssertDiagnostic(body, "BCF3010");
    }

    [Fact]
    public void Bind_BesideAttrOnSameName_ReportsBcf3010()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Attr("value", "x").Bind("value", "oninput", () => _name);
            """;

        AssertDiagnostic(body, "BCF3010");
    }

    [Fact]
    public void Bind_BeforeAttrOnSameName_ReportsBcf3010()
    {
        // The reverse of the case above. A duplicate that depended on which decoration was written first
        // would be worse than no check, so HasBinding reads the bind channel too.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _name).Attr("value", "x");
            """;

        AssertDiagnostic(body, "BCF3010");
    }

    [Fact]
    public void Bind_BesideOnForSameEvent_ReportsBcf3010()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.On("oninput", () => { }).Bind("value", "oninput", () => _name);
            """;

        AssertDiagnostic(body, "BCF3010");
    }

    [Fact]
    public void Bind_BeforeOnForSameEvent_ReportsBcf3010()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _name).On("oninput", () => { });
            """;

        AssertDiagnostic(body, "BCF3010");
    }

    [Fact]
    public void Bind_OnClassBesideClassDecoration_ReportsBcf3024()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Div.Class("card").Bind("class", "oninput", () => _name)["x"];
            """;

        AssertDiagnostic(body, "BCF3024");
    }

    [Fact]
    public void Bind_OnClassBeforeClassDecoration_ReportsBcf3024()
    {
        // The reverse order, for the reason Bind_BeforeAttrOnSameName_ReportsBcf3010 gives: the element
        // carries the same two frames either way, so the answer must not depend on which was written
        // first. The channel's own arm asks the question here, since a .Class never reaches ClassifyBind.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Div.Bind("class", "oninput", () => _name).Class("card")["x"];
            """;

        AssertDiagnostic(body, "BCF3024");
    }

    [Fact]
    public void Bind_OnClassBesideAttrClass_ReportsBcf3024()
    {
        // The channel's other spelling. .Attr("class", …) folds into the same array as .Class, so it
        // reaches the same check, and this is the shape that would otherwise look like the BCF3010 it
        // is not: two decorations, one name, and no report.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Div.Attr("class", "card").Bind("class", "oninput", () => _name)["x"];
            """;

        AssertDiagnostic(body, "BCF3024");
    }

    [Fact]
    public void Bind_OnClassBeforeAttrClass_ReportsBcf3024()
    {
        // The fourth corner of the pair of spellings times the pair of orders, and the one that reaches
        // the channel's report through .Attr rather than .Class. Both fold, so both ask the channel the
        // same question, and this is the case that would notice if the fold arm's answer came from
        // somewhere that only knew about .Class.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Div.Bind("class", "oninput", () => _name).Attr("class", "card")["x"];
            """;

        AssertDiagnostic(body, "BCF3024");
    }

    [Fact]
    public void Bind_OnClassBeforeBoolAttrClass_ReportsBcf3023NotBcf3024()
    {
        // Both of the channel's rules are broken at once, and the value's is the one reported: what the
        // author has to rewrite is the bool, and the collision is not worth naming until they have. The
        // order is the channel's, not this arm's, so it holds for either folding spelling.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Div.Bind("class", "oninput", () => _name).Attr("class", true)["x"];
            """;

        AssertDiagnostic(body, "BCF3023");
        Assert.DoesNotContain(Diags(body), d => d.Id == "BCF3024");
    }

    [Fact]
    public void Bind_OnClassAlone_ReportsNothing()
    {
        // Where the rule stops. One binding and no other class decoration emits one class frame, which is
        // what the author wrote; the duplicate is what BCF3024 is about, not the name.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Div.Bind("class", "oninput", () => _name)["x"];
            """;

        AssertNoDiagnostics(body);
    }

    [Fact]
    public void Bind_NonConstantAttributeName_ReportsBcf3011()
    {
        const string body = """
            private string _name = "";
            private string _attr = "value";
            protected override View Body =>
                Html.Input.Bind(_attr, "oninput", () => _name);
            """;

        AssertDiagnostic(body, "BCF3011");
    }

    [Fact]
    public void Bind_EventNameWithoutOnPrefix_OrSwapped_ReportsBcf3019()
    {
        // Also what catches the attribute and event names being written the wrong way round, since both
        // are adjacent string arguments and the swap compiles.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Bind("oninput", "value", () => _name);
            """;

        AssertDiagnostic(body, "BCF3019");
    }

    [Fact]
    public void On_EventNameWithoutOnPrefix_ReportsBcf3019()
    {
        const string body = """
            protected override View Body =>
                Html.Button.On("click", () => { });
            """;

        AssertDiagnostic(body, "BCF3019");
    }

    [Fact]
    public void On_EventNameWithOnPrefix_IsAccepted()
    {
        const string body = """
            protected override View Body =>
                Html.Button.On("onclick", () => { });
            """;

        AssertNoDiagnostics(body);
    }

    /// <summary>
    /// The framework declares format-taking converters for its four date/time types and their nullable
    /// forms only. A format on anything else makes the generated file's own call fail to bind, and
    /// appendix A.0 is why that cannot be left to the C# error: it is raised inside generated code, where
    /// the author does not read it.
    /// </summary>
    [Fact]
    public void Bind_FormatOnInteger_ReportsBcf3031()
    {
        const string body = """
            private int _age;
            protected override View Body =>
                Html.Input.Bind(
                    "value", "oninput", () => _age, "D4",
                    System.Globalization.CultureInfo.InvariantCulture);
            """;

        var diagnostic = Assert.Single(Diags(body), d => d.Id == "BCF3031");
        Assert.Contains("int", diagnostic.GetMessage());
    }

    [Fact]
    public void Bind_FormatOnString_ReportsBcf3031()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Bind(
                    "value", "oninput", () => _name, "X",
                    System.Globalization.CultureInfo.InvariantCulture);
            """;

        AssertDiagnostic(body, "BCF3031");
    }

    [Fact]
    public void Bind_FormatOnDateTime_IsAccepted()
    {
        const string body = """
            private System.DateTime _due;
            protected override View Body =>
                Html.Input.Bind(
                    "value", "oninput", () => _due, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture);
            """;

        AssertNoDiagnostics(body);
    }

    [Fact]
    public void Bind_FormatOnNullableDateOnly_IsAccepted()
    {
        const string body = """
            private System.DateOnly? _due;
            protected override View Body =>
                Html.Input.Bind(
                    "value", "oninput", () => _due, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture);
            """;

        AssertNoDiagnostics(body);
    }

    /// <summary>
    /// The rule is about a format, not about a culture: every type this surface binds may carry one.
    /// </summary>
    [Fact]
    public void Bind_CultureWithoutFormatOnInteger_IsAccepted()
    {
        const string body = """
            private int _age;
            protected override View Body =>
                Html.Input.Bind(
                    "value", "oninput", () => _age,
                    System.Globalization.CultureInfo.InvariantCulture);
            """;

        AssertNoDiagnostics(body);
    }
}
