using System.Globalization;
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3031: a <c>.Bind</c> format written for a type the framework declares no format-taking converter
/// for. <c>int</c> is the shape an author reaches for, having a numeric format language of its own; the
/// accepted side of the rule and the other rejected types are covered in-process by
/// <c>HtmlBindDiagnosticTests</c>, and a real build only needs one of them to prove the rule is reachable.
/// </summary>
public partial class Bcf3031Host : BodyComponentBase
{
    private int _age;

    // No children: input is a void element, and giving it any would report BCF3016 beside the diagnostic
    // this fixture is about.
    protected override View Body =>
        Input.Bind("value", "oninput", () => _age, "D4", CultureInfo.InvariantCulture);
}
