using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

public partial class NativeIfComponent : BodyComponentBase
{
    private bool _showPrefix = true;

    protected override View Body
    {
        get
        {
            if (_showPrefix)
            {
                return Div[Span["Prefix"], Span["Always"], Button.OnClick(() => _showPrefix = false)["Toggle"]];
            }
            else
            {
                return Div[Span["Always"], Button.OnClick(() => _showPrefix = false)["Toggle"]];
            }
        }
    }
}
