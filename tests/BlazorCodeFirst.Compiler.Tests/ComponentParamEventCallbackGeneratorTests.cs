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

    [Fact]
    public void NonGeneric_ActionHandler_WrapsInFactoryCreate()
    {
        var generated = GenerateBodyWith("""
            private void HandleClose() { }
            protected override View Body => Html.Component<Probe>().Param(c => c.OnClose, HandleClose);
            """);

        Assert.Contains(
            "__builder.AddComponentParameter(1, \"OnClose\", "
            + "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, "
            + "(global::System.Action)(HandleClose)));",
            generated);
    }

    [Fact]
    public void NonGeneric_FuncTaskHandler_WrapsInFactoryCreate()
    {
        var generated = GenerateBodyWith("""
            private System.Threading.Tasks.Task HandleCloseAsync() => System.Threading.Tasks.Task.CompletedTask;
            protected override View Body => Html.Component<Probe>().Param(c => c.OnClose, HandleCloseAsync);
            """);

        Assert.Contains(
            "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, "
            + "(global::System.Func<global::System.Threading.Tasks.Task>)(HandleCloseAsync)));",
            generated);
    }

    [Fact]
    public void Generic_ActionOfTArgHandler_WrapsInFactoryCreateWithTypeArgument()
    {
        var generated = GenerateBodyWith("""
            private void HandlePicked(string value) { }
            protected override View Body => Html.Component<Probe>().Param(c => c.OnPicked, HandlePicked);
            """);

        Assert.Contains(
            "__builder.AddComponentParameter(1, \"OnPicked\", "
            + "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<global::System.String>(this, "
            + "(global::System.Action<global::System.String>)(HandlePicked)));",
            generated);
    }

    [Fact]
    public void Generic_FuncOfTArgTaskHandler_WrapsInFactoryCreateWithTypeArgument()
    {
        var generated = GenerateBodyWith("""
            private System.Threading.Tasks.Task HandlePickedAsync(string value) =>
                System.Threading.Tasks.Task.CompletedTask;
            protected override View Body => Html.Component<Probe>().Param(c => c.OnPicked, HandlePickedAsync);
            """);

        Assert.Contains(
            "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<global::System.String>(this, "
            + "(global::System.Func<global::System.String, global::System.Threading.Tasks.Task>)"
            + "(HandlePickedAsync)));",
            generated);
    }

    [Fact]
    public void Generic_ArgumentIgnoringActionHandler_WrapsInFactoryCreateWithTypeArgument()
    {
        // The Razor OnValidSubmit="HandleCreate" shape: a parameterless handler bound to an
        // EventCallback<TArg>-typed parameter. EventCallbackFactory.Create<T> itself has this overload
        // beside Action<T>, so .Param needs it too (#492) — TArg is inferred from the selector alone.
        var generated = GenerateBodyWith("""
            private void HandleSubmit() { }
            protected override View Body => Html.Component<Probe>().Param(c => c.OnPicked, HandleSubmit);
            """);

        Assert.Contains(
            "__builder.AddComponentParameter(1, \"OnPicked\", "
            + "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<global::System.String>(this, "
            + "(global::System.Action)(HandleSubmit)));",
            generated);
    }

    [Fact]
    public void Generic_ArgumentIgnoringFuncTaskHandler_WrapsInFactoryCreateWithTypeArgument()
    {
        var generated = GenerateBodyWith("""
            private System.Threading.Tasks.Task HandleSubmitAsync() => System.Threading.Tasks.Task.CompletedTask;
            protected override View Body =>
                Html.Component<Probe>().Param(c => c.OnPicked, HandleSubmitAsync);
            """);

        Assert.Contains(
            "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<global::System.String>(this, "
            + "(global::System.Func<global::System.Threading.Tasks.Task>)(HandleSubmitAsync)));",
            generated);
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
