using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3027: a component member shadowing a curated element helper. <c>Data</c> is a parameter here, so it
/// wins simple-name lookup over <c>Html.Data</c>, and <c>Data["Heading"]</c> reads the character indexer of
/// a <c>string</c> instead of opening a <c>&lt;data&gt;</c> element.
/// </summary>
/// <remarks>
/// The delivery claim is the whole point of this fixture. C# raises CS1503 on the index argument, and that
/// error never reaches the author: this class carries CS0534 because no <c>RenderView</c> was generated, and
/// <c>csc</c> stops after the declaration stage without binding method bodies. Only a real build shows that,
/// so the in-process assertions in <c>BracketSurfaceDiagnosticTests</c> cannot stand in for it —
/// <c>Compilation.GetDiagnostics()</c> binds bodies unconditionally and reports a CS1503 no author sees.
/// </remarks>
public partial class Bcf3027Host : BodyComponentBase
{
    [Parameter] public string Data { get; set; } = "";

    protected override View Body => Div[Data["Heading"]];
}
