using BlazorCompose;
using static BlazorCompose.UI;

namespace BlazorCompose.IntegrationTests.Components;

public partial class ClassDecoratedComponent : ComposeComponentBase
{
    private readonly bool _active = true;

    protected override View Body =>
        VStack(
            Text("Hi").Class("badge"),
            Text("Multi").Class("a").Class("b"),
            Text("Dyn").Class(_active ? "on" : "off"))
        .Class("panel");
}
