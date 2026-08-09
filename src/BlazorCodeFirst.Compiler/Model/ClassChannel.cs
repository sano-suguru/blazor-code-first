namespace BlazorCodeFirst.Compiler;

/// <summary>
/// The one attribute name that does not occupy a name once. <c>.Class</c> and <c>.Attr("class", …)</c>
/// both route here rather than to <see cref="ElementTemplateNode.Attributes"/>, and however many of them
/// an element carries, <see cref="ElementTemplateNode.Classes"/> emits a single space-joined frame. That
/// is why <c>class</c> is the exception BCF3010 names, and why it is not an exception to anything else:
/// the channel is what repeats, not the attribute.
/// </summary>
/// <remarks>
/// Routing, emission, and the checks that guard the channel all ask about the same name, so they ask it
/// in one place. <c>.Bind("class", …)</c> is the third spelling that reaches the name and the one that
/// does not fold — it emits its own frame from the bindings loop — which is the collision BCF3024
/// reports (#188).
/// </remarks>
internal static class ClassChannel
{
    /// <summary>The attribute name the channel routes and emits under.</summary>
    internal const string AttributeName = "class";

    /// <summary>
    /// Whether a decoration written with <paramref name="name"/> folds into the channel. Ordinal, like
    /// every other name comparison in the compiler: HTML attribute names are matched as written.
    /// </summary>
    internal static bool Owns(string? name) =>
        string.Equals(name, AttributeName, System.StringComparison.Ordinal);
}
