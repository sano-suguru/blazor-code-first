using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// One <c>.cs.css</c> file's stamped scope: its full path as <c>AdditionalText.Path</c> reports it
/// (backslash separators normalized to <c>/</c>, see <see cref="CssScopePath.NormalizeSeparators"/>),
/// and the <c>bcf-xxxxxxxx</c> value <c>BlazorCodeFirst.Build</c> computed for it.
/// </summary>
internal readonly record struct CssScopeEntry(string CssFilePath, string Scope)
{
    public string CssFilePath { get; init; } = CssScopePath.NormalizeSeparators(CssFilePath);

    /// <summary>
    /// The matching <c>.cs</c> file's path: <see cref="CssFilePath"/> with the trailing <c>.css</c>
    /// removed. The one place this convention is computed — <see cref="CssScopeRegistry"/>'s lookup
    /// and <c>OrphanScopedCssResolver</c>'s orphan check both read it from here rather than each
    /// re-deriving their own direction of the same string arithmetic. A computed property, not a
    /// stored field, so it takes no part in this record struct's generated equality.
    /// </summary>
    public string ComponentFilePath => CssFilePath.Substring(0, CssFilePath.Length - ".css".Length);
}

/// <summary>
/// Normalizes a path before it enters a CSS-scope lookup key or query, so a path-separator
/// difference between the two independently-sourced providers behind a <c>.cs.css</c> file's scope
/// (<c>AdditionalText.Path</c> and <c>SyntaxTree.FilePath</c>, see #524) does not silently miss.
/// Case is already handled by the <c>OrdinalIgnoreCase</c> comparers at each lookup; this closes the
/// separator gap those comparers do not cover. Deliberately does not resolve relative-to-absolute (no
/// <c>Path.GetFullPath</c>): a source generator's current directory is not a trustworthy base, so a
/// relative/absolute mismatch between the two providers is left as documented residue in #524.
/// <c>BlazorCodeFirst.Build</c>'s own <c>ScopeIdentifier.ToRelativePath</c> normalizes separators the
/// same way for the same reason, but that helper is private to a different assembly (an MSBuild task
/// host, not this analyzer's load context), so there is no shared project the two sides could pull a
/// common helper from without adding a cross-assembly dependency neither side needs otherwise.
/// </summary>
internal static class CssScopePath
{
    public static string NormalizeSeparators(string path) => path.Replace('\\', '/');
}

/// <summary>
/// The value-equal collection of every <c>.cs.css</c> file's scope in a compilation, read from
/// <c>AdditionalFiles</c>' <c>CssScope</c> metadata. Mirrors <see cref="ViewPartRegistry"/>'s shape:
/// entries sorted by key for deterministic equality, a lookup dictionary kept alongside for callers.
/// </summary>
internal sealed class CssScopeRegistry : IEquatable<CssScopeRegistry>
{
    public static readonly CssScopeRegistry Empty = new([]);

    private readonly Dictionary<string, string> _scopeByComponentFilePath;

    private CssScopeRegistry(ImmutableArray<CssScopeEntry> entries)
    {
        Entries = entries;
        _scopeByComponentFilePath = new Dictionary<string, string>(entries.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            _scopeByComponentFilePath[entry.ComponentFilePath] = entry.Scope;
    }

    public EquatableArray<CssScopeEntry> Entries { get; }

    public static CssScopeRegistry Create(ImmutableArray<CssScopeEntry> entries)
    {
        if (entries.IsDefaultOrEmpty)
            return Empty;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = ImmutableArray.CreateBuilder<CssScopeEntry>(entries.Length);
        foreach (var entry in entries)
        {
            if (seen.Add(entry.CssFilePath))
                unique.Add(entry);
        }

        unique.Sort(static (left, right) =>
            string.Compare(left.CssFilePath, right.CssFilePath, StringComparison.OrdinalIgnoreCase));

        return new CssScopeRegistry(unique.ToImmutable());
    }

    /// <summary>
    /// Resolves the scope stamped on <paramref name="componentFilePath"/>'s sibling <c>.cs.css</c> file,
    /// or <see langword="false"/> when there is none.
    /// </summary>
    public bool TryGetScopeForComponentFile(string componentFilePath, [MaybeNullWhen(false)] out string scope) =>
        _scopeByComponentFilePath.TryGetValue(componentFilePath, out scope);

    /// <summary>
    /// <see cref="TryGetScopeForComponentFile"/> as an expression rather than a <c>bool</c>/<c>out</c>
    /// pair, for the common case of a caller that just wants the scope or <see langword="null"/>.
    /// </summary>
    public string? GetScopeOrDefault(string componentFilePath) =>
        TryGetScopeForComponentFile(componentFilePath, out var scope) ? scope : null;

    public bool Equals(CssScopeRegistry? other) =>
        other is not null && Entries.Equals(other.Entries);

    public override bool Equals(object? obj) => Equals(obj as CssScopeRegistry);

    public override int GetHashCode() => Entries.GetHashCode();
}
