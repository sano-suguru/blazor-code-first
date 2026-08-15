using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The value cascaded by <see cref="CascadingThemeHost"/>. A reference type, so a reader that never
/// received the cascade renders "none" rather than a default that could pass for a delivered value.
/// </summary>
public sealed record ThemeInfo(string Name);

/// <summary>
/// Provides two cascades from an ordinary <c>Body</c>: an unnamed <see cref="ThemeInfo"/> whose value the
/// button replaces, and a fixed named string. Nothing in the surface knows what
/// <see cref="CascadingValue{TValue}"/> is — it is reached as any other component, with <c>.Param</c> for
/// its parameters and brackets for its <c>ChildContent</c> (#313).
/// </summary>
/// <remarks>
/// This is also the first <c>Component&lt;T&gt;()</c> in these tests whose type argument is a closed generic
/// type, so it holds that the generator writes <c>OpenComponent&lt;CascadingValue&lt;ThemeInfo&gt;&gt;</c>
/// with the argument intact.
/// </remarks>
public partial class CascadingThemeHost : BodyComponentBase
{
    private ThemeInfo _theme = new("dark");

    protected override View Body =>
        Div[
            Button.Class("swap").OnClick(() => _theme = new ThemeInfo("light"))["swap"],
            Component<CascadingValue<ThemeInfo>>()
                .Param(c => c.Value, _theme)[
                    Component<ThemeReader>()],
            Component<CascadingValue<string>>()
                .Param(c => c.Value, "en-US")
                .Param(c => c.Name, "locale")
                .Param(c => c.IsFixed, true)[
                    Div[Component<LocaleReader>()]]];
}

/// <summary>
/// Receives the unnamed cascade. The receiving half is ordinary Blazor: <c>[CascadingParameter]</c> is a
/// property on the class, which the generator never reads, so this proves delivery reaches a
/// BlazorCodeFirst component and not only a <c>.razor</c> one.
/// </summary>
public partial class ThemeReader : BodyComponentBase
{
    [CascadingParameter]
    public ThemeInfo? Theme { get; set; }

    protected override View Body =>
        Span.Class("theme")[Theme?.Name ?? "none"];
}

/// <summary>
/// Receives the named cascade, one element deeper than the provider's own bracket, so the value has to
/// travel the tree rather than land on the immediate child.
/// </summary>
public partial class LocaleReader : BodyComponentBase
{
    [CascadingParameter(Name = "locale")]
    public string? Locale { get; set; }

    protected override View Body =>
        Span.Class("locale")[Locale ?? "none"];
}
