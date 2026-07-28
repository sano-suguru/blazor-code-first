using BlazorCompose.IntegrationTests.Components;
using Bunit;

namespace BlazorCompose.IntegrationTests;

public sealed class ComponentSlotRenderingTests : BunitContext
{
    [Fact]
    public void Children_RenderInsideTheTargetComponentsChildContentSlot()
    {
        var cut = Render<SlotHostComponent>();

        // The slot content lands inside the card's body div, i.e. the fragment really was passed
        // to the child component rather than rendered in the host's own frame list.
        Assert.Equal("inner", cut.Find("section.card div.body .marker").TextContent);
    }

    [Fact]
    public void FragmentParam_RendersInsideTheNamedSlot()
    {
        var cut = Render<SlotHostComponent>();

        Assert.Equal("count=0", cut.Find("section.card div.foot .count").TextContent);
    }

    [Fact]
    public void SiblingAfterAComponentWithSlots_RendersOutsideIt()
    {
        var cut = Render<SlotHostComponent>();

        // The sibling is a direct child of the host div, not of the card: the post-slot sequence
        // reservation kept it out of the fragment.
        Assert.Equal("after", cut.Find("div > .sibling").TextContent);

        // The check above is NOT sufficient on its own, and this second one is what gives the test its
        // teeth. SlotCardComponent wraps each slot in a plain <div> (class "body"/"foot"), so if a
        // sequencing regression swallowed this sibling into the ChildContent fragment it would render as
        // `div.body > span.sibling` — which satisfies `div > .sibling` just as well, leaving the first
        // assertion green. Verified by mutation: moving the sibling into the slot keeps the selector
        // above passing. Asserting it is absent from inside the card is what actually detects that.
        Assert.Empty(cut.FindAll("section.card .sibling"));
    }

    [Fact]
    public void EventInsideASlot_DispatchesAndRerendersTheSlot()
    {
        var cut = Render<SlotHostComponent>();

        cut.Find("section.card div.body button").Click();

        Assert.Equal("count=1", cut.Find("section.card div.foot .count").TextContent);
        // The slot content survives the re-render in place.
        Assert.Equal("inner", cut.Find("section.card div.body .marker").TextContent);
    }
}
