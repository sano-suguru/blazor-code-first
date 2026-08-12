using BlazorCodeFirst.CompilerServices;
using Microsoft.AspNetCore.Components;

namespace BlazorCodeFirst.Runtime.Tests;

public sealed class ViewRuntimeTests
{
    [Fact]
    public void FragmentOf_WhenViewIsDefault_ReturnsNull()
    {
        Assert.Null(ViewRuntime.FragmentOf(default));
    }

    [Fact]
    public void FragmentOf_WhenViewCameFromAFragment_ReturnsThatFragment()
    {
        RenderFragment fragment = builder => builder.AddContent(0, "x");

        View view = fragment;

        Assert.Same(fragment, ViewRuntime.FragmentOf(view));
    }

    [Fact]
    public void FragmentOf_WhenViewCameFromANullFragment_ReturnsNull()
    {
        View view = (RenderFragment?)null;

        Assert.Null(ViewRuntime.FragmentOf(view));
    }

    [Fact]
    public void FragmentOf_WhenViewCameFromTheInertSurface_ReturnsNull()
    {
        // The design-time surface never builds a fragment, which is the whole reason BCF3030 exists.
        View view = Html.Div["x"];

        Assert.Null(ViewRuntime.FragmentOf(view));
    }
}
