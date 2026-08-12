using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BlazorCodeFirst.Site.DocGen;
using ColorCode.Styling;
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
    [InlineData("keyword", "#463ECC", "#C5A2FF")]
    [InlineData("string", "#006647", "#69D6AA")]
    [InlineData("comment", "#676871", "#8F919F")]
    public void Emit_PairsBothPalettesInOneDeclaration(string cls, string light, string dark)
    {
        string css = HighlightCssEmitter.Emit();

        Assert.Contains($".{cls}{{color:light-dark({light},{dark});}}", css, StringComparison.OrdinalIgnoreCase);
    }

    // The defaults each palette would have carried had the repaint stopped being applied. They
    // differ because the dark theme is built from DefaultDark, not from the light dictionary: an
    // unreached scope has to inherit a colour meant for the surface it will land on.
    //
    // Asserting their absence is the other half of the pair above. The presence check catches a
    // repaint that stopped happening; this catches one applied to a duplicate entry while the
    // original rule survived alongside it.
    [Theory]
    [InlineData("#0000FF")]
    [InlineData("#A31515")]
    [InlineData("#008000")]
    [InlineData("#569CD6")]
    [InlineData("#D69D85")]
    [InlineData("#57A64A")]
    public void Emit_LeavesNoVisualStudioDefaultOnARepaintedScope(string defaultHex)
    {
        string css = HighlightCssEmitter.Emit();

        foreach (string cls in new[] { "keyword", "string", "comment" })
        {
            Match rule = Regex.Match(css, @"\." + cls + @"\{([^}]*)\}");
            Assert.True(rule.Success, $".{cls} has no rule at all");
            Assert.DoesNotContain(defaultHex, rule.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    // A pair is only worth emitting when the two halves differ; a scope both dictionaries agree on
    // is written once. Without this, light-dark(#FFFF00,#FFFF00) would be correct CSS and pure
    // noise, and the file would stop showing at a glance which scopes actually turn over.
    [Fact]
    public void Emit_WritesAgreedColoursOnce()
    {
        string css = HighlightCssEmitter.Emit();

        Assert.Contains(".htmlServerSideScript{color:#FFFF00;}", css, StringComparison.OrdinalIgnoreCase);
    }

    // Non-colour declarations survive the merge. light-dark() takes colours only, so a font-weight
    // could not have been paired even if the palettes had disagreed on one -- it has to pass
    // through, and a merge that dropped what it could not pair would silently unbold every
    // Markdown list item.
    [Fact]
    public void Emit_KeepsDeclarationsThatAreNotColours()
    {
        string css = HighlightCssEmitter.Emit();

        Assert.Contains(".markdownListItem{font-weight:bold;}", css, StringComparison.OrdinalIgnoreCase);
    }

    // Both dictionaries have to answer for the same selectors, and the merge refuses rather than
    // dropping one side. This asserts the guard is wired to something real: it fires on dictionaries
    // that disagree, which is what a scope repainted in one palette and forgotten in the other
    // produces.
    [Fact]
    public void Emit_RefusesPalettesThatStyleDifferentSelectors()
    {
        // One scope in the second palette that the first cannot have. Built by addition rather than
        // by handing in two stock dictionaries, so the test states the asymmetry itself instead of
        // depending on ColorCode's defaults happening to differ.
        StyleDictionary lopsided = StyleDictionary.DefaultDark;
        lopsided.Add(new Style("scopeThatOnlyOnePaletteStyles") { Foreground = "#FF00FF" });

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => { HighlightCssEmitter.Emit(ColorCodeTheme.Styles, lopsided); });

        Assert.Contains("different selectors", thrown.Message, StringComparison.Ordinal);
    }
}
