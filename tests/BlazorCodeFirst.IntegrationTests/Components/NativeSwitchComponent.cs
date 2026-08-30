using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

public partial class NativeSwitchComponent : BodyComponentBase
{
    private int _mode;

    protected override View Body
    {
        get
        {
            // A real switch STATEMENT, not IDE0066's suggested switch expression: this component exists to
            // exercise the native-`switch` transplant path (ARCHITECTURE.md §5.3), which only a statement
            // reaches.
#pragma warning disable IDE0066
            switch (_mode)
            {
                case 0:
                    return Div[Span["Prefix"], Span["Always"], Button.OnClick(() => _mode = 1)["Toggle"]];
                default:
                    return Div[Span["Always"], Button.OnClick(() => _mode = 1)["Toggle"]];
            }
#pragma warning restore IDE0066
        }
    }
}
