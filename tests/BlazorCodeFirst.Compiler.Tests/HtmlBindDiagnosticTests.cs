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
    public void Bind_ComposableParameterItself_ReportsBcf3018()
    {
        // Expansion replaces the parameter with a local holding a copy of the caller's argument, so an
        // inverted setter would assign to that copy and the caller's own field would never see it. The
        // rule is the same one the iteration variable falls under, reached through the same arm.
        const string body = """
            private string _name = "";

            [Composable]
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
    public void Bind_TwiceOnOneElement_ReportsBcf3021()
    {
        const string body = """
            private string _a = "";
            private string _b = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _a).Bind("data-x", "onfocus", () => _b);
            """;

        AssertDiagnostic(body, "BCF3021");
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
    public void Bind_EventNameWithoutOnPrefix_ReportsBcf3019()
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
}
