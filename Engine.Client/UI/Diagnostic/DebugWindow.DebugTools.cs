using Engine.Shared.Configuration;
#pragma warning disable CS0618

using System;
using System.IO;
using Engine.Client.Assets;
using Engine.Client.Assets.Atlas;

using Engine.Shared.Configuration.CVars;
using Engine.Shared.Physics.Configuration;
using Microsoft.Xna.Framework.Graphics;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.TextureAtlases;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.File;
using MyraLabel = Myra.Graphics2D.UI.Label;
using MyraListBox = Myra.Graphics2D.UI.ListBox;

namespace Engine.Client.UI.Debug;

public sealed class DebugToolsTab : TabItem, IDisposable
{
    private readonly IConfigurationManager _cfg;
    private CheckButton _collisionCheck;
    private CheckButton _scaleCheck;
    private bool _disposed;

    public DebugToolsTab(IConfigurationManager cfg)
    {
        _cfg = cfg;
        Text = "Debug Tools";

        BuildUI();
        ReloadCvars();
        _cfg.OnConfigLoad += ReloadCvars;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cfg.OnConfigLoad -= ReloadCvars;
    }

    private void ReloadCvars()
    {
        _collisionCheck.IsChecked = _cfg.Get(PhysicsCvars.CollisionMask);
        _scaleCheck.IsChecked = _cfg.Get(GameCVars.ScaleOuter);
    }

    private void BuildUI()
    {
        var layout = new VerticalStackPanel();

        var saveLoadLayout = new HorizontalStackPanel() { Margin = new(5)};
        var saveCvarsBtn = new Button
        {
            Content = new MyraLabel { Text = "Save CVars" },
            Padding = new(5),
            Margin = new(0, 0, 5, 0),
        };
        var loadCvarsBtn = new Button
        {
            Content = new MyraLabel { Text = "Load CVars" },
            Padding = new(5),
        };
        layout.Widgets.Add(new MyraLabel { Text = "CVars"} );
        saveLoadLayout.Widgets.Add(saveCvarsBtn);
        saveLoadLayout.Widgets.Add(loadCvarsBtn);

        _collisionCheck = new CheckButton
        {
            Content = new MyraLabel { Text = "Show collision mask" }
        };

        _scaleCheck = new CheckButton
        {
            Content = new MyraLabel { Text = "Scale outer" }
        };

        saveCvarsBtn.Click += (_, _) => _cfg.SaveConfig();
        loadCvarsBtn.Click += (_, _) => _cfg.LoadConfig();
        _collisionCheck.Click += OnCollisionMask;
        _scaleCheck.Click += OnScaleChange;

        layout.Widgets.Add(saveLoadLayout);
        layout.Widgets.Add(_collisionCheck);
        layout.Widgets.Add(_scaleCheck);

        Content = layout;
    }

    private void OnScaleChange(object? sender, EventArgs e)
    {
        _cfg.Set(GameCVars.ScaleOuter, _scaleCheck.IsChecked);
    }

    private void OnCollisionMask(object? sender, EventArgs e)
    {
        _cfg.Set(PhysicsCvars.CollisionMask, _collisionCheck.IsChecked);
    }
}