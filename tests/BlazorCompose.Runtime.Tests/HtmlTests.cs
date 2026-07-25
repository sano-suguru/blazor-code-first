using System;
using System.Collections.Generic;
using BlazorCompose;

namespace BlazorCompose.Runtime.Tests;

public sealed class HtmlTests
{
    [Fact]
    public void Div_IsInert_ReturnsDefaultView() =>
        Assert.Equal(default, Html.Div("a", Html.Span("b")));

    [Fact]
    public void ForEach_WhenInvokedAtRuntime_ReturnsDefaultInertView()
    {
        IEnumerable<int> source = [1, 2, 3];

        var view = Html.ForEach(source, key: static x => x, content: static x => Html.Span(x.ToString()));

        Assert.Equal(default, view);
    }

    [Fact]
    public void Span_IsInert_ReturnsDefaultView() =>
        Assert.Equal(default, Html.Span("x"));

    [Fact]
    public void Button_IsInert_ReturnsDefaultView() =>
        Assert.Equal(default, Html.Button("OK"));

    [Fact]
    public void Element_IsInert_ReturnsDefaultView() =>
        Assert.Equal(default, Html.Element("nav", Html.Span("x")));

    [Fact]
    public void OnClick_IsInert_ReturnsReceiverView() =>
        Assert.Equal(default, Html.Button("OK").OnClick(() => { }));

    [Fact]
    public void CuratedElementHelpers_AreInert_ReturnDefault()
    {
        // Inert design-time syntax: at runtime they must return the default View and do no work.
        Assert.Equal(default, Html.Nav());
        Assert.Equal(default, Html.Header("x"));
        Assert.Equal(default, Html.Main());
        Assert.Equal(default, Html.Aside());
        Assert.Equal(default, Html.Footer());
        Assert.Equal(default, Html.Section());
        Assert.Equal(default, Html.Article());
        Assert.Equal(default, Html.P("t"));
        Assert.Equal(default, Html.H1("h"));
        Assert.Equal(default, Html.H6("h"));
        Assert.Equal(default, Html.Ul(Html.Li("a")));
        Assert.Equal(default, Html.Ol());
        Assert.Equal(default, Html.A("link"));
        Assert.Equal(default, Html.Img());
    }
}
