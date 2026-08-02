using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// The value-equal collection of all source-declared composable entries in a compilation.  Entries are
/// sorted by <see cref="ComposableDefinitionEntry.MethodKey"/> so equality and hashing are deterministic
/// and independent of syntax-tree discovery order; a method-key lookup index is retained for expansion.
/// </summary>
internal sealed class ComposableRegistry : IEquatable<ComposableRegistry>
{
    public static readonly ComposableRegistry Empty =
        new([]);

    private readonly Dictionary<string, ComposableDefinitionEntry> _byMethodKey;

    private ComposableRegistry(ImmutableArray<ComposableDefinitionEntry> entries)
    {
        Entries = entries;
        _byMethodKey = new Dictionary<string, ComposableDefinitionEntry>(
            entries.Length,
            StringComparer.Ordinal);
        foreach (var entry in entries)
            _byMethodKey[entry.MethodKey] = entry;
    }

    /// <summary>The registry entries in deterministic ascending <see cref="ComposableDefinitionEntry.MethodKey"/> order.</summary>
    public EquatableArray<ComposableDefinitionEntry> Entries { get; }

    public static ComposableRegistry Create(ImmutableArray<ComposableDefinitionEntry> entries)
    {
        if (entries.IsDefaultOrEmpty)
            return Empty;

        // Deduplicate by method key (keeping the first occurrence) so a partial declaration observed
        // through multiple syntax contexts does not produce duplicate slots, then sort for determinism.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = ImmutableArray.CreateBuilder<ComposableDefinitionEntry>(entries.Length);
        foreach (var entry in entries)
        {
            if (seen.Add(entry.MethodKey))
                unique.Add(entry);
        }

        unique.Sort(static (left, right) =>
            string.CompareOrdinal(left.MethodKey, right.MethodKey));

        return new ComposableRegistry(unique.ToImmutable());
    }

    /// <summary>Attempts to resolve a source-declared entry by its stable method key.</summary>
    public bool TryGet(string methodKey, [MaybeNullWhen(false)] out ComposableDefinitionEntry entry) =>
        _byMethodKey.TryGetValue(methodKey, out entry);

    public bool Equals(ComposableRegistry? other) =>
        other is not null && Entries.Equals(other.Entries);

    public override bool Equals(object? obj) => Equals(obj as ComposableRegistry);

    public override int GetHashCode() => Entries.GetHashCode();
}
