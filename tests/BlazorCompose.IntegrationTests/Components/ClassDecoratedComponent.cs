using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.IntegrationTests.Components;

public partial class ClassDecoratedComponent : ComposeComponentBase
{
    private readonly bool _active = true;

    protected override View Body =>
        Div(
            Span("Hi").Class("badge"),
            Span("Multi").Class("a").Class("b"),
            Span("Dyn").Class(_active ? "on" : "off"))
        .Class("panel");
}
