using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>BCF3016: children given to a void element through a curated helper.</summary>
/// <remarks>
/// The other spelling of the same mistake, <c>Element("img")["…"]</c>, is asserted in
/// <c>VoidElementDiagnosticTests</c> rather than added here. <c>DiagnosticDeliveryTests</c> requires
/// exactly one report per id per fixture project, so a second shape in this project would fail the test
/// this fixture exists for. The split is also the right one: this layer proves the diagnostic reaches an
/// author building an ordinary project, and the in-process layer proves both surface paths reduce to the
/// same tag check.
/// </remarks>
public partial class Bcf3016Host : BodyComponentBase
{
    protected override View Body => Img.Src("/bcf3016.png")["bcf3016"];
}
