using System.Collections.Immutable;
using System.Text;
using BlazorCodeFirst.Compiler.Analysis;

namespace BlazorCodeFirst.Compiler.Generation;

/// <summary>
/// Decides which node subtrees can be emitted as one <c>AddMarkupContent</c> frame instead of a run of
/// element and text frames, and writes that markup (ARCHITECTURE.md §2.7).
/// </summary>
/// <remarks>
/// <para>
/// The predicate and the writer live together because they are two faces of one rule: what can be folded
/// and how it is folded. Splitting them would put the obligation to agree across a file boundary.
/// </para>
/// <para>
/// Foldability requires a node's <em>value</em> to be a compile-time constant, which is strictly narrower
/// than the SSC classification (§2.3): <c>Span[$"Count: {Count}"]</c> is SSC but not constant.
/// </para>
/// </remarks>
internal static class StaticMarkupSerializer
{
    /// <summary>
    /// Tags that the allow-list would admit but whose HTML text interpretation differs from an ordinary
    /// element, so serializing them cannot be made DOM-equivalent to the element path by escaping alone.
    /// <c>pre</c> and <c>textarea</c> lose a newline immediately after the open tag; <c>textarea</c> is
    /// escapable raw text, so a child element would parse as literal text; <c>iframe</c> is parsed by the
    /// generic raw text algorithm, so character references are not resolved. The raw-text tags that carry
    /// the same hazard (<c>script</c>, <c>style</c>, <c>title</c>, <c>svg</c>) need no entry: none is
    /// curated, so the allow-list already excludes them.
    /// </summary>
    private static readonly HashSet<string> TextInterpretingTags =
        new(System.StringComparer.Ordinal) { "pre", "textarea", "iframe" };

    /// <summary>
    /// Whether <paramref name="node"/> and everything under it can be serialized to markup whose parse
    /// yields the same DOM as the frame path.
    /// </summary>
    public static bool IsFoldable(RenderNode node) => node switch
    {
        TextContentNode text => IsFoldableConstantString(text.Content),
        ElementNode element => IsFoldableElement(element),
        FragmentNode fragment => AreAllFoldable(fragment.Children),

        // An expansion emits C# local declarations, which cannot sit inside markup. It is foldable only
        // when every local is constant-initialized, because then the declarations are side-effect free and
        // can be dropped entirely.
        ExpansionNode expansion =>
            AreAllConstant(expansion.Locals) && IsFoldable(expansion.Body),

        // A component frame is not markup; If and ForEach open regions and carry runtime control flow;
        // RenderFragmentContentNode places an externally supplied fragment.
        //
        // RawMarkupNode is excluded although its content may be constant markup. It is already one markup
        // frame, so folding it alone wins nothing, and merging it into an adjacent run is unsafe: an
        // unbalanced string such as Raw("<i>") would, in the run's single parse, reparent the following
        // siblings inside the <i>. That is a parse-unit problem, not a trust-boundary one.
        _ => false,
    };

    /// <summary>
    /// Serializes a run of consecutive foldable siblings into one markup string, appending it to
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">Receives the markup. Written into, never cleared, and left as it stands
    /// when the caller decides not to take the fold.</param>
    /// <param name="run">Nodes that all satisfy <see cref="IsFoldable"/>.</param>
    /// <returns>
    /// The number of frames the element path would have emitted for the same run. The count is advisory:
    /// <see cref="RenderViewEmitter"/> uses it only to decide whether folding is a win (a run worth less
    /// than two frames is emitted node by node). It takes no part in sequence arithmetic, because a markup
    /// frame consumes exactly one sequence number whatever the count says, so a disagreement with the
    /// emitter costs a missed or pointless fold and can never corrupt numbering.
    /// </returns>
    /// <exception cref="System.NotSupportedException">
    /// A node in <paramref name="run"/> is not foldable. Callers partition on <see cref="IsFoldable"/>
    /// first; reaching here means the two have drifted, which is a bug and not a case to absorb, matching
    /// the exhaustiveness contract in <c>RenderViewEmitter.EmitNode</c>. The builder may already hold part
    /// of the run when this throws, which costs nothing: the exception leaves the generator.
    /// </exception>
    /// <remarks>
    /// The frame count is only knowable by walking the run, and the walk that knows it is the one that
    /// writes: which attributes append a frame is decided case by case as they are serialized, so a
    /// separate counting pass would be a second implementation of that rule and free to disagree with this
    /// one. Returning the count rather than the string lets <c>RenderViewEmitter</c> learn it, discover the
    /// fold is not worth taking, and abandon the builder without paying for the copy
    /// <see cref="StringBuilder.ToString()"/> makes — which is what the discarded case always was.
    /// </remarks>
    public static int WriteTo(StringBuilder builder, ImmutableArray<RenderNode> run)
    {
        var absorbed = 0;
        foreach (var node in run)
            WriteNode(builder, node, ref absorbed);

        return absorbed;
    }

