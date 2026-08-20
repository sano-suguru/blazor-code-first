using System.Globalization;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The #307 value-type bindings, each carrying the culture its call site wrote. One file because the five
/// exist for one set of measurements and are read together.
/// </summary>
public partial class BoundDecimalInput : BodyComponentBase
{
    public decimal Amount { get; set; } = 1234.5m;

    protected override View Body =>
        Html.Input.Type("number").Bind(
            "value", "oninput", () => Amount, CultureInfo.InvariantCulture);
}

/// <summary>A nullable value, for the #171 rule reaching this path: null omits the attribute.</summary>
public partial class BoundNullableIntInput : BodyComponentBase
{
    public int? Age { get; set; }

    protected override View Body =>
        Html.Input.Type("number").Bind(
            "value", "oninput", () => Age, CultureInfo.InvariantCulture);
}

/// <summary>The parse-failure path: an input the framework's converter cannot read.</summary>
public partial class BoundIntInput : BodyComponentBase
{
    public int Age { get; set; } = 30;

    protected override View Body =>
        Html.Input.Type("number").Bind(
            "value", "oninput", () => Age, CultureInfo.InvariantCulture);
}

/// <summary>
/// An enum, whose parse is the one path this surface now reaches that runs on reflection
/// (<c>ParserDelegateCache</c> pulls <c>ConvertToEnum</c> out with <c>MakeGenericMethod</c>).
/// </summary>
public partial class BoundEnumSelect : BodyComponentBase
{
    public DayOfWeek Day { get; set; } = DayOfWeek.Monday;

    protected override View Body =>
        Html.Select.Bind("value", "onchange", () => Day, CultureInfo.InvariantCulture);
}

/// <summary>
/// A date carrying the format its round trip needs. <c>&lt;input type="date"&gt;</c> requires
/// <c>yyyy-MM-dd</c>, and this surface cannot read the element's <c>type</c> to supply it the way Razor
/// does, which is the whole reason a format is in the surface at all.
/// </summary>
public partial class BoundDateInput : BodyComponentBase
{
    public DateOnly Due { get; set; } = new(2026, 8, 14);

    protected override View Body =>
        Html.Input.Type("date").Bind(
            "value", "oninput", () => Due, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
