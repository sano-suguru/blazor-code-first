using BlazorCodeFirst.IntegrationTests.Components;
using Bunit;

namespace BlazorCodeFirst.IntegrationTests;

/// <summary>
/// Holds that a cascade can be provided from a BlazorCodeFirst <c>Body</c> with nothing added to the
/// surface: <c>CascadingValue&lt;TValue&gt;</c> is placed by <c>Component&lt;T&gt;()</c>, its parameters are
/// set by <c>.Param</c>, and its <c>ChildContent</c> comes from the brackets (#313).
/// </summary>
/// <remarks>
/// <see cref="ChromeLayoutRenderingTests"/> covers the receiving half against a cascade bUnit supplies. What
/// is unproven without these tests is the providing half, where the frames come from the generator.
/// </remarks>
public sealed class CascadingValueRenderingTests : BunitContext
{
    [Fact]
    public void CascadingValue_DeliversItsValueToADescendant()
    {
        var cut = Render<CascadingThemeHost>();

        Assert.Equal("dark", cut.Find(".theme").TextContent);
    }

    [Fact]
    public void CascadingValue_WithAName_DeliversToAMatchingCascadingParameter()
    {
        var cut = Render<CascadingThemeHost>();

        // The reader sits under a <div> inside the brackets, so the value travelled past the immediate
        // child, and it declares [CascadingParameter(Name = "locale")], so it is the Name parameter that
        // matched rather than the type.
        Assert.Equal("en-US", cut.Find(".locale").TextContent);
    }

    [Fact]
    public void CascadingValue_ChangingTheValue_RerendersTheDescendant()
    {
        var cut = Render<CascadingThemeHost>();

        cut.Find("button.swap").Click();

        // The reader has no parameter of its own and is not re-rendered by the host's own diff, since the
        // frames that open it are unchanged. Only Blazor's cascading-value subscription can change this
        // text, so a value that had been copied into the frames once would still read "dark".
        Assert.Equal("light", cut.Find(".theme").TextContent);
    }
}
