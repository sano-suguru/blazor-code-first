using BlazorCodeFirst.IntegrationTests.Components;
using Bunit;

namespace BlazorCodeFirst.IntegrationTests;

/// <summary>
/// Renders both <c>.Template</c> overloads, and the brackets, into a real
/// <see cref="Microsoft.AspNetCore.Components.Forms.EditForm"/>, which is the one shape whose
/// <c>ChildContent</c> is a <c>RenderFragment&lt;EditContext&gt;</c>. The assertions look at what the form does — validation messages, the
/// <c>EditContext</c>'s own modification state, and what survives a parent re-render — rather than at generated
/// source, which <c>Compiler.Tests</c> already pins.
/// </summary>
/// <remarks>
/// <see cref="BindRenderingTests"/> covers the other spelling of the same target: a caller-owned, cached
/// <c>RenderFragment&lt;EditContext&gt;</c> handed to the scalar <c>.Param</c> overload. Both paths are kept
/// because they fail differently — the cached one depends on the author remembering to cache, and this one does
/// not.
/// </remarks>
public sealed class GenericTemplateRenderingTests : BunitContext
{
    [Fact]
    public void ContextIgnoredTemplate_SubmitRunsEditFormValidation()
    {
        var cut = Render<ContextIgnoringTemplateForm>();
        cut.Find("form").Submit();
        Assert.Contains("Name is required", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void BracketChildContent_SubmitRunsEditFormValidation()
    {
        // Same form and same assertion as the test above: the two spellings reach one parameter, so the
        // bracket one has to run the form's validation too (#322).
        var cut = Render<BracketChildContentForm>();
        cut.Find("form").Submit();
        Assert.Contains("Name is required", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextualTemplate_UsesEditContextAndSurvivesParentRerender()
    {
        var cut = Render<ContextReadingTemplateForm>();
        Assert.Equal("unchanged", cut.Find(".edit-context-state").TextContent);
        cut.Find("input").Change("Ada");
        Assert.Equal("modified", cut.Find(".edit-context-state").TextContent);
        cut.Find("button.rerender").Click();
        Assert.Equal("Ada", cut.Instance.Value.Name);
        Assert.Equal("modified", cut.Find(".edit-context-state").TextContent);
    }
}
