using BlazorCompose;
using static BlazorCompose.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>BC3002 (warning): the key selector does not reference the item.</summary>
public partial class Bc3002Host : ComposeComponentBase
{
    private readonly string[] _items = ["a", "b"];

    protected override View Body => ForEach(_items, item => 0, item => Div(item));
}

/// <summary>BC3003: the content root is a Fragment, which has no frame to key.</summary>
public partial class Bc3003Host : ComposeComponentBase
{
    private readonly string[] _items = ["a", "b"];

    protected override View Body => ForEach(_items, item => item, item => Fragment(Div(item)));
}

/// <summary>BC3004: the content is a method group rather than an inline expression lambda.</summary>
public partial class Bc3004Host : ComposeComponentBase
{
    private readonly string[] _items = ["a", "b"];

    protected override View Body => ForEach(_items, item => item, Render);

    private static View Render(string item) => Div(item);
}
