using Engine.Client.Graphics.Lighting;
using Engine.Shared.Configuration;
using Engine.Shared.GameObjects;
using Engine.Shared.Lighting;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug;

/// <summary>
/// Inspects and controls the lighting system at runtime: master toggles, tuning sliders,
/// live stats and a list of every active light in the current scene.
/// </summary>
public sealed class LightingDebugTab
{
    [Dependency] private readonly LightingManager _lighting = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly CheckBox _enabledCheck;
    private readonly CheckBox _debugCheck;
    private readonly CheckBox _hardShadowsCheck;
    private readonly CheckBox _wallBleedCheck;
    private readonly CheckBox _lightBlurCheck;
    private readonly CheckBox _pixelatedCheck;
    private readonly CheckBox _occluderMaskCheck;

    private readonly Slider _scaleSlider;
    private readonly Label _scaleLabel;
    private readonly Slider _pixelSizeSlider;
    private readonly Label _pixelSizeLabel;
    private readonly Slider _bleedStrengthSlider;
    private readonly Label _bleedStrengthLabel;
    private readonly Slider _bleedRadiusSlider;
    private readonly Label _bleedRadiusLabel;
    private readonly Slider _bleedIterationsSlider;
    private readonly Label _bleedIterationsLabel;

    private readonly Label _statsLabel;

    public Control Root { get; }

    public LightingDebugTab()
    {
        IoCManager.ResolveDependencies(this);

        var layout = new BoxContainer { Orientation = Orientation.Vertical, _separation = 5 };
        Root = layout;

        layout.AddChild(new Label { Text = "Lighting System" });

        _enabledCheck = new CheckBox { Text = "Enabled" };
        _enabledCheck.OnToggled += pressed => _lighting.SetEnabled(pressed);

        _debugCheck = new CheckBox { Text = "Debug draw (raw lightmap)" };
        _debugCheck.OnToggled += pressed => _lighting.DebugDraw = pressed;

        _hardShadowsCheck = new CheckBox { Text = "Hard shadows (single-sample, faster)" };
        _hardShadowsCheck.OnToggled += pressed => _lighting.HardShadows = pressed;

        _wallBleedCheck = new CheckBox { Text = "Wall bleed (blur + merge over occluders)" };
        _wallBleedCheck.OnToggled += pressed => _lighting.WallBleedEnabled = pressed;

        _lightBlurCheck = new CheckBox { Text = "Light blur (full lightmap gaussian)" };
        _lightBlurCheck.OnToggled += pressed => _lighting.LightBlurEnabled = pressed;

        _pixelatedCheck = new CheckBox { Text = "Pixelated lighting (point-sampled lightmap)" };
        _pixelatedCheck.OnToggled += pressed => _cfg.Set(LightingCvars.PixelatedLighting, pressed);

        _occluderMaskCheck = new CheckBox { Text = "Show occluder mask" };
        _occluderMaskCheck.OnToggled += pressed => _cfg.Set(LightingCvars.ShowOccluderMask, pressed);

        layout.AddChild(_enabledCheck);
        layout.AddChild(_debugCheck);
        layout.AddChild(_hardShadowsCheck);
        layout.AddChild(_wallBleedCheck);
        layout.AddChild(_lightBlurCheck);
        layout.AddChild(_pixelatedCheck);
        layout.AddChild(_occluderMaskCheck);

        _scaleLabel = new Label { Text = "Lightmap Scale: 1.0" };
        _scaleSlider = new Slider { MinValue = 0.1f, MaxValue = 1f, Step = 0.1f, Width = 200 };
        _scaleSlider.OnValueChanged += v =>
        {
            _scaleLabel.Text = $"Lightmap Scale: {v:0.0}";
            _cfg.Set(LightingCvars.LightmapScale, v);
        };
        layout.AddChild(SliderRow(_scaleLabel, _scaleSlider));

        _pixelSizeLabel = new Label { Text = "Light Pixel Size: 8px" };
        _pixelSizeSlider = new Slider { MinValue = 1f, MaxValue = 32f, Step = 1f, Width = 200 };
        _pixelSizeSlider.OnValueChanged += v =>
        {
            _pixelSizeLabel.Text = $"Light Pixel Size: {(int)v}px";
            _cfg.Set(LightingCvars.LightPixelSize, (int)v);
        };
        layout.AddChild(SliderRow(_pixelSizeLabel, _pixelSizeSlider));

        _bleedStrengthLabel = new Label { Text = "Wall Bleed Strength: 1.0" };
        _bleedStrengthSlider = new Slider { MinValue = 0f, MaxValue = 4f, Step = 0.1f, Width = 200 };
        _bleedStrengthSlider.OnValueChanged += v =>
        {
            _bleedStrengthLabel.Text = $"Wall Bleed Strength: {v:0.0}";
            _lighting.WallBleedStrength = v;
        };
        layout.AddChild(SliderRow(_bleedStrengthLabel, _bleedStrengthSlider));

        _bleedRadiusLabel = new Label { Text = "Wall Bleed Radius: 1.0" };
        _bleedRadiusSlider = new Slider { MinValue = 0.3f, MaxValue = 6f, Step = 0.1f, Width = 200 };
        _bleedRadiusSlider.OnValueChanged += v =>
        {
            _bleedRadiusLabel.Text = $"Wall Bleed Radius: {v:0.0}";
            _lighting.WallBleedRadius = v;
        };
        layout.AddChild(SliderRow(_bleedRadiusLabel, _bleedRadiusSlider));

        _bleedIterationsLabel = new Label { Text = "Wall Bleed Iterations: 2" };
        _bleedIterationsSlider = new Slider { MinValue = 1f, MaxValue = 4f, Step = 1f, Width = 200 };
        _bleedIterationsSlider.OnValueChanged += v =>
        {
            _bleedIterationsLabel.Text = $"Wall Bleed Iterations: {(int)v}";
            _lighting.WallBleedIterations = (int)v;
        };
        layout.AddChild(SliderRow(_bleedIterationsLabel, _bleedIterationsSlider));

        _statsLabel = new Label { Text = "..." };
        layout.AddChild(new Label { Text = "Stats" });
        layout.AddChild(_statsLabel);

        layout.AddChild(new Label { Text = "Active Lights" });

        ReloadState();
    }

