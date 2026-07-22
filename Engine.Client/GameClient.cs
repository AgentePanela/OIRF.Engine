using Apos.Shapes;
using Engine.Client.Audio;
using Engine.Client.Assets;
using Engine.Shared.Configuration;
using Engine.Client.GameObjects;
using Engine.Client.Graphics;
using Engine.Client.Graphics.Fonts;
using Engine.Client.Graphics.Shaders;
using Engine.Client.Inputs;
using Engine.Client.UI;
using Engine.Client.UI.Fonts;
using Engine.Client.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Reflection;
using Engine.Client.Debug.Diagnostics;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.IoC;
using Engine.Shared;
using Engine.Shared.Storage;
using System.Linq;
using Engine.Shared.Prototypes;
using Engine.Shared.GameObjects;
using Engine.Client.Graphics.Lighting;

namespace Engine.Client;

/// <summary>
/// Represents the current state of the game.
/// </summary>
public enum GameState
{
    /// <summary>
    /// First frame - Windows is still not rendered.
    /// </summary>
    Booting,

    /// <summary>
    /// The game is currently in the loading scene and loading all it systems and resources.
    /// </summary>
    Loading,

    /// <summary>
    /// All loading is finnished, it is safe to run anything related to content (your game code).
    /// </summary>
    Running,
}

/// <summary>
/// Engine "core" class, here you will find almost every instances.
/// </summary>
public class GameClient : Game
{
    internal static GameClient s_instance;

    /// <summary>
    /// Gets a reference to the GameClient instance.
    /// </summary>
    public static GameClient Instance => s_instance;

    /// <summary>
    /// Gets the graphics device manager to control the presentation of graphics.
    /// </summary>
    public static GraphicsDeviceManager Graphics { get; private set; }

    /// <summary>
    /// Gets the graphics device used to create graphical resources and perform primitive rendering.
    /// </summary>
    public static new GraphicsDevice GraphicsDevice { get; private set; }

    /// <summary>
    /// Gets the sprite batch used for all 2D rendering.
    /// </summary>
    public static SpriteBatch SpriteBatch { get; private set; }

    /// <summary>
    /// Gets the shapes batch used for shape rendering.
    /// </summary>
    public static ShapeBatch ShapeBatch { get; private set; }

    /// <summary>
    /// Gets the content manager used to load global assets.
    /// </summary>
    public static new ContentManager Content { get; private set; }

    public static IAssetManager Assets { get; private set; }
    public static IAudioManager Audio { get; private set; }
    public static IPrototypeManager Prototypes { get; private set; }
    public static RenderManager Renderer { get; private set; }
    public static IFontManager FontManager { get; private set; }
    public static SceneManager Scenes { get; private set; }
    public static EntityManager EntityManager { get; private set; }
    public static InputManager InputManager { get; private set; }
    public static IConfigurationManager ConfigManager { get; private set; }
    public static ViewportAdapter Viewport { get; private set; }
    public static Camera2D Camera { get; private set; }
    public static UIManager InterfaceManager { get; private set; }
    public static WindowManager WindowManager { get; private set; }
    public static GameState GameState = GameState.Booting;

    public static ClientOptions Options = new ClientOptions();
    public static GTime GameTime = new GTime();
    public GCMeter GCMeter = new();

    private bool _paused = false;

