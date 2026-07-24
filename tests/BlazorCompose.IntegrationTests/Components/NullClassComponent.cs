using BlazorCompose;

namespace BlazorCompose.IntegrationTests.Components;

public partial class NullClassComponent : ComposeComponentBase
{
    // Yields null without tripping the null-forgiving-operator or nullable-reference-type analyzers:
    // the property's declared type is nullable, so `.Class(NullClass!)` documents intent explicitly
    // rather than suppressing a warning about an unexpectedly-null non-nullable value.
    private static string? NullClass => null;

    protected override View Body =>
        Html.Div(
            // A single .Class whose expression is null: Blazor omits a null attribute value entirely,
            // so the rendered <span> has no class attribute at all.
            Html.Span("single").Class(NullClass!),
            // N >= 2 chained .Class calls where one is null: the folded class expression is a C# `+`
            // concatenation, and null participates as "", so the class attribute is still emitted.
            Html.Span("multi").Class("a").Class(NullClass!));
}
