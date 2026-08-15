namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlDecorationGeneratorTests
{
    private const string ButtonOnClickSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private int _n = 0;
            protected override View Body => Html.Button.OnClick(() => _n++)["OK"];
        }
        """;

    // Non-constant class, so the div is not folded away: what this test checks is that the decoration lands
    // on its own attribute frame at seq+1.
    private const string ClassOnDivSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private string _cls => "panel";
            protected override View Body => Html.Div.Class(_cls)[Html.Span["x"]];
        }
        """;

    // The generated RenderView carries no using directives, so a transplanted lambda parameter annotation
    // has to be rewritten to a global::-qualified name. That is the assumption typed event arguments rest
    // on, and AssertOutputCompiles is what proves it: the generated file is compiled without the using
    // that this input source has.
    private const string TypedOnInputSource = """
        using BlazorCodeFirst;
        using Microsoft.AspNetCore.Components;

        public partial class C : BodyComponentBase
        {
            private object? _seen;
            protected override View Body =>
                Html.Input.On("oninput", (ChangeEventArgs e) => _seen = e.Value);
        }
        """;

    private const string TypedAsyncOnInputSource = """
        using BlazorCodeFirst;
        using Microsoft.AspNetCore.Components;

        public partial class C : BodyComponentBase
        {
            private System.Threading.Tasks.Task SaveAsync(object? value) =>
                System.Threading.Tasks.Task.CompletedTask;

            protected override View Body =>
                Html.Input.On("oninput", (ChangeEventArgs e) => SaveAsync(e.Value));
        }
        """;

    [Fact]
    public void Button_WithOnClick_EmitsOnclickAttributeThenContent()
    {
        var result = CompilationTestHost.RunGenerator(ButtonOnClickSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"button\")", generated);
        Assert.Contains(
            "__builder.AddAttribute(1, \"onclick\", global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => _n++))",
            generated);
        Assert.Contains("__builder.AddContent(2, \"OK\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Class_OnDiv_EmitsClassAttributeAtSeqPlusOne()
    {
        var result = CompilationTestHost.RunGenerator(ClassOnDivSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.AddAttribute(1, \"class\", _cls)", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void On_WithTypedActionHandler_QualifiesTheLambdaParameterType()
    {
        var result = CompilationTestHost.RunGenerator(TypedOnInputSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"input\")", generated);
        Assert.Contains(
            "__builder.AddAttribute(1, \"oninput\", " +
            "global::Microsoft.AspNetCore.Components.EventCallback.Factory" +
            ".Create<global::Microsoft.AspNetCore.Components.ChangeEventArgs>(this, " +
            "(global::Microsoft.AspNetCore.Components.ChangeEventArgs e) => _seen = e.Value))",
            generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void On_WithTypedAsyncHandler_QualifiesTheLambdaParameterType()
    {
        var result = CompilationTestHost.RunGenerator(TypedAsyncOnInputSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains(
            "__builder.AddAttribute(1, \"oninput\", " +
            "global::Microsoft.AspNetCore.Components.EventCallback.Factory" +
            ".Create<global::Microsoft.AspNetCore.Components.ChangeEventArgs>(this, " +
            "(global::Microsoft.AspNetCore.Components.ChangeEventArgs e) => SaveAsync(e.Value)))",
            generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// Generates a component whose <c>Body</c> is <paramref name="body"/>, and returns the generated text.
    /// </summary>
    /// <remarks>
    /// It compiles the generated output, which is what the three tests below it measure: every handler
    /// spelling <c>.On&lt;TArgs&gt;</c> admits has to bind in a file that carries no <c>using</c> directives
    /// and no parameter type to infer from (#371).
    /// </remarks>
    private static string GenerateWheel(string body)
    {
        var result = CompilationTestHost.RunGenerator($$"""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using Microsoft.AspNetCore.Components.Web;

            public partial class C : BodyComponentBase
            {
                private bool _locked;
                private ElementReference _el;
                private void Zoom(WheelEventArgs e) { }
                private System.Threading.Tasks.Task ZoomAsync(WheelEventArgs e) =>
                    System.Threading.Tasks.Task.CompletedTask;
                private void Click() { }
                protected override View Body => {{body}};
            }
            """);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        CompilationTestHost.AssertOutputCompiles(result);
        return generated;
    }

    /// <summary>
    /// The two spellings that used to reach generated code and fail there: a method group, and a lambda
    /// whose parameter type is left to inference (#371).
    /// </summary>
    /// <remarks>
    /// The assertion on the emitted text says what fixes them, but the check that matters is
    /// <c>GenerateWheel</c>'s own <c>AssertOutputCompiles</c>: both bound at the call site before this
    /// change too, and the defect was a CS1503 in the generated file rather than anything the author could
    /// see.
    /// </remarks>
    [Theory]
    [InlineData("Zoom")]
    [InlineData("e => Zoom(e)")]
    [InlineData("ZoomAsync")]
    [InlineData("async e => await ZoomAsync(e)")]
    public void On_WithATypeArgument_NamesItOnCreate(string handler)
    {
        var generated = GenerateWheel($$"""Html.Div.On<WheelEventArgs>("onwheel", {{handler}})""");

        Assert.Contains(
            "__builder.AddAttribute(1, \"onwheel\", " +
            "global::Microsoft.AspNetCore.Components.EventCallback.Factory" +
            ".Create<global::Microsoft.AspNetCore.Components.Web.WheelEventArgs>(this, ",
            generated);
    }

    /// <summary>
    /// The overloads that carry no type argument keep the unqualified call. There is nothing to name: the
    /// handler takes no argument, and the event's own name is a string this surface does not infer from.
    /// </summary>
    [Fact]
    public void On_WithoutATypeArgument_LeavesCreateUnqualified()
    {
        var generated = GenerateWheel("""Html.Div.On("onclick", () => Click())""");

        Assert.Contains(
            "__builder.AddAttribute(1, \"onclick\", " +
            "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => Click()))",
            generated);
    }

    [Fact]
    public void PreventDefault_AfterAnEvent_EmitsTheInternalAttribute()
    {
        var generated = GenerateWheel("""Html.Div.On<WheelEventArgs>("onwheel", (WheelEventArgs e) => Zoom(e)).PreventDefault()""");

        Assert.Contains("__builder.AddAttribute(1, \"onwheel\", ", generated);
        Assert.Contains("__builder.AddAttribute(2, \"__internal_preventDefault_onwheel\", true)", generated);
    }

    [Fact]
    public void NoModifier_EmitsNoInternalAttribute()
    {
        var generated = GenerateWheel("""Html.Div.On<WheelEventArgs>("onwheel", (WheelEventArgs e) => Zoom(e))""");

        Assert.DoesNotContain("__internal_", generated);
    }

    /// <summary>
    /// A written <c>false</c> emits its call and takes a sequence number with it, which the number of the
    /// event after it is what proves.
    /// </summary>
    /// <remarks>
    /// The second decoration is an event rather than an attribute on purpose. The emitter writes the whole
    /// attribute channel before the whole event channel, so an attribute written later in the chain still
    /// lands ahead of the event this modifier belongs to and would say nothing about what the modifier
    /// consumed.
    /// </remarks>
    [Fact]
    public void PreventDefaultFalse_EmitsTheCallAndConsumesItsSequenceNumber()
    {
        var generated = GenerateWheel(
            """
            Html.Div.On<WheelEventArgs>("onwheel", (WheelEventArgs e) => Zoom(e))
                    .PreventDefault(false)
                    .On("onclick", () => Click())
            """);

        Assert.Contains("__builder.AddAttribute(1, \"onwheel\", ", generated);
        Assert.Contains("__builder.AddAttribute(2, \"__internal_preventDefault_onwheel\", false)", generated);
        Assert.Contains("__builder.AddAttribute(3, \"onclick\", ", generated);
    }

    [Fact]
    public void PreventDefault_RuntimeExpression_EmitsTheExpression()
    {
        var generated = GenerateWheel(
            """Html.Div.On<WheelEventArgs>("onwheel", (WheelEventArgs e) => Zoom(e)).PreventDefault(_locked)""");

        Assert.Contains("__builder.AddAttribute(2, \"__internal_preventDefault_onwheel\", _locked)", generated);
    }

    /// <summary>
    /// Emission order is the emitter's, not the chain's, and both modifiers precede the reference capture.
    /// </summary>
    /// <remarks>
    /// The source below writes <c>.Ref</c> first and <c>.StopPropagation</c> before <c>.PreventDefault</c>,
    /// so a generator that echoed the chain would fail both assertions. The capture ordering is not a
    /// preference: measured on 10.0.10, an attribute added after AddElementReferenceCapture throws
    /// InvalidOperationException, and both modifiers are attribute frames.
    /// </remarks>
    [Fact]
    public void BothModifiers_EmitPreventDefaultThenStopPropagationThenRef()
    {
        var generated = GenerateWheel(
            """
            Html.Div.Ref(r => _el = r)
                    .On<WheelEventArgs>("onwheel", (WheelEventArgs e) => Zoom(e)).StopPropagation().PreventDefault()
            """);

        var preventDefault = generated.IndexOf("__internal_preventDefault_onwheel", System.StringComparison.Ordinal);
        var stopPropagation = generated.IndexOf("__internal_stopPropagation_onwheel", System.StringComparison.Ordinal);
        var capture = generated.IndexOf("AddElementReferenceCapture", System.StringComparison.Ordinal);

        Assert.True(preventDefault >= 0 && stopPropagation >= 0 && capture >= 0);
        Assert.True(preventDefault < stopPropagation, "preventDefault is emitted before stopPropagation");
        Assert.True(stopPropagation < capture, "both modifiers precede the reference capture");
    }

    [Fact]
    public void ModifierAfterASecondEvent_AttachesToThatEvent()
    {
        var generated = GenerateWheel(
            """
            Html.Div.On("onclick", () => Click())
                    .On<WheelEventArgs>("onwheel", (WheelEventArgs e) => Zoom(e)).PreventDefault()
            """);

        Assert.Contains("__internal_preventDefault_onwheel", generated);
        Assert.DoesNotContain("__internal_preventDefault_onclick", generated);
    }
}
