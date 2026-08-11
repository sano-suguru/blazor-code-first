using BlazorCodeFirst;

namespace BlazorCodeFirst.Runtime.Tests;

public sealed class DecorationsTests
{
    [Fact]
    public void Class_WhenCalled_ReturnsInertReceiver()
    {
        // .Class is design-time syntax read by the generator; at runtime it must be a no-op
        // that returns the receiver, never evaluated for real work.
        ElementView decorated = default(ElementView).Class("badge");

        Assert.Equal(default, decorated);
    }

    [Fact]
    public void Class_WhenChained_RemainsInert()
    {
        ElementView decorated = default(ElementView).Class("a").Class("b");

        Assert.Equal(default, decorated);
    }

    [Fact]
    public void AttributeAndEventDecorations_AreInert_ReturnReceiver()
    {
        ElementView e = default;
        Assert.Equal(e, e.Href("/x"));
        Assert.Equal(e, e.Src("/y").Alt("a").Id("i").Type("button").Title("t").Role("nav"));
        Assert.Equal(e, e.Attr("aria-label", "menu"));
        Assert.Equal(e, e.On("onmouseenter", () => { }));
        Assert.Equal(e, e.On("onmouseenter", () => System.Threading.Tasks.Task.CompletedTask));
        Assert.Equal(e, e.OnClick(() => System.Threading.Tasks.Task.CompletedTask));
        Assert.Equal(e, e.On("oninput", (Microsoft.AspNetCore.Components.ChangeEventArgs a) => { }));
        Assert.Equal(e, e.On(
            "oninput",
            (Microsoft.AspNetCore.Components.ChangeEventArgs a) => System.Threading.Tasks.Task.CompletedTask));
    }

    [Fact]
    public void Bind_StringGetterOnly_ReturnsReceiverUnchanged()
    {
        var element = Html.Input;
        Assert.Equal(element, element.Bind("value", "oninput", () => "x"));
    }

    [Fact]
    public void Bind_StringWithSyncSetter_ReturnsReceiverUnchanged()
    {
        var element = Html.Input;
        Assert.Equal(element, element.Bind("value", "oninput", () => "x", _ => { }));
    }

    [Fact]
    public void Bind_StringWithAsyncSetter_ReturnsReceiverUnchanged()
    {
        var element = Html.Input;
        Assert.Equal(element, element.Bind("value", "oninput", () => "x", _ => System.Threading.Tasks.Task.CompletedTask));
    }

    [Fact]
    public void Bind_BoolGetterOnly_ReturnsReceiverUnchanged()
    {
        var element = Html.Input;
        Assert.Equal(element, element.Bind("checked", "onchange", () => true));
    }

    [Fact]
    public void Bind_BoolWithSyncSetter_ReturnsReceiverUnchanged()
    {
        var element = Html.Input;
        Assert.Equal(element, element.Bind("checked", "onchange", () => true, _ => { }));
    }

    [Fact]
    public void Bind_BoolWithAsyncSetter_ReturnsReceiverUnchanged()
    {
        var element = Html.Input;
        Assert.Equal(element, element.Bind("checked", "onchange", () => true, _ => System.Threading.Tasks.Task.CompletedTask));
    }
}
