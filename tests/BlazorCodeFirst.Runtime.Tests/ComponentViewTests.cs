using Microsoft.AspNetCore.Components;

namespace BlazorCodeFirst.Runtime.Tests;

public sealed class ComponentViewTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "This probes ComponentView<TComponent>.Bind and .Template, which are inert design-time syntax: " +
            "TComponent is only ever a generic type argument here, never constructed, so the analyzer's " +
            "'apparently never instantiated' finding is expected rather than dead code.")]
    private sealed class Probe : ComponentBase
    {
        [Parameter] public string Value { get; set; } = "";
        [Parameter] public EventCallback<string> ValueChanged { get; set; }
        [Parameter] public RenderFragment<int>? RowTemplate { get; set; }
    }

    [Fact]
    public void Bind_GetterOnly_ReturnsReceiverUnchanged()
    {
        var view = Html.Component<Probe>();
        Assert.Equal(view, view.Bind(c => c.Value, () => "x"));
    }

    [Fact]
    public void Bind_WithSyncSetter_ReturnsReceiverUnchanged()
    {
        var view = Html.Component<Probe>();
        Assert.Equal(view, view.Bind(c => c.Value, () => "x", _ => { }));
    }

    [Fact]
    public void Bind_WithAsyncSetter_ReturnsReceiverUnchanged()
    {
        var view = Html.Component<Probe>();
        Assert.Equal(view, view.Bind(c => c.Value, () => "x", _ => System.Threading.Tasks.Task.CompletedTask));
    }

    [Fact]
    public void Template_ContextIgnored_ReturnsReceiverUnchanged()
    {
        var view = Html.Component<Probe>();
        Assert.Equal(view, view.Template(c => c.RowTemplate, Html.Div["x"]));
    }

    [Fact]
    public void Template_ContextUsed_ReturnsReceiverUnchanged()
    {
        var view = Html.Component<Probe>();
        Assert.Equal(view, view.Template(c => c.RowTemplate, context => Html.Span[context.ToString()]));
    }
}
