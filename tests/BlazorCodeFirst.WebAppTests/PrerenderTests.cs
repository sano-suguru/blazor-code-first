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

    [Fact]
    public async Task Counter_page_prerenders_the_generated_markup()
    {
        var document = await GetDocumentAsync("counter");

        Assert.Contains("class=\"counter\"", document, StringComparison.Ordinal);
        Assert.Contains("Count: 0", document, StringComparison.Ordinal);
    }
}
