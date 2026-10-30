using System.Collections.Generic;
using Engine.Client.Graphics.Lighting;
using Engine.Client.Inputs;
using Engine.Shared.Configuration;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.UI.Debug;

public sealed class LightingDebugTab
{
    private readonly EntityManager _entManager;
    private readonly IConfigurationManager _cfg;

    private readonly List<(string Name, Texture2D Page)> _pages = new();
    private readonly ItemList _list;
    private readonly TextureRect _preview;
    private readonly Label _info;
    private readonly CheckBox _baseCheck;
    private readonly CheckBox _occluderCheck;

    public Control Root { get; }

    public LightingDebugTab(EntityManager entManager, IConfigurationManager cfg)
    {
        _entManager = entManager;
        _cfg = cfg;

        var column = new BoxContainer { Orientation = Orientation.Vertical, Separation = 8 };
        Root = column;

        var toggles = new BoxContainer { Orientation = Orientation.Horizontal, Separation = 8 };
        _baseCheck = new CheckBox { Text = "Light base (light.debug-base)" };
        _baseCheck.OnToggled += OnBaseToggled;
        _occluderCheck = new CheckBox { Text = "Occluders (light.occluder-overlay)" };
        _occluderCheck.OnToggled += OnOccludersToggled;
        toggles.AddChild(_baseCheck);
        toggles.AddChild(_occluderCheck);
        column.AddChild(toggles);

        var row = new BoxContainer { Orientation = Orientation.Horizontal, Separation = 8 };
        column.AddChild(row);

        var left = new BoxContainer { Orientation = Orientation.Vertical, Separation = 6 };
        row.AddChild(left);

        var refresh = new Button("Refresh");
        refresh.OnClick += OnRefresh;
        left.AddChild(refresh);

        _list = new ItemList { MinWidth = 200, MinHeight = 440 };
        _list.OnSelectionChanged += OnPageSelected;
        left.AddChild(_list);

        var right = new BoxContainer { Orientation = Orientation.Vertical, Separation = 6 };
        row.AddChild(right);

        var previewPanel = new PanelContainer { Background = Color.Black, Width = 500, Height = 500 };
        _preview = new TextureRect { Stretch = true, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        previewPanel.AddChild(_preview);
        right.AddChild(previewPanel);

        _info = new Label { Text = "..." };
        right.AddChild(_info);

        _baseCheck.Pressed = _cfg.Get(LightingCvars.DebugBase);
        _occluderCheck.Pressed = _cfg.Get(LightingCvars.DebugOccluders);
        Refresh();
    }

    private void OnBaseToggled(bool pressed)
        => _cfg.Set(LightingCvars.DebugBase, pressed);

    private void OnOccludersToggled(bool pressed)
        => _cfg.Set(LightingCvars.DebugOccluders, pressed);

    private void OnRefresh(MouseButton _)
        => Refresh();

    // the pages come and go with the lights, so they are listed on demand
    private void Refresh()
    {
        _pages.Clear();
        _list.Clear();
        _preview.Source = null;

        _entManager.GetSystem<LightingSystem>()?.CollectPages(_pages);

        foreach (var (name, _) in _pages)
            _list.AddItem(name);

        _info.Text = _pages.Count == 0 ? "No pages" : $"{_pages.Count} pages";
    }

    private void OnPageSelected(int index)
    {
        if (index < 0 || index >= _pages.Count)
            return;

        var (name, page) = _pages[index];
        if (page.IsDisposed)
        {
            Refresh();
            return;
        }

        _preview.Source = page;
        _info.Text = $"{name} | {page.Width}x{page.Height} | {page.Width * page.Height * 4 / 1024f / 1024f:0.00}Mb";
    }
}
