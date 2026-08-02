using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>BCF3002 (warning): the key selector does not reference the item.</summary>
public partial class Bcf3002Host : ComposeComponentBase
{
    private readonly string[] _items = ["a", "b"];

    protected override View Body => ForEach(_items, item => 0, item => Div[item]);
}

/// <summary>BCF3003: the content root is a Fragment, which has no frame to key.</summary>
public partial class Bcf3003Host : ComposeComponentBase
{
    private readonly string[] _items = ["a", "b"];

    protected override View Body => ForEach(_items, item => item, item => Fragment(Div[item]));
}

/// <summary>BCF3004: the content is a method group rather than an inline expression lambda.</summary>
public partial class Bcf3004Host : ComposeComponentBase
{
    private readonly string[] _items = ["a", "b"];

    protected override View Body => ForEach(_items, item => item, Render);

    private static View Render(string item) => Div[item];
}
