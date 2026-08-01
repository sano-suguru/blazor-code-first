using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.IntegrationTests.Components;

public partial class ConditionalComponent : ComposeComponentBase
{
    private bool _showPrefix = true;

    protected override View Body =>
        Div[
            If(_showPrefix, () => Span["Prefix"]),
            Span["Always"],
            Button.OnClick(() => _showPrefix = !_showPrefix)["Toggle"]];
}
