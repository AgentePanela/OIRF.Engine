using System;
using System.Collections.Generic;
using System.IO;
using Engine.Client.Assets;
using Engine.Client.Assets.Atlas;
using Engine.Shared.IoC;
using Engine.Shared.Storage;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug;

/// <summary>
/// Lists every loaded texture atlas page and previews the selected one.
/// </summary>
public sealed class AtlasDebugTab
{
    private readonly List<AtlasPage> _atlases = new();
    private readonly ItemList _list;
    private readonly TextureRect _preview;
    private readonly Label _info;

    public Control Root { get; }

    public AtlasDebugTab(IAssetManager asset)
    {
        var row = new BoxContainer { Orientation = Orientation.Horizontal, _separation = 8 };
        Root = row;

        _list = new ItemList { MinWidth = 200, MinHeight = 500 };
        _list.OnSelectionChanged += OnAtlasSelected;
        row.AddChild(_list);

        var rightPanel = new BoxContainer { Orientation = Orientation.Vertical, _separation = 6 };
        row.AddChild(rightPanel);

        var previewPanel = new PanelContainer { Background = Color.Black, Width = 500, Height = 500 };
        _preview = new TextureRect { Stretch = true, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        previewPanel.AddChild(_preview);
        rightPanel.AddChild(previewPanel);

        var footer = new BoxContainer { Orientation = Orientation.Horizontal, _separation = 8 };
        var saveButton = new Button("Save Atlas As PNG");
        saveButton.OnClick += _ => SaveSelectedAtlas();
        _info = new Label { Text = "..." };
        footer.AddChild(saveButton);
        footer.AddChild(_info);
        rightPanel.AddChild(footer);

        foreach (var atlas in asset.GetAllAtlasses())
        {
            _atlases.Add(atlas);
            _list.AddItem($"Atlas {_atlases.Count - 1}");
        }
    }

    private void OnAtlasSelected(int index)
    {
        if (index < 0 || index >= _atlases.Count)
            return;

        var atlas = _atlases[index];
        _preview.Source = atlas.Texture;
        _info.Text = $"{GetTextureMb(atlas.Texture)}Mb | Sprites: {atlas.Regions.Count}";
    }

    private void SaveSelectedAtlas()
    {
        var index = _list.SelectedIndex;
        if (index < 0 || index >= _atlases.Count)
            return;

        var atlas = _atlases[index];
        var storage = IoCManager.Resolve<UserStorageManager>();
        var relative = $"DebugExports/Atlas_{index}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        var full = storage.GetFullPath(relative);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var stream = File.Create(full);
        atlas.Texture.SaveAsPng(stream, atlas.Texture.Width, atlas.Texture.Height);

        _info.Text = $"Saved to {relative}";
    }

    private static string GetTextureMb(Microsoft.Xna.Framework.Graphics.Texture2D tex)
    {
        var bytes = tex.Width * tex.Height * 4;
        return (bytes / 1024f / 1024f).ToString("0.00");
    }
}
