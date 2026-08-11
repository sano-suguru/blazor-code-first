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
        string css = HighlightCssEmitter.Emit();

        Assert.Contains($".{cls}{{color:{expected};}}", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($".{cls}{{color:{defaultLight};}}", css, StringComparison.OrdinalIgnoreCase);
    }
}
