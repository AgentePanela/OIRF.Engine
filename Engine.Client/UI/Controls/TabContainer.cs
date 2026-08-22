using System;
using System.Collections.Generic;

namespace Engine.Client.UI;

/// <summary>
/// Tab bar on top, one page shown at a time below it.
/// </summary>
public sealed partial class TabContainer : BoxContainer
{
    private readonly BoxContainer _tabBar;
    private readonly PanelContainer _pages;
    private readonly List<(Button Button, Control Page)> _tabs = new();
    private int _currentTab = -1;

    public int CurrentTab
    {
        get => _currentTab;
        set => SelectTab(value);
    }

    public event Action<int>? OnTabChanged;

    public TabContainer()
    {
        Orientation = Orientation.Vertical;
        StyleAliasses.Add("tabContainer");

        _tabBar = new BoxContainer { Orientation = Orientation.Horizontal };
        _tabBar.StyleAliasses.Add("tabBar");

        _pages = new PanelContainer { VerticalExpand = true };
        _pages.StyleAliasses.Add("tabPages");

        base.AddChild(_tabBar);
        base.AddChild(_pages);
    }

    public new void AddChild(Control child) =>
        throw new InvalidOperationException("Use AddTab(title, content) on a TabContainer.");

    public new void RemoveChild(Control child, bool dispose = false)
    {
        var index = _tabs.FindIndex(t => t.Page == child);
        if (index >= 0)
            RemoveTab(index);
    }

    public int AddTab(string title, Control page)
    {
        var button = new Button(title);
        button.StyleAliasses.Add("tabButton");
        button.OnClick += _ => SelectTab(_tabs.FindIndex(t => t.Button == button));

        page.Visible = false;
        page.StyleAliasses.Add("tabPage");

        _tabBar.AddChild(button);
        _pages.AddChild(page);
        _tabs.Add((button, page));

        if (_currentTab < 0)
            SelectTab(_tabs.Count - 1);

        return _tabs.Count - 1;
    }

    public void RemoveTab(int index)
    {
        if (index < 0 || index >= _tabs.Count)
            return;

        var (button, page) = _tabs[index];
        _tabs.RemoveAt(index);
        _tabBar.RemoveChild(button, dispose: true);
        _pages.RemoveChild(page);

        if (index < _currentTab)
        {
            _currentTab--;
        }
        else if (index == _currentTab)
        {
            _currentTab = -1;
            if (_tabs.Count > 0)
                SelectTab(Math.Min(index, _tabs.Count - 1));
        }
    }

    private void SelectTab(int index)
    {
        if (index < 0 || index >= _tabs.Count || index == _currentTab)
            return;

        if (_currentTab >= 0)
        {
            var (prevButton, prevPage) = _tabs[_currentTab];
            prevButton.PseudoClasses.Remove("active");
            prevPage.Visible = false;
        }

        _currentTab = index;
        var (button, page) = _tabs[index];
        button.PseudoClasses.Add("active");
        page.Visible = true;

        OnTabChanged?.Invoke(index);
    }
}
