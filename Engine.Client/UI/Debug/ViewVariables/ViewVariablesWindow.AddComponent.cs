using System;
using Engine.Client.Inputs;
using Engine.Shared.Debug.ViewVariables;
using Engine.Shared.IoC;

namespace Engine.Client.UI.Debug.ViewVariables;

public sealed partial class ViewVariablesWindow
{
    private ClickCatcher? _addComponentCatcher;
    private PanelContainer? _addComponentPopup;

    private void ToggleAddComponentPopup(Button anchor, SideView side)
    {
        if (_addComponentPopup is not null)
        {
            CloseAddComponentPopup();
            return;
        }

        OpenAddComponentPopup(anchor, side);
    }

    private void OpenAddComponentPopup(Button anchor, SideView side)
    {
        var addable = side.Access.GetAddableComponents(side.Path.Root);

        var windows = IoCManager.Resolve<WindowManager>();

        _addComponentCatcher = new ClickCatcher();
        _addComponentCatcher.OnCatch += CloseAddComponentPopup;
        windows.WindowRoot.AddChild(_addComponentCatcher);
        LayoutContainer.SetAnchorPreset(_addComponentCatcher, LayoutPreset.Wide);

        var search = new LineEdit { PlaceholderText = Loc.GetString("engine-vv-search-component"), HorizontalExpand = true };
        var list = new ItemList { VerticalExpand = true, HorizontalExpand = true };

        void Populate(string filter)
        {
            list.Clear();
            foreach (var name in addable)
            {
                if (filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    list.AddItem(name);
            }
        }

        search.OnTextChanged += Populate;
        list.OnSelectionChanged += index => OnComponentPicked(list.GetItemText(index), side);

        Populate("");

        var content = new BoxContainer { Orientation = Orientation.Vertical, Separation = 4, Margin = new(4) };
        content.AddChild(search);
        content.AddChild(list);

        _addComponentPopup = new PanelContainer { Background = new(0, 0, 0, 0.9f) };
        _addComponentPopup.AddChild(content);

        windows.WindowRoot.AddChild(_addComponentPopup);
        LayoutContainer.SetPosition(_addComponentPopup, new(anchor.Bounds.X, anchor.Bounds.Bottom));
        LayoutContainer.SetSize(_addComponentPopup, new(240, 280));
    }

    private void OnComponentPicked(string name, SideView side)
    {
        CloseAddComponentPopup();

        if (side.Access.TryAddComponent(side.Path.Root, name, out var error))
            Rebuild(side);
        else
            _statusLabel.Text = error ?? Loc.GetString("engine-vv-fail-add-component");
    }

    private void CloseAddComponentPopup()
    {
        if (_addComponentPopup is null)
            return;

        var windows = IoCManager.Resolve<WindowManager>();
        windows.WindowRoot.RemoveChild(_addComponentPopup, dispose: true);

        if (_addComponentCatcher is not null)
            windows.WindowRoot.RemoveChild(_addComponentCatcher, dispose: true);

        _addComponentPopup = null;
        _addComponentCatcher = null;
    }

    // full-screen and invisible, sits behind the popup so a click outside it closes the popup
    private sealed class ClickCatcher : Control
    {
        public event Action? OnCatch;

        public ClickCatcher() => MouseFilter = MouseFilterMode.Stop;

        protected internal override void MouseButtonDown(MouseButton button) => OnCatch?.Invoke();
    }
}
