using ColorCode.Styling;

namespace BlazorCompose.Site.DocGen;

/// <summary>Single source of the ColorCode style dictionary shared by the Markdown
/// pipeline (HTML class output) and the CSS emitter, so the emitted token classes and
/// the generated stylesheet stay in lockstep (parity by construction).</summary>
public static class ColorCodeTheme
{
    public static StyleDictionary Styles { get; } = StyleDictionary.DefaultLight;
}
