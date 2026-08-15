using System.Collections.Generic;
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3032: a keyed <c>ForEach</c> whose content root writes its own <c>.Key</c>. Both keys reach the
/// same element frame, and <c>SetKey</c> keeps only the last one written.
/// </summary>
public partial class Bcf3032Host : BodyComponentBase
{
    private readonly List<string> _items = ["a", "b"];

    protected override View Body =>
        ForEach(_items, item => item, item => Div.Key(item)[item]);
}
