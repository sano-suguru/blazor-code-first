using System.Collections;
using System.Collections.Immutable;

namespace BlazorCodeFirst.Compiler;

/// <summary>
/// A value-equal wrapper over <see cref="ImmutableArray{T}"/> for use in Roslyn incremental
/// generator models.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ImmutableArray{T}"/> compares by underlying-array reference in compiler-generated
/// record members, which silently breaks incremental caching (two value-equal arrays hash and
/// compare as different). This wrapper supplies structural equality so records that carry it cache
/// correctly, and its <see cref="IEquatable{T}"/> implementation lets a record's synthesized
/// equality dispatch without boxing.
/// </para>
/// <para>
/// A <c>default</c> (uninitialized) instance is treated as an empty array throughout, equality,
/// hashing, length, and enumeration all behave as empty and never throw.
/// </para>
/// </remarks>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array) => _array = array;

    /// <summary>An empty array. Equal to a <c>default</c> instance.</summary>
    public static EquatableArray<T> Empty => default;

    public bool IsDefaultOrEmpty => _array.IsDefaultOrEmpty;

    public int Length => _array.IsDefault ? 0 : _array.Length;

    int IReadOnlyCollection<T>.Count => Length;

    public T this[int index] => AsImmutableArray()[index];

    /// <summary>Returns the wrapped array, normalizing <c>default</c> to <see cref="ImmutableArray{T}.Empty"/>.</summary>
    public ImmutableArray<T> AsImmutableArray() => _array.IsDefault ? [] : _array;

    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);

    public bool Equals(EquatableArray<T> other)
    {
        var left = _array;
        var right = other._array;

        if (left.IsDefaultOrEmpty && right.IsDefaultOrEmpty)
            return true;
        if (left.IsDefault || right.IsDefault || left.Length != right.Length)
            return false;

        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < left.Length; index++)
        {
            if (!comparer.Equals(left[index], right[index]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array.IsDefaultOrEmpty)
            return 0;

        var hash = 17;
        foreach (var item in _array)
            hash = unchecked(hash * 31 + (item?.GetHashCode() ?? 0));
        return hash;
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    /// <summary>Returns a non-boxing struct enumerator so <c>foreach</c> allocates nothing.</summary>
    public Enumerator GetEnumerator() => new(_array);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)AsImmutableArray()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)AsImmutableArray()).GetEnumerator();

    public struct Enumerator
    {
        private readonly ImmutableArray<T> _array;
        private int _index;

        public Enumerator(ImmutableArray<T> array)
        {
            _array = array.IsDefault ? [] : array;
            _index = -1;
        }

        public readonly T Current => _array[_index];

        public bool MoveNext() => ++_index < _array.Length;
    }
}
