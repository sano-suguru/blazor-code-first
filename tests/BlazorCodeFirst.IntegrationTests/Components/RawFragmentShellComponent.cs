using BlazorCodeFirst;

namespace BlazorCodeFirst.IntegrationTests.Components;

// Semantic shell exercising the RM3 vocabulary end-to-end: a nav + header shell, a Main panel whose body
// is rendered-Markdown injected via Html.Raw, and a wrapper-less Fragment grouping two siblings that
// become direct children of the outer div.
public partial class RawFragmentShellComponent : BodyComponentBase
{
    // Stand-in for pre-rendered, trusted Markdown HTML (assembly-embedded in production).
    private const string RenderedMarkdown = "<p>Hello <em>world</em></p>";

    protected override View Body =>
        Html.Div[
            Html.Nav[Html.A.Href("/")["Home"]],
            Html.Header[Html.H1["Docs"]],
            Html.Main[Html.Raw(RenderedMarkdown)],
            Html.Fragment(Html.H2["Section"], Html.P["Body"])];
}
