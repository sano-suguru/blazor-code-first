using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

public partial class NullClassComponent : BodyComponentBase
{
    // The suppression is no longer required — .Class takes a string? since #171 — and is kept on purpose.
    // FactoryArguments walks up from a bare null-forgiving operator to the enclosing ArgumentSyntax,
    // because Roslyn elides the `!` from the operation tree, and this fixture plus Div[NullText!] are the
    // only two regression guards that walk has. Dropping the `!` here for being redundant would leave it
    // uncovered on the decoration side.
    private static string? NullClass => null;

    protected override View Body =>
        Div[
            // A single .Class whose expression is null: Blazor omits a null attribute value entirely,
            // so the rendered <span> has no class attribute at all.
            Span.Class(NullClass!)["single"],
            // N >= 2 chained .Class calls where one is null: the folded class expression is a C# `+`
            // concatenation, and null participates as "", so the class attribute is still emitted.
            Span.Class("a").Class(NullClass!)["multi"]];
}
