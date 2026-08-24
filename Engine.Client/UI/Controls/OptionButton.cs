using System;
using System.Collections.Generic;
using Engine.Client.Inputs;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using VAlign = Engine.Client.UI.VerticalAlignment;

namespace Engine.Client.UI;

/// <summary>
/// Button that opens a popup ItemList to pick one of several text options.
/// </summary>
public sealed partial class OptionButton : BaseButton
{
    private readonly Label _textLabel;
    private readonly Label _arrowLabel;
    private readonly List<string> _items = new();
    private int _selectedIndex = -1;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => Select(value);
    }

    public string? SelectedText => _selectedIndex >= 0 ? _items[_selectedIndex] : null;

    public int ItemCount => _items.Count;

    public event Action<int>? OnItemSelected;

    /// <summary>
    /// Max height of the popup list before it scrolls.
    /// </summary>
    [StyleField("popupMaxHeight", 200f)]
    private float? _popupMaxHeight;

    private ClickCatcher? _catcher;
    private PanelContainer? _popup;

    public OptionButton()
    {
        StyleAliasses.Add("optionButton");

        _textLabel = new Label { HorizontalExpand = true, VerticalAlignment = VAlign.Center };
        _textLabel.StyleAliasses.Add("optionButton");

        _arrowLabel = new Label { Text = "V", VerticalAlignment = VAlign.Center };
        _arrowLabel.StyleAliasses.Add("optionButton");

        var row = new BoxContainer { Orientation = Orientation.Horizontal };
        row.AddChild(_textLabel);
        row.AddChild(_arrowLabel);
        Content = row;

        UpdateLabel();
        OnClick += _ => TogglePopup();
    }

    public int AddItem(string text)
    {
        _items.Add(text);
        if (_selectedIndex < 0)
            Select(0);

        return _items.Count - 1;
    }

    public void RemoveItem(int index)
    {
        if (index < 0 || index >= _items.Count)
            return;

        _items.RemoveAt(index);

        if (_selectedIndex == index)
            _selectedIndex = -1;
        else if (_selectedIndex > index)
            _selectedIndex--;

        UpdateLabel();
    }

    public void Clear()
    {
        ClosePopup();
        _items.Clear();
        _selectedIndex = -1;
        UpdateLabel();
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _items.Count)
            return;

        _selectedIndex = index;
        UpdateLabel();
        OnItemSelected?.Invoke(index);
    }

    protected override void OnDispose() => ClosePopup();

    private void UpdateLabel()
        => _textLabel.Text = _selectedIndex >= 0 ? _items[_selectedIndex] : "";

    private void TogglePopup()
    {
        if (_popup is not null)
            ClosePopup();
        else
            OpenPopup();
    }

    private void OpenPopup()
    {
        if (_items.Count == 0)
            return;

        var window = IoCManager.Resolve<WindowManager>();

        _catcher = new ClickCatcher();
        _catcher.OnCatch += ClosePopup;
        window.WindowRoot.AddChild(_catcher);
        LayoutContainer.SetAnchorPreset(_catcher, LayoutPreset.Wide);

        var list = new ItemList();
        for (var i = 0; i < _items.Count; i++)
            list.AddItem(_items[i]);

        list.OnSelectionChanged += index =>
        {
            Select(index);
            ClosePopup();
        };

        if (_selectedIndex >= 0)
            list.Select(_selectedIndex);

        _popup = new PanelContainer();
        _popup.StyleAliasses.Add("optionButtonPopup");
        _popup.AddChild(list);

        window.WindowRoot.AddChild(_popup);
        LayoutContainer.SetPosition(_popup, new Vector2(Bounds.X, Bounds.Bottom));
        LayoutContainer.SetSize(_popup, new Vector2(Bounds.Width, PopupMaxHeight));
    }

    private void ClosePopup()
    {
        if (_popup is null)
            return;

        var window = IoCManager.Resolve<WindowManager>();
        window.WindowRoot.RemoveChild(_popup, dispose: true);
        if (_catcher is not null)
            window.WindowRoot.RemoveChild(_catcher, dispose: true);

        _popup = null;
        _catcher = null;
    }

    // Full-screen, invisible - sits under the popup (added first) so the popup own content
    // still gets hit-tested first, but any click that misses it closes the dropdown.
    private sealed class ClickCatcher : Control
    {
        public event Action? OnCatch;

        public ClickCatcher() => MouseFilter = MouseFilterMode.Stop;

        protected internal override void MouseButtonDown(MouseButton button) => OnCatch?.Invoke();
    }
}
