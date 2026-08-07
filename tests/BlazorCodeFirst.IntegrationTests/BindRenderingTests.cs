using BlazorCodeFirst.IntegrationTests.Components;
using Bunit;

namespace BlazorCodeFirst.IntegrationTests;

/// <summary>
/// Runs a bound component through bUnit's real dispatch (not the generator's C# output, which
/// <c>Compiler.Tests</c> already covers) to confirm the round trip works, and to settle two measurements
/// that the generated code has to be correct against: what an empty text input actually delivers to the
/// setter, and whether bUnit's headless DOM observes the resynchronization <c>SetUpdatesAttributeName</c>
/// exists for.
/// </summary>
public sealed class BindRenderingTests : BunitContext
{
    [Fact]
    public void BoundTextInput_OnInput_WritesBackToTheField()
    {
        var cut = Render<BoundTextInput>();

        cut.Find("input").Input("hello");

        Assert.Equal("hello", cut.Instance.Name);
    }

    [Fact]
    public void BoundTextInput_StateChange_UpdatesTheValueAttribute()
    {
        var cut = Render<BoundTextInput>();

        cut.Find("input").Input("hello");

        Assert.Equal("hello", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void BoundCheckbox_OnChange_TogglesTheField()
    {
        var cut = Render<BoundCheckbox>();

        cut.Find("input").Change(true);

        Assert.True(cut.Instance.Agreed);
    }

    [Fact]
    public void BoundCheckbox_FalseState_OmitsTheCheckedAttribute()
    {
        // The #158 bool path: false omits the attribute rather than writing checked="false".
        var cut = Render<BoundCheckbox>();

        Assert.Null(cut.Find("input").GetAttribute("checked"));
    }

    [Fact]
    public void NormalizingTextInput_TrimmedValue_NormalizesTheField()
    {
        var cut = Render<NormalizingTextInput>();

        cut.Find("input").Input("  x  ");

        Assert.Equal("x", cut.Instance.Name);
    }

    [Fact]
    public void NormalizingTextInput_TrimmedValue_ResynchronizesTheValueAttribute()
    {
        // Measured (see the report): SetUpdatesAttributeName's DOM resync is observable through
        // bUnit's headless renderer, not only through a browser's. After typing "  x  ", the field
        // holds the trimmed "x" (asserted above); this checks that the rendered value attribute
        // catches up to it rather than keeping the untrimmed text the user actually typed.
        var cut = Render<NormalizingTextInput>();

        cut.Find("input").Input("  x  ");

        Assert.Equal("x", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void EmptyInput_DeliversEmptyStringNotNull()
    {
        var cut = Render<BoundTextInput>();
        cut.Instance.Name = "seeded";

        cut.Find("input").Input("");

        // Measured (see Decorations.cs Bind <remarks>): an empty text input's oninput dispatch
        // delivers "", not null. The non-nullable Func<string> / Action<string> surface needed no
        // change for this outcome.
        Assert.Equal("", cut.Instance.Name);
    }
}
