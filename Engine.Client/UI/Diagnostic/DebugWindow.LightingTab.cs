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
    private CheckButton _wallBleedCheck = default!;
    private CheckButton _lightBlurCheck = default!;
    private CheckButton _pixelatedCheck = default!;
    private CheckButton _occluderMaskCheck = default!;
    private HorizontalSlider _scaleSlider = default!;
    private MyraLabel _scaleLabel = default!;
    private HorizontalSlider _pixelSizeSlider = default!;
    private MyraLabel _pixelSizeLabel = default!;
    private HorizontalSlider _bleedStrengthSlider = default!;
    private MyraLabel _bleedStrengthLabel = default!;
    private HorizontalSlider _bleedIterationsSlider = default!;
    private MyraLabel _bleedIterationsLabel = default!;
    private HorizontalSlider _bleedRadiusSlider = default!;
    private MyraLabel _bleedRadiusLabel = default!;
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

        // the two post passes, split out so their cost can be A/B'd in place
        _wallBleedCheck = new CheckButton
        {
            Content = new MyraLabel { Text = "Wall bleed (blur + merge over occluders)" }
        };
        _wallBleedCheck.IsCheckedChanged += (_, _) =>
            _lighting.WallBleedEnabled = _wallBleedCheck.IsChecked;

        _lightBlurCheck = new CheckButton
        {
            Content = new MyraLabel { Text = "Light blur (full lightmap gaussian)" }
        };
        _lightBlurCheck.IsCheckedChanged += (_, _) =>
            _lighting.LightBlurEnabled = _lightBlurCheck.IsChecked;

        _pixelatedCheck = new CheckButton
        {
            Content = new MyraLabel { Text = "Pixelated lighting (point-sampled lightmap)" }
        };
        _pixelatedCheck.IsCheckedChanged += (_, _) =>
            _cfg.Set(LightingCvars.PixelatedLighting, _pixelatedCheck.IsChecked);

        _occluderMaskCheck = new CheckButton
        {
            Content = new MyraLabel { Text = "Show occluder mask" }
        };
        _occluderMaskCheck.IsCheckedChanged += (_, _) =>
            _cfg.Set(LightingCvars.ShowOccluderMask, _occluderMaskCheck.IsChecked);

        layout.Widgets.Add(_enabledCheck);
        layout.Widgets.Add(_debugCheck);
        layout.Widgets.Add(_hardShadowsCheck);
        layout.Widgets.Add(_wallBleedCheck);
        layout.Widgets.Add(_lightBlurCheck);
        layout.Widgets.Add(_pixelatedCheck);
        layout.Widgets.Add(_occluderMaskCheck);

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

        // ---- Wall bleed strength ----
        _bleedStrengthLabel = new MyraLabel { Text = "Wall Bleed Strength: 1.0" };
        _bleedStrengthSlider = new HorizontalSlider
        {
            Minimum = 0,
            Maximum = 40,
            Value = 10,
            Width = 200,
        };
        _bleedStrengthSlider.ValueChanged += (_, _) =>
        {
            var v = _bleedStrengthSlider.Value / 10.0f;
            _bleedStrengthLabel.Text = $"Wall Bleed Strength: {v:0.0}";
            _lighting.WallBleedStrength = v;
        };
        var bleedStrengthRow = new HorizontalStackPanel { Spacing = 8 };
        bleedStrengthRow.Widgets.Add(_bleedStrengthLabel);
        bleedStrengthRow.Widgets.Add(_bleedStrengthSlider);
        layout.Widgets.Add(bleedStrengthRow);

        // ---- Wall bleed radius ----
        // How long the fade to black is at the edge of the glow. Strength
        // scales it in place, iterations only smooth it - this is the one
        // that makes the gradient longer.
        _bleedRadiusLabel = new MyraLabel { Text = "Wall Bleed Radius: 1.0" };
        _bleedRadiusSlider = new HorizontalSlider
        {
            Minimum = 3,
            Maximum = 60,
            Value = 10,
            Width = 200,
        };
        _bleedRadiusSlider.ValueChanged += (_, _) =>
        {
            var v = _bleedRadiusSlider.Value / 10.0f;
            _bleedRadiusLabel.Text = $"Wall Bleed Radius: {v:0.0}";
            _lighting.WallBleedRadius = v;
        };
        var bleedRadiusRow = new HorizontalStackPanel { Spacing = 8 };
        bleedRadiusRow.Widgets.Add(_bleedRadiusLabel);
        bleedRadiusRow.Widgets.Add(_bleedRadiusSlider);
        layout.Widgets.Add(bleedRadiusRow);

        // ---- Wall bleed iterations ----
        // Trades fill for a smoother falloff at the same width - raise this
        // only if a wide radius starts to band.
        _bleedIterationsLabel = new MyraLabel { Text = "Wall Bleed Iterations: 2" };
        _bleedIterationsSlider = new HorizontalSlider
        {
            Minimum = 1,
            Maximum = 4,
            Value = 2,
            Width = 200,
        };
        _bleedIterationsSlider.ValueChanged += (_, _) =>
        {
            int v = (int)_bleedIterationsSlider.Value;
            _bleedIterationsLabel.Text = $"Wall Bleed Iterations: {v}";
            _lighting.WallBleedIterations = v;
        };
        var bleedIterationsRow = new HorizontalStackPanel { Spacing = 8 };
        bleedIterationsRow.Widgets.Add(_bleedIterationsLabel);
        bleedIterationsRow.Widgets.Add(_bleedIterationsSlider);
        layout.Widgets.Add(bleedIterationsRow);

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
        _wallBleedCheck.IsChecked = _lighting.WallBleedEnabled;
        _lightBlurCheck.IsChecked = _lighting.LightBlurEnabled;
        _pixelatedCheck.IsChecked = _cfg.Get(LightingCvars.PixelatedLighting);
        _occluderMaskCheck.IsChecked = _cfg.Get(LightingCvars.ShowOccluderMask);

        var scale = _cfg.Get(LightingCvars.LightmapScale);
        _scaleSlider.Value = (int)MathF.Round(scale * 10f);
        _scaleLabel.Text = $"Lightmap Scale: {scale:0.0}";

        int ps = _cfg.Get(LightingCvars.LightPixelSize);
        _pixelSizeSlider.Value = ps;
        _pixelSizeLabel.Text = $"Light Pixel Size: {ps}px";

        _bleedStrengthSlider.Value = MathF.Round(_lighting.WallBleedStrength * 10f);
        _bleedStrengthLabel.Text = $"Wall Bleed Strength: {_lighting.WallBleedStrength:0.0}";

        _bleedRadiusSlider.Value = MathF.Round(_lighting.WallBleedRadius * 10f);
        _bleedRadiusLabel.Text = $"Wall Bleed Radius: {_lighting.WallBleedRadius:0.0}";

        _bleedIterationsSlider.Value = _lighting.WallBleedIterations;
        _bleedIterationsLabel.Text = $"Wall Bleed Iterations: {_lighting.WallBleedIterations}";
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
        if (_wallBleedCheck.IsChecked != _lighting.WallBleedEnabled)
            _wallBleedCheck.IsChecked = _lighting.WallBleedEnabled;
        if (_lightBlurCheck.IsChecked != _lighting.LightBlurEnabled)
            _lightBlurCheck.IsChecked = _lighting.LightBlurEnabled;
        var pixelated = _cfg.Get(LightingCvars.PixelatedLighting);
        if (_pixelatedCheck.IsChecked != pixelated)
            _pixelatedCheck.IsChecked = pixelated;
        var showOccluderMask = _cfg.Get(LightingCvars.ShowOccluderMask);
        if (_occluderMaskCheck.IsChecked != showOccluderMask)
            _occluderMaskCheck.IsChecked = showOccluderMask;

        RefreshLightList();
    }

    private void RefreshLightList()
    {
        _statsLabel.Text =
            $"Lights: {_lighting.LastVisibleLights}/{_lighting.LastShadowLights} shadow | " +
            $"Occluders: {_lighting.LastOccluders} | " +
            $"Shadow map: {_lighting.LastShadowMapWidth}x{_lighting.LastShadowMapHeight}\n" +
            // these are cpu submit times, not gpu time - GL draws are async, so
            // a low number here doesn't mean the pass is cheap. Toggle the
            // passes off and watch the frame time to get the real cost.
            $"cpu submit - total: {_lighting.LastLightingTotalMs:0.00}ms | " +
            $"Shadow: {_lighting.LastShadowPassMs:0.00}ms " +
            $"(build {_lighting.LastShadowBuildMs:0.00} / setup {_lighting.LastShadowSetupMs:0.00} / draw {_lighting.LastShadowDrawMs:0.00}) | " +
            $"Light: {_lighting.LastLightPassMs:0.00}ms | " +
            $"WallBleed: {_lighting.LastWallBleedMs:0.00}ms | " +
            $"Blur: {_lighting.LastLightBlurMs:0.00}ms\n" +
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
