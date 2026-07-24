using BlazorCompose;

namespace BlazorCompose.IntegrationTests.Components;

public partial class ConditionalComponent : ComposeComponentBase
{
    private bool _showPrefix = true;

    protected override View Body =>
        Html.Div(
            Html.If(_showPrefix, () => Html.Span("Prefix")),
            Html.Span("Always"),
            Html.Button("Toggle").OnClick(() => _showPrefix = !_showPrefix));
}
