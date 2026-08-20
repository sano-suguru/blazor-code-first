using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using BlazorCodeFirst.Site.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Pages;

/// <summary>
/// The landing page, built as a run of diptychs: every claim is placed beside the code that shows
/// it, and the halves alternate sides down the page.
/// </summary>
/// <remarks>
/// The shape is the argument. This library's whole proposition is a correspondence — the expression
/// you write against the render method the compiler emits, the markup language against the one
/// language — so a page whose repeating unit is a pair says it before any sentence does.
///
/// Nothing here is a mock. The figures are the surface's real spelling, the generated frames are the
/// ones ARCHITECTURE.md §2.4 documents, the build error is BCF3016's message as the compiler reports
/// it, and the first pair's output half is what rendering its left half actually produces
/// (FigureTests).
///
/// The hero carries no figure. The pair under it is the first thing the page shows, because the
/// correspondence a reader needs first is the one between the expression and the HTML — not the one
/// between the expression and the frames, which is the compiler's question and comes second. The
/// page ran for a while without that pair at all, which left it arguing a correspondence it never
/// exhibited.
/// </remarks>
[Route("/")]
public sealed partial class Home : BodyComponentBase
{
    // --prerelease is not decoration. The published version carries a prerelease suffix, so without
    // the flag the command looks for a stable version that does not exist and resolves nothing. This
    // is the first command a reader runs, and it was wrong here while both READMEs had it right;
    // InstallCommandTests now reads it out of README.md rather than trusting this literal.
    private const string InstallCommand = "dotnet add package BlazorCodeFirst --prerelease";

    /// <summary>The sentence this page leads with, and the lede under it.</summary>
    /// <remarks>
    /// Literals here rather than strings in the content tree, unlike every sentence a document carries.
    /// The home page is not a document: it has no front matter to declare them in, and no translation
    /// to keep them in step with. ShellStrings makes the argument that puts reader-facing text in the
    /// content tree, and it is about strings that exist once per language.
    /// </remarks>
    private const string Headline = "Write Blazor UI as plain C#";

    /// <inheritdoc cref="Headline"/>
    private const string Lede =
        "An ordinary Blazor component, written in the language the rest of the app is written in.";

    /// <summary>What a search result shows under this page's title.</summary>
    /// <remarks>
    /// Composed from the two above rather than written a third time. A paraphrase of the page is what
    /// this was, and it had already lost a word.
    /// </remarks>
    private const string Summary = Headline + ". " + Lede;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    private bool _copied;

    protected override View Body =>
        Fragment(
            Component<PageTitle>()[SiteMetadata.Name],
            // No Alternates: this page has one edition. SiteMeta.Tags says why that means none.
            Component<SiteMeta>()
                .Param(m => m.Title, SiteMetadata.Name)
                .Param(m => m.Description, Summary)
                .Param(m => m.Path, "/")
                .Param(m => m.Lang, Docs.Canonical),
            Div.Class("shell")[
                // One cell, not three. The three blocks below carry their own margins, and .hero is
                // a grid: as direct children they would each take a row and the grid gap would add
                // itself on top of every margin, which measured 88px and 104px where the design
                // asks for 24px and 40px.
                Section.Class("hero")[
                    Div[
                        H1.Class("hero-title")[Headline],
                        P.Class("hero-lede")[Lede],
                        Div.Class("hero-actions")[
                            Div.Class("install")[
                                Code[InstallCommand],
                                Button
                                    .Class("install-copy")
                                    .Type("button")
                                    .Attr("aria-label", "Copy the install command")
                                    .OnClick(() => CopyInstallCommandAsync())[
                                        _copied ? "Copied" : "Copy"]],
                            A.Href("/docs/getting-started").Class("chip chip--primary")["Get started"]]]],

                // The correspondence a reader needs first, before any claim about what the build
                // does with it. Full width: two code columns need the measure.
                Section.Class("split split--wide")[
                    Div[
                        H2.Class("split-title")["The expression names the HTML it produces"],
                        P.Class("split-body")[
                            "Attributes chain onto the element, children go in brackets, and a bare string "
                                + "is a text node."]],
                    Div.Class("pair")[
                        Figure.Class("figure")[
                            Figcaption["What you write", Em["C#"]],
                            CodeSamples.Surface()],
                        Figure.Class("figure")[
                            Figcaption["What it renders", Em["HTML"]],
                            CodeSamples.SurfaceOutput()]]],

                // Between the two pairs, not after them. Reversed as well, so three consecutive
                // sections do not repeat one shape: a pair, a single figure on the other side, a
                // pair. Two pair sections in a row read as a template even when their contents
                // differ. It also follows the section above by argument -- the HTML correspondence
                // holds, and this is the HTML the build declines to let you write.
                Section.Class("split split--flip")[
                    Div[
                        H2.Class("split-title")["Some HTML that parses is still wrong"],
                        P.Class("split-body")[
                            "A void element with children prerenders one DOM tree and hydrates another, so "
                                + "the build refuses it by name."]],
                    Figure.Class("figure")[
                        Figcaption["dotnet build", Em["error"]],
                        CodeSamples.Diagnostic()]],

                // What the compiler does with it. Full width: two code columns need the measure.
                Section.Class("split split--wide")[
                    Div[
                        H2.Class("split-title")["The expression does not survive the build"],
                        P.Class("split-body")[
                            "The generator translates it into a RenderView override on the same partial "
                                + "class, and the IL trimmer removes the design-time API it was written "
                                + "with. Adjacent constants fold, so the class channel below costs one "
                                + "attribute frame."]],
                    Div.Class("pair")[
                        Figure.Class("figure")[
                            Figcaption["What you write", Em["design time"]],
                            CodeSamples.DesignTime()],
                        Figure.Class("figure")[
                            Figcaption["What it compiles to", Em["generated"]],
                            CodeSamples.Generated()]]],

                Section.Class("split split--wide split--close")[
                    Div[
                        H2.Class("split-title")["Read the guide"],
                        P.Class("split-body")[
                            "The surface is small enough to read end to end."]],
                    Ul.Class("index-list")[
                        ForEach(
                            Docs.ForLang(Docs.Canonical),
                            key: d => d.Slug,
                            content: d => Li[
                                A.Href($"/docs/{d.Slug}").Class("index-link")[
                                    d.Title,
                                    Span["/docs/" + d.Slug]]])]]]);

    /// <summary>Copies the install command, and marks the button as having done so.</summary>
    /// <remarks>
    /// Deliberately not a toast: the label is the confirmation, and it sits under the pointer that
    /// asked for it. The state is not reset on a timer — a second click re-copies and the label is
    /// already correct, so nothing is gained by making it flicker back.
    ///
    /// The button does nothing until the WebAssembly runtime has started. Every route here is
    /// prerendered to static HTML, so the page is readable and navigable before that; this is the
    /// same trade-off the counter demo has, recorded in the project file.
    /// </remarks>
    private async Task CopyInstallCommandAsync()
    {
        await Js.InvokeVoidAsync("navigator.clipboard.writeText", InstallCommand);
        _copied = true;
    }
}
