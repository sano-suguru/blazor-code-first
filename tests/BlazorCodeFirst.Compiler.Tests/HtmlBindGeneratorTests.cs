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
    public void Bind_OnAnOtherwiseConstantElement_IsNotFoldedIntoMarkup()
    {
        // Every other channel on this element is constant and the tag is foldable, so without the
        // binding the whole element would serialize to one AddMarkupContent frame. A fold here would
        // drop the binder frame and SetUpdatesAttributeName and leave a plain attribute behind, with
        // nothing failing to compile to say so.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Class("field").Type("text").Bind("value", "oninput", () => _name);
            """;

        var generated = GenerateBody(body);

        Assert.DoesNotContain("AddMarkupContent", generated);
        Assert.Contains("__builder.OpenElement(0, \"input\");", generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
    }
}
