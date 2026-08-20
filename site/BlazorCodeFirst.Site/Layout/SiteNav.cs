using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Layout;

/// <summary>
/// The site-level navigation: wordmark hard left, utilities hard right, nothing between them.
/// </summary>
/// <remarks>
/// The shape is deliberate. The wordmark-left / centred-link-cluster / filled-button-right bar is
/// the most-copied marketing header there is, and it says nothing about what kind of site it sits
/// on. This site has exactly three routes a reader navigates to by name, so the bar carries three
/// links and no cluster; the documentation's own navigation is the rail beside the prose, which
/// renders only on the documentation routes.
///
/// The element keeps the class "site-nav" because the site workflow asserts its presence on every
/// prerendered route as the signal that the shell rendered at all.
///
/// The wordmark link is wrapped in a div rather than carrying the brand styling itself: the CI
/// guard matches class="nav-link active" as a whole attribute value, so a third class on that
/// element would turn the guard into a silent no-op.
///
/// The component holds no state field for the current location. Body reads
/// <see cref="NavigationManager.Uri"/> directly as a pure projection, BCF3001 forbids mutation
/// inside Body, not reads, so there is no second source of truth to keep in sync. The
/// LocationChanged subscription lives in OnInitialized (outside Body) and only requests a re-render.
///
/// This component lives in the persistent layout, so it is never re-mounted and is long-lived: the
/// subscription MUST be released in Dispose or handlers accumulate and leak.
/// </remarks>
public sealed partial class SiteNav : BodyComponentBase, IDisposable
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    // LocationChanged normally fires on Blazor's synchronization context, but dispatch through
    // InvokeAsync so a notification raised off-context (for example around the WASM hydration
    // boundary) still re-renders safely.
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) =>
        InvokeAsync(StateHasChanged);

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;

    protected override View Body =>
        Nav.Class("site-nav")[
            Div.Class("brand")[
                A.Href("/").Class(LinkClass("/"))[SiteMetadata.Name]],
            Div.Class("nav-utilities")[
                A.Href("/docs").Class(DocsLinkClass())["Docs"],
                A.Href("/counter").Class(LinkClass("/counter"))["Demo"],
                A.Href("https://github.com/sano-suguru/blazor-code-first")
                    .Class("chip")
                    .Rel("noopener")["Source"],
                ThemeToggle(Docs.Shell(CurrentLang()))]];

    /// <summary>The colour-scheme control: system, light, dark, in that cycle.</summary>
    /// <remarks>
    /// Stateless markup, and no OnClick, neither of which is an omission. All three state words are
    /// always present and css/app.css shows the one matching the attribute on :root, so this renders
    /// identically whatever the theme is -- which is what lets the prerendered HTML be correct
    /// before the runtime exists, and what makes a re-render of this long-lived component harmless.
    /// wwwroot/index.html owns the click, on a listener delegated to document, because a handler
    /// here would not answer until WebAssembly had started; the note beside that script has the
    /// reasoning.
    ///
    /// The visible label is decorative and hidden from assistive technology, and drops out visually
    /// on a narrow viewport. The accessible name comes from the span before it, hidden at every
    /// width: <see cref="ShellText.ThemeToggleName"/> ends in a "{state}" placeholder that is never
    /// substituted (site/README.md's Shell text section has the full contract), stripped here to
    /// yield the prefix ("Color theme: " in English), so the button reads as "Color theme: Dark"
    /// throughout. The two words that are not current are visibility:hidden, which excludes them
    /// from that name while keeping their width.
    ///
    /// [ViewPart] is what makes this a method at all rather than more markup inline in Body. It is
    /// expanded into the caller's frame sequence, so the split costs nothing at runtime; without the
    /// attribute the call would be Opaque, which is unimplemented and reports BCF1003 today rather
    /// than the BCF2001 the finished design intends (ARCHITECTURE.md appendix A).
    /// </remarks>
    [ViewPart]
    private static View ThemeToggle(ShellText shell) =>
        Button.Class("theme-toggle").Type("button").Attr("data-theme-toggle", "")[
            Span.Class("visually-hidden")[
                shell.ThemeToggleName.Replace("{state}", "", StringComparison.Ordinal)],
            Span.Class("theme-toggle__label").Attr("aria-hidden", "true")[shell.ThemeToggleLabel],
            Span.Class("theme-toggle__states")[
                Span.Class("is-system")[shell.ThemeSystem],
                Span.Class("is-light")[shell.ThemeLight],
                Span.Class("is-dark")[shell.ThemeDark]]];

    /// <summary>The current route's documentation language, or <see cref="Docs.Canonical"/> for a
    /// route outside the documentation tree.</summary>
    /// <remarks>
    /// The canonical language is skipped in the loop rather than compared like the others: its own
    /// prefix ("/docs") is a prefix of every other language's ("/docs/ja"), so comparing it in
    /// declaration order would match it first on every translated document route. Skipping it and
    /// falling through to it below is equivalent, since a document route matches its own language's
    /// prefix or none at all. "/" and "/counter" match no language's prefix and fall through, the
    /// same default the rest of the manifest uses for a route with no language of its own.
    /// </remarks>
    private string CurrentLang()
    {
        string current = CurrentPath();
        foreach (string lang in Docs.Languages)
        {
            if (lang == Docs.Canonical)
            {
                continue;
            }

            string prefix = Docs.RoutePrefix(lang);
            if (string.Equals(current, prefix, StringComparison.OrdinalIgnoreCase) ||
                current.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return lang;
            }
        }

        return Docs.Canonical;
    }

    /// <summary>The current route as a normalized absolute path ("/", "/counter", "/docs/x").</summary>
    /// <remarks>
    /// Must be derived from ToBaseRelativePath, never from the absolute Uri string: during build-time
    /// prerendering the app is hosted on http://localhost:&lt;random port&gt;, so absolute comparisons
    /// would behave differently in the prerendered HTML than in the browser.
    /// </remarks>
    private string CurrentPath()
    {
        string relative = Navigation.ToBaseRelativePath(Navigation.Uri);

        int cut = relative.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            relative = relative[..cut];
        }

        relative = relative.TrimEnd('/');
        return relative.Length == 0 ? "/" : "/" + relative;
    }

    // Exact match only: a prefix match would light up "/" (Home) on every route, and would light up
    // "/docs" on every "/docs/{slug}" route. On a document route the active link is the rail's, not
    // this one's, and the CI guard asserting exactly one active link per route is what keeps the two
    // from both claiming it.
    private string LinkClass(string path) =>
        string.Equals(CurrentPath(), path, StringComparison.OrdinalIgnoreCase)
            ? "nav-link active"
            : "nav-link";

    /// <summary>The Docs entry, which is active on every language's documentation index.</summary>
    /// <remarks>
    /// This link names the documentation index, and each language has one, so "/docs/ja" lights it up
    /// as "/docs" does: <see cref="CurrentLang"/> resolves "ja" for that route, and its own prefix is
    /// "/docs/ja". Without this, "/docs/ja" would be the only prerendered route with no active nav
    /// link at all: the rail lists documents, so nothing there matches an index either.
    ///
    /// Still an exact match per index rather than a prefix match on "/docs". A prefix would claim the
    /// active mark on every document route as well, where it belongs to the rail, and the CI guard
    /// asserting exactly one active link per route is what keeps the two from both taking it.
    /// </remarks>
    private string DocsLinkClass() =>
        string.Equals(CurrentPath(), Docs.RoutePrefix(CurrentLang()), StringComparison.OrdinalIgnoreCase)
            ? "nav-link active"
            : "nav-link";
}
