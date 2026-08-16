using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BlazorCodeFirst.Site.DocGen;
using ColorCode.Common;
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
    // One row per role, not per scope. Which scopes have to be repainted is derived below; this pins
    // what each of the four roles is painted with, so a palette edit that swapped two of them is read
    // as the change to the design it would be.
    [Theory]
    [InlineData("keyword", "#463ECC", "#C5A2FF")]
    [InlineData("string", "#006647", "#69D6AA")]
    [InlineData("comment", "#676871", "#8F919F")]
    [InlineData("htmlElementName", "#161822", "#E6E7ED")]
    public void Emit_PairsBothPalettesInOneDeclaration(string cls, string light, string dark)
    {
        string css = HighlightCssEmitter.Emit();

        Assert.Contains($".{cls}{{color:light-dark({light},{dark});}}", css, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every fence language a snippet can open, as the extension map spells it.</summary>
    public static TheoryData<string> MappedLanguages()
    {
        var data = new TheoryData<string>();
        foreach (string language in SnippetConverter.Languages.Values.Distinct().Order(StringComparer.Ordinal))
        {
            data.Add(language);
        }

        return data;
    }

    // A language ColorCode does not know renders as unhighlighted text, which is the state
    // SnippetConverter's own map exists to prevent ("a figure that silently lost its highlighting
    // looks like a theme regression"). The fence token is what Markdown.ColorCode resolves, so the
    // lookup here is the one the pipeline performs.
    [Theory]
    [MemberData(nameof(MappedLanguages))]
    public void Emit_HasALanguageBehindEveryMappedFenceToken(string language) =>
        Assert.True(
            ColorCode.Languages.FindById(language) is not null,
            $"SnippetConverter maps a source extension to the fence language '{language}', which " +
            "ColorCode does not know. A fence in it renders as plain text.");

    /// <summary>Every scope those languages can emit, one case each.</summary>
    public static TheoryData<string, string> ScopesOfMappedLanguages()
    {
        var data = new TheoryData<string, string>();
        foreach (string language in SnippetConverter.Languages.Values.Distinct().Order(StringComparer.Ordinal))
        {
            foreach (string scope in ScopesOf(language))
            {
                data.Add(language, scope);
            }
        }

        return data;
    }

    [Fact]
    public void ScopesOfMappedLanguages_AreFound() =>
        // The theory below reads as a pass over an empty set if the scope walk stops finding
        // anything, which is what a ColorCode release that reshaped LanguageRule would produce.
        Assert.NotEmpty(ScopesOfMappedLanguages());

    // The check #400 records as missing. Adding ".html" to the extension map needed every HTML scope
    // repainted, and nothing said so: Emit_CoversEveryClassUsedInRealHtml asserts a rule EXISTS for
    // every emitted class, and ColorCode's own dictionaries already carry one for every scope of
    // every language they know. Measured at the time, the landing page's HTML figure rendered with
    // .htmlElementName at #A31515 and .htmlAttributeName at #FF0000 while that test stayed green.
    //
    // Derived from the language rather than from a sample, so it covers a scope no figure has reached
    // yet -- .htmlEntity is in no figure today and arrives red the first time prose about HTML writes
    // an entity. It is also why the seven no longer have to be hand-listed above, which was the step
    // a third language would have had to remember.
    [Theory]
    [MemberData(nameof(ScopesOfMappedLanguages))]
    public void Emit_PaintsEveryScopeAMappedLanguageCanEmit(string language, string scope)
    {
        Assert.True(
            ColorCodeTheme.Styles.Contains(scope),
            $"'{language}' can emit the scope '{scope}', which ColorCodeTheme does not style at all.");

        string cls = ColorCodeTheme.Styles[scope].ReferenceName;
        string css = HighlightCssEmitter.Emit();

        // Every rule for the class, not the first: a repaint applied to a duplicate dictionary entry
        // would leave the original beside it, and reading only one of the two would miss that.
        MatchCollection rules = Regex.Matches(css, @"\." + Regex.Escape(cls) + @"\{([^}]*)\}");
        Assert.True(rules.Count > 0, $"'{scope}' emits class '{cls}', which has no rule in the stylesheet.");

        foreach (Match rule in rules)
        {
            Match colour = Regex.Match(rule.Groups[1].Value, @"color:light-dark\((#[0-9A-Fa-f]{6}),(#[0-9A-Fa-f]{6})\);");

            Assert.True(
                colour.Success
                && ColorCodeTheme.LightColours.Contains(colour.Groups[1].Value)
                && ColorCodeTheme.DarkColours.Contains(colour.Groups[2].Value),
                $"'{scope}' emits class '{cls}', whose rule is '{rule.Value}'. A scope a mapped fence " +
                "language can reach has to be repainted onto the site's palette; this one kept " +
                "ColorCode's Visual Studio colour.");
        }
    }

    /// <summary>Every scope one language can emit, following the languages it embeds.</summary>
    /// <remarks>
    /// ColorCode marks an embedded language with a "&amp;" prefix where a scope name would go, which
    /// is how an HTML rule hands a <c>&lt;script&gt;</c> body to the JavaScript language. Those
    /// scopes reach the same stylesheet, so they are the same obligation.
    /// </remarks>
    private static SortedSet<string> ScopesOf(string language)
    {
        var scopes = new SortedSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Walk(language);
        return scopes;

        void Walk(string id)
        {
            if (!visited.Add(id) || ColorCode.Languages.FindById(id) is not { } found)
            {
                return;
            }

            foreach (var rule in found.Rules)
            {
                foreach (string? scope in rule.Captures.Values)
                {
                    if (scope is null)
                    {
                        continue;
                    }

                    if (scope.StartsWith(ScopeName.LanguagePrefix, StringComparison.Ordinal))
                    {
                        Walk(scope[ScopeName.LanguagePrefix.Length..]);
                        continue;
                    }

                    scopes.Add(scope);
                }
            }
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
