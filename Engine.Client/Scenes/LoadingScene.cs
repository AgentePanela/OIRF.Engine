using System.IO;
using System.Threading.Tasks;
using Engine.Client.Extensions;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Graphics.Shaders;
using Engine.Client.Scenes.Factories;
using Engine.ResourcesBuilder;
using Engine.Shared.Assets;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.IoC;
using Engine.Shared.Storage;
using Engine.Shared.Threading;
using Microsoft.Xna.Framework.Content.Pipeline;
using MonoGame.Framework.Utilities;
using static System.Environment;

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
    [Dependency] protected readonly UserStorageManager _storage = default!;
    [Dependency] protected readonly SharedResourceManager _resMan = default!;
    protected Task? _registryTask;
    protected Task? _shaderTask;

    protected bool _autoStartLoading = true;

    protected LoadingState _state = LoadingState.Idle;
    
    protected enum LoadingState
    {
        Idle,
        TextureLoading,
        Shaders,
        Registry,
        Done
    }

    public override void OnSceneStart()
    {
        base.OnSceneStart();
        if (_autoStartLoading)
            StartLoading();

        _asset.OnLoadingCompleted += () => _state = LoadingState.Shaders;
    }

    public override void Update(float dt)
    {
        base.Update(dt);
        switch (_state)
        {
            case LoadingState.TextureLoading:
                TexturesPhase(dt);
                break;
            case LoadingState.Shaders:
                ShadersPhase();
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
        _audio.Init();
        Log.Debug("LoadingState = TextureLoading.");
        _state = LoadingState.TextureLoading;
    }

    protected virtual void ShadersPhase()
    {
        if (_shaderTask?.IsCompleted == true)
        {
            _state = LoadingState.Registry;
            return;
        }

        if (_shaderTask is not null)
            return;

        Log.Debug("LoadingState = Shaders.");
        _shaderTask = Task.Run(() =>
        {
            var platform = PlatformInfo.MonoGamePlatform.ToTargetPlatform();

            // skip running the content-pipeline compiler on-device entirely.
            if (ShaderBuilder.HasPrecompiled(platform))
            {
                var precompiledDir = ShaderBuilder.GetPrecompiledDirectory(platform);
                Log.Debug($"Using precompiled shaders from {precompiledDir}");
                GameClient.Content.RootDirectory = precompiledDir;
            }
            else
            {
                var cachePath = _storage.GetFullPath("shaders");
                var profile = GameClient.Graphics.GraphicsProfile;
                var resources = _resMan.GetResourcesFolders();
                GameClient.Content.RootDirectory = cachePath;

                Log.Debug($"Caching shaders in {cachePath}");
                ShaderBuilder.Build(platform, profile, resources, cachePath);
            }

            IoCManager.Resolve<ShaderManager>().Init();
        });
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
            //required to run safe with threading.
            var threadId = CurrentManagedThreadId;
            MainThread.SafeThreads.Add(threadId);
            try
            {
                _sceneFac.LoadScenes();
                _compFac.LoadComponents();

                _entMan.Init();
                _entMan.RegisterSystems();
            }
            finally
            {
                MainThread.SafeThreads.Remove(threadId);
            }
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