    private static void WriteNode(StringBuilder builder, RenderNode node, ref int absorbed)
    {
        switch (node)
        {
            case TextContentNode text:
                AppendEscapedText(builder, ConstantTextOf(text.Content, node));
                absorbed++;
                return;

            case ElementNode element:
                WriteElement(builder, element, ref absorbed);
                return;

            case FragmentNode fragment:
                foreach (var child in fragment.Children)
                    WriteNode(builder, child, ref absorbed);
                return;

            // The locals are dropped: this case is reached only when every initializer is a compile-time
            // constant, so the declarations are side-effect free and nothing observable depends on them.
            case ExpansionNode expansion:
                WriteNode(builder, expansion.Body, ref absorbed);
                return;

            default:
                throw new System.NotSupportedException(
                    $"'{node.GetType().Name}' is not foldable; the caller must partition on IsFoldable first.");
        }
    }

    private static void WriteElement(StringBuilder builder, ElementNode element, ref int absorbed)
    {
        builder.Append('<').Append(element.Tag);
        absorbed++;

        if (element.Classes.Length > 0)
        {
            // All .Class decorations collapse into one class attribute (ARCHITECTURE.md §2.7(A)), which is
            // one frame however many there are. The name and the separator come from the channel, not from
            // literals written here: this markup and the emitter's join have to reach the same DOM, so
            // neither of them gets to spell them for itself (#193). Which terms are terms at all is read
            // from the channel for the same reason — a constant null takes its separator with it here too
            // (#236) — and when every term is one, no attribute is written at all, which is what
            // AddAttribute does with the null the frame path hands it.
            var written = false;
            foreach (var @class in element.Classes)
            {
                if (!ClassChannel.Contributes(@class))
                    continue;

                if (written)
                {
                    builder.Append(ClassChannel.Separator);
                }
                else
                {
                    builder.Append(' ').Append(ClassChannel.AttributeName).Append("=\"");
                    written = true;
                }

                AppendEscapedAttributeValue(builder, ConstantTextOf(@class, element));
            }

            if (written)
                builder.Append('"');

            // Counted whether or not anything was written, because the frame path emits AddAttribute for
            // any class decoration at all, and this count exists to say what that path would have emitted.
            absorbed++;
        }

        foreach (var attribute in element.Attributes)
        {
            // Each case writes what AddAttribute would have produced, and counts a frame only where
            // AddAttribute appends one. A constant null and a false bool append nothing at all, so they
            // write no markup and absorb no frame; a true bool appends a frame whose value reaches the
            // DOM as an empty string (measured, #158), so it is written as name="".
            var value = attribute.Value.Constant switch
            {
                NullConstant or BooleanConstant { Value: false } => null,
                BooleanConstant { Value: true } => string.Empty,
                StringConstant constant => constant.Text,
                _ => throw new System.NotSupportedException(
                    $"Attribute '{attribute.Name}' carries a value the fold cannot serialize; "
                        + "the caller must partition on IsFoldable first."),
            };

            if (value is null)
                continue;

            builder.Append(' ').Append(attribute.Name).Append("=\"");
            AppendEscapedAttributeValue(builder, value);
            builder.Append('"');
            absorbed++;
        }

        // What AddAttribute(seq, "bcf-xxxxxxxx") writes on the frame path (RenderViewEmitter), as a
        // bare attribute in the folded markup string instead: no value, matching what the browser
        // parses a presence-only attribute frame into. Counted here for the same reason every other
        // branch in this method counts: the frame path emits one more Attribute frame on a scoped
        // element, so WriteTo's caller (RenderViewEmitter.TryEmitFolded) must see one more absorbed
        // frame or it would under-count what folding actually replaced.
        if (element.CssScope is { } cssScope)
        {
            builder.Append(' ').Append(cssScope);
            absorbed++;
        }

        builder.Append('>');

        if (KnownSymbols.IsVoidTag(element.Tag))
            return;

        foreach (var child in element.Children)
            WriteNode(builder, child, ref absorbed);

        builder.Append("</").Append(element.Tag).Append('>');
    }

