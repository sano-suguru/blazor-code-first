using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
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
/// ones ARCHITECTURE.md §2.4 documents, and the build error is BCF3016's message as the compiler
/// reports it.
/// </remarks>
[Route("/")]
public sealed partial class Home : BodyComponentBase
{
    private const string InstallCommand = "dotnet add package BlazorCodeFirst";

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    private bool _copied;

    protected override View Body =>
        Fragment(
            Component<PageTitle>()["BlazorCodeFirst"],
            Div.Class("shell")[
                Section.Class("hero")[
                    Div[
                        H1.Class("hero-title")["Write Blazor UI as plain C#"],
                        P.Class("hero-lede")[
                            "A design-time Body expression becomes a RenderTreeBuilder method at compile "
                                + "time. No runtime UI tree, no reflection, no expression compilation — the "
                                + "result is an ordinary Blazor component."],
                        Div.Class("hero-actions")[
                            Div.Class("install")[
                                Code[InstallCommand],
                                Button
                                    .Class("install-copy")
                                    .Attr("type", "button")
                                    .Attr("aria-label", "Copy the install command")
                                    .OnClick(() => CopyInstallCommandAsync())[
                                        _copied ? "Copied" : "Copy"]],
                            A.Href("/docs/getting-started").Class("chip chip--primary")["Get started"]]],
                    Figure.Class("figure")[
                        Figcaption["Pages/Home.cs", Em["surface"]],
                        CodeSamples.Component()]],

                // What the compiler does with it. Full width: two code columns need the measure.
                Section.Class("split split--wide")[
                    Div[
                        H2.Class("split-title")["The expression does not survive the build"],
                        P.Class("split-body")[
                            "The generator translates the expression into an override of RenderView on the "
                                + "same partial class, and the design-time API it was written with is "
                                + "unreachable at runtime — the IL trimmer removes it. Adjacent constants "
                                + "fold, so the class channel below costs one attribute frame no matter how "
                                + "many times it is written."]],
                    Div.Class("pair")[
                        Figure.Class("figure")[
                            Figcaption["What you write", Em["design time"]],
                            CodeSamples.DesignTime()],
                        Figure.Class("figure")[
                            Figcaption["What it compiles to", Em["generated"]],
                            CodeSamples.Generated()]]],

                Section.Class("split")[
                    Div[
                        H2.Class("split-title")["Three stages, all before the app runs"],
                        P.Class("split-body")[
                            "Nothing in this sequence happens in the browser. Sequence numbers are assigned "
                                + "at emit time and are stable against the syntax, which is what lets Blazor "
                                + "diff the result."]],
                    // The index sits above its heading, never beside it. A number in a narrow left
                    // column with the heading to its right is the single most recognisable
                    // templated-editorial shape there is, and this is the only numbered section on
                    // the site precisely because the order here is load-bearing.
                    Ol.Class("steps")[
                        Li.Class("step")[
                            Span.Class("step-index")["1.0"],
                            H3.Class("step-title")["Read the expression"],
                            P.Class("step-note")[
                                "The Body or Chrome getter is classified against the statically "
                                    + "sequenceable subset."]],
                        Li.Class("step")[
                            Span.Class("step-index")["2.0"],
                            H3.Class("step-title")["Fold what is constant"],
                            P.Class("step-note")[
                                "Runs of constant siblings collapse into a single markup frame, and the "
                                    + "class channel joins into one attribute."]],
                        Li.Class("step")[
                            Span.Class("step-index")["3.0"],
                            H3.Class("step-title")["Emit RenderView"],
                            P.Class("step-note")[
                                "A partial-class override of RenderView, called from the base "
                                    + "BuildRenderTree like any other component."]]]],

                // Reversed: the failure is the evidence, so it takes the left half at wide widths.
                Section.Class("split split--flip")[
                    Div[
                        H2.Class("split-title")["Some HTML that parses is still wrong"],
                        P.Class("split-body")[
                            "A void element with children serializes a closing tag under static SSR, and the "
                                + "HTML parser pushes those children out to siblings. Interactive rendering "
                                + "has no parser in the way and keeps them inside, so the prerendered page "
                                + "and the hydrated one disagree."],
                        P.Class("split-body")[
                            "The build refuses it by name rather than letting the two disagree in "
                                + "production."]],
                    Figure.Class("figure")[
                        Figcaption["dotnet build", Em["error"]],
                        CodeSamples.Diagnostic()]],

                Section.Class("split split--wide split--close")[
                    Div[
                        H2.Class("split-title")["Read the guide"],
                        P.Class("split-body")[
                            "The surface is small enough to read end to end. Start with installation and the "
                                + "first component."]],
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
