using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.AnalyzerDelivery;

/// <summary>
/// BCF3001, the one analyzer-reported diagnostic. This shape compiles — the generator translates the
/// body and emits RenderView — so the compilation has no declaration-level error and the analyzer
/// driver runs. Nothing else broken may be added to this project: a single declaration error anywhere
/// in the compilation would suppress BCF3001 along with every other analyzer diagnostic.
/// </summary>
public partial class Mutating : BodyComponentBase
{
    private int _count;

    protected override View Body => Span[$"{_count++}"];
}
