using System.ComponentModel.DataAnnotations;
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The model <see cref="ValidatedNameForm"/> edits. Exactly one <c>[Required]</c> property, and that is
/// load-bearing rather than merely sufficient. The discriminating assertion is the input's
/// <c>"invalid"</c> CSS class, which comes from <c>EditContext.FieldCssClass(FieldIdentifier)</c> and so
/// is <c>"invalid"</c> only when the identifier resolved from <c>{Name}Expression</c> names a field that
/// actually failed. With one required property, a mis-pointed identifier names something with no
/// message and the class comes back wrong, which is what makes the assertion measure anything. Add a
/// second <c>[Required]</c> property and every field has a message, so a mis-pointed identifier yields
/// <c>"invalid"</c> too — the repository's only measurement of <em>which</em> field the identifier names
/// would keep passing while measuring nothing. Add one only together with assertions that tell the two
/// fields apart.
/// </summary>
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
/// assertions look at is still generated from a BlazorCodeFirst <c>Body</c>. #161 closed that gap with
/// <c>.Template</c>, so this is no longer the shape to copy for an <c>EditForm</c> — see
/// <see cref="ContextIgnoringTemplateForm"/> for the one that is. It stays here as the regression
/// fixture for a caller-owned cached <c>RenderFragment&lt;EditContext&gt;</c> through the scalar
/// <c>.Param</c>, which remains the way to give the parameter a stable delegate identity.
/// </remarks>
public partial class ValidatedNameForm : BodyComponentBase
{
    public NameModel Value { get; } = new();

    // Cached, not an expression-bodied property. A property hands ChildContent a freshly allocated
    // delegate on every render, so EditForm's diff sees a changed parameter each time and re-renders
    // the fragment unconditionally. One delegate built once keeps the parameter reference-stable, which
    // is what a reader copying this as the EditForm workaround should copy. Built in the constructor
    // because a field initializer cannot read the Value property.
    private readonly RenderFragment<EditContext> _fields;

    public ValidatedNameForm() =>
        _fields = _ => builder =>
        {
            builder.OpenComponent<NameFields>(0);
            builder.AddComponentParameter(1, nameof(NameFields.Value), Value);
            builder.CloseComponent();
        };

    protected override View Body =>
        Component<EditForm>()
            .Param(c => c.Model, Value)
            .Param(c => c.ChildContent, _fields);
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
