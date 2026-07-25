using BlazorCompose;

namespace BlazorCompose.Runtime.Tests;

public sealed class DecorationsTests
{
    [Fact]
    public void Class_WhenCalled_ReturnsInertReceiverView()
    {
        // .Class is design-time syntax read by the generator; at runtime it must be a no-op
        // that returns the receiver, never evaluated for real work.
        View decorated = default(View).Class("badge");

        Assert.Equal(default, decorated);
    }

    [Fact]
    public void Class_WhenChained_RemainsInert()
    {
        View decorated = default(View).Class("a").Class("b");

        Assert.Equal(default, decorated);
    }

    [Fact]
    public void AttributeAndEventDecorations_AreInert_ReturnReceiver()
    {
        View v = default;
        Assert.Equal(v, v.Href("/x"));
        Assert.Equal(v, v.Src("/y").Alt("a").Id("i").Type("button").Title("t").Role("nav"));
        Assert.Equal(v, v.Attr("aria-label", "menu"));
        Assert.Equal(v, v.On("onmouseenter", () => { }));
        Assert.Equal(v, v.On("onmouseenter", () => System.Threading.Tasks.Task.CompletedTask));
        Assert.Equal(v, v.OnClick(() => System.Threading.Tasks.Task.CompletedTask));
    }
}
