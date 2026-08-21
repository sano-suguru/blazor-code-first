using System.Text;

namespace BlazorCodeFirst.Build;

/// <summary>
/// One <c>@import</c> found inside a <c>.cs.css</c> file being scoped. <c>@import</c> is rejected
/// rather than rewritten because the loading order of an imported stylesheet relative to its scoped
/// parent is undefined once scope attributes are involved.
/// </summary>
/// <param name="FilePath">The <c>.cs.css</c> file the error was found in.</param>
/// <param name="Line">The 1-based line the error starts at.</param>
/// <param name="Column">The 1-based column the error starts at.</param>
/// <param name="Message">The human-readable description of the error.</param>
public readonly record struct CssRewriteError(string FilePath, int Line, int Column, string Message)
{
    /// <summary>Formats the error as <c>path(line,column): message</c>, matching MSBuild's diagnostic format.</summary>
    public override string ToString() => $"{FilePath}({Line},{Column}): {Message}";
}

/// <summary>
/// Rewrites a <c>.cs.css</c> file's selectors, <c>@keyframes</c> names, and animation-name
/// declarations to carry a scope attribute, mirroring dotnet/sdk's <c>RewriteCss.cs</c>
/// (src/StaticWebAssetsSdk/Tasks/ScopedCss/RewriteCss.cs) but implemented on a hand-rolled,
/// position-tracking scanner instead of Microsoft.Css.Parser's AST (see
/// docs/superpowers/specs/2026-08-18-scoped-css-design.md, "スパイクで確定した事実 7." for why an
/// off-the-shelf parser -- specifically ExCSS -- cannot support this).
///
/// The rewrite runs in two passes over the original text. Pass 1 collects every
/// <c>@keyframes</c> identifier in the document, because an <c>animation</c>/<c>animation-name</c>
/// declaration is free to reference a keyframes name declared later in the same file. Pass 2 walks
/// the document to build an edit list (insert <c>[scope]</c>, insert <c>-{scope}</c>, or delete a
/// stripped <c>::deep</c> token) expressed as absolute offsets into the ORIGINAL text, which are
/// applied in a single left-to-right pass at the end. This is what preserves comments and
/// whitespace exactly outside of the edited spans.
/// </summary>
public static class ScopedCssRewriter
{
    private static readonly HashSet<string> LegacyPseudoElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "after", "before", "first-letter", "first-line",
    };

    /// <summary>
    /// Rewrites <paramref name="css"/>'s selectors, <c>@keyframes</c> names, and animation-name
    /// declarations to carry <paramref name="scope"/>.
    /// </summary>
    /// <param name="filePath">The source file, used only to attach a location to <paramref name="errors"/>.</param>
    /// <param name="css">The original, unscoped stylesheet text.</param>
    /// <param name="scope">The scope identifier to inject into selectors and keyframe names.</param>
    /// <param name="errors">Every <c>@import</c> found; the rewrite is not applied to the rule that produced one.</param>
    /// <returns>The rewritten stylesheet text.</returns>
    public static string Rewrite(string filePath, string css, string scope, out IReadOnlyList<CssRewriteError> errors)
    {
        if (filePath is null)
            throw new ArgumentNullException(nameof(filePath));
        if (css is null)
            throw new ArgumentNullException(nameof(css));
        if (scope is null)
            throw new ArgumentNullException(nameof(scope));

        var keyframeNames = CollectKeyframeNames(css);
        var edits = new List<Edit>();
        var errorList = new List<CssRewriteError>();

        ProcessRuleList(css, 0, css.Length, keyframeNames, edits, errorList, filePath);

        errors = errorList;
        return ApplyEdits(css, scope, edits);
    }

    // ---- Pass 1: keyframe name collection --------------------------------------------------

    // Reuses ReadAtKeyword/FindIdentifierAfterAtKeyword (the same helpers Pass 2 uses to
    // recognize and name-suffix a @keyframes block) rather than hand-rolling a second
    // keyword/identifier scan here. Passing css.Length as the upper bound in place of a
    // prelude-bounded end is safe: FindIdentifierAfterAtKeyword's identifier scan already
    // self-terminates on the first non-identifier character.
    private static HashSet<string> CollectKeyframeNames(string css)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var i = 0;

        while (i < css.Length)
        {
            var skipped = CssLexer.SkipLiteral(css, i);
            if (skipped != i) { i = skipped; continue; }

            if (css[i] == '@')
            {
                var keyword = ReadAtKeyword(css, i);
                if (string.Equals(keyword, "keyframes", StringComparison.OrdinalIgnoreCase))
                {
                    var nameRange = FindIdentifierAfterAtKeyword(css, i, css.Length);
                    if (nameRange is { } range && range.End > range.Start)
                    {
                        names.Add(css.Substring(range.Start, range.End - range.Start));
                        i = range.End;
                        continue;
                    }
                }
            }

            i++;
        }

        return names;
    }

    // ---- Pass 2: edit list construction ------------------------------------------------------

    // Walks a rule list (the top-level stylesheet, or the body of a block at-rule such as
    // @media) and dispatches each rule found within [start, end) to selector scoping or (via
    // recursion) another rule list.
    private static void ProcessRuleList(
        string css, int start, int end, HashSet<string> keyframeNames,
        List<Edit> edits, List<CssRewriteError> errors, string filePath)
    {
        var i = start;
        var preludeStart = start;

        while (i < end)
        {
            var skipped = CssLexer.SkipLiteral(css, i);
            if (skipped != i) { i = skipped; continue; }

            var c = css[i];

            if (c == '{')
            {
                var preludeEnd = i;
                var isAtRule = TryGetAtRuleStart(css, preludeStart, preludeEnd, out var trimmedPreludeStart);

                var bodyStart = i + 1;
                var bodyEnd = FindMatchingBrace(css, bodyStart, end);

                if (isAtRule)
                {
                    var keyword = ReadAtKeyword(css, trimmedPreludeStart);

                    if (string.Equals(keyword, "keyframes", StringComparison.OrdinalIgnoreCase))
                    {
                        var nameRange = FindIdentifierAfterAtKeyword(css, trimmedPreludeStart, preludeEnd);
                        if (nameRange is { } range && range.End > range.Start)
                            edits.Add(new Edit(range.End, EditKind.InsertSuffix));

                        // The body is intentionally not descended into: keyframe selectors
                        // (from/to/N%) never get [scope], and animation-name declarations don't
                        // occur inside keyframe steps.
                    }
                    else
                    {
                        // @media, @supports, @font-face, or an unrecognized block at-rule:
                        // recurse. A declarations-only body (@font-face) has no nested '{', so the
                        // recursive call finds nothing and returns immediately -- a safe no-op.
                        ProcessRuleList(css, bodyStart, bodyEnd, keyframeNames, edits, errors, filePath);
                    }
                }
                else
                {
                    foreach (var selector in SplitTopLevel(css, preludeStart, preludeEnd, ','))
                        ScanSelector(css, selector.Start, selector.End, edits);

                    ScanDeclarationsForAnimationNames(css, bodyStart, bodyEnd, keyframeNames, edits);
                }

                i = bodyEnd < end ? bodyEnd + 1 : bodyEnd;
                preludeStart = i;
                continue;
            }

            if (c == ';')
            {
                var isAtRule = TryGetAtRuleStart(css, preludeStart, i, out var trimmedPreludeStart);

                if (isAtRule)
                {
                    var keyword = ReadAtKeyword(css, trimmedPreludeStart);
                    if (string.Equals(keyword, "import", StringComparison.OrdinalIgnoreCase))
                    {
                        var (line, column) = LocateLineColumn(css, trimmedPreludeStart);
                        errors.Add(new CssRewriteError(
                            filePath, line, column,
                            "@import rules are not supported within scoped CSS files because the " +
                            "loading order would be undefined. @import may only be placed in " +
                            "non-scoped CSS files."));
                    }

                    // Any other statement at-rule (e.g. "@otheratrule \"...\";") passes through
                    // silently.
                }

                i++;
                preludeStart = i;
                continue;
            }

            i++;
        }
    }

    // Trims leading whitespace/comments off [preludeStart, preludeEnd) and reports whether what
    // remains starts with '@'. Shared by both of ProcessRuleList's dispatch branches ('{' and
    // ';'), which otherwise repeated this exact trim-and-check.
    private static bool TryGetAtRuleStart(string css, int preludeStart, int preludeEnd, out int trimmedStart)
    {
        trimmedStart = SkipInsignificant(css, preludeStart, preludeEnd);
        return trimmedStart < preludeEnd && css[trimmedStart] == '@';
    }

    private static string ReadAtKeyword(string css, int atIndex)
    {
        var i = atIndex + 1;
        var start = i;
        while (i < css.Length && (char.IsLetterOrDigit(css[i]) || css[i] == '-'))
            i++;

        return css.Substring(start, i - start);
    }

    private static TextSpan? FindIdentifierAfterAtKeyword(string css, int atIndex, int preludeEnd)
    {
        var i = atIndex + 1;
        while (i < preludeEnd && (char.IsLetterOrDigit(css[i]) || css[i] == '-'))
            i++;

        i = SkipInsignificant(css, i, preludeEnd);

        var nameStart = i;
        while (i < preludeEnd && IsIdentifierChar(css[i]))
            i++;

        return i == nameStart ? null : new TextSpan(nameStart, i);
    }

    private static (int Line, int Column) LocateLineColumn(string css, int index)
    {
        var line = 1;
        var lastNewline = -1;

        for (var i = 0; i < index && i < css.Length; i++)
        {
            if (css[i] == '\n')
            {
                line++;
                lastNewline = i;
            }
        }

        return (line, index - lastNewline);
    }

    // Given the index just past an opening '{' (so already one level deep), finds the index of
    // its matching '}' within [i, end). Literal-skip-aware so a brace inside a string or comment
    // is never mistaken for a structural one.
    private static int FindMatchingBrace(string css, int i, int end)
    {
        var depth = 1;

        while (i < end)
        {
            var skipped = CssLexer.SkipLiteral(css, i);
            if (skipped != i) { i = skipped; continue; }

            var c = css[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }

            i++;
        }

        return end;
    }

    // Finds the first top-level (outside strings, comments, and parens) occurrence of `target`
    // within [start, end), or -1 if none. The single shared paren-depth/literal-skip primitive
    // behind both top-level scans below.
    private static int FindTopLevelChar(string css, int start, int end, char target)
    {
        var depth = 0;
        var i = start;

        while (i < end)
        {
            var skipped = CssLexer.SkipLiteral(css, i);
            if (skipped != i) { i = skipped; continue; }

            var c = css[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                if (depth > 0) depth--;
            }
            else if (depth == 0 && c == target)
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    // Splits [start, end) on top-level occurrences of `delimiter` (outside strings, comments, and
    // parens), by repeatedly calling FindTopLevelChar. Used for comma-separated selector lists
    // and semicolon-separated declarations.
    private static List<TextSpan> SplitTopLevel(string css, int start, int end, char delimiter)
    {
        var result = new List<TextSpan>();
        var segmentStart = start;

        while (true)
        {
            var delimiterIndex = FindTopLevelChar(css, segmentStart, end, delimiter);
            if (delimiterIndex < 0)
            {
                result.Add(new TextSpan(segmentStart, end));
                return result;
            }

            result.Add(new TextSpan(segmentStart, delimiterIndex));
            segmentStart = delimiterIndex + 1;
        }
    }

    private static void ScanDeclarationsForAnimationNames(
        string css, int bodyStart, int bodyEnd, HashSet<string> keyframeNames, List<Edit> edits)
    {
        foreach (var declaration in SplitTopLevel(css, bodyStart, bodyEnd, ';'))
        {
            var colon = FindTopLevelChar(css, declaration.Start, declaration.End, ':');
            if (colon < 0)
                continue;

            var propertyStart = SkipInsignificant(css, declaration.Start, colon);
            var propertyEnd = colon;
            while (propertyEnd > propertyStart && char.IsWhiteSpace(css[propertyEnd - 1]))
                propertyEnd--;

            var propertyName = css.Substring(propertyStart, propertyEnd - propertyStart);
            if (!string.Equals(propertyName, "animation", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(propertyName, "animation-name", StringComparison.OrdinalIgnoreCase))
                continue;

            ScanValueForKeyframeIdentifiers(css, colon + 1, declaration.End, keyframeNames, edits);
        }
    }

    private static void ScanValueForKeyframeIdentifiers(
        string css, int start, int end, HashSet<string> keyframeNames, List<Edit> edits)
    {
        var i = start;

        while (i < end)
        {
            var skipped = CssLexer.SkipLiteral(css, i);
            if (skipped != i) { i = skipped; continue; }

            var c = css[i];
            if (char.IsLetter(c) || c == '-' || c == '_')
            {
                var identStart = i;
                i++;
                while (i < end && IsIdentifierChar(css[i]))
                    i++;

                var identifier = css.Substring(identStart, i - identStart);
                if (keyframeNames.Contains(identifier))
                    edits.Add(new Edit(i, EditKind.InsertSuffix));

                continue;
            }

            i++;
        }
    }

    // Scans one comma-split selector for its scope insertion point (mirrors
    // FindScopeInsertionEdits.VisitSelector + FindPositionToInsertInSelector +
    // IsSingleColonPseudoElement + IsTrailingCombinator from dotnet/sdk's RewriteCss.cs).
    //
    // A single left-to-right pass tracks two things: `contentEnd`, the offset just past the last
    // "real" selector-content character seen (excluding top-level whitespace and top-level
    // >/+/~ combinators, and excluding anything inside a skipped string/comment), and
    // `pseudoBoundary`, the start of the first pseudo-element-like token seen since the last
    // top-level whitespace/combinator reset -- i.e. within the CURRENT compound selector only.
    // The reset is deferred (`pendingReset`) rather than applied immediately: the scanned span
    // always runs up to the delimiter ('{'/','), which includes trailing whitespace *after* the
    // real content (e.g. "a::before " before '{'), and that trailing whitespace must not discard
    // a pseudo-element boundary found just before it.
    //
    // If a standalone "::deep" is found first, it wins outright: the scope goes right after
    // whatever content preceded it (or at "::deep" itself if nothing did), the "::deep" text
    // itself is deleted, and everything after it in this selector is left untouched (only the
    // first "::deep" in a selector is ever treated as the boundary).
    private static void ScanSelector(string css, int start, int end, List<Edit> edits)
    {
        var depth = 0;
        var contentEnd = start;
        int? pseudoBoundary = null;
        var pendingReset = false;
        var i = start;

        while (i < end)
        {
            var skipped = CssLexer.SkipLiteral(css, i);
            if (skipped != i) { i = skipped; continue; }

            var c = css[i];

            if (depth == 0 && c == ':' && i + 6 <= end && string.CompareOrdinal(css, i, "::deep", 0, 6) == 0
                && (i + 6 == end || !IsIdentifierChar(css[i + 6])))
            {
                var insertionPoint = pseudoBoundary ?? (contentEnd > start ? contentEnd : i);
                edits.Add(new Edit(insertionPoint, EditKind.InsertScope));
                edits.Add(new Edit(i, EditKind.DeleteDeep));
                return;
            }

            if (depth == 0 && IsWhitespace(c))
            {
                pendingReset = true;
                i++;
                continue;
            }

            if (depth == 0 && (c == '>' || c == '+' || c == '~'))
            {
                pendingReset = true;
                i++;
                continue;
            }

            if (depth == 0 && c == ':')
            {
                if (pendingReset) { pseudoBoundary = null; pendingReset = false; }

                if (i + 1 < end && css[i + 1] == ':')
                {
                    pseudoBoundary ??= i;
                    i += 2;
                    while (i < end && IsIdentifierChar(css[i]))
                        i++;
                    contentEnd = i;
                    continue;
                }

                var identStart = i + 1;
                var identEnd = identStart;
                while (identEnd < end && IsIdentifierChar(css[identEnd]))
                    identEnd++;

                if (identEnd > identStart && LegacyPseudoElementNames.Contains(css.Substring(identStart, identEnd - identStart)))
                    pseudoBoundary ??= i;

                i = identEnd > identStart ? identEnd : i + 1;
                contentEnd = i;
                continue;
            }

            if (c == '(')
            {
                if (pendingReset) { pseudoBoundary = null; pendingReset = false; }
                depth++;
                i++;
                contentEnd = i;
                continue;
            }

            if (c == ')')
            {
                if (pendingReset) { pseudoBoundary = null; pendingReset = false; }
                if (depth > 0) depth--;
                i++;
                contentEnd = i;
                continue;
            }

            if (pendingReset) { pseudoBoundary = null; pendingReset = false; }
            i++;
            contentEnd = i;
        }

        var finalInsertionPoint = pseudoBoundary ?? contentEnd;
        edits.Add(new Edit(finalInsertionPoint, EditKind.InsertScope));
    }

    // ---- Small shared scanning helpers -------------------------------------------------------

    private static int SkipInsignificant(string css, int start, int end)
    {
        var i = start;
        while (i < end)
        {
            var skipped = CssLexer.SkipLiteral(css, i);
            if (skipped != i) { i = skipped; continue; }

            if (char.IsWhiteSpace(css[i])) { i++; continue; }
            break;
        }

        return i;
    }

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_';

    // ---- Edit list application ----------------------------------------------------------------

    private readonly struct TextSpan
    {
        public TextSpan(int start, int end)
        {
            Start = start;
            End = end;
        }

        public int Start { get; }
        public int End { get; }
    }

    private enum EditKind
    {
        InsertScope,
        InsertSuffix,
        DeleteDeep,
    }

    private readonly struct Edit
    {
        public Edit(int position, EditKind kind)
        {
            Position = position;
            Kind = kind;
        }

        public int Position { get; }
        public EditKind Kind { get; }
    }

    private static string ApplyEdits(string css, string scope, List<Edit> edits)
    {
        if (edits.Count == 0)
            return css;

        edits.Sort((a, b) =>
        {
            var byPosition = a.Position.CompareTo(b.Position);
            return byPosition != 0 ? byPosition : EditOrder(a.Kind).CompareTo(EditOrder(b.Kind));
        });

        var result = new StringBuilder(css.Length + edits.Count * 8);
        var cursor = 0;

        foreach (var edit in edits)
        {
            result.Append(css, cursor, edit.Position - cursor);
            cursor = edit.Position;

            switch (edit.Kind)
            {
                case EditKind.InsertScope:
                    result.Append('[').Append(scope).Append(']');
                    break;
                case EditKind.InsertSuffix:
                    result.Append('-').Append(scope);
                    break;
                case EditKind.DeleteDeep:
                    cursor += 6;
                    break;
            }
        }

        result.Append(css, cursor, css.Length - cursor);
        return result.ToString();
    }

    // An insertion and a deletion can land on the same position only in the leading-"::deep" case
    // (e.g. "::deep b"): the scope insertion point and the deletion start are both the index of
    // "::deep" itself. Insertions must sort first there, so "[scope]" is written before the six
    // characters of "::deep" are skipped.
    private static int EditOrder(EditKind kind) => kind switch
    {
        EditKind.InsertScope or EditKind.InsertSuffix => 0,
        EditKind.DeleteDeep => 1,
        _ => 2,
    };
}