    /// <summary>
    /// Creates a new GameClient instance.
    /// </summary>
    protected GameClient(ClientOptions options)
    {
        if (s_instance != null)
        {
            throw new InvalidOperationException($"Only a single GameClient instance can be created");
        }
        s_instance = this;

        Graphics = new GraphicsDeviceManager(this);

        Window.Title = options.Title;
        Window.AllowUserResizing = options.WindowResizing;

        // Set the core's content manager to a reference of the base Game
        // content manager.
        Content = base.Content;
        Content.RootDirectory = "Content";

        IsMouseVisible = true;
        Options = options;

        // shared registers here
        IoCManager.Register(new UserStorageManager(Options.DataPath, true)); // user storage manager should be inited per side, instead of in shared
        IoCManager.Register<SharedContentManager>();
        var sharedContent = IoCManager.Resolve<SharedContentManager>();
        var list = Options.Assemblies.ToList();
        list.Add(Assembly.GetExecutingAssembly());
        sharedContent.InitAsClient(list.ToArray());

        //
        IoCManager.Register<IAssetManager, AssetManager>();
        IoCManager.Register<IAudioManager, AudioManager>();
        IoCManager.Register<ShaderManager>();

        // Text/font services
        IoCManager.Register<TextStyleLibrary>();
        IoCManager.Register<IFontManager, FontManager>();
        IoCManager.Register<TextLayoutService>();
        IoCManager.Register<MyraFontBridge>();

        IoCManager.Register(new SceneManager(this));
        IoCManager.Register<ViewportAdapter>();
        IoCManager.Register<Camera2D>();
        IoCManager.Register<LightingManager>();
        IoCManager.Register<RenderManager>();
        IoCManager.Register<InputManager>();
        IoCManager.Register<UIManager>();
        IoCManager.Register<WindowManager>();

        IoCManager.AutoRegister(Assembly.GetExecutingAssembly());

        Assets = IoCManager.Resolve<IAssetManager>();
        Audio = IoCManager.Resolve<IAudioManager>();
        Prototypes = IoCManager.Resolve<IPrototypeManager>();
        FontManager = IoCManager.Resolve<IFontManager>();
        Renderer = IoCManager.Resolve<RenderManager>();
        Scenes = IoCManager.Resolve<SceneManager>();
        EntityManager = IoCManager.Resolve<EntityManager>();
        InputManager = IoCManager.Resolve<InputManager>();
        Viewport = IoCManager.Resolve<ViewportAdapter>();
        Camera = IoCManager.Resolve<Camera2D>();
        InterfaceManager = IoCManager.Resolve<UIManager>();
        WindowManager = IoCManager.Resolve<WindowManager>();
        ConfigManager = IoCManager.Resolve<IConfigurationManager>();

        ConfigManager.ForceDefaultValue(GameCVars.GameVersion, Options.Version);
        ConfigManager.ForceDefaultValue(GameCVars.ResolutionWidth, Options.Width);
        ConfigManager.ForceDefaultValue(GameCVars.ResolutionHeight, options.Height);

        BeforeInit();

        sharedContent.PostInit();
        InputManager.Init();
        
        var display = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        if (Options.Width > display.Width || Options.Height > display.Height)
        {
            // change the default screen size if the monitor is smaller than the game size.
            ConfigManager.ForceDefaultValue(GameCVars.ResolutionWidth, display.Width);
            ConfigManager.ForceDefaultValue(GameCVars.ResolutionHeight, display.Height);
        }

        Options.Width = ConfigManager.Get(GameCVars.ResolutionWidth);
        Options.Height = ConfigManager.Get(GameCVars.ResolutionHeight);
        Viewport.Init();

        // Set the graphics defaults.
        Graphics.PreferredBackBufferWidth = options.Width;
        Graphics.PreferredBackBufferHeight = options.Height;
        Graphics.IsFullScreen = options.FullScreen;
        Graphics.GraphicsProfile = GraphicsProfile.HiDef; // required by Apos.Shapes

        // Apply the graphic presentation changes.
        Graphics.ApplyChanges();

        Deactivated += (_, _) =>
        {
            if (!Options.PauseOnUnfocus)
                return;
            _paused = true;
            Log.Debug("Focus loosed > Game paused.");
        };

        Activated += (_, _) =>
        {
            if (!Options.PauseOnUnfocus)
                return;
            _paused = false;
            Log.Debug("Focus gained > Game unpaused.");
        };

        Window.ClientSizeChanged += (_, _) => OnClientSizeChanged();

        Exiting += OnClientShutdown;
    }

    private void OnClientShutdown(object? sender, ExitingEventArgs e)
    {
        EntityManager.OnShutdown();
        if (Options.SaveConfigOnExit)
            ConfigManager.SaveConfig();
    }

    private void OnClientSizeChanged()
    {
        if (Window.ClientBounds.Width <= 0 || Window.ClientBounds.Height <= 0)
            return;

        var width = Window.ClientBounds.Width;
        var height = Window.ClientBounds.Height;

        Graphics.PreferredBackBufferWidth = width;
        Graphics.PreferredBackBufferHeight = height;
        GraphicsDevice.PresentationParameters.BackBufferWidth = width;
        GraphicsDevice.PresentationParameters.BackBufferHeight = height;

        //Graphics.ApplyChanges(); - this causes the viewport offset bug

        if (Viewport != null)
            Viewport.UpdateScaleMatrix();

        InterfaceManager.Resize();
        WindowManager.Resize();

        SuppressDraw();
    }

    /// <summary>
    /// Called right before almost all systems init, right after ioc registrations
    /// </summary>
    protected virtual void BeforeInit()
    {
        
    }

    protected override void Initialize()
    {
        // Set the core's graphics device to a reference of the base Game
        // graphics device.
        GraphicsDevice = base.GraphicsDevice;

        // Create the sprite batch instance.
        SpriteBatch = new SpriteBatch(GraphicsDevice);
        ShapeBatch = new ShapeBatch(GraphicsDevice);
        Renderer.UpdateBatch(SpriteBatch, ShapeBatch);
        InterfaceManager.Init();
        Components.Add(Scenes);

        base.Initialize();
    }

