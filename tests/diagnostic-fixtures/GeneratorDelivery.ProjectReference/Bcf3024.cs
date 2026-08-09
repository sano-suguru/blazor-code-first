using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3024: a <c>.Bind</c> on <c>class</c> beside a decoration of the class channel. The channel is
/// written with <c>.Class</c> here, the spelling an author reaches for; the <c>.Attr("class", …)</c>
/// spelling and the reverse decoration order are covered in-process by <c>HtmlBindDiagnosticTests</c>,
/// and a real build only needs one of them to prove the rule is reachable.
/// </summary>
public partial class Bcf3024Host : BodyComponentBase
{
    private string _name = "";

    protected override View Body => Div.Class("card").Bind("class", "oninput", () => _name)["bcf3024"];
}
