using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler;

/// <summary>
/// What <see cref="ClassChannel.Admit"/> answered: the channel took the decoration, or the rule that
/// refused it.
/// </summary>
/// <remarks>
/// The two refusals are ordered as the cases are written, and observably so: a
/// <c>.Attr("class", bool)</c> on an element that already carries a bound <c>class</c> breaks both rules
/// at once and reports <see cref="ValueDoesNotJoin"/>, because the value is what has to be rewritten
/// before the collision is even worth naming.
/// </remarks>
internal enum ClassChannelAdmission
{
    /// <summary>The channel takes the decoration.</summary>
    Admitted,

    /// <summary>
    /// The value's type is not one the channel can join as text. Reported as BCF3023, at the value.
    /// </summary>
    ValueDoesNotJoin,

    /// <summary>
    /// The element already carries a <c>.Bind</c> on the channel's name, which does not fold. Reported as
    /// BCF3024, at the decoration name.
    /// </summary>
    NameAlreadyBound,
}

/// <summary>
/// The one attribute name that does not occupy a name once. <c>.Class</c> and <c>.Attr("class", …)</c>
/// both route here rather than to <see cref="ElementTemplateNode.Attributes"/>, and however many of them
/// an element carries, <see cref="ElementTemplateNode.Classes"/> emits a single space-joined frame. That
/// is why <c>class</c> is the exception BCF3010 names, and why it is not an exception to anything else:
/// the channel is what repeats, not the attribute.
/// </summary>
/// <remarks>
/// Routing, admission, collision, and the join all ask about the same name, so they ask it in one place.
/// A rule stated at the site that happens to need it is a rule the next site does not have to agree
/// with, which is how <c>.Attr</c> came to be checked for a value the channel cannot join while
/// <c>.Class</c> was not, and how the channel's name came to be spelled out by hand on the markup path
/// while the frame path read it from here (#193). <c>.Bind("class", …)</c> is the third spelling that
/// reaches the name and the one that does not fold — it emits its own frame from the bindings loop —
/// which is the collision BCF3024 reports (#188).
/// <para>
/// It sits here rather than beside the node records in <c>Model/</c>, although the channel is one of their
/// fields, because it reads a symbol and writes C# text. <c>Model/</c> holds symbol-free value-equal data
/// (see <see cref="RenderNode"/>'s remarks and <c>ARCHITECTURE.md</c> §3), and a file there carrying
/// <c>using Microsoft.CodeAnalysis</c> for anything but a location would make that boundary unreadable.
/// </para>
/// </remarks>
internal static class ClassChannel
{
    /// <summary>The attribute name the channel routes and emits under.</summary>
    internal const string AttributeName = "class";

    /// <summary>
    /// What the channel writes between two decorations. One character serving both paths: the markup path
    /// appends it, and the frame path spells it as a string literal inside the concatenation. The fold's
    /// whole contract is that the two produce the same DOM, so a separator either of them chose for
    /// itself would be a way for them to disagree.
    /// </summary>
    internal const char Separator = ' ';

    /// <summary>
    /// <see cref="Separator"/> as the frame path spells it, between two terms of the concatenation. Held
    /// in a field because it cannot be a <c>const</c>: a constant interpolated string needs every hole to
    /// be a constant <em>string</em>, and this one is a <see langword="char"/>, so writing it inline would
    /// format it afresh for every element carrying two or more decorations.
    /// </summary>
    private static readonly string SeparatorCode = $" + \"{Separator}\" + ";

    /// <summary>
    /// Whether a decoration written with <paramref name="name"/> folds into the channel. Ordinal, like
    /// every other name comparison in the compiler: HTML attribute names are matched as written.
    /// </summary>
    internal static bool Owns(string? name) =>
        string.Equals(name, AttributeName, System.StringComparison.Ordinal);

