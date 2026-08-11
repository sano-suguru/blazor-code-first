using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3026: a decoration name the runtime does not declare. Nothing declares <c>Clas</c>, so the call binds
/// to nothing and CS1061 is the C# error that would name the misspelling. That error never reaches the
/// author: this class carries CS0534 because no <c>RenderView</c> was generated, and <c>csc</c> stops after
/// the declaration stage without binding method bodies. This fixture is what establishes that BCF3026 is
/// delivered where CS1061 is not.
/// </summary>
/// <remarks>
/// The misspelling is the shape this fixture carries, and the other shape BCF3026 covers, a bound extension
/// method on <c>ElementView</c> that the runtime does not declare, is covered in-process instead. Not for
/// want of value: <c>DiagnosticDeliveryTests</c> requires exactly one occurrence of an id across the build,
/// so a fixture holds one shape per diagnostic. The misspelling is the one worth spending it on, because it
/// is the shape with a C# error (CS1061) that the declaration-stage cutoff suppresses. The bound shape
/// raises no C# error at all, so nothing about its delivery differs from any other generator report.
/// </remarks>
public partial class Bcf3026Host : BodyComponentBase
{
    protected override View Body => Div.Clas("card")["bcf3026"];
}
