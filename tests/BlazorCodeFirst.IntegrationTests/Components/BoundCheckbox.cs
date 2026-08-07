using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The <see langword="bool"/> overload (Task 1, Task 5), bound to <c>checked</c> rather than
/// <c>value</c>.
/// </summary>
public partial class BoundCheckbox : BodyComponentBase
{
    public bool Agreed { get; set; }

    protected override View Body =>
        Html.Input.Type("checkbox").Bind("checked", "onchange", () => Agreed);
}
