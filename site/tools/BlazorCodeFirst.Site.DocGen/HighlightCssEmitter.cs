using System.Text;
using ColorCode;
using ColorCode.Styling;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>Generates the highlight stylesheet from the shared ColorCode themes, so the
/// selectors match the classes the Markdown pipeline emits. Two rules are dropped as
/// dead weight: the ColorCode "body" rule (which would repaint the whole page
/// background) and ".plainText" (a self-overriding, white-wins rule from ColorCode's
/// default dictionaries that the pipeline never emits a matching class for). Only
/// token-class rules the pipeline actually produces are kept.</summary>
/// <remarks>
/// The file is one set of rules for paper followed by the same set for a dark page, wrapped in
/// <c>prefers-color-scheme: dark</c>. The class names are identical in both, because the HTML the
/// pipeline emits is scheme-agnostic -- only the foregrounds differ, and the cascade picks the
/// second set when the reader's system asks for it. css/tokens.css answers the same query for the
/// surface those rules draw on.
/// </remarks>
public static class HighlightCssEmitter
{
    private static readonly string[] DroppedSelectors = ["body", ".plainText"];

    /// <summary>Opens the dark half. Also the marker tests split the two halves on.</summary>
    public const string DarkSchemeMarker = "@media (prefers-color-scheme: dark) {";

    public static string Emit()
    {
        var css = new StringBuilder();
        css.Append(Rules(ColorCodeTheme.Styles, indent: ""));

        css.Append('\n').Append(DarkSchemeMarker).Append('\n');
        css.Append(Rules(ColorCodeTheme.DarkStyles, indent: "    "));
        css.Append("}\n");

        return css.ToString();
    }

    /// <summary>Every kept rule of one dictionary, one per line, each prefixed with <paramref name="indent"/>.</summary>
    private static string Rules(StyleDictionary styles, string indent)
    {
        // Same StyleDictionary instance the pipeline uses => identical class scheme.
        string css = new HtmlClassFormatter(styles).GetCSSString();

        // GetCSSString() returns space/newline-separated "selector{decls}" rules,
        // starting with a global "body{...}" rule. Keep every rule except the dropped
        // ones above.
        var kept = new StringBuilder();
        foreach (string rule in SplitRules(css))
        {
            string selector = rule.AsSpan(0, rule.IndexOf('{', StringComparison.Ordinal)).Trim().ToString();
            if (Array.IndexOf(DroppedSelectors, selector) >= 0)
            {
                continue;
            }

            kept.Append(indent).Append(rule).Append('\n');
        }

        return kept.ToString();
    }

    private static IEnumerable<string> SplitRules(string css)
    {
        int start = 0;
        while (true)
        {
            int close = css.IndexOf('}', start);
            if (close < 0)
            {
                yield break;
            }

            string rule = css[start..(close + 1)].Trim();
            if (rule.Length > 0 && rule.Contains('{', StringComparison.Ordinal))
            {
                yield return rule;
            }

            start = close + 1;
        }
    }
}
