namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlBindGeneratorTests
{
    /// <summary>
    /// Runs the generator over <paramref name="body"/> placed in a component, and returns the generated
    /// text. The output is compiled, not merely inspected: the binder is a call the generated file has no
    /// <c>using</c> for, so a spelling that reads correctly can still fail to bind.
    /// </summary>
    private static string GenerateBody(string body)
    {
        var source = $$"""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                {{body}}
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        Assert.DoesNotContain(
            result.Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
        return Assert.Single(result.GeneratedSources).SourceText.ToString();
    }

    private const string CreateBinder =
        "global::Microsoft.AspNetCore.Components.EventCallbackFactoryBinderExtensions.CreateBinder("
        + "global::Microsoft.AspNetCore.Components.EventCallback.Factory, this, ";

    [Fact]
    public void Bind_StringGetterOnly_InvertsGetterIntoSetter()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Type("text").Bind("value", "oninput", () => _name);
            """;

        var generated = GenerateBody(body);

        Assert.Contains("__builder.AddAttribute(2, \"value\", _name);", generated);
        Assert.Contains(
            "__builder.AddAttribute(3, \"oninput\", "
            + CreateBinder + "__value => _name = __value, _name));",
            generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
    }

    [Fact]
    public void Bind_BoolGetterOnly_InvertsGetterIntoSetter()
    {
        const string body = """
            private bool _agreed;
            protected override View Body =>
                Html.Input.Type("checkbox").Bind("checked", "onchange", () => _agreed);
            """;

        var generated = GenerateBody(body);

        Assert.Contains("__builder.AddAttribute(2, \"checked\", _agreed);", generated);
        Assert.Contains("__value => _agreed = __value, _agreed)", generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"checked\");", generated);
    }

    [Fact]
    public void Bind_MemberChainGetter_InvertsWholeChain()
    {
        const string body = """
            private sealed class FormModel { public string Name { get; set; } = ""; }
            private readonly FormModel _form = new();
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _form.Name);
            """;

        var generated = GenerateBody(body);

        Assert.Contains("__value => _form.Name = __value", generated);
    }

    [Fact]
    public void Bind_ExplicitSyncSetter_TransplantsAuthorLambdaCastToAction()
    {
        const string body = """
            private string Query { get; set; } = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => Query, v => Query = v.Trim());
            """;

        var generated = GenerateBody(body);

        Assert.Contains(
            CreateBinder + "(global::System.Action<global::System.String>)(v => Query = v.Trim()), Query)",
            generated);
    }

    [Fact]
    public void Bind_InsideComposable_SubstitutesBothTheValueAndTheBinder()
    {
        // Closes a gap Task 3's review found: ComposableExpander substitutes parameter holes into
        // BindTemplate.Value and BindTemplate.Binder, and nothing exercises that branch. The emitter
        // tests build ElementNode directly and never reach the expander. If either .Substitute call is
        // dropped, ToCode() throws "Expression template still contains unbound parameter holes" and only
        // a test shaped like this one sees it.
        //
        // The hole is a *member* of the composable's parameter, not the parameter itself. Expansion
        // replaces the parameter with a generated local holding a copy of the caller's argument, so an
        // inverted setter written over the parameter alone would assign to that copy and the caller's
        // field would never see it; BCF3018 rejects that shape, and this one writes through the copied
        // reference to the object the caller passed.
        const string body = """
            private sealed class FormModel { public string Name { get; set; } = ""; }
            private readonly FormModel _form = new();

            [Composable]
            private static View Field(FormModel model) =>
                Html.Input.Bind("value", "oninput", () => model.Name);

            protected override View Body => Html.Div[Field(_form)];
            """;

        var generated = GenerateBody(body);

        // The parameter hole is filled with the expansion local on both sides of the binding, and the
        // same local names both, which is what proves neither .Substitute was dropped.
        Assert.Contains("__builder.AddAttribute(2, \"value\", __bcf_arg_1_0.Name);", generated);
        Assert.Contains(
            "__value => __bcf_arg_1_0.Name = __value, __bcf_arg_1_0.Name)", generated);
    }

    [Fact]
    public void Bind_ExplicitAsyncSetter_WrapsInInferredBindSetter()
    {
        const string body = """
            private string _name = "";
            private System.Threading.Tasks.Task SetAsync(string v)
            {
                _name = v;
                return System.Threading.Tasks.Task.CompletedTask;
            }
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _name, SetAsync);
            """;

        var generated = GenerateBody(body);

        Assert.Contains(
            "global::Microsoft.AspNetCore.Components.CompilerServices.RuntimeHelpers"
            + ".CreateInferredBindSetter(callback: SetAsync, value: _name)",
            generated);
    }

    [Fact]
    public void Bind_StringGetterOnly_CarriesNoNullableMismatchDiagnostic()
    {
        // Regression: the framework's own CreateBinder(receiver, Action<string?> setter, string
        // existingValue, ...) overload annotates its setter nullable, so `__value => _name = __value`
        // triggers CS8601 wherever the assignment target's own declaration is nullable-enabled — hence
        // the explicit #nullable enable on the user source below, which GenerateBody's helper omits and
        // every other test in this file relies on that omission to stay silent on this exact defect.
        // AssertOutputCompiles alone would not have caught it either way: CS8601 is a warning, not an
        // error, until a project (any real one; this repository's own Directory.Build.props included)
        // escalates warnings to errors. Task 9's BindRenderingTests found this by building for real.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Type("text").Bind("value", "oninput", () => _name);
            """;

        var source = $$"""
            #nullable enable
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                {{body}}
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            d => d.Id is "CS8601" or "CS8620");
    }

    [Fact]
    public void Bind_ExplicitSyncSetter_CarriesNoNullableMismatchDiagnostic()
    {
        // Same regression as the getter-only case, on the (Action<string>)(<setter>) cast: the cast's
        // own type does not match CreateBinder's Action<string?> parameter, which is CS8620 rather than
        // CS8601 but the same underlying mismatch. Unlike CS8601 above, this one does not depend on the
        // user source's own nullable context — the mismatch is entirely between two casts inside the
        // generated file itself — but #nullable enable is kept here too so both tests model the same
        // (realistic, nullable-enabled) consuming project.
        const string body = """
            private string Query { get; set; } = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => Query, v => Query = v.Trim());
            """;

        var source = $$"""
            #nullable enable
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                {{body}}
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics(),
            d => d.Id is "CS8601" or "CS8620");
    }

    [Fact]
    public void Bind_OnAnElementWhoseOtherChannelsAreAllConstant_StillEmitsFrames()
    {
        // Every other channel here is a compile-time constant and 'input' is a foldable void tag, so
        // without the binding this whole element serializes to one AddMarkupContent frame. Markup can
        // express neither the binder nor SetUpdatesAttributeName, so a fold would leave a plain
        // value="" attribute behind and drop the binding, with nothing failing to compile to say so.
        //
        // Written against the frames rather than against the predicate that produces them: the element
        // path is the outcome that has to survive, whichever part of the fold decides it.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Class("field").Type("text").Bind("value", "oninput", () => _name);
            """;

        var generated = GenerateBody(body);

        Assert.DoesNotContain("AddMarkupContent", generated);
        Assert.Contains("__builder.OpenElement(0, \"input\");", generated);
        Assert.Contains("__builder.AddAttribute(1, \"class\", \"field\");", generated);
        Assert.Contains("__builder.AddAttribute(2, \"type\", \"text\");", generated);
        Assert.Contains("__builder.AddAttribute(3, \"value\", _name);", generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
        Assert.Contains("__builder.CloseElement();", generated);
    }

    [Fact]
    public void TwoBindings_OnOneElement_CompileAndEmitBothPairs()
    {
        const string body = """
            private string _live = "";
            private string _committed = "";
            protected override View Body =>
                Html.Input
                    .Bind("value", "oninput", () => _live)
                    .Bind("data-committed", "onchange", () => _committed);
            """;

        var generated = GenerateBody(body);

        Assert.Contains("__builder.AddAttribute(1, \"value\", _live);", generated);
        Assert.Contains("__builder.AddAttribute(3, \"data-committed\", _committed);", generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
        Assert.DoesNotContain("SetUpdatesAttributeName(\"data-committed\")", generated);
    }
}
