using Engine.Client.Debug.Diagnostics;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.Physics.Configuration;

namespace Engine.Client.UI.Debug;

/// <summary>
/// Save/load cvars and a couple of quick gameplay toggles.
/// </summary>
public sealed class DebugToolsTab
{
    private readonly IConfigurationManager _cfg;
    private readonly CheckBox _collisionCheck;
    private readonly CheckBox _scaleCheck;
    private readonly CheckBox _gpuSyncCheck;
    private readonly Button _sweepBtn;
    private bool _disposed;

    public Control Root { get; }

    public DebugToolsTab(IConfigurationManager cfg)
    {
        _cfg = cfg;

        var box = new BoxContainer { Orientation = Orientation.Vertical, _separation = 8 };
        Root = box;

        box.AddChild(new Label { Text = "CVars" });

        var saveLoadRow = new BoxContainer { Orientation = Orientation.Horizontal, _separation = 8 };
        var saveBtn = new Button("Save CVars");
        saveBtn.OnClick += _ => _cfg.SaveConfig();
        var loadBtn = new Button("Load CVars");
        loadBtn.OnClick += _ => _cfg.LoadConfig();
        saveLoadRow.AddChild(saveBtn);
        saveLoadRow.AddChild(loadBtn);
        box.AddChild(saveLoadRow);

        _collisionCheck = new CheckBox { Text = "Show collision mask" };
        _collisionCheck.OnToggled += pressed => _cfg.Set(PhysicsCvars.CollisionMask, pressed);
        box.AddChild(_collisionCheck);

        _scaleCheck = new CheckBox { Text = "Scale outer" };
        _scaleCheck.OnToggled += pressed => _cfg.Set(GameCVars.ScaleOuter, pressed);
        box.AddChild(_scaleCheck);

        box.AddChild(new Label { Text = "Profiler" });

        _gpuSyncCheck = new CheckBox { Text = "GPU sync (inflates frame time, measures real GPU cost)" };
        _gpuSyncCheck.OnToggled += pressed => _cfg.Set(ProfilerCvars.GpuSync, pressed);
        box.AddChild(_gpuSyncCheck);

        var sweepRow = new BoxContainer { Orientation = Orientation.Horizontal, _separation = 8 };
        _sweepBtn = new Button("Run Sweep");
        _sweepBtn.OnClick += _ => GameClient.Sweep.Start();
        var dumpBtn = new Button("Dump Report (F2)");
        dumpBtn.OnClick += _ => ProfilerReport.Dump();
        sweepRow.AddChild(_sweepBtn);
        sweepRow.AddChild(dumpBtn);
        box.AddChild(sweepRow);

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
        _collisionCheck.Pressed = _cfg.Get(PhysicsCvars.CollisionMask);
        _scaleCheck.Pressed = _cfg.Get(GameCVars.ScaleOuter);
        _gpuSyncCheck.Pressed = _cfg.Get(ProfilerCvars.GpuSync);
    }
}
