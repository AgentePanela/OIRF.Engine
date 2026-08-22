using System;
using System.Collections.Generic;

namespace Engine.Client.UI;

/// <summary>
/// Scrollable list of selectable text rows.
/// </summary>
public sealed partial class ItemList : ScrollContainer
{
    private readonly BoxContainer _rows;
    private readonly List<Button> _rowButtons = new();

    public ListSelectMode SelectMode { get; set; } = ListSelectMode.Single;

    public enum ListSelectMode
    {
        Single,
        Multiple,
    }

    public int ItemCount => _rowButtons.Count;

    public IReadOnlyList<int> SelectedIndices
    {
        get
        {
            var result = new List<int>();
            for (var i = 0; i < _rowButtons.Count; i++)
            {
                if (_rowButtons[i].Pressed)
                    result.Add(i);
            }

            return result;
        }
    }

    public int SelectedIndex
    {
        get
        {
            for (var i = 0; i < _rowButtons.Count; i++)
            {
                if (_rowButtons[i].Pressed)
                    return i;
            }

            return -1;
        }
        set => Select(value);
    }

    public event Action<int>? OnSelectionChanged;

    public ItemList()
    {
        StyleAliasses.Add("itemList");

        _rows = new BoxContainer { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Stretch };
        base.AddChild(_rows);
    }

    public new void AddChild(Control child) =>
        throw new InvalidOperationException("Use AddItem(text) on an ItemList.");

    public int AddItem(string text)
    {
        var row = new Button(text) { ToggleMode = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        row.StyleAliasses.Add("itemListRow");
        row.OnToggled += pressed => OnRowToggled(row, pressed);

        _rowButtons.Add(row);
        _rows.AddChild(row);

        return _rowButtons.Count - 1;
    }

    public void RemoveItem(int index)
    {
        if (index < 0 || index >= _rowButtons.Count)
            return;

        var row = _rowButtons[index];
        _rowButtons.RemoveAt(index);
        _rows.RemoveChild(row, dispose: true);
    }

    public void Clear()
    {
        foreach (var row in _rowButtons)
            _rows.RemoveChild(row, dispose: true);

        _rowButtons.Clear();
    }

    public string GetItemText(int index) => _rowButtons[index].Text;

    public void SetItemText(int index, string text) => _rowButtons[index].Text = text;

    public void Select(int index)
    {
        if (index < 0 || index >= _rowButtons.Count)
            return;

        if (SelectMode == ListSelectMode.Single)
        {
            foreach (var row in _rowButtons)
                row.Pressed = false;
        }

        _rowButtons[index].Pressed = true;
        OnSelectionChanged?.Invoke(index);
    }

    public void Deselect(int index)
    {
        if (index < 0 || index >= _rowButtons.Count)
            return;

        _rowButtons[index].Pressed = false;
        OnSelectionChanged?.Invoke(index);
    }

    public void DeselectAll()
    {
        foreach (var row in _rowButtons)
            row.Pressed = false;
    }

    private void OnRowToggled(Button row, bool pressed)
    {
        if (pressed && SelectMode == ListSelectMode.Single)
        {
            foreach (var other in _rowButtons)
            {
                if (other != row)
                    other.Pressed = false;
            }
        }

        OnSelectionChanged?.Invoke(_rowButtons.IndexOf(row));
    }
}
