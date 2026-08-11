using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.AnalyzerDelivery;

/// <summary>
/// BCF3001, one of the two analyzer-reported diagnostics (<c>Bcf3029.cs</c> carries the other). This shape
/// compiles, the generator translates the body and emits RenderView, so the compilation has no
/// declaration-level error and the analyzer driver runs. Nothing whose failure is a <em>declaration</em>
/// error may be added to this project: a single one anywhere in the compilation would suppress BCF3001
/// along with every other analyzer diagnostic. A second shape that compiles, as BCF3029's does, is fine.
/// </summary>
public partial class Mutating : BodyComponentBase
{
    private int _count;

    protected override View Body => Span[$"{_count++}"];
}
