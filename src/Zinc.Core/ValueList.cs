using System.Collections;

namespace Zinc;

/// <summary>
/// An immutable list that equals another with the same items in the same order, so a record holding one (such as
/// <see cref="Config"/>) still compares by value.
/// </summary>
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly T[] _items;

    public ValueList(IEnumerable<T> items) => _items = [.. items];

    public static ValueList<T> Empty { get; } = new([]);

    public int Count => _items.Length;

    public T this[int index] => _items[index];

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(ValueList<T>? other) => other is not null && _items.SequenceEqual(other._items);

    public override bool Equals(object? obj) => Equals(obj as ValueList<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
