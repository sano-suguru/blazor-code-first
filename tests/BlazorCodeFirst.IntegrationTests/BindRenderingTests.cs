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
    public void BoundTextInput_OnInput_RendersTheFieldIntoTheValueAttribute()
    {
        var cut = Render<BoundTextInput>();

        cut.Find("input").Input("hello");

        // Not a DOM round trip: the re-render puts the field's value into the value attribute, which is
        // the direction the sibling write-back test does not measure.
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

    [Fact]
    public void TwoBoundInputs_OnInput_WritesOnlyTheFirstField()
    {
        var cut = Render<TwoBoundInputs>();

        cut.Find("input").Input("typed");

        Assert.Equal("typed", cut.Instance.Live);
        Assert.Equal("", cut.Instance.Committed);
    }

    [Fact]
    public void TwoBoundInputs_OnChange_WritesOnlyTheSecondField()
    {
        var cut = Render<TwoBoundInputs>();

        cut.Find("input").Change("committed");

        Assert.Equal("committed", cut.Instance.Committed);
        Assert.Equal("", cut.Instance.Live);
    }

    [Fact]
    public void TwoBoundInputs_BothEvents_LeaveBothFieldsIntact()
    {
        // The handler IDs are assigned per event frame. If the second binding's binder were wired to the
        // first frame, or the two shared one ID, one of these writes would land in the wrong field —
        // which no frame-level assertion can see.
        var cut = Render<TwoBoundInputs>();

        cut.Find("input").Input("live");
        cut.Find("input").Change("done");

        Assert.Equal("live", cut.Instance.Live);
        Assert.Equal("done", cut.Instance.Committed);
    }

    /// <summary>
    /// #158 measured that a non-string attribute value follows the formatting thread's culture, and
    /// withheld non-string values on that ground. A binding formats inside <c>BindConverter.FormatValue</c>
    /// under the culture written at the call site, so the same measurement run in the opposite direction
    /// is what says the ground no longer holds (#307).
    /// </summary>
    [Fact]
    public void BoundDecimal_IgnoresTheDefaultThreadCulture()
    {
        var previous = System.Globalization.CultureInfo.DefaultThreadCurrentCulture;
        try
        {
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var cut = Render<BoundDecimalInput>();

            // de-DE writes 1234,5. The call site asked for the invariant culture and gets it.
            Assert.Equal("1234.5", cut.Find("input").GetAttribute("value"));
        }
        finally
        {
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = previous;
        }
    }

    /// <summary>#171's rule — a null value omits the attribute — reaching the binding path.</summary>
    [Fact]
    public void BoundNullableInt_WithNoValue_OmitsTheValueAttribute()
    {
        var cut = Render<BoundNullableIntInput>();

        Assert.Null(cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void BoundNullableInt_OnInput_WritesBackTheParsedValue()
    {
        var cut = Render<BoundNullableIntInput>();

        cut.Find("input").Input("41");

        Assert.Equal(41, cut.Instance.Age);
    }

    [Fact]
    public void BoundInt_OnInput_WritesBackTheParsedValue()
    {
        var cut = Render<BoundIntInput>();

        cut.Find("input").Input("41");

        Assert.Equal(41, cut.Instance.Age);
    }

    /// <summary>
    /// The parse-failure semantics, which cost no emission of this compiler's own:
    /// <c>CreateBinderCore</c> swallows the conversion failure and never calls the setter, so the field
    /// keeps what it had. The other half — the element still showing what the author typed — is
    /// <c>SetUpdatesAttributeName</c>'s, which this class's summary records as unobservable from bUnit and
    /// covered by <c>bind-resync.spec.ts</c> against a real browser.
    /// </summary>
    [Fact]
    public void BoundInt_WithUnparsableInput_KeepsThePreviousValue()
    {
        var cut = Render<BoundIntInput>();

        cut.Find("input").Input("not a number");

        Assert.Equal(30, cut.Instance.Age);
    }

    [Fact]
    public void BoundEnum_RendersItsName()
    {
        var cut = Render<BoundEnumSelect>();

        Assert.Equal("Monday", cut.Find("select").GetAttribute("value"));
    }

    /// <summary>
    /// The enum round trip: <c>FormatEnumValueCore</c> writes the name out and <c>ConvertToEnum</c> reads
    /// the same spelling back. The two are the framework's, and this measures that they meet.
    /// </summary>
    [Fact]
    public void BoundEnum_OnChange_RoundTripsThroughItsName()
    {
        var cut = Render<BoundEnumSelect>();

        cut.Find("select").Change("Friday");

        Assert.Equal(System.DayOfWeek.Friday, cut.Instance.Day);
    }

    [Fact]
    public void BoundEnum_WithAnUnknownName_KeepsThePreviousValue()
    {
        var cut = Render<BoundEnumSelect>();

        cut.Find("select").Change("Caturday");

        Assert.Equal(System.DayOfWeek.Monday, cut.Instance.Day);
    }

    /// <summary>
    /// The format's own measurement, and the reason it is in this surface: an <c>input type="date"</c>
    /// requires <c>yyyy-MM-dd</c> in both directions, and this surface cannot read the element's
    /// <c>type</c> to supply it the way Razor does.
    /// </summary>
    [Fact]
    public void BoundDate_RendersUnderTheWrittenFormat()
    {
        var cut = Render<BoundDateInput>();

        Assert.Equal("2026-08-14", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void BoundDate_OnInput_RoundTripsThroughTheWrittenFormat()
    {
        var cut = Render<BoundDateInput>();

        cut.Find("input").Input("2027-01-31");

        Assert.Equal(new System.DateOnly(2027, 1, 31), cut.Instance.Due);
    }
}
