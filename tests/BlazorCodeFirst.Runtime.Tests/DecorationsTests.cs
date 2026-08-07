using BlazorCodeFirst;

namespace BlazorCodeFirst.Runtime.Tests;

public sealed class DecorationsTests
{
    [Fact]
    public void Class_WhenCalled_ReturnsInertReceiver()
    {
        // .Class is design-time syntax read by the generator; at runtime it must be a no-op
        // that returns the receiver, never evaluated for real work.
        ElementBuilder decorated = default(ElementBuilder).Class("badge");

        Assert.Equal(default, decorated);
    }

    [Fact]
    public void Class_WhenChained_RemainsInert()
    {
        ElementBuilder decorated = default(ElementBuilder).Class("a").Class("b");

        Assert.Equal(default, decorated);
    }

    [Fact]
    public void AttributeAndEventDecorations_AreInert_ReturnReceiver()
    {
        ElementBuilder e = default;
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
}
