using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.IntegrationTests.Components;

public partial class ClassDecoratedComponent : ComposeComponentBase
{
    private readonly bool _active = true;

    protected override View Body =>
        Div.Class("panel")[
            Span.Class("badge")["Hi"],
            Span.Class("a").Class("b")["Multi"],
            Span.Class(_active ? "on" : "off")["Dyn"]];
}