    protected override void LoadContent()
    {
        // TODO: use this.Content to load your game content here
        IoCManager.Resolve<ShaderManager>().Init();
    }

    private int _gen0 = 0;
    private int _gen1 = 0;
    private int _gen2 = 0;
    private long _gcBytes = 0;
    protected override void Update(GameTime gameTime)
    {
        if (_paused)
            return;
        
        int agen0 = GC.CollectionCount(0);
        int agen1 = GC.CollectionCount(1);
        int agen2 = GC.CollectionCount(2);
        var agcBytes = GC.GetAllocatedBytesForCurrentThread();
        GCMeter.allocatedBytes = agcBytes - _gcBytes;
        GCMeter.gen0 = agen0 - _gen0;
        GCMeter.gen1 = agen1 - _gen1;
        GCMeter.gen2 = agen2 - _gen2;

        GameTime.UpdateDelta(gameTime);
        InputManager.Update(IsActive);
        Audio.Update(GameTime.DeltaTime);
        base.Update(gameTime);
        Assets.Update(gameTime);
        float uiScreenDeltaTime = _paused ? 0f : GameTime.DeltaTime;

        InterfaceManager.Update(uiScreenDeltaTime);
        WindowManager.Update(GameTime.DeltaTime);

        // why do we even use this
        if (GameState == GameState.Booting)
            GameState = GameState.Loading;

        if (GameState != GameState.Running)
            return;

        float simulationDeltaTime = _paused ? 0f : GameTime.DeltaTime;
        EntityManager.Update(simulationDeltaTime);

        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        _gen2 = GC.CollectionCount(2);
        _gcBytes = GC.GetAllocatedBytesForCurrentThread();
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_paused)
            return;

        GraphicsDevice.Clear(Scenes.CurrentScene?.BackgroundColor ?? Options.BackgroundColor);
        GameTime.UpdateFps(gameTime);

        /// estou passando isso pra cá, nao sei se é correto e está muito
        /// de tarde no momento q to escrevendo pra procurar,
        /// mas parecisava que o SceneManager recebesse a chamada de draw antes do renderer começar a
        /// realmente desenhar algo, e como o scene manager é um draw game component, ele so 
        /// recebe essa chamada quando ocoore o base.Draw();
        base.Draw(gameTime);

        // Lock the backbuffer viewport to the letterboxed rect before any
        // world draw. The lighting SceneTarget path in Renderer.DrawQueue
        // bypasses Begin on-backbuffer branch, so without this
        // LastBackbufferViewport ends up as the full backbuffer and the
        // apply pass stretches the (uniformly-scaled) SceneTarget to fill
        // it, producing a vertical squash whenever the window aspect ratio
        // doesn't match the virtual resolution.
        if (Renderer.Resizing)
        {
            if (Renderer.FinalTarget is not null)
                Renderer.SetFullViewport(Renderer.FinalTarget.Width, Renderer.FinalTarget.Height);
            else
                Renderer.SetLetterboxedBackbufferViewport();
        }

        if (GameState == GameState.Running)
        {
            Camera.CacheFrame();

            // Allocate (or resize) the offscreen scene target. The world is
            // drawn into this target first so the lighting pass can sample it
            // and blend the lightmap on top in Renderer.DrawQueue().
            Renderer.EnsureSceneTarget(Viewport.VirtualWidth, Viewport.VirtualHeight);

            EntityManager.Draw(GameTime.DeltaTime);
        }

        Renderer.DrawQueue();

        if (GameState == GameState.Running)
        {
            var lightingSystem = EntityManager.GetSystem<LightingSystem>();
            lightingSystem?.ApplyAfterWorld();
        }

        //Renderer.End();

        // restore the viewport before drawing the UI
        GraphicsDevice.Viewport = Renderer.FinalTarget is not null
            ? new Viewport(0, 0, Renderer.FinalTarget.Width, Renderer.FinalTarget.Height)
            : new Viewport(
                0,
                0,
                GraphicsDevice.PresentationParameters.BackBufferWidth,
                GraphicsDevice.PresentationParameters.BackBufferHeight
            );

        // A captured frame (Renderer.FinalTarget set) shouldn't bake in Myra UI.
        if (Renderer.FinalTarget is null)
        {
            InterfaceManager.Draw(GameTime.DeltaTime);
            WindowManager.Draw(GameTime.DeltaTime);
        }

        //base.Draw(gameTime);
    }
}
