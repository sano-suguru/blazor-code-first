using Xunit;

namespace BlazorCodeFirst.Build.Tests;

public class ScopeIdentifierTests
{
    [Fact]
    public void Compute_returns_bcf_prefixed_eight_char_hash()
    {
        var scope = ScopeIdentifier.Compute(
            projectDirectory: "/repo/App",
            cssFilePath: "/repo/App/Counter.cs.css",
            assemblyName: "App");

        Assert.StartsWith("bcf-", scope);
        Assert.Equal(12, scope.Length); // "bcf-" + 8 hex chars
        Assert.Matches("^bcf-[0-9a-f]{8}$", scope);
    }

    [Fact]
    public void Compute_is_deterministic_across_calls()
    {
        var first = ScopeIdentifier.Compute("/repo/App", "/repo/App/Counter.cs.css", "App");
        var second = ScopeIdentifier.Compute("/repo/App", "/repo/App/Counter.cs.css", "App");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_differs_for_different_relative_paths()
    {
        var counter = ScopeIdentifier.Compute("/repo/App", "/repo/App/Counter.cs.css", "App");
        var nav = ScopeIdentifier.Compute("/repo/App", "/repo/App/NavMenu.cs.css", "App");

        Assert.NotEqual(counter, nav);
    }

    [Fact]
    public void Compute_normalizes_backslash_separators_to_match_forward_slash_paths()
    {
        var withBackslash = ScopeIdentifier.Compute(
            @"C:\repo\App", @"C:\repo\App\Sub\Counter.cs.css", "App");
        var withForwardSlash = ScopeIdentifier.Compute(
            "C:/repo/App", "C:/repo/App/Sub/Counter.cs.css", "App");

        Assert.Equal(withBackslash, withForwardSlash);
    }
}