    private static string ConstantTextOf(ExpressionTemplate template, RenderNode owner) =>
        template.Constant is StringConstant constant
            ? constant.Text
            : throw new System.NotSupportedException(
                $"'{owner.GetType().Name}' carries no constant string; the caller must partition on IsFoldable first.");

    /// <summary>
    /// Escapes text content. <c>&gt;</c> is not strictly required outside an attribute value, but it is
    /// escaped anyway so no sequence of written text can be read as closing a construct.
    /// </summary>
    private static void AppendEscapedText(StringBuilder builder, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                default: builder.Append(c); break;
            }
        }
    }

    /// <summary>Escapes an attribute value written inside double quotes.</summary>
    private static void AppendEscapedAttributeValue(StringBuilder builder, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '"': builder.Append("&quot;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                default: builder.Append(c); break;
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="template"/>'s value is a constant string this markup can carry unchanged.
    /// Asked of a text node's content and of each term of the class channel, which are the two places a
    /// value reaches markup as text rather than as an attribute value with its own constant cases.
    /// </summary>
    private static bool IsFoldableConstantString(ExpressionTemplate template) =>
        template.Constant is StringConstant { Text: var value } && CanRoundTrip(value);

    private static bool IsFoldableElement(ElementNode element)
    {
        if (!IsFoldableTag(element.Tag))
            return false;

        // An event is an attribute frame whose value is an EventCallback; markup cannot express it.
        if (element.Events.Length > 0)
            return false;

        // A binding is two such frames plus SetUpdatesAttributeName, none of which markup can express.
        // Its value is never a compile-time constant either, being a field or property read, but that is
        // not what disqualifies it: folding here would drop the binding silently and leave a plain
        // attribute behind, which is the one failure this predicate must not be able to produce.
        if (element.Bindings.Length > 0)
            return false;

        // A key is not markup. SetKey writes into the open element frame, and a markup frame has no frame
        // to write into, so folding would drop the key and leave the element looking unkeyed. Same shape
        // as the binding case above: what disqualifies it is the channel, not the value (§2.7(E)).
        if (element.Key is not null)
            return false;

        // A reference capture is a frame carrying a delegate, which markup cannot express any more than it
        // can express an event handler. Unlike the key it also has a width to lose: folding would drop the
        // frame and the sequence number with it.
        if (element.Ref is not null)
            return false;

        // A named event has no markup spelling either, same reason as the key: folding would erase the
        // AddNamedEvent call and leave the element looking like an ordinary form with no route back.
        if (element.FormName is not null)
            return false;

        // A splat's dictionary is a runtime value with no markup spelling at all, same shape as the
        // three checks above: folding would silently drop the whole AddMultipleAttributes call and
        // every attribute the author wrote .Attrs to carry, leaving output that matches nothing the
        // source asked for (ARCHITECTURE.md Appendix B.14, revised #387).
        if (element.AttributesSplat is not null)
            return false;

        // The channel admits nothing but a string in the first place (ClassChannel.Admit), so the question
        // left here is not the value's type but whether it is a constant this markup can carry: a constant
        // null has no text to join and no attribute to write — the frame path drops it too (#236), so both
        // paths reach the same DOM without it — and a non-constant has no text at all. What is left once
        // the nulls are gone is the same question a text node answers, so it is asked with the same
        // predicate.
        foreach (var @class in element.Classes)
        {
            if (!ClassChannel.Contributes(@class))
                continue;

            if (!IsFoldableConstantString(@class))
                return false;
        }

        foreach (var attribute in element.Attributes)
        {
            if (!IsSafeAttributeName(attribute.Name))
                return false;

            var foldable = attribute.Value.Constant switch
            {
                // Written by omitting the attribute, which is what AddAttribute does with either.
                NullConstant or BooleanConstant { Value: false } => true,

                // Written as name="", which reaches the same DOM AddAttribute gives a true bool
                // (measured in Chromium, #158).
                BooleanConstant { Value: true } => true,

                StringConstant constant => CanRoundTrip(constant.Text),

                // Every other constant renders under the runtime's formatting culture, which the
                // compiler cannot know, so it stays on the element path. Same shape as CanRoundTrip's
                // refusals: the cost is a missed fold, and the reason is a measurement (#158).
                RuntimeFormattedConstant => false,

                // Not a compile-time constant at all.
                _ => false,
            };

            if (!foldable)
                return false;
        }

        // Children on a void element are BCF3016, so this list is empty for a void tag; the predicate
        // does not depend on that being enforced elsewhere.
        if (KnownSymbols.IsVoidTag(element.Tag) && element.Children.Length > 0)
            return false;

        return AreAllFoldable(element.Children);
    }

    private static bool IsFoldableTag(string tag)
    {
        if (TextInterpretingTags.Contains(tag))
            return false;

        return KnownSymbols.IsCuratedTag(tag)
            || KnownSymbols.IsVoidTag(tag)
            || IsCustomElementName(tag);
    }

    /// <summary>
    /// Whether <paramref name="tag"/> is a custom element name: it starts with an ASCII lowercase letter,
    /// contains at least one hyphen, and is otherwise ASCII lowercase letters, digits, hyphen, underscore
    /// or period. Deliberately narrower than the HTML standard's production, which admits a wide range of
    /// non-ASCII characters: the fold predicate is conservative on purpose, and refusing an exotic name
    /// costs a missed optimisation rather than correctness.
    /// </summary>
    private static bool IsCustomElementName(string tag)
    {
        if (tag.Length == 0 || tag[0] < 'a' || tag[0] > 'z')
            return false;

        var hasHyphen = false;
        for (var index = 1; index < tag.Length; index++)
        {
            var c = tag[index];
            if (c == '-')
            {
                hasHyphen = true;
                continue;
            }

            var isAllowed = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.';
            if (!isAllowed)
                return false;
        }

        return hasHyphen;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is safe to write as an attribute name: non-empty ASCII letters,
    /// digits, hyphen, underscore, colon or period, starting with a letter, underscore or colon. Anything
    /// else could close the tag or open a second attribute.
    /// </summary>
    /// <remarks>
    /// The first character is restricted further than the rest because the two paths disagree about a
    /// name the markup path accepts. <c>&lt;div 9x="1"&gt;</c> parses to an element carrying a <c>9x</c>
    /// attribute, while the element path's <c>setAttribute("9x", …)</c> throws
    /// <c>InvalidCharacterError</c> — the DOM applies the XML Name production, the parser does not. Names
    /// starting with a digit, hyphen or period are refused for that reason. There is no breakout risk
    /// either way; this closes a behavioral divergence between the two paths, which is what the fold
    /// predicate exists to prevent.
    /// </remarks>
    private static bool IsSafeAttributeName(string name)
    {
        if (name.Length == 0)
            return false;

        var first = name[0];
        var isValidStart = first is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_' or ':';
        if (!isValidStart)
            return false;

        foreach (var c in name)
        {
            var isAllowed = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
                or '-' or '_' or ':' or '.';
            if (!isAllowed)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="value"/> reaches the DOM unchanged by the markup path, so that folding it
    /// produces the same DOM the element path's <c>createTextNode</c> and <c>setAttribute</c> produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four values are refused, and every one of them was measured in Chromium through the browser gate
    /// (<c>FoldParityView</c> and <c>fold-parity.spec.ts</c>) rather than derived from the parsing
    /// specification. That standard was set when the carriage return was found: reading the specification
    /// produced the right answer there and the wrong one twice below, so a refusal in this predicate is
    /// expected to name a measurement.
    /// </para>
    /// <para>
    /// <b>A carriage return.</b> Input-stream preprocessing normalizes CRLF and a lone CR to LF before
    /// tokenization, and that applies to fragment parsing on a <c>&lt;template&gt;</c> as much as to a
    /// document, so the markup path yields <c>"a\nb"</c> where <c>setAttribute</c> and
    /// <c>createTextNode</c> keep <c>"a\rb"</c>. This is by far the most reachable of the four: any
    /// verbatim string literal in a file checked out with CRLF line endings carries one. Escaping cannot
    /// rescue it — <c>&amp;#13;</c> is resolved after preprocessing and would survive, but only inside an
    /// attribute value, since in text content the parser has already folded the character away — so
    /// admitting a CR would need two escaping rules that disagree, for one character.
    /// </para>
    /// <para>
    /// <b>A NUL.</b> It diverges in two different shapes, and neither is the one this comment used to
    /// claim. Handling is spread across tokenization and tree construction rather than preprocessing:
    /// measured, the markup path replaces a NUL with U+FFFD in an attribute value and drops it entirely
    /// from text content, while the element path keeps it in both. The character-reference route the
    /// earlier note also cited (<c>&amp;#0;</c> resolving to U+FFFD) cannot arise at all, because
    /// <see cref="AppendEscapedText"/> and <see cref="AppendEscapedAttributeValue"/> turn <c>&amp;</c>
    /// into <c>&amp;amp;</c>, so no character reference can ever form out of a value.
    /// </para>
    /// <para>
    /// <b>A lone surrogate.</b> Refused conservatively, and deliberately kept although no divergence
    /// backs it. The specification makes a lone surrogate in the input stream a parse error and nothing
    /// more, and a parse error does not rewrite the stream — so the earlier claim that it was refused
    /// "for the same reason" as a NUL was wrong. Measured end to end, it never reaches the parser in the
    /// first place: .NET's UTF-8 encoding of the render batch replaces it with U+FFFD, so both paths
    /// deliver U+FFFD and agree. The check costs a missed fold on a value no author writes by accident,
    /// and <c>LoneSurrogateProbe</c> keeps the "they agree" half under measurement, so if that ever stops
    /// holding the reason will be visible rather than assumed.
    /// </para>
    /// <para>
    /// <b>A leading U+FEFF.</b> The one refusal here that no parsing stage explains, and the one #150's
    /// sweep found. The browser strips a byte order mark in first position when it decodes each frame
    /// string of the render batch, and keeps it anywhere else, so the divergence is positional rather
    /// than character-based — and folding is precisely what moves the position. Unfolded, a text or
    /// attribute value is its own frame string and a leading BOM is stripped; folded, the same value sits
    /// inside a larger markup string that opens with <c>&lt;</c>, and it survives. Measured on all three
    /// containers of <c>LeadingByteOrderMarkProbe</c>, including a <c>Raw</c> string that itself begins
    /// with the BOM and loses it, which is what identifies the rule as positional instead of
    /// "the element path drops a BOM".
    /// </para>
    /// <para>
    /// <b>What was swept, and found safe (#150).</b> The rest of input-stream preprocessing rewrites
    /// nothing: newline normalization is the only transforming step in that stage, and surrogates,
    /// noncharacters, and controls other than ASCII whitespace and NUL are classified as parse errors,
    /// which leave the stream alone. Measured accordingly and admitted: C0 controls, DEL, NEL, NBSP,
    /// U+2028 and U+2029, BMP and astral noncharacters, an interior BOM, U+FFFD itself, tab, line feed,
    /// repeated spaces, and correctly paired astral characters. <c>SweptCharactersProbe</c> holds one
    /// representative of each class under measurement, because a finding of "nothing here diverges" is
    /// worth nothing unless something keeps checking it.
    /// </para>
    /// </remarks>
    private static bool CanRoundTrip(string value)
    {
        // Position-dependent, so it is checked once here rather than inside the loop: only a BOM in
        // first position is at risk, and only because folding is what moves it out of that position.
        if (value.Length > 0 && value[0] == '\uFEFF')
            return false;

        for (var index = 0; index < value.Length; index++)
        {
            var c = value[index];
            if (c is '\0' or '\r')
                return false;

            if (char.IsHighSurrogate(c))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    return false;

                index++;
                continue;
            }

            if (char.IsLowSurrogate(c))
                return false;
        }

        return true;
    }

    private static bool AreAllFoldable(EquatableArray<RenderNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!IsFoldable(node))
                return false;
        }

        return true;
    }

    private static bool AreAllConstant(EquatableArray<LocalBinding> locals)
    {
        foreach (var local in locals)
        {
            if (local.Initializer.Constant is null)
                return false;
        }

        return true;
    }
}
