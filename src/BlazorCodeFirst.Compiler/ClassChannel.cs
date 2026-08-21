using System.Collections.Immutable;
using System.Text;
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
    /// The name of the join the generated class carries for its own use. Private to that class and
    /// spelled long enough that a hand-written member of the same signature is not a case worth
    /// designing against.
    /// </summary>
    internal const string JoinHelperName = "__BlazorCodeFirstJoinClasses";

    /// <summary>
    /// What the channel writes between two decorations. One character serving both paths: the markup path
    /// appends it, and the frame path spells it as a string literal inside the concatenation. The fold's
    /// whole contract is that the two produce the same DOM, so a separator either of them chose for
    /// itself would be a way for them to disagree.
    /// </summary>
    internal const char Separator = ' ';

    /// <summary>
    /// <see cref="Separator"/> as a string literal in generated code, for the join bodies to concatenate
    /// around. Held in a field rather than a <c>const</c> because it cannot be one: a constant
    /// interpolated string needs every hole to be a constant <em>string</em>, and this one is a
    /// <see langword="char"/>.
    /// </summary>
    private static readonly string SeparatorLiteral = $"\"{Separator}\"";

    /// <summary>
    /// What the channel's value is spelled as when it has none: a constant <see langword="null"/> of the
    /// channel's own type, carrying through <see cref="ExpressionTemplate.ToAttributeValueCode"/> the
    /// cast an overloaded argument position needs (#234).
    /// </summary>
    /// <remarks>
    /// Stated here rather than read back from the decorations that folded away. That every term dropped
    /// means the value is nothing, and what nothing is spelled as is the channel's own answer; deriving
    /// it from a term makes it an answer about the input instead. The two agree today only because a
    /// constant <see langword="null"/> is the single reason <see cref="Contributes"/> drops a term, so a
    /// surviving term is never the one read. Widening that rule would point the derivation at a term the
    /// fold chose to keep, and what came out would be neither a diagnostic nor an exception but a
    /// different attribute value (#240).
    /// </remarks>
    private static readonly string AbsentValueCode =
        ExpressionTemplate.NullLiteral.ToAttributeValueCode();

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
    /// <param name="element">The element the decoration would fold onto.</param>
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
    /// Whether <paramref name="term"/> contributes to the channel's value. A constant
    /// <see langword="null"/> does not: it carries no text, and a term that is nothing has no separator to
    /// earn (#236).
    /// </summary>
    /// <remarks>
    /// Asked here rather than at each site that needs it, because both paths have to drop the same terms
    /// or they reach different DOM — the join below, and the markup fold's writer and its predicate. That
    /// is the same reason <see cref="AttributeName"/> and <see cref="Separator"/> are read from here
    /// instead of spelled on the markup path (#193); a rule stated at the site that happens to need it is
    /// a rule the next site does not have to agree with.
    /// </remarks>
    internal static bool Contributes(ExpressionTemplate term) => term.Constant is not NullConstant;

    /// <summary>
    /// The C# expression <paramref name="classes"/> join into, for the one attribute frame the channel
    /// emits. No surviving term is <see cref="AbsentValueCode"/>; one is written as it stands, through
    /// <see cref="ExpressionTemplate.ToAttributeValueCode"/>, which is what the position asks whatever
    /// the term turns out to be; two or more are passed to <see cref="JoinHelperName"/> with each term
    /// parenthesized, so a ternary or a <c>??</c> keeps its own precedence — and that call returns a
    /// <see cref="string"/> whatever its arguments are, so it needs no cast of its own.
    /// </summary>
    /// <remarks>
    /// Terms the compiler can read are settled here rather than left to the join: a constant
    /// <see langword="null"/> is dropped, because a term that is nothing has no separator to earn, and
    /// adjacent constant strings become one literal, because the text they join into is already known.
    /// What survives is the terms whose value exists only at render time.
    /// <para>
    /// How many survive does not decide whether a frame is emitted.
    /// <c>RenderViewEmitter.EmitClassAttribute</c> emits one whenever the element carries any class
    /// decoration, including when every one of them folds away here, because sequence numbers are
    /// allocated to <em>emitted</em> calls (<c>ARCHITECTURE.md</c> §2.7's <c>FrameWidth</c>) and
    /// suppressing the emission would split this case from a constant <see langword="false"/>
    /// <see langword="bool"/>, which is emitted and appends no frame at runtime (#234).
    /// </para>
    /// </remarks>
    /// <param name="classes">
    /// At least one decoration. An element carrying none emits no frame at all, which is the caller's
    /// decision to make and not one an expression can carry: <see cref="AbsentValueCode"/> is a value
    /// that is nothing, and a frame carrying it is still a different output from no frame.
    /// </param>
    /// <param name="joinArity">
    /// The number of terms the returned code joins, or zero when it joins none. The emitter accumulates
    /// the widest of these to know which helper bodies to write; which of them is widest is its question,
    /// not the channel's.
    /// </param>
    internal static string JoinAsCode(EquatableArray<ExpressionTemplate> classes, out int joinArity)
    {
        var terms = ReadableTerms(classes.AsImmutableArray());
        joinArity = 0;

        // Nothing survived the fold, so the channel carries no value and says so itself.
        if (terms.Length == 0)
            return AbsentValueCode;

        if (terms.Length == 1)
            return terms[0].ToAttributeValueCode();

        joinArity = terms.Length;
        return $"{JoinHelperName}({string.Join(", ", terms.Select(static c => $"({c.ToCode()})"))})";
    }

    /// <summary>
    /// The body of the join the generated class carries, for <paramref name="arity"/> terms, as one line
    /// of C#. The emitter writes one per arity from the widest call it made down to two.
    /// </summary>
    /// <remarks>
    /// A null term is not a term: it contributes neither text nor a separator, which is the whole of
    /// #236. Each arm drops one null and defers to the arity below — or, where one term is left, to that
    /// term itself — so several nulls resolve one at a time and the body stays linear in
    /// <paramref name="arity"/> rather than doubling with it.
    /// <para>
    /// The last arm is what the cost rests on. With no null it is one <c>string.Concat</c> over the terms
    /// and the separators, which is what the concatenation this replaced lowered to, so the spelling that
    /// never carried a residue pays what it always paid and the ones that did pay less. Measured against
    /// the rule it replaces by <c>ClassChannelBenchmarks</c>, which is what #236 required before the rule
    /// could change at all: at three terms the call has five arguments and binds to
    /// <c>Concat(params ReadOnlySpan&lt;string?&gt;)</c>, which stack-allocates, where the operator chain
    /// it replaces allocated a <c>string[]</c> unless the leading term happened to be constant.
    /// </para>
    /// </remarks>
    internal static string JoinHelperCode(int arity)
    {
        var names = new string[arity];
        for (var i = 0; i < arity; i++)
            names[i] = $"a{i}";

        var parameters = string.Join(", ", names.Select(static name => $"string? {name}"));
        var code = new StringBuilder($"private static string? {JoinHelperName}({parameters}) =>");

        // "all names but index N", for every N, without re-scanning and re-joining the full array on
        // each pass (#529): each prefix/suffix extends the previous one by a single name instead.
        var prefixes = new string[arity];
        for (var i = 1; i < arity; i++)
            prefixes[i] = i == 1 ? names[0] : $"{prefixes[i - 1]}, {names[i - 1]}";

        var suffixes = new string[arity];
        for (var i = arity - 2; i >= 0; i--)
            suffixes[i] = i == arity - 2 ? names[arity - 1] : $"{names[i + 1]}, {suffixes[i + 1]}";

        for (var dropped = 0; dropped < arity; dropped++)
        {
            var prefix = dropped == 0 ? null : prefixes[dropped];
            var suffix = dropped == arity - 1 ? null : suffixes[dropped];
            var rest = prefix is null ? suffix : suffix is null ? prefix : $"{prefix}, {suffix}";

            // Below two, what is left is a term rather than a call: there is no join of one.
            var withoutIt = arity == 2 ? rest : $"{JoinHelperName}({rest})";
            code.Append($" {names[dropped]} is null ? {withoutIt} :");
        }

        var joined = string.Join($", {SeparatorLiteral}, ", names);
        return code.Append($" string.Concat({joined});").ToString();
    }

    /// <summary>
    /// <paramref name="classes"/> with the terms the compiler can read already settled: constant
    /// <see langword="null"/>s dropped, and runs of adjacent constant strings joined into one term.
    /// </summary>
    /// <remarks>
    /// Only <em>adjacent</em> constants join, because the channel's order is the author's and a constant
    /// on either side of a term that is not one cannot be moved across it.
    /// <para>
    /// Joining the runs is not tidiness, it is what pays for spelling the join as a call. An operator
    /// chain over string literals was constant-folded by the C# compiler; a call is not, so without this
    /// <c>.Class("a").Class("b")</c> would reach <see cref="JoinHelperName"/> at render time for a value
    /// that was known at compile time. It is the frame path's own compensation, which is why the markup
    /// path does not do it: that path writes the text directly and has nothing to fold back.
    /// </para>
    /// <para>
    /// Returns <paramref name="classes"/> unchanged when there is nothing to settle, which is the common
    /// case and the reason this can run on every emission — the same guard, for the same reason, as
    /// <c>ExpressionTemplate.CoalesceLiterals</c>. Rebuilding a lone constant term would allocate a
    /// template byte-identical to the one already in hand.
    /// </para>
    /// </remarks>
    private static ImmutableArray<ExpressionTemplate> ReadableTerms(
        ImmutableArray<ExpressionTemplate> classes)
    {
        if (!HasReadableTerms(classes))
            return classes;

        var terms = ImmutableArray.CreateBuilder<ExpressionTemplate>(classes.Length);
        string? run = null;

        foreach (var @class in classes)
        {
            if (!Contributes(@class))
                continue;

            if (@class.Constant is StringConstant { Text: var text })
            {
                run = run is null ? text : run + Separator + text;
                continue;
            }

            FlushRun(terms, ref run);
            terms.Add(@class);
        }

        FlushRun(terms, ref run);
        return terms.ToImmutable();
    }

    /// <summary>
    /// Whether <paramref name="classes"/> holds anything <see cref="ReadableTerms"/> would settle: a
    /// constant <see langword="null"/> to drop, or two adjacent constant strings to join.
    /// </summary>
    private static bool HasReadableTerms(ImmutableArray<ExpressionTemplate> classes)
    {
        for (var index = 0; index < classes.Length; index++)
        {
            if (!Contributes(classes[index]))
                return true;

            if (index > 0
                && classes[index].Constant is StringConstant
                && classes[index - 1].Constant is StringConstant)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Turns an accumulated run of constant text into one term, and clears the run.</summary>
    private static void FlushRun(ImmutableArray<ExpressionTemplate>.Builder terms, ref string? run)
    {
        if (run is null)
            return;

        terms.Add(ExpressionTemplate.StringLiteral(run));
        run = null;
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
