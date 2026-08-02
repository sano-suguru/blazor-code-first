using System.Collections.Generic;
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// A generic Compose component. Text-level assertions on the generated header cannot prove the generated
/// part actually joins the user's generic type at runtime, so this renders through bUnit with a real
/// closed type argument.
/// </summary>
public partial class GenericListComponent<TItem> : BodyComponentBase
    where TItem : notnull
{
    [Parameter]
    public IReadOnlyList<TItem> Items { get; set; } = [];

    protected override View Body =>
        Ul[ForEach(Items, item => item, item => Li[item.ToString()!])];
}
