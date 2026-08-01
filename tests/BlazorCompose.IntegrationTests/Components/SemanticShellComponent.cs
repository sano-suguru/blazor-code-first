using BlazorCompose;

namespace BlazorCompose.IntegrationTests.Components;

public partial class SemanticShellComponent : ComposeComponentBase
{
    public int Hovered { get; private set; }

    protected override View Body =>
        Html.Div[
            Html.Nav.Attr("aria-label", "Primary").On("onmouseenter", () => Hovered++)[
                Html.A.Href("/")[Html.Img.Src("/logo.png").Alt("Logo")],
                Html.A.Href("/docs")["Docs"]],
            Html.Header[Html.H1["Title"]],
            Html.Main[Html.P["Content"]]];
}
