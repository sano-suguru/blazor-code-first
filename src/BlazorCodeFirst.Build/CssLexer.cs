namespace BlazorCodeFirst.Build;

/// <summary>
/// Low-level string/comment-skipping primitive shared by every scanning routine in
/// <see cref="ScopedCssRewriter"/>. A bare (unquoted) <c>url(...)</c> literal needs no special
/// handling here: CSS Syntax disallows unescaped braces, '@', and quotes inside an unquoted
/// url-token, and a quoted <c>url("...")</c> is already covered by string-skipping.
/// </summary>
public static class CssLexer
{
    /// <summary>
    /// Returns the index past the string literal or comment starting at <paramref name="i"/>, or
    /// <paramref name="i"/> itself if neither starts there.
    /// </summary>
    /// <param name="css">The CSS source being scanned.</param>
    /// <param name="i">The index to check for a string literal or comment.</param>
    public static int SkipLiteral(string css, int i)
    {
        if (css is null)
            throw new ArgumentNullException(nameof(css));
        if (i >= css.Length)
            return i;

        var c = css[i];

        if (c is '"' or '\'')
            return SkipString(css, i, c);

        if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
            return SkipComment(css, i);

        return i;
    }

    private static int SkipString(string css, int start, char quote)
    {
        var i = start + 1;
        while (i < css.Length)
        {
            if (css[i] == '\\' && i + 1 < css.Length) { i += 2; continue; }
            if (css[i] == quote) return i + 1;
            i++;
        }
        return i;
    }

    private static int SkipComment(string css, int start)
    {
        var end = css.IndexOf("*/", start + 2, StringComparison.Ordinal);
        return end < 0 ? css.Length : end + 2;
    }
}
