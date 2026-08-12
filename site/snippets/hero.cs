[Route("/")]
public sealed partial class Home
    : BodyComponentBase
{
    protected override View Body =>
        Section.Class("prose")[
            H1["Blazor UI in C#"],
            P["Attributes first."],
            A.Href("/docs")["The guide"]];
}
