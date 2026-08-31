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
    private bool _disposed;

    public Control Root { get; }

    public DebugToolsTab(IConfigurationManager cfg)
    {
        _cfg = cfg;

        var box = new BoxContainer { Orientation = Orientation.Vertical, Separation = 8 };
        Root = box;

        box.AddChild(new Label { Text = "CVars" });

        var saveLoadRow = new BoxContainer { Orientation = Orientation.Horizontal, Separation = 8 };
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
    }
}
