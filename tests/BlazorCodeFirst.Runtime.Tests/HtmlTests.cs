using System;
using System.Collections.Generic;
using BlazorCodeFirst;

namespace BlazorCodeFirst.Runtime.Tests;

public sealed class HtmlTests
{
    [Fact]
    public void Div_IsInert_ReturnsDefaultBuilder() =>
        Assert.Equal(default, Html.Div);

    [Fact]
    public void Div_WithChildren_IsInert_ReturnsDefaultView() =>
        Assert.Equal(default, Html.Div["a", Html.Span["b"]]);

    [Fact]
    public void Span_IsInert_ReturnsDefaultBuilder() =>
        Assert.Equal(default, Html.Span);

    [Fact]
    public void Span_WithChildren_IsInert_ReturnsDefaultView() =>
        Assert.Equal(default, Html.Span["x"]);

    [Fact]
    public void Button_IsInert_ReturnsDefaultBuilder() =>
        Assert.Equal(default, Html.Button);

    [Fact]
    public void Button_WithChildren_IsInert_ReturnsDefaultView() =>
        Assert.Equal(default, Html.Button["OK"]);

    [Fact]
    public void Element_IsInert_ReturnsDefaultBuilder() =>
        Assert.Equal(default, Html.Element("nav"));

    [Fact]
    public void Element_WithChildren_IsInert_ReturnsDefaultView() =>
        Assert.Equal(default, Html.Element("nav")[Html.Span["x"]]);

    [Fact]
    public void OnClick_IsInert_ReturnsReceiverBuilder() =>
        Assert.Equal(default, Html.Button.OnClick(() => { }));

    [Fact]
    public void ForEach_WhenInvokedAtRuntime_ReturnsDefaultInertView()
    {
        IEnumerable<int> source = [1, 2, 3];

        var view = Html.ForEach(source, key: static x => x, content: static x => Html.Span[x.ToString()]);

        Assert.Equal(default, view);
    }

    [Fact]
    public void Fragment_IsInert_ReturnsDefaultView() =>
        Assert.Equal(default, Html.Fragment(Html.Span["a"], "b"));

    [Fact]
    public void Raw_IsInert_ReturnsDefaultView() =>
        Assert.Equal(default, Html.Raw("<b>x</b>"));

    [Fact]
    public void CuratedElementHelpers_AreInert_ReturnDefaultBuilder()
    {
        // Inert design-time syntax: reading a curated element helper must do no work and return the
        // default ElementBuilder. Div/Span/Button are pinned above; this spot-checks a further
        // sample, not the full curated set. BracketSurfaceGeneratorTests's
        // EveryCuratedHelper_CompilesUnqualifiedAndOpensItsOwnTag proves all 100 exist and resolve
        // unqualified.
        Assert.Equal(default, Html.Nav);
        Assert.Equal(default, Html.Header);
        Assert.Equal(default, Html.Main);
        Assert.Equal(default, Html.Aside);
        Assert.Equal(default, Html.Footer);
        Assert.Equal(default, Html.Section);
        Assert.Equal(default, Html.Article);
        Assert.Equal(default, Html.P);
        Assert.Equal(default, Html.H1);
        Assert.Equal(default, Html.H2);
        Assert.Equal(default, Html.H3);
        Assert.Equal(default, Html.H4);
        Assert.Equal(default, Html.H5);
        Assert.Equal(default, Html.H6);
        Assert.Equal(default, Html.Ul);
        Assert.Equal(default, Html.Ol);
        Assert.Equal(default, Html.Li);
        Assert.Equal(default, Html.A);
        Assert.Equal(default, Html.Img);
    }

    [Fact]
    public void CuratedElementHelpers_WithChildren_AreInert_ReturnDefaultView()
    {
        // Same contract as above, exercised through the children indexer rather than the bare property.
        Assert.Equal(default, Html.Header["x"]);
        Assert.Equal(default, Html.P["t"]);
        Assert.Equal(default, Html.H1["h"]);
        Assert.Equal(default, Html.H6["h"]);
        Assert.Equal(default, Html.Ul[Html.Li["a"]]);
        Assert.Equal(default, Html.A["link"]);
    }
}
