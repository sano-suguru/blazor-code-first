using Microsoft.AspNetCore.Components.Forms;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// <see cref="EditForm.ChildContent"/> bound through the context-ignoring <c>.Template</c> overload. This is the
/// shape <see cref="ValidatedNameForm"/> had to hand-write a cached <c>RenderFragment&lt;EditContext&gt;</c> for:
/// the scalar <c>.Param</c> overload can only take a delegate the author built. Here the generator emits the
/// outer context lambda, and the content is ordinary BlazorCodeFirst.
/// <see cref="BracketChildContentForm"/> is the shorter spelling of this same emission; this one stays because
/// <c>.Template</c> is the only channel a generic fragment under another name has.
/// </summary>
/// <remarks>
/// The model and the fields come from <see cref="ValidatedNameForm"/>'s file deliberately, so the two spellings
/// of the same form render the same content and only the channel differs.
/// </remarks>
public partial class ContextIgnoringTemplateForm : BodyComponentBase
{
    public NameModel Value { get; } = new();

    protected override View Body =>
        Component<EditForm>()
            .Param(form => form.Model, Value)
            .Template(form => form.ChildContent,
                Component<NameFields>().Param(fields => fields.Value, Value));
}

/// <summary>
/// The same form with its content in brackets. <see cref="EditForm.ChildContent"/> is a
/// <c>RenderFragment&lt;EditContext&gt;</c>, and the bracket channel reaches it by discarding the context,
/// emitting what <see cref="ContextIgnoringTemplateForm"/> gets from <c>.Template</c>. Kept beside that one
/// because the two spellings must reach the same parameter and render the same content (#322).
/// </summary>
public partial class BracketChildContentForm : BodyComponentBase
{
    public NameModel Value { get; } = new();

    protected override View Body =>
        Component<EditForm>()
            .Param(form => form.Model, Value)[
                Component<NameFields>().Param(fields => fields.Value, Value)];
}

/// <summary>
/// The same form through the contextual <c>.Template</c> overload, which names the <see cref="EditContext"/> the
/// form supplies. Reading <c>editContext.IsModified()</c> is what makes the context observable: no other
/// parameter of this component can produce that text, so a template that ignored or mis-bound its context would
/// render "unchanged" forever.
/// </summary>
/// <remarks>
/// <para>
/// The rerender button sits outside the <see cref="EditForm"/> on purpose. Clicking it re-runs this component's
/// generated <c>RenderView</c>, which hands <see cref="EditForm"/> a freshly allocated template delegate; the
/// form therefore re-renders its child content while keeping the <see cref="EditContext"/> it already had. What
/// that shows is that the template reads live context state across re-renders: the span is recomputed from the
/// context on every run rather than frozen at whatever the first render produced.
/// </para>
/// <para>
/// The <see cref="EditContext"/> is constructed here rather than left to <c>EditForm.Model</c> because nothing
/// else re-renders this component when a field changes (measured). <c>InputText</c>'s <c>ValueChanged</c>
/// callback is received by <see cref="NameFields"/>, and neither <see cref="EditForm"/> nor the
/// <c>CascadingValue</c> under it subscribes to <c>OnFieldChanged</c>, so the template's span would keep showing
/// the state it was first rendered with. Owning the context and re-rendering on <c>OnFieldChanged</c> is what a
/// component whose chrome reacts to the form's state has to do anyway.
/// </para>
/// </remarks>
public sealed partial class ContextReadingTemplateForm : BodyComponentBase, IDisposable
{
    private readonly EditContext _editContext;
    private int _renderCount;

    public NameModel Value { get; } = new();

    public ContextReadingTemplateForm()
    {
        _editContext = new EditContext(Value);
        _editContext.OnFieldChanged += OnFieldChanged;
    }

    protected override View Body =>
        Div[
            Button.Class("rerender").OnClick(() => _renderCount++)["Rerender"],
            Span.Class("render-count")[$"{_renderCount}"],
            Component<EditForm>()
                .Param(form => form.EditContext, _editContext)
                .Template(form => form.ChildContent, editContext =>
                    Fragment(
                        Span.Class("edit-context-state")[editContext.IsModified() ? "modified" : "unchanged"],
                        Component<NameFields>().Param(fields => fields.Value, Value)))];

    public void Dispose() => _editContext.OnFieldChanged -= OnFieldChanged;

    private void OnFieldChanged(object? sender, FieldChangedEventArgs e) => StateHasChanged();
}
