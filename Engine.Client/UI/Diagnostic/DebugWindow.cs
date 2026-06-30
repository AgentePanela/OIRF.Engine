using Engine.Shared.Configuration;
using Engine.Client.Assets;

using Engine.Client.Scenes;
using Myra.Graphics2D.UI;
using Engine.Shared.IoC;
using Engine.Shared.GameObjects;

namespace Engine.Client.UI.Debug;

public sealed class DebugWindow : DefaultWindow
{
    [Dependency] private IAssetManager _asset = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SceneManager _sceneManager = default!;
    [Dependency] private EntityManager _entManager = default!;

    private TabControl _tabs = default!;
    private EntityDebugTab? _entityTab = null;
    private DebugToolsTab? _debugToolsTab = null;

    public override void BuildElements()
    {
        Title = "Debug Tools";
        Width = 700;
        Height = 600;

        SetRootType<VerticalStackPanel>();

        BuildTabs();

        base.BuildElements();
    }

    private void BuildTabs()
    {
        _tabs = new TabControl();
        var desktop = IoCManager.Resolve<UIManager>().Desktop;
        _entityTab = new EntityDebugTab(_sceneManager, _entManager);
        _debugToolsTab = new DebugToolsTab(_cfg);

        _tabs.Items.Add(new AtlasDebugTab(_asset, desktop));
        _tabs.Items.Add(_entityTab);
        _tabs.Items.Add(_debugToolsTab);

        AddElement(_tabs);
    }

    public override void Update(float dt)
    {
        base.Update(dt);
        _entityTab?.Update(dt);
    }

    public override void OnClose()
    {
        _entityTab?.Dispose();
        _debugToolsTab?.Dispose();

        _entityTab = null;
        _debugToolsTab = null;

        base.OnClose();
    }
}
