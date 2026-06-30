using Engine.Client.Graphics;
using Engine.Shared.Configuration;

using Engine.Shared.Configuration.CVars;
using Engine.Client.Debug.Diagnostics;
using Engine.Client.Graphics.Lighting;

using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using System;
using Engine.Shared.IoC;
using Engine.Shared.Debug.Diagnostics;
using MonoGame.Framework.Utilities;

namespace Engine.Client.UI.Debug;

public sealed class DebugOverlayScreen : UICanvas
{
    [Dependency] private readonly Camera2D _camera = default!;
    [Dependency] private readonly RenderManager _renderMan = default!;
    [Dependency] private readonly LightingManager _lighting = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SystemsProfiler _proff = default!;

    private Label _generalLabel = default!;
    private Label _fpsLabel = default!;
    private Label _frameTimeLabel = default!;
    private Label _renderSpentTime = default!;
    private Label _cameraLabel = default!;
    private Label _zoomLabel = default!;
    private Label _resolutionLabel = default!;
    private Label _entityCountLabel = default!;
    private Label _memoryLabel = default!;
    private Label _profilerSummaryLabel = default!;
    private Label _lightingLabel = default!;
    private Label _gameStateLabel = default!;
    private Label _elapsedLabel = default!;
    private SystemProfilerWidget _profilerWidget = default!;

    private float _updateTimer;
    private const float UpdateInterval = 0.25f;

    private readonly SolidBrush _bgColor = new (new Color(0, 0, 0, 140));

    public DebugOverlayScreen()
    {
        IoCManager.ResolveDependencies(this);
    }

    public override void BuildElements()
    {
        SetRootType<Panel>();
        Root.HorizontalAlignment = HorizontalAlignment.Stretch;
        Root.VerticalAlignment = VerticalAlignment.Stretch;
        Root.ZIndex = 998;
 
        var leftPanel = new VerticalStackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = _bgColor,
            Padding = new (8, 6),
        };

        _fpsLabel = CreateLabel("fps");
        _frameTimeLabel = CreateLabel("frametime");
        _generalLabel = CreateLabel("general");
        _renderSpentTime = CreateLabel("renderer");
        _cameraLabel = CreateLabel("camera");
        _zoomLabel = CreateLabel("zoom");
        _resolutionLabel = CreateLabel("resolution");
        _entityCountLabel = CreateLabel("entities");
        _memoryLabel = CreateLabel("memory");
        _profilerSummaryLabel = CreateLabel("profilerSummary");
        _lightingLabel = CreateLabel("lighting");
        _gameStateLabel = CreateLabel("gamestate");
        _elapsedLabel = CreateLabel("elapsed");

        leftPanel.Widgets.Add(CreateSpacer());
        leftPanel.Widgets.Add(_fpsLabel);
        leftPanel.Widgets.Add(_frameTimeLabel);
        leftPanel.Widgets.Add(_generalLabel);
        leftPanel.Widgets.Add(CreateSpacer());
        leftPanel.Widgets.Add(_cameraLabel);
        leftPanel.Widgets.Add(_zoomLabel);
        leftPanel.Widgets.Add(CreateSpacer());
        leftPanel.Widgets.Add(_resolutionLabel);
        leftPanel.Widgets.Add(_entityCountLabel);
        leftPanel.Widgets.Add(CreateSpacer());
        leftPanel.Widgets.Add(_renderSpentTime);
        leftPanel.Widgets.Add(_profilerSummaryLabel);
        leftPanel.Widgets.Add(_lightingLabel);
        leftPanel.Widgets.Add(_memoryLabel);
        leftPanel.Widgets.Add(CreateSpacer());
        leftPanel.Widgets.Add(_gameStateLabel);
        leftPanel.Widgets.Add(_elapsedLabel);
        leftPanel.Widgets.Add(CreateSpacer());
 
