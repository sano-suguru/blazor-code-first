using System.Collections.Generic;
using System.Linq;
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// A child list spliced from a projection, mixed with siblings written beside it (#172).
/// </summary>
/// <remarks>
/// The compiler tests assert that this spelling generates the same source as
/// <c>ForEach(source, key: null, …)</c>. What they cannot show is that the frames it emits render, and
/// that the siblings around the splice keep their order once they do.
/// </remarks>
public partial class SplicedListComponent : BodyComponentBase
{
    private readonly List<string> _items = ["one", "two"];

    protected override View Body =>
        Ul[[
            Li["first"],
            .. _items.Select(i => Li.Class("row")[i]),
            Li["last"],
        ]];
}