    /// <summary>
    /// Whether the channel takes a decoration of <paramref name="valueType"/> onto
    /// <paramref name="element"/>, and when it does not, which rule refused it.
    /// </summary>
    /// <param name="valueType">
    /// The type of the decoration's value parameter on the overload the C# compiler picked, not the type
    /// of the value expression written there. The channel's question is which spelling of the surface was
    /// reached, and that is what the resolved overload answers — the same reading
    /// <c>KnownSymbols.TryGetBindParameters</c> takes of a <c>.Bind</c>'s setter shape.
    /// </param>
    /// <remarks>
    /// Both folding spellings ask this, so neither can be the one that forgets. The allow-list is the
    /// point: a channel that joins its terms as text has exactly one value shape it can carry, and
    /// naming that shape closes the question against overloads the surface does not have yet, where
    /// naming the shapes it refuses would answer <see cref="ClassChannelAdmission.Admitted"/> for the
    /// next one added (#193).
    /// </remarks>
    internal static ClassChannelAdmission Admit(ElementTemplateNode element, ITypeSymbol valueType) =>
        !JoinsAsText(valueType) ? ClassChannelAdmission.ValueDoesNotJoin
        : IsBound(element) ? ClassChannelAdmission.NameAlreadyBound
        : ClassChannelAdmission.Admitted;

    /// <summary>
    /// Whether <paramref name="element"/> already carries decorations folded into the channel. The other
    /// half of BCF3024, asked from the binding's side: a <c>.Bind</c> does not fold, so it collides with
    /// the whole channel rather than with a particular decoration, and it is only by asking from both
    /// sides that the report stops depending on which of the two was written first.
    /// </summary>
    internal static bool IsFolded(ElementTemplateNode element) => element.Classes.Length > 0;

    /// <summary>
    /// The C# expression <paramref name="classes"/> join into, for the one attribute frame the channel
    /// emits. A single decoration is written as it stands; two or more are concatenated around
    /// <see cref="Separator"/> with each term parenthesized, so a ternary or a <c>??</c> keeps its own
    /// precedence. When every term is a string literal the C# compiler constant-folds the result back to
    /// one string.
    /// </summary>
    /// <param name="classes">
    /// At least one decoration. An element carrying none emits no frame at all, which is the caller's
    /// decision to make: there is no expression that stands for an absent attribute.
    /// </param>
    internal static string JoinAsCode(EquatableArray<ExpressionTemplate> classes)
    {
        var array = classes.AsImmutableArray();
        return array.Length == 1
            ? array[0].ToCode()
            : string.Join(SeparatorCode, array.Select(static c => $"({c.ToCode()})"));
    }

    /// <summary>
    /// Whether a value of <paramref name="valueType"/> has a meaning as one term of a space-joined list.
    /// A <see langword="string"/> does and nothing else does: the channel's value is text by
    /// construction, so a type that has to be formatted to become text either means two things depending
    /// on how many other decorations the element carries (#159) or means nothing at all.
    /// </summary>
    private static bool JoinsAsText(ITypeSymbol valueType) =>
        valueType.SpecialType == SpecialType.System_String;

    /// <summary>
    /// Whether <paramref name="element"/> already carries a <c>.Bind</c> on the channel's name. That
    /// binding emits its own attribute frame and joins nothing, so a decoration arriving at a channel
    /// this answers <see langword="true"/> for would leave the element carrying <c>class</c> twice, which
    /// is BCF3024.
    /// </summary>
    /// <remarks>
    /// Only the bindings are read. Neither of the other two channels can hold this name — both folding
    /// spellings route here instead of to <see cref="ElementTemplateNode.Attributes"/>, and an event name
    /// that does not start with <c>on</c> is BCF3019 — so asking the bindings directly is the same answer
    /// a sweep of all three would give, without depending on either of those rules being enforced
    /// somewhere else.
    /// </remarks>
    private static bool IsBound(ElementTemplateNode element)
    {
        foreach (var bind in element.Bindings)
        {
            if (Owns(bind.AttributeName))
                return true;
        }

        return false;
    }
}
