using System.Net;
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
}
