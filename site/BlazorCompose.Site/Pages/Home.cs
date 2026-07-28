using BlazorCompose;
using BlazorCompose.Site.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Pages;

[Route("/")]
public sealed partial class Home : ComposeComponentBase
{
    protected override View Body =>
        Section(
            Component<PageTitle>("BlazorCompose"),
            H1("Write Blazor UI as plain C#"),
            P("BlazorCompose turns a design-time Body expression into a RenderTreeBuilder method at "
                + "compile time. There is no runtime UI tree, no reflection, and no runtime expression "
                + "compilation — the generated component is an ordinary Blazor component."),
            P("This site is itself built with BlazorCompose and published as prerendered static HTML."),
            H2("Documentation"),
            Ul(
                ForEach(
                    Docs.All,
                    key: d => d.Slug,
                    content: d => Li(A(d.Title).Href($"/docs/{d.Slug}")))),
            H2("Live demo"),
            P(A("Counter").Href("/counter"), " exercises events, If, and keyed ForEach."))
        .Class("prose");
}
