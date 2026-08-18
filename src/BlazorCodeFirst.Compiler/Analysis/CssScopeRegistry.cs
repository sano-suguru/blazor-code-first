using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// One <c>.cs.css</c> file's stamped scope: its full path exactly as <c>AdditionalText.Path</c>
/// reports it, and the <c>bcf-xxxxxxxx</c> value <c>BlazorCodeFirst.Build</c> computed for it.
/// </summary>
internal readonly record struct CssScopeEntry(string CssFilePath, string Scope);

/// <summary>
/// The value-equal collection of every <c>.cs.css</c> file's scope in a compilation, read from
/// <c>AdditionalFiles</c>' <c>CssScope</c> metadata. Mirrors <see cref="ViewPartRegistry"/>'s shape:
/// entries sorted by key for deterministic equality, a lookup dictionary kept alongside for callers.
/// </summary>
internal sealed class CssScopeRegistry : IEquatable<CssScopeRegistry>
{
    public static readonly CssScopeRegistry Empty = new([]);

    private readonly Dictionary<string, string> _scopeByCssFilePath;

    private CssScopeRegistry(ImmutableArray<CssScopeEntry> entries)
    {
        Entries = entries;
        _scopeByCssFilePath = new Dictionary<string, string>(entries.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            _scopeByCssFilePath[entry.CssFilePath] = entry.Scope;
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
    /// Resolves the scope stamped on <paramref name="componentFilePath"/>'s sibling <c>.cs.css</c> file
    /// (<c>componentFilePath + ".css"</c>), or <see langword="false"/> when there is none.
    /// </summary>
    public bool TryGetScopeForComponentFile(string componentFilePath, [MaybeNullWhen(false)] out string scope) =>
        _scopeByCssFilePath.TryGetValue(componentFilePath + ".css", out scope);

    public bool Equals(CssScopeRegistry? other) =>
        other is not null && Entries.Equals(other.Entries);

    public override bool Equals(object? obj) => Equals(obj as CssScopeRegistry);

    public override int GetHashCode() => Entries.GetHashCode();
}
