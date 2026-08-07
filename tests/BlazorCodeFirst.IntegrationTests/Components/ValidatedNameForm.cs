using System.ComponentModel.DataAnnotations;
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>The model <see cref="ValidatedNameForm"/> edits; one required field is enough.</summary>
public sealed class NameModel
{
    [Required(ErrorMessage = "Name is required")]
    public string Name { get; set; } = "";
}

/// <summary>
/// A component <c>.Bind</c> under an <see cref="EditForm"/>. This is the only shape in the repository
/// that can observe the <c>{Name}Expression</c> parameter at all: everywhere else a binding renders the
/// same thing whether or not it is emitted, because <c>Value</c> and <c>ValueChanged</c> carry the whole
/// round trip on their own. <see cref="InputBase{TValue}"/> resolves a <see cref="FieldIdentifier"/>
/// from <c>ValueExpression</c>, and that identifier is what ties this input to
/// <see cref="NameModel.Name"/> — which is why the getter-lambda spelling was chosen over
/// <c>.Bind(ref _name)</c> or a bare expression, neither of which could supply it.
/// </summary>
/// <remarks>
/// <see cref="EditForm.ChildContent"/> is a <c>RenderFragment&lt;EditContext&gt;</c>, and the bracket
/// children channel binds a non-generic <c>RenderFragment</c> named <c>ChildContent</c>, so
/// <c>Component&lt;EditForm&gt;()[…]</c> reports BCF3013 (measured). The fields therefore go through
/// <c>.Param</c> with the fragment written out by hand — the one thing here that is not the surface
/// under test, and kept to rendering <see cref="NameFields"/> and nothing else, so everything the
/// assertions look at is still generated from a BlazorCodeFirst <c>Body</c>.
/// </remarks>
public partial class ValidatedNameForm : BodyComponentBase
{
    public NameModel Value { get; } = new();

    protected override View Body =>
        Component<EditForm>()
            .Param(c => c.Model, Value)
            .Param(c => c.ChildContent, Fields);

    private RenderFragment<EditContext> Fields =>
        _ => builder =>
        {
            builder.OpenComponent<NameFields>(0);
            builder.AddComponentParameter(1, nameof(NameFields.Value), Value);
            builder.CloseComponent();
        };
}

/// <summary>The fields of <see cref="ValidatedNameForm"/>, so that everything under test is generated.</summary>
public partial class NameFields : BodyComponentBase
{
    [Parameter]
    public NameModel Value { get; set; } = new();

    protected override View Body =>
        Fragment(
            Component<DataAnnotationsValidator>(),
            Component<InputText>().Bind(c => c.Value, () => Value.Name),
            Component<ValidationSummary>(),
            Button.Type("submit")["Submit"]);
}
