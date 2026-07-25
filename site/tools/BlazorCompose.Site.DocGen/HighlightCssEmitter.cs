using System.Text;
using ColorCode;

namespace BlazorCompose.Site.DocGen;

/// <summary>Generates the highlight stylesheet from the shared ColorCode theme, so the
/// selectors match the classes the Markdown pipeline emits. The ColorCode "body" rule
/// (which would repaint the whole page background) is dropped; only token-class rules
/// are kept.</summary>
public static class HighlightCssEmitter
{
    public static string Emit()
    {
        // Same StyleDictionary instance the pipeline uses => identical class scheme.
        string css = new HtmlClassFormatter(ColorCodeTheme.Styles).GetCSSString();

        // GetCSSString() returns space/newline-separated "selector{decls}" rules,
        // starting with a global "body{...}" rule. Keep every rule except body.
        var kept = new StringBuilder();
        foreach (string rule in SplitRules(css))
        {
            string selector = rule.AsSpan(0, rule.IndexOf('{', StringComparison.Ordinal)).Trim().ToString();
            if (selector is "body")
            {
                continue;
            }

            kept.Append(rule).Append('\n');
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
