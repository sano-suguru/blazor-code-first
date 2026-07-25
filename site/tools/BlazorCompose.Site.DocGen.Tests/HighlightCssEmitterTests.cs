using System.Text.RegularExpressions;
using BlazorCompose.Site.DocGen;
using Xunit;

namespace BlazorCompose.Site.DocGen.Tests;

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
}
