using System;
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3044: an <c>If</c> branch is a constructed delegate, which names no callee at the call site. A
/// bare method group is no longer this diagnostic's business: it is read as the call it stands for and
/// answered by the same three-way split every other call gets, the same as <c>ForEach</c>'s content
/// (#317).
/// </summary>
public partial class Bcf3044Host : BodyComponentBase
{
    protected override View Body => If(true, new Func<View>(Render));

    private static View Render() => Span["x"];
}
