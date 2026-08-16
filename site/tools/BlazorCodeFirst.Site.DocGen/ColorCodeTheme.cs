using ColorCode.Common;
using ColorCode.Styling;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>Single source of the ColorCode style dictionaries shared by the Markdown
/// pipeline (HTML class output) and the CSS emitter, so the emitted token classes and
/// the generated stylesheet stay in lockstep (parity by construction).</summary>
/// <remarks>
/// The bases are ColorCode's DefaultLight and DefaultDark, the Visual Studio palettes. Every scope
/// the site's own fences reach is repainted onto the site's palette, because two code vocabularies
/// on one site -- a blue-and-red one in the prose and a violet-tinted one in the landing page's
/// figures -- read as two designs stitched together.
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
/// Scopes outside this list keep the base dictionary's hex. That is deliberate: the parity test
/// asserts every class the pipeline emits has a rule, and inheriting the rest means a fence in a
/// language nobody has written yet still renders legibly rather than as unstyled text. Today's
/// documents reach keyword, string, comment and number, plus the five an output figure's HTML
/// reaches. It is also why the dark theme starts from DefaultDark rather than from the light
/// dictionary: an unreached scope has to inherit a colour meant for the surface it will actually
/// land on.
/// </para>
/// <para>
/// Inheriting was not an option for the HTML five. The parity test passes on them either way --
/// ColorCode's own dictionaries already carry every HTML scope, so the rules exist and only their
/// colour is wrong -- which is exactly the shape of defect the paragraph above says this list is
/// here to prevent. HighlightCssEmitterTests names the five instead.
/// </para>
/// </remarks>
public static class ColorCodeTheme
{
    /// <summary>The four foregrounds one surface needs, as ColorCode's alpha-less hex.</summary>
    private readonly record struct Palette(string Keyword, string Literal, string Comment, string Neutral);

    /// <summary>
    /// Prose fences on paper. Keyword is the accent <c>oklch(47% 0.21 277)</c> at 6.8:1 on the code
    /// block's surface; the literal is a cool green <c>oklch(45% 0.10 165)</c>, the palette's one
    /// literal colour, at 6.4:1; the comment is the muted grey <c>oklch(52% 0.014 277)</c> at 5.1:1,
    /// still readable; the neutral is the ink <c>oklch(21% 0.020 277)</c>, reached only by fences no
    /// document has yet.
    /// </summary>
    private static readonly Palette Light = new("463ECC", "006647", "676871", "161822");

    /// <summary>
    /// The same four turned over for a dark page, where the code block draws on
    /// <c>--color-paper-2</c> at <c>oklch(19% 0.017 277)</c>: keyword <c>oklch(78% 0.14 300)</c> at
    /// 8.8:1, literal <c>oklch(80% 0.12 165)</c> at 10.4:1, comment <c>oklch(66% 0.020 277)</c> at
    /// 5.9:1, and the neutral <c>oklch(93% 0.008 277)</c>, which is css/tokens.css's
    /// <c>--color-slab-ink</c>.
    /// <para>
    /// These four are the whole record. The landing page's figures are generated through this same
    /// theme, so the dark half also paints code on <c>--color-slab</c>, and css/tokens.css declares
    /// no <c>--color-code-*</c> family to keep in step: a token no rule renders would be a second
    /// record nothing could observe drifting from the hex below.
    /// </para>
    /// </summary>
    private static readonly Palette Dark = new("C5A2FF", "69D6AA", "8F919F", "E6E7ED");

    public static StyleDictionary Styles { get; } = BuildStyles(StyleDictionary.DefaultLight, Light);

    public static StyleDictionary DarkStyles { get; } = BuildStyles(StyleDictionary.DefaultDark, Dark);

    private static StyleDictionary BuildStyles(StyleDictionary styles, Palette palette)
    {
        // The base is a property, and each call hands back its own dictionary. Repaint the instance
        // we were given rather than composing one from nothing, so no scope can be dropped by
        // omission.
        Repaint(styles, ScopeName.Keyword, palette.Keyword);
        Repaint(styles, ScopeName.PreprocessorKeyword, palette.Keyword);
        Repaint(styles, ScopeName.String, palette.Literal);
        Repaint(styles, ScopeName.StringCSharpVerbatim, palette.Literal);
        Repaint(styles, ScopeName.Number, palette.Literal);
        Repaint(styles, ScopeName.Comment, palette.Comment);
        Repaint(styles, ScopeName.XmlDocComment, palette.Comment);
        Repaint(styles, ScopeName.XmlDocTag, palette.Comment);
        Repaint(styles, ScopeName.ClassName, palette.Neutral);

        // The output half of a figure, mapped onto the roles the C# half already uses so the two
        // are read as one correspondence rather than as two languages. An element name takes the
        // neutral, which is what an element helper takes on the other side -- Section and H1 match
        // no highlighter scope, so they render in the slab's own ink -- and an attribute value takes
        // the literal its string came from. HTML has no keyword, so the accent stays reserved for
        // one. Same role, not the same hex: the two halves of a pair sit on slabs that declare
        // opposite color-schemes, so each resolves the pair's light-dark() on its own surface.
        //
        // The operator is the '=' between an attribute's name and its value, which the highlighter
        // scopes separately from both. It is punctuation, so it goes with the delimiters; left
        // inherited it is the one blue mark on an otherwise neutral figure. The entity is here on
        // the same reasoning as the attribute value rather than from a figure that has one:
        // &amp; is ordinary in prose about HTML, and inherited it arrives red.
        //
        // HtmlServerSideScript is deliberately not repainted. No output figure can contain <% %>,
        // and Emit_WritesAgreedColoursOnce reads its inherited yellow as the worked example of a
        // scope both palettes agree on.
        Repaint(styles, ScopeName.HtmlElementName, palette.Neutral);
        Repaint(styles, ScopeName.HtmlAttributeName, palette.Neutral);
        Repaint(styles, ScopeName.HtmlTagDelimiter, palette.Neutral);
        Repaint(styles, ScopeName.HtmlOperator, palette.Neutral);
        Repaint(styles, ScopeName.HtmlAttributeValue, palette.Literal);
        Repaint(styles, ScopeName.HtmlEntity, palette.Literal);
        Repaint(styles, ScopeName.HtmlComment, palette.Comment);

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
