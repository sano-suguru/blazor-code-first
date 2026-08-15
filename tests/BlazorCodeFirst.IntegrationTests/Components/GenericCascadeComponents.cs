using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// Cascades its own type parameter. The type argument written on <c>Component&lt;T&gt;()</c> is
/// <c>CascadingValue&lt;TValue&gt;</c>, which no closed type the generator could name outright (#382).
/// </summary>
/// <remarks>
/// Every other <c>Component&lt;T&gt;()</c> in these tests names a type that is already closed where it is
/// written, so two things go unheld. The generator has to emit <c>TValue</c> into another file rather than
/// a resolvable named type, and the header it writes for this class has to re-declare
/// <c>&lt;TValue&gt;</c> for that emitted name to bind to anything.
///
/// The reader sits under a <c>&lt;div&gt;</c> so the value has to travel the tree, and it is a separate
/// generic component so its own <c>TValue</c> has to arrive closed too.
/// </remarks>
public partial class GenericCascadeHost<TValue> : BodyComponentBase
{
    [Parameter]
    public TValue Value { get; set; } = default!;

    protected override View Body =>
        Component<CascadingValue<TValue>>()
            .Param(c => c.Value, Value)[
                Div[Component<GenericCascadeReader<TValue>>()]];
}

/// <summary>
/// Receives the cascade at its own type parameter. Blazor matches a cascade by the receiving property's
/// type, so this reads the provided value only if the frames really opened
/// <c>CascadingValue&lt;TValue&gt;</c> at the host's closed argument.
/// </summary>
public partial class GenericCascadeReader<TValue> : BodyComponentBase
{
    [CascadingParameter]
    public TValue? Received { get; set; }

    protected override View Body =>
        Span.Class("received")[Received?.ToString() ?? "none"];
}
