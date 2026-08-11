using System.Net;
using BlazorCodeFirst.WebAppTestHost.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BlazorCodeFirst.WebAppTests;

/// <summary>
/// Asserts what the generator's output looks like after a real Blazor Web App has prerendered it over an
/// InteractiveServer render mode. No other test layer here can see this: the bUnit tests have no HTTP
/// pipeline and no prerender pass, the compiler tests run the generator against a fixed compilation, and
/// the diagnostic tests drive a batch build.
/// </summary>
public sealed class PrerenderTests
{
    /// <summary>
    /// Fetches <paramref name="relativeUrl"/> and returns the response body truncated at
    /// <c>&lt;/html&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Blazor Server writes <c>&lt;!--Blazor-Server-Component-State:…--&gt;</c> AFTER the closing tag,
    /// carrying raw base64 — an alphabet that includes '+' and digits, so a short pattern can match
    /// inside it by chance. Measured over 300 responses of a page containing none of them, the bare
    /// substring "+1" matched 72 times and "+5" 63 times; truncating at &lt;/html&gt; matched zero every
    /// time. No assertion in this file is currently short enough to be at risk, but pinning the document
    /// boundary here means none added later can be, and the failure this prevents runs in the dangerous
    /// direction: asserted markup disappearing while the suite stays green.
    /// </remarks>
    private static async Task<string> GetDocumentAsync(string relativeUrl)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync(new Uri(relativeUrl, UriKind.Relative));

