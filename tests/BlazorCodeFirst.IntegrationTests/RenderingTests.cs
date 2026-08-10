using BlazorCodeFirst.IntegrationTests.Components;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace BlazorCodeFirst.IntegrationTests;

public sealed class RenderingTests : BunitContext
{
    private static readonly string[] GenericItems = ["alpha", "beta"];

    [Fact]
    public void Counter_WhenIncrementButtonClicked_RerendersWithIncrementedCount()
    {
        var cut = Render<CounterComponent>();

        cut.MarkupMatches("<div><span>Count: 0</span><button>Increment</button></div>");

        cut.Find("button").Click();

        cut.MarkupMatches("<div><span>Count: 1</span><button>Increment</button></div>");
    }

    [Fact]
    public void ConditionalComponent_WhenToggleButtonClicked_RemovesPrefixAndPreservesMarkupOrder()
    {
        var cut = Render<ConditionalComponent>();

        cut.MarkupMatches("<div><span>Prefix</span><span>Always</span><button>Toggle</button></div>");

        cut.Find("button").Click();

        cut.MarkupMatches("<div><span>Always</span><button>Toggle</button></div>");
    }

    [Fact]
    public void ComposableCounterComponent_WhenRenderedAndIncremented_EvaluatesArgumentsOncePerRender()
    {
        var cut = Render<ComposableCounterComponent>();

        cut.MarkupMatches("<div><span>Count: 0</span><button>Increment</button></div>");
        Assert.Equal(1, cut.Instance.ArgumentEvaluations);

        cut.Find("button").Click();

        cut.MarkupMatches("<div><span>Count: 1</span><button>Increment</button></div>");
        Assert.Equal(2, cut.Instance.ArgumentEvaluations);
    }

    /// <summary>
    /// A content-taking part renders its caller's brackets inside its own markup, and an additional
    /// <c>View</c> parameter fills a second slot (#34, #176). The click matters as much as the first
    /// assertion: the spliced content is emitted into the part's sequence space, so a diff driven by state the
    /// content reads is where a misnumbered splice would show up.
    /// </summary>
    [Fact]
    public void ComposableContentComponent_WhenIncremented_RerendersContentInsideThePartsMarkup()
    {
        var cut = Render<ComposableContentComponent>();

        const string Markup = """
            <div class="page">
              <div class="panel">
                <div class="panel-head"><h2>Counter</h2></div>
                <div class="panel-body">
                  <span>Count: {0}</span><button>Increment</button>
                </div>
              </div>
            </div>
            """;

        cut.MarkupMatches(string.Format(System.Globalization.CultureInfo.InvariantCulture, Markup, 0));

        cut.Find("button").Click();

        cut.MarkupMatches(string.Format(System.Globalization.CultureInfo.InvariantCulture, Markup, 1));
    }

    [Fact]
    public void KeyedList_WhenRotateClicked_RerendersRowsInNewOrder()
    {
        // NOTE: rows are stateless Text, so this asserts render order only; it does NOT discriminate keyed
        // from index/broken keys (both produce identical markup after a reorder). The keying guarantee is
        // locked at the generator level (SetKey emitted on the content root with the item-derived key); an
        // end-to-end state-preservation test requires a stateful content primitive (see tracking Issue).
        var cut = Render<KeyedListComponent>();

        cut.MarkupMatches(
            "<div><span>one</span><span>two</span><span>three</span><button>Rotate</button></div>");

        cut.Find("button").Click();

        cut.MarkupMatches(
            "<div><span>two</span><span>three</span><span>one</span><button>Rotate</button></div>");
    }

    [Fact]
    public void KeyedList_ClickingARow_InvokesThatRowsHandler()
    {
        var cut = Render<KeyedHandlerComponent>();

        cut.MarkupMatches(
            "<div><span>Total: 0</span><button>+1</button><button>+5</button><button>+10</button></div>");

        // Clicking the second row's button (+5) must mutate state using THAT row's captured item,
        // not the last item from the loop, a last-item-capture bug would produce 10 here instead.
        cut.FindAll("button")[1].Click();
        cut.Find("span").MarkupMatches("<span>Total: 5</span>");

        cut.FindAll("button")[2].Click();
        cut.Find("span").MarkupMatches("<span>Total: 15</span>");
    }

    [Fact]
    public void KeyedComponentList_WhenRowStateChangedThenRotated_StatePreservedFollowsItem()
    {
        // Positive control: key = item identity. Per-row component state (an internal counter) must
        // follow its item across a reorder, impossible to observe with stateless rows, and it would
        // break under a positional key (see the negative-control test below).
        var cut = Render<StatefulKeyedListComponent>();

        // Rows start a:0, b:0, c:0. Increment the first row (a) twice.
        cut.FindAll("button")[0].Click();
        cut.FindAll("button")[0].Click();
        Assert.Equal("a:2", cut.FindAll("span")[0].TextContent);

        // Rotate -> order becomes b, c, a. Item a is now last; its counter followed it.
        cut.FindAll("button")[3].Click();
        Assert.Equal("b:0", cut.FindAll("span")[0].TextContent);
        Assert.Equal("a:2", cut.FindAll("span")[2].TextContent);
    }

