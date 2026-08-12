using Microsoft.AspNetCore.Components;

namespace BlazorCodeFirst.Runtime.Tests;

public sealed class ViewConversionTests
{
    [Fact]
    public void RenderFragment_ConvertsToView_AndCarriesTheFragment()
    {
        RenderFragment fragment = builder => builder.AddContent(0, "x");

        View view = fragment;

        // This is the one conversion that is not inert. It is the only route into View.Fragment, which
        // is what makes the Opaque path renderable and what makes a surface-built View render nothing
        // (ARCHITECTURE.md §3.2, BCF3030). The string conversion below it stays inert.
        Assert.NotEqual(default, view);
    }

    [Fact]
    public void String_ConvertsToView_AndIsInert()
    {
        View view = "x";

        // The generator reads the original string expression and emits a text node; the value itself
        // carries nothing.
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