        var end = body.IndexOf("</html>", StringComparison.Ordinal);
        Assert.True(end >= 0, "the response carries no </html>, so it is not a rendered document");
        return body[..end];
    }

    /// <summary>Fetches <paramref name="relativeUrl"/> and returns only its status code.</summary>
    private static async Task<HttpStatusCode> GetStatusAsync(string relativeUrl)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri(relativeUrl, UriKind.Relative));
        return response.StatusCode;
    }

    [Fact]
    public async Task Counter_page_prerenders_the_generated_markup()
    {
        var document = await GetDocumentAsync("counter");

        Assert.Contains("class=\"counter\"", document, StringComparison.Ordinal);
        Assert.Contains("Count: 0", document, StringComparison.Ordinal);

        // The ForEach rows. Blazor's HTML encoder escapes '+', so the labels reach the wire as
        // &#x2B;1 and the literal "+1" never appears in the document. The closing tag is part of each
        // pattern because bare "&#x2B;1" also matches inside "&#x2B;10".
        Assert.Contains("&#x2B;1</button>", document, StringComparison.Ordinal);
        Assert.Contains("&#x2B;5</button>", document, StringComparison.Ordinal);
        Assert.Contains("&#x2B;10</button>", document, StringComparison.Ordinal);

        // One Increment button plus one per row. This is the assertion that fails if ForEach stops
        // emitting rows, since a Contains check cannot notice a missing sibling.
        Assert.Equal(4, document.Split("<button", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task Counter_page_prerenders_the_code_first_layout()
    {
        var document = await GetDocumentAsync("counter");

        Assert.Contains("class=\"app-shell\"", document, StringComparison.Ordinal);
        Assert.Contains("<main>", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Counter_page_is_prerendered_for_interactive_server()
    {
        var document = await GetDocumentAsync("counter");

        // Pins the render mode, not just the markup. Without this, converting the host to plain static
        // SSR would leave this suite green while deleting the only coverage of the prerender-then-
        // hydrate path that this project exists to provide.
        Assert.Contains("<!--Blazor:{\"type\":\"server\"", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Counter_page_can_hydrate()
    {
        var document = await GetDocumentAsync("counter");

        // Prerendering correctly and hydrating never is a real, reachable state: removing
        // MapStaticAssets leaves every other assertion here green while the framework script 404s.
        // The filename carries a content fingerprint that moves with the framework version, so only
        // the stable prefix is matched.
        Assert.Contains("src=\"_framework/blazor.web.", document, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("_framework/blazor.web.js"));
    }

    [Fact]
    public async Task Counter_page_evaluates_conditional_content_server_side()
    {
        var document = await GetDocumentAsync("counter");

        // If(_count >= 3, …) is false at count 0, so the branch must be absent rather than emitted
        // and hidden.
        Assert.DoesNotContain("Milestone reached", document, StringComparison.Ordinal);
    }

    [Fact]
    public void FoldEscaping_page_folds_to_a_single_markup_frame()
    {
        // Pins the premise the test below relies on. FoldEscapingPage is fully static, so the
        // generator's #140 fold is expected to coalesce it into one AddMarkupContent frame; without
        // this assertion, if the page ever stopped folding, the escaping test below would keep passing
        // through the ordinary AddContent/AddAttribute path instead — the .NET HtmlRenderer escapes
        // those byte-identically to StaticMarkupSerializer.WriteTo for every character asserted there, so
        // that test alone cannot tell the two paths apart.
        var builder = new RenderTreeBuilder();
        new FoldEscapingPage().Build(builder);

        Assert.Equal(1, builder.GetFrames().Count);
    }

    [Fact]
    public async Task FoldEscaping_page_escapes_folded_text_and_attribute_content()
    {
        // The .NET HtmlRenderer writes markup frames verbatim, so given the single-frame fold pinned by
        // FoldEscaping_page_folds_to_a_single_markup_frame above, this is the one place in the suite
        // that would catch StaticMarkupSerializer.WriteTo emitting an unescaped '&', '<', '>', or '"' into
        // that frame: bUnit's MarkupMatches compares parsed DOM trees, which treats raw and escaped
        // markup as equivalent once parsed.
        var document = await GetDocumentAsync("fold-escaping");

        Assert.Contains("class=\"fold-escaping\"", document, StringComparison.Ordinal);
        Assert.Contains(
            "Q&amp;A: &lt;script&gt;alert(1)&lt;/script&gt; &amp; more",
            document,
            StringComparison.Ordinal);
        Assert.Contains(
            "title=\"say &quot;hi&quot; &amp; bye\"",
            document,
            StringComparison.Ordinal);

        // The Contains checks above only prove the escaped forms are present; a regression that wrote
        // both the escaped text and this literal, unescaped tag would still pass them. This pins the
        // absence directly.
        Assert.DoesNotContain("<script>alert(1)</script>", document, StringComparison.Ordinal);
    }

    /// <summary>
    /// The static-SSR half of #171: the .NET <c>HtmlRenderer</c> writes no attribute for a null value and
    /// <c>title=""</c> for an empty string. Asserted as substrings of the response rather than through a
    /// parser, because what is under test is the text the renderer wrote and a parser normalizes exactly
    /// the difference between an absent attribute and an empty one.
    /// </summary>
    /// <remarks>
    /// The view starts in its present state, so what this reads is a value, not the null. The absent state
    /// is measured where a state change is available: the integration tests re-render it, and
    /// <c>null-attribute.spec.ts</c> toggles it in a browser. What belongs here instead is the one shape
    /// only this layer produces — a <see langword="true"/> <see langword="bool"/> written bare, with no
    /// <c>=""</c>, which parses to the same DOM the client-side path sets.
    /// </remarks>
    [Fact]
    public async Task NullAttribute_page_prerenders_a_present_value_and_a_bare_bool()
    {
        var document = await GetDocumentAsync("null-attribute-prerender");

        Assert.Contains("""<span id="to-null" title="tip">""", document, StringComparison.Ordinal);
        Assert.Contains("""<span id="to-empty" title="tip">""", document, StringComparison.Ordinal);

        // class precedes the other attributes whatever order the decorations were written in: the channel
        // emits its one folded frame first (EmitClassAttribute, before the attribute loop), so the id
        // written ahead of .Class in the view still lands after it here.
        Assert.Contains("""<span class="card active" id="class-join">""", document, StringComparison.Ordinal);
        Assert.Contains("""<span id="bool" data-v>""", document, StringComparison.Ordinal);
    }
}
