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
public partial class Bcf3026Host : BodyComponentBase
{
    protected override View Body => Div.Clas("card")["bcf3026"];
}
