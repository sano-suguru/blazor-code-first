using BlazorCodeFirst.IntegrationTests.Components;
using Bunit;

namespace BlazorCodeFirst.IntegrationTests;

/// <summary>
/// Runs a bound component through bUnit's real dispatch (not the generator's C# output, which
/// <c>Compiler.Tests</c> already covers) to confirm the round trip works, and to settle what an empty
/// text input actually delivers to the setter (<see cref="EmptyInput_DeliversEmptyStringNotNull"/>).
/// The two <see cref="Components.ValidatedNameForm"/> tests are the only ones anywhere whose result
/// depends on the <c>{Name}Expression</c> parameter; every other binding test renders the same with or
/// without it.
/// <c>SetUpdatesAttributeName</c>'s DOM resynchronization is <em>not</em> covered here — measured and
/// found unobservable from bUnit, because bUnit's <c>Input()</c> writes into the AngleSharp DOM the
/// value that reaches the setter, so the divergence resync repairs cannot be constructed. It is covered
/// by <c>bind-resync.spec.ts</c> in <c>BlazorCodeFirst.WebAppTests/browser</c>, against a real browser.
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
    public void ValidatedNameForm_SubmittedEmpty_ReportsTheRequiredFieldByName()
    {
        // InputText identifies its field from ValueExpression. Without it, InputBase either finds no
        // FieldIdentifier at all or resolves the wrong one, and the DataAnnotations result never attaches
        // to this input. These two assertions are the only place in the repository where the presence of
        // the {Name}Expression parameter changes an observed outcome.
        var cut = Render<ValidatedNameForm>();

        cut.Find("form").Submit();

        Assert.Contains("Name is required", cut.Markup, StringComparison.Ordinal);

        // The discriminating half. ValidationSummary lists every message in the EditContext regardless of
        // which field produced it, so it alone would still show the text if the identifier pointed
        // somewhere else. InputBase's CssClass comes from EditContext.FieldCssClass(FieldIdentifier), so
        // "invalid" here says the identifier resolved from ValueExpression really is NameModel.Name.
        // Compared for equality rather than containment: "modified invalid" contains "valid".
        Assert.Equal("invalid", cut.Find("input").GetAttribute("class"));
    }

    [Fact]
    public void ValidatedNameForm_FilledIn_MarksTheSameFieldModifiedAndValid()
    {
        // The counterpart that keeps the assertion above honest: the class is not simply always "invalid".
        // It also measures the second consumer of the FieldIdentifier — InputBase calls
        // EditContext.NotifyFieldChanged with it on every write, which is what "modified" records.
        var cut = Render<ValidatedNameForm>();

        cut.Find("input").Change("Ada");
        cut.Find("form").Submit();

        Assert.DoesNotContain("Name is required", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("modified valid", cut.Find("input").GetAttribute("class"));
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
