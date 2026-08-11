using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BlazorCodeFirst.Site.DocGen;
using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class HighlightCssEmitterTests
{
    [Fact]
    public void Emit_DropsBodyRule()
    {
        string css = HighlightCssEmitter.Emit();
        Assert.DoesNotContain("body{", css);
        Assert.DoesNotContain("body {", css);
    }

    [Fact]
    public void Emit_ContainsTokenClassRules()
    {
        string css = HighlightCssEmitter.Emit();
        Assert.Contains(".keyword", css);
    }

    // ColorCode's DefaultLight dictionary defines .plainText as a self-overriding
    // (white-wins) rule. The pipeline never emits class="plainText" (inter-token text
    // is unwrapped; plain/unknown/empty fences emit no class), so keeping it would be
    // dead weight that renders invisible white-on-light text if ever hit.
    [Fact]
    public void Emit_DropsPlainTextRule()
    {
        string css = HighlightCssEmitter.Emit();
        Assert.DoesNotContain(".plainText", css);
    }

    // Parity: every class the pipeline actually emits into real HTML must have a
    // selector in the generated CSS. Derives "used classes" from real HTML output,
    // NOT from the emitter itself, so any class-scheme drift between the HTML
    // formatter and the CSS formatter turns this test red.
    [Fact]
    public void Emit_CoversEveryClassUsedInRealHtml()
    {
        string html = MarkdownConverter.ToHtml(
            "```csharp\npublic class C { public string S => \"hi\"; } // note\n```");
        string css = HighlightCssEmitter.Emit();

        var usedClasses = new HashSet<string>();
        foreach (Match m in Regex.Matches(html, "class=\"([^\"]+)\""))
        {
            foreach (string cls in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                usedClasses.Add(cls);
            }
        }

        // The language wrapper class (e.g. "csharp") is not a token style; ignore it.
        usedClasses.Remove("csharp");

        Assert.NotEmpty(usedClasses);
        foreach (string cls in usedClasses)
        {
            // Boundary-aware match: a raw substring check would let ".string" pass
            // merely because ".stringCSharpVerbatim{...}" is present, masking
            // dropped rules for classes that are prefixes of others.
            Assert.Matches(@"\." + Regex.Escape(cls) + @"\s*\{", css);
        }
    }

    // ColorCodeTheme repaints DefaultLight onto the site's palette, and it has to write hex rather
    // than a var() reference because ColorCode trims what it assumes is an alpha channel off the
    // front of the value before emitting it. That trimming is the fragile part: it is undocumented,
    // and a version that started parsing the value -- or one that renamed a scope so Repaint added
    // a new entry instead of overwriting the old one -- would leave the Visual Studio palette in
    // place with every other test here still green.
    //
    // Asserting both directions is the point. The presence check catches a repaint that stopped
    // being applied; the absence check catches a repaint that was applied to a duplicate entry
    // while the original rule survived alongside it.
    [Theory]
    [InlineData("keyword", "#463ECC", "#0000FF")]
    [InlineData("string", "#006647", "#A31515")]
    [InlineData("comment", "#676871", "#008000")]
    public void Emit_RepaintsScopeOntoTheSitePalette(string cls, string expected, string defaultLight)
    {
        string css = LightHalf();

        Assert.Contains($".{cls}{{color:{expected};}}", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($".{cls}{{color:{defaultLight};}}", css, StringComparison.OrdinalIgnoreCase);
    }

    // The dark half, asserted the same two ways and for the same reasons. The defaults differ
    // because the dark theme is built from DefaultDark, not from the light dictionary: an unreached
    // scope has to inherit a colour meant for the surface it will land on.
    [Theory]
    [InlineData("keyword", "#C5A2FF", "#569CD6")]
    [InlineData("string", "#69D6AA", "#D69D85")]
    [InlineData("comment", "#8F919F", "#57A64A")]
    public void Emit_RepaintsScopeOntoTheDarkPalette(string cls, string expected, string defaultDark)
    {
        string css = DarkHalf();

        Assert.Contains($".{cls}{{color:{expected};}}", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($".{cls}{{color:{defaultDark};}}", css, StringComparison.OrdinalIgnoreCase);
    }

    // Neither half may carry the other's palette. A single Emit() that built both from one
    // dictionary would still satisfy every presence check above for the half it got right, and
    // would ship the light hexes onto a dark page.
    [Theory]
    [InlineData("#463ECC")]
    [InlineData("#006647")]
    [InlineData("#676871")]
    public void Emit_KeepsTheLightPaletteOutOfTheDarkHalf(string lightHex)
    {
        Assert.DoesNotContain(lightHex, DarkHalf(), StringComparison.OrdinalIgnoreCase);
    }

    // Both halves have to answer for the same selectors. A scope repainted in one dictionary and
    // forgotten in the other leaves a class styled in one scheme and inherited in the other, which
    // no single-half assertion above can see.
    [Fact]
    public void Emit_StylesTheSameSelectorsInBothHalves()
    {
        Assert.Equal(Selectors(LightHalf()), Selectors(DarkHalf()));
    }

    [Fact]
    public void Emit_ClosesTheDarkSchemeBlock()
    {
        string css = HighlightCssEmitter.Emit();

        Assert.Contains(HighlightCssEmitter.DarkSchemeMarker, css, StringComparison.Ordinal);
        Assert.EndsWith("}\n", css, StringComparison.Ordinal);
        // A rule outside a selector would mean the split below silently measured the wrong text.
        Assert.Equal(1, css.Split(HighlightCssEmitter.DarkSchemeMarker).Length - 1);
    }

    private static string LightHalf() =>
        HighlightCssEmitter.Emit().Split(HighlightCssEmitter.DarkSchemeMarker)[0];

    private static string DarkHalf() =>
        HighlightCssEmitter.Emit().Split(HighlightCssEmitter.DarkSchemeMarker)[1];

    private static SortedSet<string> Selectors(string css) =>
        [.. Regex.Matches(css, @"\.([A-Za-z][A-Za-z0-9]*)\s*\{").Select(m => m.Groups[1].Value)];
}
