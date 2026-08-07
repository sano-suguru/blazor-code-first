using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3019: an event name that does not begin with the required <c>on</c> prefix. <c>.On</c> and
/// <c>.Bind</c> both reach this check, but a real build only needs one of them to prove it is
/// reachable at all; the other route is covered in-process by <c>HtmlBindDiagnosticTests</c>.
/// </summary>
public partial class Bcf3019Host : BodyComponentBase
{
    protected override View Body => Button.On("click", () => { });
}
