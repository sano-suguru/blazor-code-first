using System;
using System.Text;

namespace BlazorCodeFirst.Build;

/// <summary>
/// Appends <c>[scope]</c> to every top-level selector in a CSS document that contains only flat
/// rule blocks (no at-rules, no nesting, no <c>::deep</c>). This is deliberately narrow: a
/// <c>.cs.css</c> file that needs <c>@media</c>/<c>@keyframes</c>/<c>::deep</c> throws rather than
/// silently emitting something that looks scoped but is not — full fidelity is a follow-up plan
/// (docs/superpowers/specs/2026-08-18-scoped-css-design.md, subsystem 3).
/// </summary>
public static class FlatSelectorCssRewriter
{
    public static string Rewrite(string css, string scope)
    {
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
                    "FlatSelectorCssRewriter cannot rewrite a CSS file containing a top-level " +
                    "at-rule ('@media', '@keyframes', '@import', etc.). Full at-rule support lands " +
                    "in a follow-up plan.");
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
        return result.ToString();
    }

    private static bool IsAtTopLevelRuleStart(string css, int atIndex)
    {
        // "Top level" here means not inside a string or url(...) literal. This rewriter only ever
        // sees flat rule blocks (by construction — anything else already threw), so the only
        // context an '@' can appear in in-bounds input is either the start of an at-rule or inside
        // a declaration value (e.g. content: "@"), which never appears before the first '{'.
        //
        // Substring/EndsWith(string) rather than range indexers or the char overload of EndsWith:
        // this project multi-targets net472, which has neither System.Index/Range nor
        // string.EndsWith(char).
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