        _profilerWidget = new SystemProfilerWidget(_fpsLabel.Font, _proff)
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new (8, 6),
            Background = _bgColor,
        };
 
        AddElement(leftPanel);
        AddElement(_profilerWidget);

        base.BuildElements();
    }

    public override void Initialize()
    {
        base.Initialize();
        RefreshLabels();
    }

    public override void Update(float dt)
    {
        base.Update(dt);
        _profilerWidget.Tick(dt);
        _updateTimer += dt;

        if (_updateTimer >= UpdateInterval)
        {
            _updateTimer = 0f;
            RefreshLabels();
        }
    }

    private void RefreshLabels()
    {
        var time = GameClient.GameTime;
        var gfx = GameClient.Graphics;

        _fpsLabel.Text = $"FPS: {time.Fps}";
        _frameTimeLabel.Text = $"Frame: {time.DeltaTime * 1000f:0.00} ms";
        _generalLabel.Text = $"{GameClient.Options.Title} - {_cfg.Get(GameCVars.GameVersion)}\n" +
                            $"Engine Version: {_cfg.Get(EngineCvars.EngineVersion)}\n" +
                            $"{PlatformInfo.GraphicsBackend} | {PlatformInfo.MonoGamePlatform}";
        
        _renderSpentTime.Text = $"Renderer draw time: {FormatMs(_renderMan.DrawStopwatch.Elapsed.TotalMilliseconds)}";
        RefreshProfilerLabels();

        _cameraLabel.Text = $"Camera XY: {_camera.Position.X:0.0}, {_camera.Position.Y:0.0}";
        _zoomLabel.Text = $"Zoom: {_camera.Zoom:0.00}x";

        _resolutionLabel.Text = $"Resolution: {gfx.PreferredBackBufferWidth}x{gfx.PreferredBackBufferHeight}";

        int entityCount = GameClient.EntityManager.GetEntityCount();
        _entityCountLabel.Text = $"Entities: {entityCount}";

        _memoryLabel.Text = MemoryMeter.GetInfo();

        _gameStateLabel.Text = $"State: {GameClient.GameState}";
        _elapsedLabel.Text = $"Elapsed: {FormatTime(time.TotalTime)}";
    }

    private void RefreshProfilerLabels()
    {
        double updateMs = 0;
        double drawMs = 0;
        int systems = 0;

        foreach (var snapshot in _proff.GetAll())
        {
            updateMs += snapshot.UpdateMs;
            drawMs += snapshot.DrawMs;
            systems++;
        }

        _profilerSummaryLabel.Text =
            $"Profiler: {systems} systems | Update {FormatMs(updateMs)} | Draw {FormatMs(drawMs)}";

        _lightingLabel.Text =
            $"Lighting: {FormatMs(_lighting.LastLightingTotalMs)} | " +
            $"lights {_lighting.LastVisibleLights}/{_lighting.LastShadowLights} | " +
            $"occ {_lighting.LastOccluders} | shadow {_lighting.LastShadowMapWidth}x{_lighting.LastShadowMapHeight}\n" +
            $"  shadow {FormatMs(_lighting.LastShadowPassMs)} | mask {FormatMs(_lighting.LastOcclusionMaskMs)} | " +
            $"light {FormatMs(_lighting.LastLightPassMs)} | bleed {FormatMs(_lighting.LastWallBleedMs)} | blur {FormatMs(_lighting.LastLightBlurMs)}";
    }

    private static Label CreateLabel(string id)
    {
        return new Label
        {
            Id = $"_dbgOverlay_{id}",
            Text = "...",
            TextColor = new Color(180, 255, 180),
            Margin = new Thickness(0, 1),
        };
    }

    private static Panel CreateSpacer()
    {
        return new Panel
        {
            Height = 4,
        };
    }

    private static string FormatMB(long bytes)
        => (bytes / 1024f / 1024f).ToString("0.00");

    private static string FormatTime(double t)
    {
        var span = TimeSpan.FromSeconds(t);
        return span.TotalHours >= 1
            ? span.ToString(@"hh\:mm\:ss")
            : span.ToString(@"mm\:ss");
    }

    private static string FormatMs(double ms)
    {
        if (ms >= 1000.0) return $"{ms / 1000.0:0.00}s";
        if (ms >= 1.0) return $"{ms:0.00}ms";
        if (ms >= 0.001) return $"{ms * 1000.0:0.0}µs";
        return "0µs";
    }
}
