using System.Globalization;
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Samples.Guestbook.Components.Pages;

[Route("/")]
public sealed partial class GuestbookPage : BodyComponentBase
{
    [Inject]
    public required GuestbookStore Store { get; set; }

    // EditForm.Model is just an object reference; nothing repopulates it from the posted fields
    // unless the property holding it is itself [SupplyParameterFromForm] (verified: a private field
    // here left the posted values unbound and DataAnnotationsValidator reported "required" on an
    // empty model). The framework form-value-binds every [SupplyParameterFromForm] property on
    // every POST to this page, not only ones on the form that was actually submitted, and sets
    // NewEntry to null when this POST carried no NewEntry.* fields (verified: POSTing a delete
    // form threw "EditForm requires either a Model parameter" from this property coming back null).
    // OnInitialized, which runs after that binding, is what BL0008 points at instead of a property
    // initializer, and `??=` there covers both the plain GET and the binding-left-it-null case.
    [SupplyParameterFromForm]
    public NewEntryModel NewEntry { get; set; } = null!;

    protected override void OnInitialized() => NewEntry ??= new();

    private void HandleCreate()
    {
        Store.Add(NewEntry.Name, NewEntry.Message);
        NewEntry = new();
    }

    private void HandleDelete(int id) => Store.Delete(id);

    protected override View Body => Div.Class("guestbook")[
        Header.Class("guestbook-header")[
            H1["Guestbook"],
            P["A static SSR .FormName() form, a per-row named delete form, and one " +
                "InteractiveServer island."]],

        Section.Class("guestbook-form")[
            H2["Sign the guestbook"],
            // The fields live directly under this EditForm, not in a separate [ViewPart]/component,
            // because InputText/InputTextArea derive the posted field's name from their own
            // ValueExpression: a child component's own `Value` parameter would render `Value.Name`,
            // which would not match what [SupplyParameterFromForm] on NewEntry (below) expects to
            // bind back (verified: split into a child component first, POST came back 500,
            // "EditForm requires either a Model parameter" — the posted fields never reached it).
            Component<EditForm>()
                .Param(f => f.Model, NewEntry)
                .Param(f => f.FormName, "create")
                .Param(f => f.OnValidSubmit, EventCallback.Factory.Create<EditContext>(this, HandleCreate))[
                    Component<DataAnnotationsValidator>(),
                    Component<ValidationSummary>(),
                    Label["Name"],
                    Component<InputText>().Bind(c => c.Value, () => NewEntry.Name),
                    Label["Message"],
                    Component<InputTextArea>().Bind(c => c.Value, () => NewEntry.Message),
                    Button.Type("submit")["Sign the guestbook"]]],

        Section.Class("guestbook-search")[
            H2["Search"],
            Component<LiveSearch>().RenderMode(RenderMode.InteractiveServer)],

        Section.Class("guestbook-entries")[
            H2["Entries"],
            ForEach(Store.All(),
                key: e => e.Id,
                content: entry =>
                    Article.Class("entry")[
                        Header[
                            Strong[entry.Name],
                            Time.Attr("datetime", entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture))[
                                entry.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)]],
                        P[entry.Message],
                        Form.Attr("method", "post").FormName($"delete-{entry.Id}")
                            .On("onsubmit", () => HandleDelete(entry.Id))[
                                Component<AntiforgeryToken>(),
                                Button.Type("submit").Class("delete")["Delete"]]])]];
}
