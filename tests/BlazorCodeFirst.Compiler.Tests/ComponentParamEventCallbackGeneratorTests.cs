using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The component surface's EventCallback-aware <c>.Param</c> overloads (#492): the generator wraps the
/// author's handler in <c>EventCallback.Factory.Create</c> the way <c>.On</c>'s handler and <c>.Bind</c>'s
/// derived <c>{name}Changed</c> already are, rather than casting the value through verbatim the way the
/// generic <c>.Param</c> does.
/// </summary>
public sealed class ComponentParamEventCallbackGeneratorTests
{
    private const string Probe = """
        public sealed class Probe : Microsoft.AspNetCore.Components.ComponentBase
        {
            [Microsoft.AspNetCore.Components.Parameter]
            public Microsoft.AspNetCore.Components.EventCallback OnClose { get; set; }
            [Microsoft.AspNetCore.Components.Parameter]
            public Microsoft.AspNetCore.Components.EventCallback<string> OnPicked { get; set; }
        }
        """;

    private static string GenerateBodyWith(string body)
    {
        var source = $$"""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                {{body}}
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Probe.cs", Probe), ("Host.cs", source));
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
        return result.GeneratedSources.Single().SourceText.ToString();
    }

    // The six EventCallback-aware .Param overloads, by handler shape: non-generic Action/Func<Task> for
    // OnClose (EventCallback), generic Action<TArg>/Func<TArg, Task> for OnPicked (EventCallback<string>),
    // and OnPicked's two argument-ignoring overloads -- the Razor OnValidSubmit="HandleCreate" shape,
    // where EventCallbackFactory.Create<T> itself offers a plain Action/Func<Task> beside Action<T>, and
    // TArg is inferred from the selector alone.
    [Theory]
    [InlineData(
        "private void HandleClose() { }",
        "Html.Component<Probe>().Param(c => c.OnClose, HandleClose)",
        "__builder.AddComponentParameter(1, \"OnClose\", "
            + "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, "
            + "(global::System.Action)(HandleClose)));")]
    [InlineData(
        "private System.Threading.Tasks.Task HandleCloseAsync() => System.Threading.Tasks.Task.CompletedTask;",
        "Html.Component<Probe>().Param(c => c.OnClose, HandleCloseAsync)",
        "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, "
            + "(global::System.Func<global::System.Threading.Tasks.Task>)(HandleCloseAsync)));")]
    [InlineData(
        "private void HandlePicked(string value) { }",
        "Html.Component<Probe>().Param(c => c.OnPicked, HandlePicked)",
        "__builder.AddComponentParameter(1, \"OnPicked\", "
            + "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<global::System.String>(this, "
            + "(global::System.Action<global::System.String>)(HandlePicked)));")]
    [InlineData(
        "private System.Threading.Tasks.Task HandlePickedAsync(string value) => "
            + "System.Threading.Tasks.Task.CompletedTask;",
        "Html.Component<Probe>().Param(c => c.OnPicked, HandlePickedAsync)",
        "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<global::System.String>(this, "
            + "(global::System.Func<global::System.String, global::System.Threading.Tasks.Task>)"
            + "(HandlePickedAsync)));")]
    [InlineData(
        "private void HandleSubmit() { }",
        "Html.Component<Probe>().Param(c => c.OnPicked, HandleSubmit)",
        "__builder.AddComponentParameter(1, \"OnPicked\", "
            + "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<global::System.String>(this, "
            + "(global::System.Action)(HandleSubmit)));")]
    [InlineData(
        "private System.Threading.Tasks.Task HandleSubmitAsync() => System.Threading.Tasks.Task.CompletedTask;",
        "Html.Component<Probe>().Param(c => c.OnPicked, HandleSubmitAsync)",
        "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<global::System.String>(this, "
            + "(global::System.Func<global::System.Threading.Tasks.Task>)(HandleSubmitAsync)));")]
    public void HandlerShape_WrapsInFactoryCreate(string handlerDeclaration, string invocation, string expected)
    {
        var generated = GenerateBodyWith($$"""
            {{handlerDeclaration}}
            protected override View Body => {{invocation}};
            """);

        Assert.Contains(expected, generated);
    }

    [Fact]
    public void InlineLambdaHandler_IsWrappedTheSameWay()
    {
        var generated = GenerateBodyWith("""
            private int _count;
            protected override View Body => Html.Component<Probe>().Param(c => c.OnClose, () => _count++);
            """);

        Assert.Contains(
            "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, "
            + "(global::System.Action)(() => _count++)));",
            generated);
    }

    [Fact]
    public void HandwrittenFactoryCreate_StillBindsAsAnOrdinaryParameter()
    {
        // The four new overloads are additions, not replacements: an author who already writes the
        // framework's own EventCallback.Factory.Create by hand still reaches the plain generic .Param,
        // because its value is EventCallback itself rather than an Action/Func<Task> handler (#492).
        var generated = GenerateBodyWith("""
            private int _count;
            protected override View Body =>
                Html.Component<Probe>().Param(
                    c => c.OnClose,
                    Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => _count++));
            """);

        Assert.Contains("__builder.AddComponentParameter(1, \"OnClose\", ", generated);
        Assert.DoesNotContain("Factory.Create(this, (global::System.Action)(", generated);
    }

    [Fact]
    public void InsideViewPart_SubstitutesTheHandlerHole()
    {
        var generated = GenerateBodyWith("""
            private sealed class Model { public int Count { get; set; } }
            private readonly Model _model = new();

            [ViewPart]
            private static View Field(Model model) =>
                Html.Component<Probe>().Param(c => c.OnClose, () => model.Count++);

            protected override View Body => Html.Div[Field(_model)];
            """);

        Assert.Contains("__bcf_arg_1_0.Count++", generated);
    }
}
