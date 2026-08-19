using System;
using System.Collections.Generic;
using System.Text;

namespace BlazorCodeFirst.Build;

/// <summary>
/// One <c>@import</c> found inside a <c>.cs.css</c> file being scoped. <c>@import</c> is rejected
/// rather than rewritten because the loading order of an imported stylesheet relative to its scoped
/// parent is undefined once scope attributes are involved.
/// </summary>
public readonly struct CssRewriteError : IEquatable<CssRewriteError>
{
    public CssRewriteError(string filePath, int line, int column, string message)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
        Message = message;
    }

    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public string Message { get; }

    public override string ToString() => $"{FilePath}({Line},{Column}): {Message}";

    public bool Equals(CssRewriteError other) =>
        FilePath == other.FilePath && Line == other.Line && Column == other.Column && Message == other.Message;

    public override bool Equals(object? obj) => obj is CssRewriteError other && Equals(other);

    // System.HashCode is unavailable on net472 (this project multi-targets net472;net10.0), so the
    // combine is done by hand instead.
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (FilePath?.GetHashCode() ?? 0);
            hash = hash * 31 + Line;
            hash = hash * 31 + Column;
            hash = hash * 31 + (Message?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public static bool operator ==(CssRewriteError left, CssRewriteError right) => left.Equals(right);

    public static bool operator !=(CssRewriteError left, CssRewriteError right) => !left.Equals(right);
}

/// <summary>
/// Appends <c>[scope]</c> to every top-level selector in a CSS document that contains only flat
/// rule blocks (no at-rules, no nesting, no <c>::deep</c>). This is deliberately narrow: a
/// <c>.cs.css</c> file that needs <c>@media</c>/<c>@keyframes</c>/<c>::deep</c> throws rather than
/// silently emitting something that looks scoped but is not -- full at-rule support lands in a
/// follow-up task in this same plan.
/// </summary>
public static class ScopedCssRewriter
{
    public static string Rewrite(string filePath, string css, string scope, out IReadOnlyList<CssRewriteError> errors)
    {
        if (filePath is null)
            throw new ArgumentNullException(nameof(filePath));
        if (css is null)
            throw new ArgumentNullException(nameof(css));
        if (scope is null)
            throw new ArgumentNullException(nameof(scope));

        var result = new StringBuilder(css.Length + 64);
        var selectorStart = 0;

        for (var i = 0; i < css.Length; i++)
        {
            var c = css[i];

            if (c == '@' && IsAtTopLevelRuleStart(css, i))
            {
                throw new NotSupportedException(
                    "ScopedCssRewriter cannot rewrite a CSS file containing a top-level " +
                    "at-rule ('@media', '@keyframes', '@import', etc.). Full at-rule support lands " +
                    "in a follow-up task.");
            }

            if (c == '{')
            {
                var selectorText = css.Substring(selectorStart, i - selectorStart);
                result.Append(AppendScopeToSelectorList(selectorText, scope));
                result.Append('{');
                selectorStart = i + 1;
            }
            else if (c == '}')
            {
                result.Append(css.Substring(selectorStart, i + 1 - selectorStart));
                selectorStart = i + 1;
            }
        }

        result.Append(css.Substring(selectorStart));
        errors = [];
        return result.ToString();
    }

    private static bool IsAtTopLevelRuleStart(string css, int atIndex)
    {
        var firstBrace = css.IndexOf('{');
        var beforeFirstBrace = firstBrace < 0 || atIndex < firstBrace;
        return beforeFirstBrace ||
            css.Substring(0, atIndex).TrimEnd().EndsWith("}", StringComparison.Ordinal);
    }

    private static string AppendScopeToSelectorList(string selectorList, string scope)
    {
        var parts = selectorList.Split(',');
        for (var i = 0; i < parts.Length; i++)
            parts[i] = parts[i].TrimEnd() + $"[{scope}]" + TrailingWhitespace(parts[i]);

        return string.Join(",", parts);
    }

    private static string TrailingWhitespace(string value)
    {
        var trimmed = value.TrimEnd();
        return value.Substring(trimmed.Length);
    }
}
