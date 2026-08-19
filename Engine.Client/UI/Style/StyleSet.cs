using System.Collections;
using System.Collections.Generic;

namespace Engine.Client.UI;

/// <summary>
/// A string set that invalidates the owning control resolved-style cache.
/// </summary>
public sealed class StyleSet : IReadOnlyCollection<string>
{
    private readonly HashSet<string> _set = new();
    private readonly Control _owner;

    internal StyleSet(Control owner) => _owner = owner;

    public int Count => _set.Count;

    public bool Contains(string item) => _set.Contains(item);

    public bool Add(string item)
    {
        if (!_set.Add(item))
            return false;

        _owner.InvalidateStyleCache();
        return true;
    }

    public bool Remove(string item)
    {
        if (!_set.Remove(item))
            return false;

        _owner.InvalidateStyleCache();
        return true;
    }

    public IEnumerator<string> GetEnumerator() => _set.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
