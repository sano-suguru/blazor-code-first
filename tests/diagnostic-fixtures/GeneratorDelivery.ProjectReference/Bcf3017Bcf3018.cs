using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3017: a getter that is not an inline lambda with an expression body. The method-group spelling of
/// the same mistake is asserted in <c>HtmlBindDiagnosticTests</c> rather than added here, for the reason
/// <c>Bcf3016.cs</c> records: exactly one report per id per fixture project.
/// </summary>
public partial class Bcf3017Host : BodyComponentBase
{
    private string _name = "bcf3017";

    protected override View Body => Input.Bind("value", "oninput", () => { return _name; });
}

/// <summary>BCF3018: a getter body that cannot be assigned to, so no setter can be derived from it.</summary>
public partial class Bcf3018Host : BodyComponentBase
{
    private string Name => "bcf3018";

    protected override View Body => Input.Bind("value", "oninput", () => Name);
}
