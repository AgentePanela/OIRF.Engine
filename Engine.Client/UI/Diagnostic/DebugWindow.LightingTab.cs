#pragma warning disable CS0618

using System;
using Engine.Client.Graphics.Lighting;
using Engine.Shared.Configuration;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Components.Lighting;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using MyraLabel = Myra.Graphics2D.UI.Label;
using MyraListBox = Myra.Graphics2D.UI.ListBox;

namespace Engine.Client.UI.Debug;

/// <summary>
/// Debug tab for inspecting and controlling the lighting system at runtime.
/// Lists every active <see cref="PointLightComponent"/>, <see cref="SpotLightComponent"/>,
/// <see cref="AmbientLightComponent"/> and <see cref="TextureLightComponent"/>, and exposes
/// toggles for <see cref="LightingManager.Enabled"/> and <see cref="LightingManager.DebugDraw"/>.
/// </summary>
public sealed class LightingDebugTab : TabItem, IDisposable
{
    [Dependency] private readonly LightingManager _lighting = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly EntityManager _entManager = default!;

    private CheckButton _enabledCheck = default!;
    private CheckButton _debugCheck = default!;
    private CheckButton _hardShadowsCheck = default!;
    private CheckButton _pixelatedCheck = default!;
    private HorizontalSlider _scaleSlider = default!;
    private MyraLabel _scaleLabel = default!;
    private HorizontalSlider _pixelSizeSlider = default!;
    private MyraLabel _pixelSizeLabel = default!;
    private MyraLabel _statsLabel = default!;
    private MyraListBox _lightList = default!;

    public LightingDebugTab()
    {
        IoCManager.ResolveDependencies(this);

        Text = "Lighting";

        BuildUI();
        ReloadState();
    }

    public void Dispose() { }

    private void BuildUI()
    {
        var layout = new VerticalStackPanel { Spacing = 5 };

        // ---- Top: master toggles ----
        var header = new MyraLabel { Text = "Lighting System" };
        layout.Widgets.Add(header);

        _enabledCheck = new CheckButton
        {
            Content = new MyraLabel { Text = "Enabled" }
        };
        // Use IsCheckedChanged instead of Click — in Myra, Click fires on
        // press, but IsChecked is only updated on release. Reading it inside
        // Click reads the pre-toggle value, so the handler effectively no-ops.
        _enabledCheck.IsCheckedChanged += (_, _) =>
            _lighting.SetEnabled(_enabledCheck.IsChecked);

        _debugCheck = new CheckButton
        {
            Content = new MyraLabel { Text = "Debug draw (raw lightmap)" }
        };
        _debugCheck.IsCheckedChanged += (_, _) =>
            _lighting.DebugDraw = _debugCheck.IsChecked;

        _hardShadowsCheck = new CheckButton
        {
            Content = new MyraLabel { Text = "Hard shadows (single-sample, faster)" }
        };
        _hardShadowsCheck.IsCheckedChanged += (_, _) =>
            _lighting.HardShadows = _hardShadowsCheck.IsChecked;

        _pixelatedCheck = new CheckButton
        {
            Content = new MyraLabel { Text = "Pixelated lighting (point-sampled lightmap)" }
        };
        _pixelatedCheck.IsCheckedChanged += (_, _) =>
            _cfg.Set(LightingCvars.PixelatedLighting, _pixelatedCheck.IsChecked);

        layout.Widgets.Add(_enabledCheck);
        layout.Widgets.Add(_debugCheck);
        layout.Widgets.Add(_hardShadowsCheck);
        layout.Widgets.Add(_pixelatedCheck);

        // ---- Lightmap scale (smooth mode) ----
        _scaleLabel = new MyraLabel { Text = "Lightmap Scale: 1.0" };
        _scaleSlider = new HorizontalSlider
        {
            Minimum = 1,
            Maximum = 10,
            Value = 10,
            Width = 200,
        };
        _scaleSlider.ValueChanged += (_, _) =>
        {
            var v = _scaleSlider.Value / 10.0f;
            _scaleLabel.Text = $"Lightmap Scale: {v:0.0}";
            _cfg.Set(LightingCvars.LightmapScale, v);
        };
        var scaleRow = new HorizontalStackPanel { Spacing = 8 };
        scaleRow.Widgets.Add(_scaleLabel);
        scaleRow.Widgets.Add(_scaleSlider);
        layout.Widgets.Add(scaleRow);

        // ---- Light pixel size (pixelated mode) ----
        _pixelSizeLabel = new MyraLabel { Text = "Light Pixel Size: 8px" };
        _pixelSizeSlider = new HorizontalSlider
        {
            Minimum = 1,
            Maximum = 32,
            Value = 8,
            Width = 200,
        };
        _pixelSizeSlider.ValueChanged += (_, _) =>
        {
            int v = (int)_pixelSizeSlider.Value;
            _pixelSizeLabel.Text = $"Light Pixel Size: {v}px";
            _cfg.Set(LightingCvars.LightPixelSize, v);
        };
        var pixelSizeRow = new HorizontalStackPanel { Spacing = 8 };
        pixelSizeRow.Widgets.Add(_pixelSizeLabel);
        pixelSizeRow.Widgets.Add(_pixelSizeSlider);
        layout.Widgets.Add(pixelSizeRow);

        // ---- Stats ----
        _statsLabel = new MyraLabel { Text = "..." };
        layout.Widgets.Add(new MyraLabel { Text = "Stats" });
        layout.Widgets.Add(_statsLabel);

        // ---- List of lights ----
        layout.Widgets.Add(new MyraLabel { Text = "Active Lights" });
        _lightList = new MyraListBox { Width = 600, Height = 300 };
        layout.Widgets.Add(_lightList);

        Content = layout;
    }

