using Engine.Client.Assets;
using Engine.Client.Scenes.Factories;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.IoC;
using Engine.Client.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.GameObjects;
using Engine.Shared.Assets;

namespace Engine.Client.Scenes;

/// <summary>
/// Handles game ENTIRE loading logic
/// </summary>
public class DefaultLoadingScene : Scene
{
    [Dependency] private readonly SceneFactory _sceneFac = default!;
    [Dependency] private readonly ComponentFactory _compFac = default!;
    [Dependency] private readonly EntityManager _entMan = default!;
    [Dependency] private readonly IFontManager _fonts = default!;
    [Dependency] private readonly TextLayoutService _textLayout = default!;

    private Label2D _label;
    private Vector2 _labelPos;
    private string _loadingFlavour = string.Empty;
    private TextureRect _logo;
    private Vector2 _logoPos;
    protected LoadingState _state = LoadingState.Registry;

    public override UICanvas? DefaultCanvas { get; protected set; } = null;

    private Task? _loadingTask;
    private bool _startedLoading = false;

    public override void OnSceneStart()
    {
        base.OnSceneStart();

        _fonts.BootstrapDefaults(_content);

        _loadingFlavour = Loc.GetString("engine-loading-flavour-default");

        var logoPath = Path.Combine(SharedResourceManager.GetMainResourcesFolder(), "Textures", "Interface");
        _logo = new TextureRect(Texture2D.FromFile(GameClient.GraphicsDevice, Path.Combine(logoPath, "Logo.png"))); // we dont have asset manager ready lol
        _logo.Origin = new Vector2(
            _logo.Texture.Width / 2,
            _logo.Texture.Height
        );
        _logo.Scale = new Vector2(0.8f);

        _asset.OnLoadingCompleted += LoadingCompleted;

        _logoPos = new Vector2(
            GameClient.Options.Width / 2,
            (GameClient.Options.Height / 2) + (_logo.Texture.Height / 4)
        );
        _labelPos = new Vector2(
            GameClient.Options.Width / 2,
            (GameClient.Options.Height / 2) + (_logo.Texture.Height / 4) + 20
        );

        _label = new Label2D(
            TextStyle.Loading,
            _loadingFlavour,
            Vector2.Zero,
            0f,
            Vector2.One,
            Color.White,
            0f
        );

        _renderer.Resizing = false; // disable resizing for a better look
        _renderer.BlendState = BlendState.NonPremultiplied; // as we are using texture2d from files the alpha is fucked up so we need to change the blend state
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        if (_state == LoadingState.Done)
            _loadingFlavour = Loc.GetString("engine-loading-flavour-done");

        if (_state == LoadingState.AssetManager)
        {
            _asset.UpdateLoading(null); // will load 30 textures each call and bake the atlas in the last call
            var asset = IoCManager.Resolve<IAssetManager>() as AssetManager;

            _loadingFlavour = Loc.GetString("engine-loading-flavour-asset",
                ("textures", $"{asset?.initialPendingSprites - asset?._pending.Count}"), ("maxTextures", $"{asset?.initialPendingSprites}"));
        }

        // fun fact: if this get called two times or more by some perf issue
        // the whole loading logic will get fucked
        if (_state == LoadingState.Registry && !_startedLoading)
        {
            _startedLoading = true;

            _loadingFlavour = Loc.GetString("engine-loading-flavour-registry");

            _loadingTask = Task.Run(() =>
            {
                _sceneFac.LoadScenes();
                _compFac.LoadComponents();
                
                _entMan.Init();
                _entMan.RegisterSystems();

                _loadingFlavour = Loc.GetString("engine-loading-flavour-rawtex-load");

                _asset.Init(GameClient.GraphicsDevice, GameClient.SpriteBatch);
            });
        }

        if (_loadingTask != null && _loadingTask.IsCompleted)
            _state = LoadingState.AssetManager;

        _label.String = _loadingFlavour;
        _label.Origin = _textLayout.GetCenteredOrigin(_label);
    }

    public override void Draw(float dt)
    {
        base.Draw(dt);
        _renderer.Submit(_label, _labelPos);
        _renderer.Submit(_logo, _logoPos);
    }

    private void LoadingCompleted()
    {
        _state = LoadingState.Done;
        GameClient.GameState = GameState.Running;
        Log.Debug("GameState: Loading > Running!");
        #if !DEBUG
        Thread.Sleep((int)(1.5 * 1000)); // loading is currently too fast </3
        #endif
        var ops = GameClient.Options;
        if (!ops.InitialScene.IsSubclassOf(typeof(Scene)))
            throw new System.Exception("Initial scene is not a scene!");
        var ins = Activator.CreateInstance(ops.InitialScene) as Scene;
        if (ins is null)
            throw new NullReferenceException("Initial scene type instance is invalid/null.");
        _scene.ChangeScene(ins);

        _logo.Texture.Dispose();
        _renderer.Resizing = true;
        _renderer.BlendState = BlendState.AlphaBlend;

        _entMan.EventBus.RaiseEvent(new LoadingFinishedEvent());
    }

    public override void OnSceneEnd()
    {
        base.OnSceneEnd();
    }

    protected enum LoadingState
    {
        Registry,
        AssetManager,
        Done
    }
}

/// <summary>
/// Raised when the game loading is finished, so systems can make a good use of it.
/// </summary>
public class LoadingFinishedEvent : EntityEvent
{
    
}
