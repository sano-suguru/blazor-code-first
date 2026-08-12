using System;
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>BCF3002 (warning): the key selector does not reference the item.</summary>
public partial class Bcf3002Host : BodyComponentBase
{
    private readonly string[] _items = ["a", "b"];

    protected override View Body => ForEach(_items, item => 0, item => Div[item]);
}

/// <summary>BCF3003: the content root is a Fragment, which has no frame to key.</summary>
public partial class Bcf3003Host : BodyComponentBase
{
    private readonly string[] _items = ["a", "b"];

    protected override View Body => ForEach(_items, item => item, item => Fragment(Div[item]));
}

/// <summary>
/// BCF3004: the content is a constructed delegate, which names no callee at the call site.
/// </summary>
/// <remarks>
/// A bare method group is no longer this diagnostic's business: it is read as the call it stands for and
/// answered by the same three-way split every other call gets. The other shape left to BCF3004, a content
/// block with more than one return, is covered in-process instead. <c>DiagnosticDeliveryTests</c> requires
/// exactly one occurrence of an id across the build, so a fixture holds one shape per diagnostic, and the
/// anchor is matched within a line — which the block shape would not fit on.
/// </remarks>
public partial class Bcf3004Host : BodyComponentBase
{
    private readonly string[] _items = ["a", "b"];

    protected override View Body => ForEach(_items, item => item, new Func<string, View>(Render));

    private static View Render(string item) => Div[item];
}