    private void ReloadState()
    {
        _enabledCheck.IsChecked = _lighting.Enabled;
        _debugCheck.IsChecked = _lighting.DebugDraw;
        _hardShadowsCheck.IsChecked = _lighting.HardShadows;
        _pixelatedCheck.IsChecked = _cfg.Get(LightingCvars.PixelatedLighting);

        var scale = _cfg.Get(LightingCvars.LightmapScale);
        _scaleSlider.Value = (int)MathF.Round(scale * 10f);
        _scaleLabel.Text = $"Lightmap Scale: {scale:0.0}";

        int ps = _cfg.Get(LightingCvars.LightPixelSize);
        _pixelSizeSlider.Value = ps;
        _pixelSizeLabel.Text = $"Light Pixel Size: {ps}px";
    }

    public void Update(float dt)
    {
        // Only sync the toggle state if it actually drifted from the live
        // value (e.g. something else changed it). Forcing it every frame
        // fights with user clicks — Myra fires Click on press, but
        // IsChecked gets flipped after, so reloading the old value here
        // visually reverts the toggle before the user releases it.
        if (_enabledCheck.IsChecked != _lighting.Enabled)
            _enabledCheck.IsChecked = _lighting.Enabled;
        if (_debugCheck.IsChecked != _lighting.DebugDraw)
            _debugCheck.IsChecked = _lighting.DebugDraw;
        if (_hardShadowsCheck.IsChecked != _lighting.HardShadows)
            _hardShadowsCheck.IsChecked = _lighting.HardShadows;
        var pixelated = _cfg.Get(LightingCvars.PixelatedLighting);
        if (_pixelatedCheck.IsChecked != pixelated)
            _pixelatedCheck.IsChecked = pixelated;

        RefreshLightList();
    }

    private void RefreshLightList()
    {
        _statsLabel.Text =
            $"Lights: {_lighting.LastVisibleLights}/{_lighting.LastShadowLights} shadow | " +
            $"Occluders: {_lighting.LastOccluders} | " +
            $"Total: {_lighting.LastLightingTotalMs:0.0}ms\n" +
            $"Shadow: {_lighting.LastShadowPassMs:0.0}ms | " +
            $"Light: {_lighting.LastLightPassMs:0.0}ms | " +
            $"WallBleed: {_lighting.LastWallBleedMs:0.0}ms | " +
            $"Blur: {_lighting.LastLightBlurMs:0.0}ms\n" +
            $"Ambient: [{_lighting.AmbientLight.R},{_lighting.AmbientLight.G},{_lighting.AmbientLight.B}] | " +
            $"Intensity: {_lighting.LightIntensity:0.00}";

        _lightList.Items.Clear();

        foreach (var (uid, point, transform) in
            _entManager.Query<PointLightComponent, TransformComponent>())
        {
            _lightList.Items.Add(new ListItem(
                $"[Point] uid={uid.Id} " +
                $"pos=({transform.Position.X:0},{transform.Position.Y:0}) " +
                $"r={point.Radius:0} i={point.Intensity:0.00} " +
                $"shadows={point.CastShadows}"));
        }

        foreach (var (uid, spot, transform) in
            _entManager.Query<SpotLightComponent, TransformComponent>())
        {
            _lightList.Items.Add(new ListItem(
                $"[Spot] uid={uid.Id} " +
                $"pos=({transform.Position.X:0},{transform.Position.Y:0}) " +
                $"r={spot.Radius:0} i={spot.Intensity:0.00} " +
                $"cone={spot.ConeAngle:0}° dir={MathHelper.ToDegrees(spot.Direction):0}° " +
                $"shadows={spot.CastShadows}"));
        }

        foreach (var (uid, amb, _) in
            _entManager.Query<AmbientLightComponent, TransformComponent>())
        {
            _lightList.Items.Add(new ListItem(
                $"[Ambient] uid={uid.Id} " +
                $"color=[{amb.Color.R},{amb.Color.G},{amb.Color.B}] " +
                $"intensity={amb.Intensity:0.00} priority={amb.Priority}"));
        }

        foreach (var (uid, tex, transform) in
            _entManager.Query<TextureLightComponent, TransformComponent>())
        {
            _lightList.Items.Add(new ListItem(
                $"[Texture] uid={uid.Id} " +
                $"tex='{tex.Texture}' " +
                $"scale=({tex.Scale.X:0},{tex.Scale.Y:0}) " +
                $"pos=({transform.Position.X:0},{transform.Position.Y:0})"));
        }
    }
}
