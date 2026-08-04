using System.Collections.Generic;
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
        TextContentNode text => IsFoldableText(text.Content),
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
    /// Serializes a run of consecutive foldable siblings into one markup string.
    /// </summary>
    /// <param name="run">Nodes that all satisfy <see cref="IsFoldable"/>.</param>
    /// <returns>
    /// The markup, and the number of frames the element path would have emitted for the same run. The
    /// count is advisory: <see cref="RenderViewEmitter"/> uses it only to decide whether folding is a win
    /// (a run worth less than two frames is emitted node by node). It takes no part in sequence
    /// arithmetic, because a markup frame consumes exactly one sequence number whatever the count says,
    /// so a disagreement with the emitter costs a missed or pointless fold and can never corrupt
    /// numbering.
    /// </returns>
    /// <exception cref="System.NotSupportedException">
    /// A node in <paramref name="run"/> is not foldable. Callers partition on <see cref="IsFoldable"/>
    /// first; reaching here means the two have drifted, which is a bug and not a case to absorb, matching
    /// the exhaustiveness contract in <c>RenderViewEmitter.EmitNode</c>.
    /// </exception>
    public static (string Markup, int AbsorbedFrameCount) Write(ImmutableArray<RenderNode> run)
    {
        var builder = new StringBuilder();
        var absorbed = 0;
        foreach (var node in run)
            WriteNode(builder, node, ref absorbed);

        return (builder.ToString(), absorbed);
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
            // one frame however many there are.
            builder.Append(" class=\"");
            var first = true;
            foreach (var @class in element.Classes)
            {
                if (!first)
                    builder.Append(' ');
                AppendEscapedAttributeValue(builder, ConstantTextOf(@class, element));
                first = false;
            }

            builder.Append('"');
            absorbed++;
        }

        foreach (var attribute in element.Attributes)
        {
            // A constant null value means AddAttribute would omit the attribute, so no markup and no frame.
            if (attribute.Value.Constant is not { Text: { } value })
                continue;

            builder.Append(' ').Append(attribute.Name).Append("=\"");
            AppendEscapedAttributeValue(builder, value);
            builder.Append('"');
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
        template.Constant?.Text
            ?? throw new System.NotSupportedException(
                $"'{owner.GetType().Name}' carries a non-constant expression; the caller must partition on IsFoldable first.");

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

    private static bool IsFoldableText(ExpressionTemplate content) =>
        content.Constant is { Text: { } value } && CanRoundTrip(value);

    private static bool IsFoldableElement(ElementNode element)
    {
        if (!IsFoldableTag(element.Tag))
            return false;

        // An event is an attribute frame whose value is an EventCallback; markup cannot express it.
        if (element.Events.Length > 0)
            return false;

        foreach (var @class in element.Classes)
        {
            if (@class.Constant is not { Text: { } value } || !CanRoundTrip(value))
                return false;
        }

        foreach (var attribute in element.Attributes)
        {
            if (!IsSafeAttributeName(attribute.Name))
                return false;

            // A constant null value is foldable: it is written by omitting the attribute, which is what
            // AddAttribute does with a null string.
            if (attribute.Value.Constant is not { } constant)
                return false;

            if (constant.Text is { } text && !CanRoundTrip(text))
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
    /// digits, hyphen, underscore, colon or period. Anything else could close the tag or open a second
    /// attribute.
    /// </summary>
    private static bool IsSafeAttributeName(string name)
    {
        if (name.Length == 0)
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
    /// Whether <paramref name="value"/> survives a markup round trip. A NUL does not: the HTML parser
    /// maps both a literal NUL and the reference <c>&amp;#0;</c> to U+FFFD, while the element path's
    /// createTextNode keeps it, so the two paths would produce different DOM. A lone surrogate is refused
    /// for the same reason.
    /// </summary>
    private static bool CanRoundTrip(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var c = value[index];
            if (c == '\0')
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