    [Fact]
    public void PositionKeyedComponentList_WhenRowStateChangedThenRotated_StateStaysAtPosition()
    {
        // Negative control: key = list position (index). State sticks to the DOM position, not the item,
        // so after a reorder the position-0 row shows the new item's label with the OLD counter. This is
        // the index-key failure mode ARCHITECTURE.md 2.7(B) describes; contrasting it with the positive
        // test proves the key is load-bearing.
        var cut = Render<PositionKeyedListComponent>();

        cut.FindAll("button")[0].Click();
        cut.FindAll("button")[0].Click();
        Assert.Equal("a:2", cut.FindAll("span")[0].TextContent);

        // Rotate -> labels become b, c, a. Position 0 now shows b but keeps the counter (2).
        cut.FindAll("button")[3].Click();
        Assert.Equal("b:2", cut.FindAll("span")[0].TextContent);
    }

    [Fact]
    public void ClassDecoratedComponent_WhenRendered_EmitsFoldedClassAttributes()
    {
        var cut = Render<ClassDecoratedComponent>();

        cut.MarkupMatches(
            "<div class=\"panel\">" +
            "<span class=\"badge\">Hi</span>" +
            "<span class=\"a b\">Multi</span>" +
            "<span class=\"on\">Dyn</span>" +
            "</div>");
    }

    [Fact]
    public void NullClassComponent_WhenRendered_OmitsAttributeForSingleNullButKeepsItForChained()
    {
        var cut = Render<NullClassComponent>();

        var spans = cut.FindAll("span");

        // A single .Class whose expression is null: Blazor omits a null attribute value, so no
        // class attribute is rendered at all. Assert with GetAttribute (returns null when absent)
        // rather than MarkupMatches, which would blur presence vs. absence.
        Assert.Null(spans[0].GetAttribute("class"));

        // N >= 2 chained .Class calls where one is null: the folded expression is a C# `+`
        // concatenation, and null participates as "", so the class attribute is still present.
        Assert.NotNull(spans[1].GetAttribute("class"));
    }

    /// <summary>
    /// A <c>bool</c> attribute value through real rendering and a real re-render diff. The compiler tests
    /// pin the emitted call and the browser gate pins the folded/element parity for a constant; this pins
    /// what the overload is actually for, that a runtime <see langword="false"/> removes the attribute
    /// from an element that already carried it.
    /// </summary>
    [Fact]
    public void BooleanAttributeComponent_WhenToggled_AddsAndRemovesTheAttribute()
    {
        var cut = Render<BooleanAttributeComponent>();

        // Present with an empty value, which is how a true bool reaches the DOM.
        Assert.Equal(string.Empty, cut.Find("input").GetAttribute("disabled"));

        cut.Find("button").Click();

        // GetAttribute returns null when the attribute is absent, which is the distinction under test;
        // MarkupMatches would blur presence-with-an-empty-value against absence.
        Assert.Null(cut.Find("input").GetAttribute("disabled"));
    }

    [Fact]
    public void SemanticShell_RendersExpectedDom()
    {
        var cut = Render<SemanticShellComponent>();

        cut.Find("nav[aria-label=\"Primary\"]");
        var logo = cut.Find("nav a[href=\"/\"] img");
        Assert.Equal("/logo.png", logo.GetAttribute("src"));
        Assert.Equal("Logo", logo.GetAttribute("alt"));
        cut.Find("nav a[href=\"/docs\"]");
        cut.Find("header h1");
        Assert.Equal("Content", cut.Find("main p").TextContent);
    }

    [Fact]
    public void SemanticShell_OnMouseEnter_DispatchesHandler()
    {
        var cut = Render<SemanticShellComponent>();
        cut.Find("nav").MouseEnter();     // bUnit strongly-typed trigger for the onmouseenter attribute
        Assert.Equal(1, cut.Instance.Hovered);
    }

    [Fact]
    public void GenericComponent_RendersItemsThroughItsTypeParameter()
    {
        var cut = Render<GenericListComponent<string>>(parameters => parameters
            .Add(p => p.Items, GenericItems));

        var items = cut.FindAll("ul li");
        Assert.Equal(2, items.Count);
        Assert.Equal("alpha", items[0].TextContent);
        Assert.Equal("beta", items[1].TextContent);
    }

    [Fact]
    public void TypedEventComponent_WhenInputRaised_ReceivesTheEventArgumentAndRerenders()
    {
        // Reads TextContent rather than matching markup: the claim under test is that the handler received
        // e.Value, and a full markup comparison would also pin how an empty value attribute on a void
        // element is normalized, which is not what this is about.
        var cut = Render<TypedEventComponent>();

        Assert.Equal("Hello, ", cut.Find("span").TextContent);

        cut.Find("input").Input(new ChangeEventArgs { Value = "Ada" });

        Assert.Equal("Hello, Ada", cut.Find("span").TextContent);
    }
}
