using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Engine.Client.Debug.Diagnostics;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Lighting;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.Debug.Diagnostics;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using MonoGame.Framework.Utilities;

namespace Engine.Client.UI.Debug;

/// <summary>
/// F3-style overlay: general/camera/render/memory stats on the left, a per-system
/// update/draw bar chart on the right.
/// </summary>
public sealed class ProfilerOverlay : Overlay
{
    [Dependency] private readonly Camera2D _camera = default!;
    [Dependency] private readonly RenderManager _renderMan = default!;
    [Dependency] private readonly LightingManager _lighting = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SystemsProfiler _profiler = default!;

    private const int RowsPerSection = 10;
    private const float RefreshInterval = 0.25f;

    private static readonly Color UpdateBarColor = new(80, 180, 255, 220);
    private static readonly Color DrawBarColor = new(80, 255, 140, 220);
    private static readonly Color PanelBg = new(0, 0, 0, 140);

    private readonly Label _generalLabel;
    private readonly Label _fpsLabel;
    private readonly Label _cameraLabel;
    private readonly Label _resourcesLabel;
    private readonly Label _renderLabel;
    private readonly Label _lightingLabel;
    private readonly Label _memoryLabel;
    private readonly Label _stateLabel;

    private readonly List<ProfilerRow> _updateRows;
    private readonly List<ProfilerRow> _drawRows;
    private readonly List<SystemSnapshot> _scratch = new(64);

    private float _refreshTimer;

    private readonly record struct ProfilerRow(BoxContainer Row, Label Name, ProgressBar Bar, Label Value);

