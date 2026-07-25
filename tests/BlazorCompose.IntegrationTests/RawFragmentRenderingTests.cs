using BlazorCompose.IntegrationTests.Components;
using Bunit;

namespace BlazorCompose.IntegrationTests;

public sealed class RawFragmentRenderingTests : BunitContext
{
    [Fact]
    public void Raw_InjectsMarkupVerbatim_IntoMainPanel()
    {
        var cut = Render<RawFragmentShellComponent>();

        // The Raw HTML materializes as real DOM inside <main>, including its inner <em>.
        Assert.Equal("world", cut.Find("main em").TextContent);
        Assert.Contains("Hello", cut.Find("main").TextContent);
    }

    [Fact]
    public void Fragment_EmitsChildrenAsSiblings_WithNoWrapperElement()
    {
        var cut = Render<RawFragmentShellComponent>();

        // The fragment's h2 and p are DIRECT children of the outer div (the child combinator `div > …`
        // matches only if no wrapper element was emitted for the fragment). The raw markdown's own <p>
        // lives under <main>, so `div > p` uniquely selects the fragment's "Body" paragraph.
        Assert.Equal("Section", cut.Find("div > h2").TextContent);
        Assert.Equal("Body", cut.Find("div > p").TextContent);
    }
}
