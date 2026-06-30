#pragma warning disable CS0618

using System;
using System.IO;
using Engine.Client.Assets;
using Engine.Client.Assets.Atlas;
using Microsoft.Xna.Framework.Graphics;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.TextureAtlases;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.File;
using MyraLabel = Myra.Graphics2D.UI.Label;
using MyraListBox = Myra.Graphics2D.UI.ListBox;

namespace Engine.Client.UI.Debug;

public sealed class AtlasDebugTab : TabItem
{
    private readonly IAssetManager _asset;
    private readonly Desktop _desktop;

    private MyraListBox _atlasList = default!;
    private Image _atlasPreview = default!;
    private MyraLabel _atlasInfo = default!;

    public AtlasDebugTab(IAssetManager asset, Desktop desktop)
    {
        _asset = asset;
        _desktop = desktop;

        Text = "Atlases";

        BuildUI();
        PopulateAtlasList();
    }

    private void BuildUI()
    {
        var layout = new HorizontalStackPanel();

        _atlasList = new MyraListBox
        {
            Width = 200
        };

        _atlasPreview = new Image
        {
            Background = new SolidBrush(Microsoft.Xna.Framework.Color.Black),
            Width = 500,
            Height = 500
        };

        var rightPanel = new VerticalStackPanel();
        var previewFooter = new HorizontalStackPanel { Spacing = 5 };

        var saveButton = new Button
        {
            Content = new MyraLabel { Text = "Save Atlas As PNG" }
        };

        _atlasInfo = new MyraLabel { Text = "..." };

        saveButton.Click += OnSaveAtlas;

        rightPanel.Widgets.Add(_atlasPreview);
        rightPanel.Widgets.Add(previewFooter);

        previewFooter.Widgets.Add(saveButton);
        previewFooter.Widgets.Add(_atlasInfo);

        layout.Widgets.Add(_atlasList);
        layout.Widgets.Add(rightPanel);

        Content = layout;

        _atlasList.SelectedIndexChanged += OnAtlasSelected;
    }

    private string GetTextureMb(Texture2D tex)
    {
        var bytes = tex.Width * tex.Height * 4;
        return (bytes / 1024f / 1024f).ToString("0.00");
    }

    private void PopulateAtlasList()
    {
        _atlasList.Items.Clear();

        var atlases = _asset.GetAllAtlasses();

        for (var i = 0; i < atlases.Count; i++)
        {
            var atlas = atlases[i];

            _atlasList.Items.Add(new ListItem($"Atlas {i}")
            {
                Tag = atlas
            });
        }
    }

    private void OnAtlasSelected(object? sender, EventArgs args)
    {
        if (_atlasList.SelectedItem?.Tag is not AtlasPage atlas)
            return;

        _atlasPreview.Renderable = new TextureRegion(atlas.Texture);
        _atlasInfo.Text = $"{GetTextureMb(atlas.Texture)}Mb | Sprites: {atlas.Regions.Count}";
    }

    private void OnSaveAtlas(object? sender, EventArgs args)
    {
        if (_atlasList.SelectedItem?.Tag is not AtlasPage atlas)
            return;

        var dialog = new FileDialog(FileDialogMode.SaveFile)
        {
            Title = "Save Atlas",
            Filter = "*.png"
        };

        dialog.Closed += (s, a) =>
        {
            if (!dialog.Result)
                return;

            using var stream = File.Create(dialog.FilePath);

            atlas.Texture.SaveAsPng(stream, atlas.Texture.Width, atlas.Texture.Height);
        };

        dialog.ShowModal(_desktop);
    }
}

#pragma warning restore CS0618