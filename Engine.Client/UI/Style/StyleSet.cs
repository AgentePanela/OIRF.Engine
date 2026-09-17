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

    /// <summary>
    /// Whether membership in this set cascades down to descendants for style matching purposes
    /// (see <see cref="Control.StyleClasses"/>) - if so, changing it must also invalidate every
    /// descendant's style cache, since their resolved rules may depend on it.
    /// </summary>
    private readonly bool _cascades;

    internal StyleSet(Control owner, bool cascades = false)
    {
        _owner = owner;
        _cascades = cascades;
    }

    public int Count => _set.Count;

    public bool Contains(string item) => _set.Contains(item);

    public bool Add(string item)
    {
        if (!_set.Add(item))
            return false;

        _owner.InvalidateStyleCache(_cascades);
        return true;
    }

    public bool Remove(string item)
    {
        if (!_set.Remove(item))
            return false;

        _owner.InvalidateStyleCache(_cascades);
        return true;
    }

    public IEnumerator<string> GetEnumerator() => _set.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
