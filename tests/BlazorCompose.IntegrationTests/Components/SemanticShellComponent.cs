using BlazorCompose;

namespace BlazorCompose.IntegrationTests.Components;

public partial class SemanticShellComponent : ComposeComponentBase
{
    public int Hovered { get; private set; }

    protected override View Body =>
        Html.Div(
            Html.Nav(
                Html.A(Html.Img().Src("/logo.png").Alt("Logo")).Href("/"),
                Html.A("Docs").Href("/docs"))
                .Attr("aria-label", "Primary")
                .On("onmouseenter", () => Hovered++),
            Html.Header(Html.H1("Title")),
            Html.Main(Html.P("Content")));
}
