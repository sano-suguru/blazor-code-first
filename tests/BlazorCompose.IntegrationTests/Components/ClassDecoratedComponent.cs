using BlazorCompose;

namespace BlazorCompose.IntegrationTests.Components;

public partial class ClassDecoratedComponent : ComposeComponentBase
{
    private readonly bool _active = true;

    protected override View Body =>
        Html.Div(
            Html.Span("Hi").Class("badge"),
            Html.Span("Multi").Class("a").Class("b"),
            Html.Span("Dyn").Class(_active ? "on" : "off"))
        .Class("panel");
}
