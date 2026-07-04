using System.Threading.Tasks;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Scenes.Factories;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Factories;

namespace Engine.Client.Scenes;

/// <summary>
/// Base class for loading scenes. This handles everything related to loading the base game.
/// </summary>
public abstract class LoadingScene : Scene
{
    [Dependency] protected readonly SceneFactory _sceneFac = default!;
    [Dependency] protected readonly ComponentFactory _compFac = default!;
    [Dependency] protected readonly EntityManager _entMan = default!;
    [Dependency] protected readonly IFontManager _fonts = default!;
    [Dependency] protected readonly TextLayoutService _textLayout = default!;
    protected Task? _registryTask;

    protected bool _autoStartLoading = true;

    protected LoadingState _state = LoadingState.Idle;
    
    protected enum LoadingState
    {
        Idle,
        TextureLoading,
        Registry,
        Done
    }

    public override void OnSceneStart()
    {
        base.OnSceneStart();
        if (_autoStartLoading)
            StartLoading();

        _asset.OnLoadingCompleted += () => _state = LoadingState.Registry;
    }

    public override void Update(float dt)
    {
        base.Update(dt);
        switch (_state)
        {
            case LoadingState.TextureLoading:
                TexturesPhase(dt);
                break;
            case LoadingState.Registry:
                RegistryPhase();
                break;
            case LoadingState.Done:
                LoadingCompleted();
                break;
        }
    }

    protected virtual void StartLoading()
    {
        _asset.Init(GameClient.GraphicsDevice, GameClient.SpriteBatch);
        _fonts.BootstrapDefaults(_content);
        Log.Debug("LoadingState = TextureLoading.");
        _state = LoadingState.TextureLoading;
    }

    protected virtual void RegistryPhase()
    {
        if (_registryTask?.IsCompleted == true)
        {
            _state = LoadingState.Done;
            Log.Debug("LoadingState = Done.");
            return;
        }

        if (_registryTask is not null)
            return;

        Log.Debug("LoadingState = Registry.");
        _registryTask = Task.Run(() =>
        {
            _sceneFac.LoadScenes();
            _compFac.LoadComponents();
                
            _entMan.Init();
            _entMan.RegisterSystems();
        });
    }

    protected virtual void TexturesPhase(float dt)
    {
        _asset.UpdateLoading(null);
    }

    private bool _completed = false;
    protected virtual void LoadingCompleted()
    {
        if (_completed)
            return;
        
        _completed = true;
        _state = LoadingState.Done;
        GameClient.GameState = GameState.Running;
        Log.Debug("GameState: Loading > Running!");

        _entMan.EventBus.RaiseEvent(new LoadingFinishedEvent());
    }
}

/// <summary>
/// Raised when the game loading is finished, so systems can make a good use of it.
/// </summary>
public class LoadingFinishedEvent : EntityEvent
{
    
}