    public ProfilerOverlay()
    {
        IoCManager.ResolveDependencies(this);
        MouseFilter = MouseFilterMode.Ignore; // never blocks clicks to the game/UI below
        //ZIndex = 998;
        StylesheetOverride = "EngineDefault";

        var left = new BoxContainer
        {
            Orientation = Orientation.Vertical,
            _separation = 4,
            Background = PanelBg,
            Padding = new(8, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        AddChild(left);

        _generalLabel = CreateLabel();
        _fpsLabel = CreateLabel();
        _cameraLabel = CreateLabel();
        _resourcesLabel = CreateLabel();
        _renderLabel = CreateLabel();
        _lightingLabel = CreateLabel();
        _memoryLabel = CreateLabel();
        _stateLabel = CreateLabel();

        left.AddChild(_generalLabel);
        left.AddChild(new Separator());
        left.AddChild(_fpsLabel);
        left.AddChild(_cameraLabel);
        left.AddChild(new Separator());
        left.AddChild(_resourcesLabel);
        left.AddChild(new Separator());
        left.AddChild(_renderLabel);
        left.AddChild(_lightingLabel);
        left.AddChild(_memoryLabel);
        left.AddChild(new Separator());
        left.AddChild(_stateLabel);

        var right = new BoxContainer
        {
            Orientation = Orientation.Vertical,
            _separation = 4,
            Background = PanelBg,
            Padding = new(8, 6),
            MinWidth = 350,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        AddChild(right);

        right.AddChild(new Label { Text = "SYSTEMS PROFILER" });
        right.AddChild(new Separator());
        right.AddChild(new Label { Text = "UPDATE", FontSize = 28f, Color = Color.White });
        _updateRows = BuildRows(right, RowsPerSection, UpdateBarColor);
        right.AddChild(new Separator());
        right.AddChild(new Label { Text = "DRAW", FontSize = 28f, Color = Color.White });
        _drawRows = BuildRows(right, RowsPerSection, DrawBarColor);

        RefreshStats();
        RefreshSystems();
    }

    protected override void Update(float dt)
    {
        base.Update(dt);
        _refreshTimer += dt;
        if (_refreshTimer < RefreshInterval)
            return;

        _refreshTimer = 0f;
        RefreshStats();
        RefreshSystems();
    }

    private void RefreshStats()
    {
        var time = GameClient.GameTime;
        var gfx = GameClient.Graphics;
        var builtDate = File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location);

        _generalLabel.Text = $"{GameClient.Options.Title} - {_cfg.Get(GameCVars.GameVersion)} (built {builtDate:g})\n" +
                              $"Engine {_cfg.Get(EngineCvars.EngineVersion)} | {PlatformInfo.GraphicsBackend} | {PlatformInfo.MonoGamePlatform}";

        _fpsLabel.Text = $"FPS: {time.Fps} ({time.DeltaTime * 1000f:0.00}ms)";
        _cameraLabel.Text = $"Camera: {_camera.Position.X:0.0}, {_camera.Position.Y:0.0} | Zoom {_camera.Zoom:0.00}x";

        var entityCount = GameClient.EntityManager.GetEntityCount();
        _resourcesLabel.Text = $"Resolution: {gfx.PreferredBackBufferWidth}x{gfx.PreferredBackBufferHeight} | Entities: {entityCount}";

        _renderLabel.Text = $"Render draw: {FormatMs(_renderMan.DrawStopwatch.Elapsed.TotalMilliseconds)}\n" +
                             UIProfiler.LogSnapshot(true).TrimEnd();

        _lightingLabel.Text = $"Lighting: {FormatMs(_lighting.LastLightingTotalMs)} | " +
                               $"lights {_lighting.LastVisibleLights}/{_lighting.LastShadowLights} | occ {_lighting.LastOccluders}\n" +
                               $"  shadow {FormatMs(_lighting.LastShadowPassMs)} " +
                               $"(build {FormatMs(_lighting.LastShadowBuildMs)} / setup {FormatMs(_lighting.LastShadowSetupMs)} / draw {FormatMs(_lighting.LastShadowDrawMs)}) | " +
                               $"light {FormatMs(_lighting.LastLightPassMs)} | bleed {FormatMs(_lighting.LastWallBleedMs)} | blur {FormatMs(_lighting.LastLightBlurMs)}";

        _memoryLabel.Text = MemoryMeter.GetInfo();
        _stateLabel.Text = $"State: {GameClient.GameState} | Elapsed: {FormatTime(time.TotalTime)}";
    }

    private void RefreshSystems()
    {
        _scratch.Clear();
        foreach (var snapshot in _profiler.GetAll())
            _scratch.Add(snapshot);

        FillSection(_updateRows, s => s.UpdateMs);
        FillSection(_drawRows, s => s.DrawMs);
    }

    private void FillSection(List<ProfilerRow> rows, Func<SystemSnapshot, double> selector)
    {
        var ordered = _scratch.OrderByDescending(selector).Take(rows.Count).ToList();
        var maxMs = ordered.Count > 0 ? Math.Max(0.001, selector(ordered[0])) : 0.001;

        for (var i = 0; i < rows.Count; i++)
        {
            if (i >= ordered.Count)
            {
                rows[i].Row.Visible = false;
                continue;
            }

            var snapshot = ordered[i];
            var ms = selector(snapshot);

            rows[i].Row.Visible = true;
            rows[i].Name.Text = TruncateName(snapshot.Name);
            rows[i].Bar.Value = (float)(ms / maxMs);
            rows[i].Value.Text = FormatMs(ms);
        }
    }

    private static List<ProfilerRow> BuildRows(BoxContainer parent, int count, Color barColor)
    {
        var rows = new List<ProfilerRow>(count);
        for (var i = 0; i < count; i++)
        {
            var row = new BoxContainer { Orientation = Orientation.Horizontal, _separation = 6, Visible = false };
            var name = new Label { Width = 120, FontSize = 18f, Color = Color.White, AutoWrap = false  };
            var bar = new ProgressBar { Width = 100, BarThickness = 12f, FillColor = barColor };
            var value = new Label { MinWidth = 55, FontSize = 18f, Color = Color.White, TextAlign = HorizontalAlignment.Right };

            row.AddChild(name);
            row.AddChild(bar);
            row.AddChild(value);
            parent.AddChild(row);

            rows.Add(new ProfilerRow(row, name, bar, value));
        }
        return rows;
    }

    private static Label CreateLabel() => new() { Color = Color.White, FontSize = 18f };

    private static string TruncateName(string name)
    {
        name = name.Replace("System", "Sys").Replace("Manager", "Mgr");
        return name.Length <= 14 ? name : name[..14];
    }

    private static string FormatTime(double t)
    {
        var span = TimeSpan.FromSeconds(t);
        return span.TotalHours >= 1 ? span.ToString(@"hh\:mm\:ss") : span.ToString(@"mm\:ss");
    }

    private static string FormatMs(double ms)
    {
        if (ms >= 1000.0) return $"{ms / 1000.0:0.00}s";
        if (ms >= 1.0) return $"{ms:0.00}ms";
        if (ms >= 0.001) return $"{ms * 1000.0:0.0}µs";
        return "0µs";
    }
}
