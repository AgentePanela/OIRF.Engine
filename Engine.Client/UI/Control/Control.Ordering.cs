using System.Collections.Generic;

namespace Engine.Client.UI;

public abstract partial class Control
{
    [StyleField("zIndex", 0)]
    private int? _zIndex;

    private List<Control>? _sortedChildren;

    /// <summary>
    /// Children in draw order: sorted by <see cref="ZIndex"/>, insertion order within a tie.
    /// </summary>
    protected internal IReadOnlyList<Control> OrderedChildren => _sortedChildren ?? (IReadOnlyList<Control>)_children;

    private void RefreshChildOrder()
    {
        var needsSorting = false;
        foreach (var child in _children)
        {
            if (child.ZIndex == 0)
                continue;

            needsSorting = true;
            break;
        }

        if (!needsSorting)
        {
            _sortedChildren = null;
            return;
        }

        _sortedChildren ??= new List<Control>(_children.Count);
        _sortedChildren.Clear();

        // insertion sort
        foreach (var child in _children)
        {
            var i = _sortedChildren.Count;
            while (i > 0 && _sortedChildren[i - 1].ZIndex > child.ZIndex)
                i--;

            _sortedChildren.Insert(i, child);
        }
    }

    /// <summary>
    /// Moves a child to the end of its sibling list, so it draws over siblings of the same
    /// <see cref="ZIndex"/>.
    /// </summary>
    public void MoveChildToFront(Control child)
    {
        if (child.Parent != this)
            return;

        var last = _children.Count - 1;
        if (last < 0 || _children[last] == child)
            return;

        _children.Remove(child);
        _children.Add(child);
        InvalidateLayout();
    }
}
