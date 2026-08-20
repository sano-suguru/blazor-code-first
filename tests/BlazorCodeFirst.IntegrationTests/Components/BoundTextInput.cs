namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The inverted-getter path (Task 5): no explicit setter, so the generator derives one from the
/// getter's own assignable expression.
/// </summary>
public partial class BoundTextInput : BodyComponentBase
{
    public string Name { get; set; } = "";

    protected override View Body =>
        Html.Input.Type("text").Bind("value", "oninput", () => Name);
}
