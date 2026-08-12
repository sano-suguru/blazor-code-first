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
/// The file is one rule per selector, carrying both palettes: a colour that differs between schemes
/// is emitted as <c>light-dark(paper, dark)</c>, and everything else is emitted once. The class
/// names are the same either way, because the HTML the pipeline emits is scheme-agnostic.
///
/// A <c>prefers-color-scheme</c> block would have been the obvious shape and is the wrong one. It
/// answers the operating system and cannot see a reader who chose the other scheme on the page,
/// so the dark half would keep applying under an explicit "light". <c>light-dark()</c> resolves
/// against <c>color-scheme</c> instead, which css/tokens.css sets from that choice -- and it keeps
/// each dark value written once rather than in a second block that can be half-updated.
///
/// Only colour is ever paired. Measured across both dictionaries, the sole properties ColorCode
/// emits are colour, font-style and font-weight, and the latter two are identical in both -- which
/// is what makes a colour-only function sufficient here. A future dictionary that broke that
/// symmetry would trip <see cref="Merge"/>'s guard rather than silently dropping one side, but only
/// for the selector set; a bold that appeared in one palette alone would need this comment revisited
/// along with the merge.
/// </remarks>
public static class HighlightCssEmitter
{
    private static readonly string[] DroppedSelectors = ["body", ".plainText"];

    public static string Emit() => Emit(ColorCodeTheme.Styles, ColorCodeTheme.DarkStyles);

    /// <summary>The stylesheet for one pair of palettes.</summary>
    public static string Emit(StyleDictionary light, StyleDictionary dark) =>
        Merge(Rules(light), Rules(dark));

    /// <summary>
    /// One rule per selector, pairing the two palettes' colours and passing everything else through.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The dictionaries disagree on which selectors they style. Left unchecked, a scope repainted in
    /// one and forgotten in the other would vanish from the stylesheet with every presence assertion
    /// still passing, which is the same defect class as a check that never runs (#163).
    /// </exception>
    private static string Merge(
        IReadOnlyDictionary<string, Dictionary<string, string>> light,
        IReadOnlyDictionary<string, Dictionary<string, string>> dark)
    {
        if (!light.Keys.OrderBy(k => k, StringComparer.Ordinal)
                .SequenceEqual(dark.Keys.OrderBy(k => k, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The light and dark ColorCode dictionaries style different selectors, so one palette "
                + "would lose a scope silently. Repaint the same scopes in both, or drop it from both.");
        }

        var css = new StringBuilder();
        foreach ((string selector, Dictionary<string, string> lightDecls) in light)
        {
            Dictionary<string, string> darkDecls = dark[selector];

            css.Append(selector).Append('{');
            foreach ((string property, string lightValue) in lightDecls)
            {
                string darkValue = darkDecls.TryGetValue(property, out string? d) ? d : lightValue;

                css.Append(property).Append(':');
                css.Append(
                    string.Equals(lightValue, darkValue, StringComparison.Ordinal)
                        ? lightValue
                        : $"light-dark({lightValue},{darkValue})");
                css.Append(';');
            }

            css.Append("}\n");
        }

        return css.ToString();
    }

    /// <summary>Every kept rule of one dictionary, as selector to its declarations in source order.</summary>
    private static Dictionary<string, Dictionary<string, string>> Rules(StyleDictionary styles)
    {
        // Same StyleDictionary instance the pipeline uses => identical class scheme.
        string css = new HtmlClassFormatter(styles).GetCSSString();

        // GetCSSString() returns space/newline-separated "selector{decls}" rules,
        // starting with a global "body{...}" rule. Keep every rule except the dropped
        // ones above.
        var kept = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (string rule in SplitRules(css))
        {
            int brace = rule.IndexOf('{', StringComparison.Ordinal);
            string selector = rule.AsSpan(0, brace).Trim().ToString();
            if (Array.IndexOf(DroppedSelectors, selector) >= 0)
            {
                continue;
            }

            kept[selector] = Declarations(rule.AsSpan(brace + 1, rule.Length - brace - 2));
        }

        return kept;
    }

    /// <summary>The "property: value" pairs of one rule body, in the order ColorCode wrote them.</summary>
    private static Dictionary<string, string> Declarations(ReadOnlySpan<char> body)
    {
        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Range range in body.Split(';'))
        {
            ReadOnlySpan<char> declaration = body[range].Trim();
            int colon = declaration.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            declarations[declaration[..colon].Trim().ToString()] = declaration[(colon + 1)..].Trim().ToString();
        }

        return declarations;
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
