using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// Receives delegate-typed parameters whose declared types are not the natural type of what the caller
/// writes. <c>Predicate&lt;int&gt;</c> is the one that matters: a lambda's natural type is
/// <c>Func&lt;int, bool&gt;</c>, so a value that reaches Blazor without its declared type fails when the
/// renderer assigns the property, not when anything is compiled (#377).
/// </summary>
public sealed class DelegateParameterTarget : ComponentBase
{
    [Parameter] public Func<int, string>? Formatter { get; set; }
    [Parameter] public Predicate<int>? Match { get; set; }
    [Parameter] public int Value { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "result");
        builder.AddContent(
            2,
            $"{Formatter?.Invoke(Value) ?? "no formatter"}/{Match?.Invoke(Value).ToString() ?? "no match"}");
        builder.CloseElement();
    }
}

/// <summary>
/// Writes the one spelling that carries a natural type into the <c>object?</c> slot: a lambda that writes
/// its parameter type. It binds in generated code with or without the cast, and the type it carries is
/// <c>Func&lt;int, bool&gt;</c> rather than the declared <c>Predicate&lt;int&gt;</c>, so this host is the
/// one that fails at render rather than at build when the cast is dropped.
/// </summary>
/// <remarks>
/// Separate from <see cref="UntypedDelegateParameterHost"/> deliberately. The two failures in #377 are
/// reached by different spellings, and a host carrying both fails to compile first, which would leave the
/// render-time half with no test that can observe it. A method group belongs to the other host: it does not
/// convert to <c>object</c> either (CS8974, measured), so it fails at build like the untyped lambda.
/// </remarks>
public partial class DelegateParameterHost : BodyComponentBase
{
    protected override View Body =>
        Div[
            Component<DelegateParameterTarget>()
                .Param(t => t.Value, 7)
                .Param(t => t.Match, (int x) => x > 0),
            Component<DelegateParameterTarget>()
                .Param(t => t.Value, -1)
                .Param(t => t.Match, (int x) => x > 0)];
}

/// <summary>
/// Writes the two spellings that carry no natural type the <c>object?</c> slot can take: a lambda whose
/// parameter type is left to inference, and a method group. Neither binds in generated code without the
/// cast, so what holds this half is that the test project compiles at all.
/// </summary>
public partial class UntypedDelegateParameterHost : BodyComponentBase
{
    protected override View Body =>
        Div[
            Component<DelegateParameterTarget>()
                .Param(t => t.Value, 7)
                .Param(t => t.Formatter, x => $"[{x}]"),
            Component<DelegateParameterTarget>()
                .Param(t => t.Value, -1)
                .Param(t => t.Formatter, Format)
                .Param(t => t.Match, IsPositive)];

    private static string Format(int x) => $"<{x}>";

    private static bool IsPositive(int x) => x > 0;
}
