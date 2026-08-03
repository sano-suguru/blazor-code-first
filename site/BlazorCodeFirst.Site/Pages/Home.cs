using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Pages;

[Route("/")]
public sealed partial class Home : BodyComponentBase
{
    protected override View Body =>
        Section.Class("prose")[
            Component<PageTitle>()["BlazorCodeFirst"],
            H1["Write Blazor UI as plain C#"],
            P["BlazorCodeFirst turns a design-time Body expression into a RenderTreeBuilder method at "
                + "compile time. There is no runtime UI tree, no reflection, and no runtime expression "
                + "compilation. The generated component is an ordinary Blazor component."],
            P["This site is itself built with BlazorCodeFirst and published as prerendered static HTML."],
            H2["Documentation"],
            Ul[
                ForEach(
                    Docs.All,
                    key: d => d.Slug,
                    content: d => Li[A.Href($"/docs/{d.Slug}")[d.Title]])],
            H2["Live demo"],
            P[A.Href("/counter")["Counter"], " exercises events, If, and keyed ForEach."]];
}
