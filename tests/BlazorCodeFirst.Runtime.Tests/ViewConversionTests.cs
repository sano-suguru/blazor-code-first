using Microsoft.AspNetCore.Components;

namespace BlazorCodeFirst.Runtime.Tests;

public sealed class ViewConversionTests
{
    [Fact]
    public void RenderFragment_ConvertsToView_AndIsInert()
    {
        RenderFragment fragment = builder => builder.AddContent(0, "x");

        View view = fragment;

        // The conversion is design-time syntax read by the generator; at runtime it must do no work
        // and yield the default View, exactly like the string conversion.
        Assert.Equal(default, view);
    }

    [Fact]
    public void NullRenderFragment_ConvertsToView()
    {
        // Null is the normal case: an unset [Parameter] RenderFragment? or a layout Body before the
        // first render. The conversion must accept it without a nullable warning at the call site.
        RenderFragment? fragment = null;

        View view = fragment;

        Assert.Equal(default, view);
    }
}