    private static BoxContainer SliderRow(Label label, Slider slider)
    {
        var row = new BoxContainer { Orientation = Orientation.Horizontal, _separation = 8 };
        row.AddChild(label);
        row.AddChild(slider);
        return row;
    }

    private void ReloadState()
    {
        _enabledCheck.Pressed = _lighting.Enabled;
        _debugCheck.Pressed = _lighting.DebugDraw;
        _hardShadowsCheck.Pressed = _lighting.HardShadows;
        _wallBleedCheck.Pressed = _lighting.WallBleedEnabled;
        _lightBlurCheck.Pressed = _lighting.LightBlurEnabled;
        _pixelatedCheck.Pressed = _cfg.Get(LightingCvars.PixelatedLighting);
        _occluderMaskCheck.Pressed = _cfg.Get(LightingCvars.ShowOccluderMask);

        var scale = _cfg.Get(LightingCvars.LightmapScale);
        _scaleSlider.Value = scale;
        _scaleLabel.Text = $"Lightmap Scale: {scale:0.0}";

        var ps = _cfg.Get(LightingCvars.LightPixelSize);
        _pixelSizeSlider.Value = ps;
        _pixelSizeLabel.Text = $"Light Pixel Size: {ps}px";

        _bleedStrengthSlider.Value = _lighting.WallBleedStrength;
        _bleedStrengthLabel.Text = $"Wall Bleed Strength: {_lighting.WallBleedStrength:0.0}";

        _bleedRadiusSlider.Value = _lighting.WallBleedRadius;
        _bleedRadiusLabel.Text = $"Wall Bleed Radius: {_lighting.WallBleedRadius:0.0}";

        _bleedIterationsSlider.Value = _lighting.WallBleedIterations;
        _bleedIterationsLabel.Text = $"Wall Bleed Iterations: {_lighting.WallBleedIterations}";
    }

    private float _refreshTimer;
    private const float RefreshInterval = 0.25f;

    public void Update(float dt)
    {
        if (!Root.EffectivelyVisible)
            return;

        if (_enabledCheck.Pressed != _lighting.Enabled) _enabledCheck.Pressed = _lighting.Enabled;
        if (_debugCheck.Pressed != _lighting.DebugDraw) _debugCheck.Pressed = _lighting.DebugDraw;
        if (_hardShadowsCheck.Pressed != _lighting.HardShadows) _hardShadowsCheck.Pressed = _lighting.HardShadows;
        if (_wallBleedCheck.Pressed != _lighting.WallBleedEnabled) _wallBleedCheck.Pressed = _lighting.WallBleedEnabled;
        if (_lightBlurCheck.Pressed != _lighting.LightBlurEnabled) _lightBlurCheck.Pressed = _lighting.LightBlurEnabled;

        var pixelated = _cfg.Get(LightingCvars.PixelatedLighting);
        if (_pixelatedCheck.Pressed != pixelated) _pixelatedCheck.Pressed = pixelated;

        var showOccluderMask = _cfg.Get(LightingCvars.ShowOccluderMask);
        if (_occluderMaskCheck.Pressed != showOccluderMask) _occluderMaskCheck.Pressed = showOccluderMask;

        _refreshTimer += dt;
        if (_refreshTimer < RefreshInterval)
            return;

        _refreshTimer = 0f;
        RefreshLightList();
    }

    private void RefreshLightList()
    {
        _statsLabel.Text =
            $"Lights: {_lighting.LastVisibleLights}/{_lighting.LastShadowLights} shadow | " +
            $"Occluders: {_lighting.LastOccluders} | " +
            $"Shadow map: {_lighting.LastShadowMapWidth}x{_lighting.LastShadowMapHeight}\n" +
            $"cpu submit - total: {_lighting.LastLightingTotalMs:0.00}ms | " +
            $"Shadow: {_lighting.LastShadowPassMs:0.00}ms " +
            $"(build {_lighting.LastShadowBuildMs:0.00} / setup {_lighting.LastShadowSetupMs:0.00} / draw {_lighting.LastShadowDrawMs:0.00}) | " +
            $"Light: {_lighting.LastLightPassMs:0.00}ms | " +
            $"WallBleed: {_lighting.LastWallBleedMs:0.00}ms | " +
            $"Blur: {_lighting.LastLightBlurMs:0.00}ms\n" +
            $"Ambient: [{_lighting.AmbientLight.R},{_lighting.AmbientLight.G},{_lighting.AmbientLight.B}] | " +
            $"Intensity: {_lighting.LightIntensity:0.00}";
    }
}
