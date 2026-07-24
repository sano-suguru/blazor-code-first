namespace BlazorCompose;

/// <summary>
/// Design-time decoration syntax applied to a <see cref="View"/> in a
/// <see cref="ComposeComponentBase.Body"/> expression.
/// </summary>
/// <remarks>
/// Like the <see cref="UI"/> factories, every member here is inert design-time syntax: the
/// BlazorCompose source generator reads the decoration chain statically and folds it into the owning
/// element's attributes. The members are never meant to run — at runtime they perform no work and
/// return the receiver unchanged, so they must not be invoked directly. Decorations live in a
/// dedicated static class (rather than on <see cref="View"/> itself) to keep <see cref="View"/> an
/// empty marker type.
/// </remarks>
public static class Decorations
{
    /// <summary>Design-time syntax adding a CSS class to the owning element's <c>class</c> attribute.</summary>
    /// <param name="view">The decorated view (an element factory such as Text/Button/VStack).</param>
    /// <param name="class">The CSS class value; any string expression. Chain calls to add more.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static View Class(this View view, string @class) => view;

    /// <summary>Design-time syntax adding an <c>onclick</c> handler to the owning element.</summary>
    /// <param name="view">The decorated view (an element factory such as Div/Span/Button).</param>
    /// <param name="handler">The handler invoked on click; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static View OnClick(this View view, System.Action handler) => view;
}
