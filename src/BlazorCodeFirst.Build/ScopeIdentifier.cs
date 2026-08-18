using System;
using System.Security.Cryptography;
using System.Text;

namespace BlazorCodeFirst.Build;

/// <summary>
/// Computes the deterministic scope id stamped on a component's rewritten scoped CSS and read back
/// by the generator. The <c>bcf-</c> prefix (rather than Razor's <c>b-</c>) keeps the two scope
/// namespaces from colliding when a Razor component and a BCF component render into the same app.
/// </summary>
public static class ScopeIdentifier
{
    public static string Compute(string projectDirectory, string cssFilePath, string assemblyName)
    {
        // Not ArgumentNullException.ThrowIfNull: that static helper is .NET 6+ only, and this
        // project multi-targets net472.
        if (projectDirectory is null)
            throw new ArgumentNullException(nameof(projectDirectory));
        if (cssFilePath is null)
            throw new ArgumentNullException(nameof(cssFilePath));
        if (assemblyName is null)
            throw new ArgumentNullException(nameof(assemblyName));

        var relativePath = ToRelativePath(projectDirectory, cssFilePath);

        var input = assemblyName + "|" + relativePath;

        // SHA256.HashData and Convert.ToHexString are both .NET 5+ only; this project multi-targets
        // net472 (VS's own MSBuild host), so both need the portable instance-API equivalents.
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));

#pragma warning disable CA1308 // Lowercase is deliberate: the value becomes an HTML attribute name
        // (bcf-xxxxxxxx), and lowercase matches both HTML attribute convention and Razor's own
        // scope-id spelling (b-xxxxxxxx) -- this is not a normalize-for-comparison case CA1308
        // guards against.
        // Two-argument Replace, not the (string, string, StringComparison) overload: that overload
        // is .NET Core-only and this project multi-targets net472 (see the .editorconfig
        // CA1307 suppression for this directory -- replacing a literal ASCII '-' is ordinal by
        // construction regardless of which overload is available).
        var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
#pragma warning restore CA1308

        // Substring rather than a range indexer (hex[..8]): this project multi-targets net472,
        // which has no System.Index/System.Range.
        return "bcf-" + hex.Substring(0, 8);
    }

    // Path.GetRelativePath is .NET Core 2.0+/netstandard2.1+ only and unavailable on net472, so the
    // relative path is computed through Uri instead, which has been portable since classic .NET
    // Framework. Both inputs are normalized to forward slashes first so a trailing-slash-sensitive
    // Uri comparison never depends on which separator the caller passed in.
    private static string ToRelativePath(string baseDirectory, string fullPath)
    {
        var normalizedBase = baseDirectory.Replace('\\', '/').TrimEnd('/') + "/";
        var normalizedTarget = fullPath.Replace('\\', '/');

        var baseUri = new Uri(normalizedBase, UriKind.Absolute);
        var targetUri = new Uri(normalizedTarget, UriKind.Absolute);

        var relativeUri = baseUri.MakeRelativeUri(targetUri);
        return Uri.UnescapeDataString(relativeUri.ToString());
    }
}
