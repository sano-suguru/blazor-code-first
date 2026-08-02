using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

public partial class ClassDecoratedComponent : BodyComponentBase
{
    private readonly bool _active = true;

    protected override View Body =>
        Div.Class("panel")[
            Span.Class("badge")["Hi"],
            Span.Class("a").Class("b")["Multi"],
            Span.Class(_active ? "on" : "off")["Dyn"]];
}
