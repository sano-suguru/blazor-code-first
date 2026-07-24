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
}
