using ColorCode.Common;
using ColorCode.Styling;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>Single source of the ColorCode style dictionary shared by the Markdown
/// pipeline (HTML class output) and the CSS emitter, so the emitted token classes and
/// the generated stylesheet stay in lockstep (parity by construction).</summary>
/// <remarks>
/// The base is ColorCode's DefaultLight, the Visual Studio light palette. Every scope the site's
/// own fences reach is repainted onto the site's palette, because two code vocabularies on one site
/// -- a blue-and-red one in the prose and a violet-tinted one in the landing page's figures -- read
/// as two designs stitched together.
/// <para>
/// This is the one place on the site a colour is written outside css/tokens.css, and it has to be:
/// ColorCode writes the value into the stylesheet as <c>color:#{Foreground}</c> after trimming what
/// it assumes is an alpha channel, so a <c>var(--…)</c> reference comes out mangled rather than
/// passed through. The hex below is therefore the sRGB conversion of the OKLCH token named beside
/// it, and the two have to be changed together.
/// </para>
/// <para>
/// Literals share one colour. A string and a number are the same kind of thing to a reader, and a
/// separate hue for numbers would be a third accent on a page that is meant to have one.
/// </para>
/// <para>
/// Scopes outside this list keep their DefaultLight hex. That is deliberate: the parity test asserts
/// every class the pipeline emits has a rule, and inheriting the rest means a fence in a language
/// nobody has written yet still renders legibly rather than as unstyled text. Today's documents
/// reach only keyword, string, comment and number.
/// </para>
/// </remarks>
public static class ColorCodeTheme
{
    /// <summary>The accent, <c>oklch(47% 0.21 277)</c>. 6.8:1 on the code block's surface.</summary>
    private const string Keyword = "463ECC";

    /// <summary>A cool green at <c>oklch(45% 0.10 165)</c>, the palette's one literal colour. 6.4:1.</summary>
    private const string Literal = "006647";

    /// <summary>The muted grey, <c>oklch(52% 0.014 277)</c>. 5.1:1 -- a comment is still readable.</summary>
    private const string Comment = "676871";

    /// <summary>The ink, <c>oklch(21% 0.020 277)</c>. Reached only by fences no document has yet.</summary>
    private const string Neutral = "161822";

    public static StyleDictionary Styles { get; } = BuildStyles();

    private static StyleDictionary BuildStyles()
    {
        // DefaultLight is a property, and this is the only place its result is kept. Repaint the
        // instance we were handed rather than composing a dictionary from nothing, so no scope can
        // be dropped by omission.
        StyleDictionary styles = StyleDictionary.DefaultLight;

        Repaint(styles, ScopeName.Keyword, Keyword);
        Repaint(styles, ScopeName.PreprocessorKeyword, Keyword);
        Repaint(styles, ScopeName.String, Literal);
        Repaint(styles, ScopeName.StringCSharpVerbatim, Literal);
        Repaint(styles, ScopeName.Number, Literal);
        Repaint(styles, ScopeName.Comment, Comment);
        Repaint(styles, ScopeName.XmlDocComment, Comment);
        Repaint(styles, ScopeName.XmlDocTag, Comment);
        Repaint(styles, ScopeName.ClassName, Neutral);

        return styles;
    }

    /// <summary>Sets one scope's foreground, adding the scope if the base dictionary lacks it.</summary>
    private static void Repaint(StyleDictionary styles, string scopeName, string foreground)
    {
        if (styles.Contains(scopeName))
        {
            styles[scopeName].Foreground = foreground;
            return;
        }

        styles.Add(new Style(scopeName) { Foreground = foreground });
    }
}
