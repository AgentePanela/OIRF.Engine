using Engine.Client.Assets;
using Engine.Client.Scenes;
using Engine.Shared.Configuration;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;

namespace Engine.Client.UI.Debug;

/// <summary>
/// Debug tools window
/// </summary>
public sealed class DebugWindow : Window
{
    [Dependency] private readonly IAssetManager _asset = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SceneManager _sceneManager = default!;
    [Dependency] private readonly EntityManager _entManager = default!;

    private readonly DebugToolsTab _debugToolsTab;
    private readonly EntityDebugTab _entityTab;
    private readonly LightingDebugTab _lightingTab;

    public DebugWindow()
    {
        IoCManager.ResolveDependencies(this);

        Title = "Debug Tools";
        MinWidth = 760;
        MinHeight = 620;

        var tabs = new TabContainer();
        AddChild(tabs);

        var atlasTab = new AtlasDebugTab(_asset);
        _debugToolsTab = new DebugToolsTab(_cfg);
        _entityTab = new EntityDebugTab(_sceneManager, _entManager);
        _lightingTab = new LightingDebugTab();

        tabs.AddTab("Atlases", atlasTab.Root);
        tabs.AddTab("Entities", _entityTab.Root);
        tabs.AddTab("Debug Tools", _debugToolsTab.Root);
        tabs.AddTab("Lighting", _lightingTab.Root);
    }

    protected override void Update(float dt)
    {
        base.Update(dt);
        _entityTab.Update(dt);
        _lightingTab.Update(dt);
    }

    protected override void OnDispose()
    {
        _debugToolsTab.Dispose();
        _entityTab.Dispose();
        base.OnDispose();
    }
}
