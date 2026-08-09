using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3023: the <see langword="bool"/> overload of <c>.Attr</c> written on the class channel. One class
/// decoration is the shape whose generated code binds <c>AddAttribute(int, string, bool)</c> and empties
/// the class list; the two-decoration shape, which concatenates instead, is covered in-process by
/// <c>HtmlAttributeGeneratorTests</c>. A real build only needs one of them to prove the rule is
/// reachable.
/// </summary>
public partial class Bcf3023Host : BodyComponentBase
{
    protected override View Body => Div.Attr("class", true)["bcf3023"];
}
