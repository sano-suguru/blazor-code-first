using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3025: <c>Slot</c> written where no caller content is received. A component's own <c>Body</c> is the
/// shape an author reaches by accident — <c>Slot</c> reads as if it meant "content goes here" anywhere —
/// and it is the misplacement half of the rule.
/// </summary>
/// <remarks>
/// The arity half (a <c>ContentView</c> part naming its slot zero times or twice) is reported at the
/// declaration instead, and is covered in-process by <c>ComposableContentDiagnosticTests</c>. A real build
/// only needs one shape to prove the descriptor is reachable, and this is the one whose location is
/// interesting: the report points at the <c>Slot</c> itself rather than at the enclosing member.
/// </remarks>
public partial class Bcf3025Host : BodyComponentBase
{
    protected override View Body => Div[Slot];
}
