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
//using Engine.Client.UI.Fonts;
using Engine.Client.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Diagnostics;
using System.Reflection;
using Engine.Client.Debug.Diagnostics;
using Engine.Shared.Debug.Diagnostics;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.IoC;
using Engine.Shared;
using Engine.Shared.Storage;
using System.Linq;
using Engine.Shared.Prototypes;
using Engine.Shared.GameObjects;
using Engine.Client.Graphics.Lighting;
using Engine.Shared.Locale;
using Engine.Shared.Threading;
using System.IO;
using Microsoft.Xna.Framework.Input;

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
    public static FrameProfiler Profiler { get; private set; }
    public static ProfilerSweep Sweep { get; private set; }
    public static RenderStats RenderStats { get; private set; }
    public static WindowManager WindowManager { get; private set; }
    public static ILocalizationManager LocalizationManager { get; private set; }
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

        IsMouseVisible = true;
        Options = options;

        // shared registers here
        IoCManager.Register(new UserStorageManager(Options.DataPath, !Options.SelfContainedDataPath)); // user storage manager should be inited per side, instead of in shared
        IoCManager.Register<SharedContentManager>();
        var sharedContent = IoCManager.Resolve<SharedContentManager>();
        var list = Options.Assemblies.ToList();
        list.Add(Assembly.GetExecutingAssembly());
        sharedContent.InitAsClient(list.ToArray());

        //
        IoCManager.Register<IAssetManager, AssetManager>();
        IoCManager.Register<IAudioManager, AudioManager>();
        IoCManager.Register<ShaderManager>();
        IoCManager.Register<IParallelManager, ParallelManager>();

        // Text/font services
        IoCManager.Register<IFontManager, FontManager>();
        //!REMOVE IoCManager.Register<MyraFontBridge>();

        IoCManager.Register(new SceneManager(this));
        IoCManager.Register<ViewportAdapter>();
        IoCManager.Register<Camera2D>();
        IoCManager.Register<LightingManager>();
        IoCManager.Register<RenderStats>();
        IoCManager.Register<RenderManager>();
        IoCManager.Register<InputManager>();
        IoCManager.Register<IVirtualKeyboard, NullVirtualKeyboard>(); // platform-specific mobile backends override this
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
        Profiler = IoCManager.Resolve<FrameProfiler>();
        RenderStats = IoCManager.Resolve<RenderStats>();
        Profiler.GpuFlush = GpuSync.Flush;
        Sweep = IoCManager.Resolve<ProfilerSweep>();
        WindowManager = IoCManager.Resolve<WindowManager>();
        ConfigManager = IoCManager.Resolve<IConfigurationManager>();
        LocalizationManager = IoCManager.Resolve<ILocalizationManager>();

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
        Graphics.GraphicsProfile = options.GraphicsProfile;
        // Graphics.SynchronizeWithVerticalRetrace = false;
        // IsFixedTimeStep = false;

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
        MainThread.Capture();

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
        //IoCManager.Resolve<ShaderManager>().Init(); - now in loading scene
    }

    private long _lastDrawEnd;
    private bool _dumpAfterSweep;
    protected override void Update(GameTime gameTime)
    {
        if (_paused)
            return;

        // everything between the end of the last Draw and here is Present,
        // vsync wait and driver blocking - the tell for a GPU bound frame.
        // Skipped during a sweep: vsync is off and passes are being toggled on
        // purpose, so this would just pollute the rolling average with noise.
        if (_lastDrawEnd != 0 && !Sweep.Running)
            Profiler.RecordPresentWait((Stopwatch.GetTimestamp() - _lastDrawEnd) * 1000.0 / Stopwatch.Frequency);

        // Frozen during a sweep for the same reason: BeginFrame/EndFrame is what
        // feeds the rolling windows, and a sweep frame has whole passes forced
        // off on purpose. Scopes opened while frozen (_inFrame stays false) are
        // no-ops, so nothing gets recorded until real frames resume.
        if (!Sweep.Running)
            Profiler.BeginFrame();

        // GC counters bracket this Update() call specifically, not the gap
        // since the last one (which used to fold Draw()'s allocations in here).
        int genBefore0 = GC.CollectionCount(0);
        int genBefore1 = GC.CollectionCount(1);
        int genBefore2 = GC.CollectionCount(2);
        var bytesBefore = GC.GetAllocatedBytesForCurrentThread();

        void CommitGCDelta()
        {
            GCMeter.gen0 = GC.CollectionCount(0) - genBefore0;
            GCMeter.gen1 = GC.CollectionCount(1) - genBefore1;
            GCMeter.gen2 = GC.CollectionCount(2) - genBefore2;
            GCMeter.allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
        }

        GameTime.UpdateDelta(gameTime);

        using (Profiler.Scope("update/input"))
            InputManager.Update(IsActive);

        using (Profiler.Scope("update/scenes"))
            base.Update(gameTime);

        using (Profiler.Scope("update/assets"))
            Assets.Update(gameTime);

        using (Profiler.Scope("update/prototypes"))
            Prototypes.Update();

        using (Profiler.Scope("update/locale"))
            LocalizationManager.Update();

        float uiScreenDeltaTime = _paused ? 0f : GameTime.DeltaTime;

        using (Profiler.Scope("update/ui"))
            InterfaceManager.Update(uiScreenDeltaTime);

        using (Profiler.Scope("update/windows"))
            WindowManager.Update(GameTime.DeltaTime);

        // why do we even use this
        if (GameState == GameState.Booting)
            GameState = GameState.Loading;

        if (GameState != GameState.Running)
        {
            CommitGCDelta();
            return;
        }

        float simulationDeltaTime = _paused ? 0f : GameTime.DeltaTime;

        using (Profiler.Scope("update/ecs"))
            EntityManager.Update(simulationDeltaTime);

        // after ECS so AudioSystem sees FinishedStreaming before packages get disposed here
        using (Profiler.Scope("update/audio"))
            Audio.Update(GameTime.DeltaTime);

        CommitGCDelta();

        if (_dumpAfterSweep && !Sweep.Running)
        {
            _dumpAfterSweep = false;
            WriteReport();
            ConfigManager.Set(EngineCvars.ProfilerGpuSync, false);
        }

        if (InputManager.KeyPressed(Keys.F2))
            DumpProfilerReport();
    }

    private void DumpProfilerReport()
    {
        var withSweep = InputManager.KeyDown(Keys.LeftShift) || InputManager.KeyDown(Keys.RightShift);

        if (withSweep && !Sweep.Running)
        {
            ConfigManager.Set(EngineCvars.ProfilerGpuSync, true);
            Log.Debug("Running the profiler sweep, hold on...");
            Sweep.Start();
            _dumpAfterSweep = true;
            return;
        }

        WriteReport();
    }

    private void WriteReport()
    {
        var path = ProfilerReport.Dump("FORCED-REPORT");
        if (path is not null)
            Log.Debug($"Profiler report written to {path}");
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_paused)
            return;

        // Same freeze as Profiler.BeginFrame in Update(): a sweep frame has
        // whole passes forced off on purpose and must not dilute the windows
        // that a report reads from.
        if (!Sweep.Running)
            RenderStats.BeginFrame();

        using (Profiler.GpuScope("draw/clear"))
            GraphicsDevice.Clear(Scenes.CurrentScene?.BackgroundColor ?? Options.BackgroundColor);

        GameTime.UpdateFps(gameTime);

        /// estou passando isso pra cá, nao sei se é correto e está muito
        /// de tarde no momento q to escrevendo pra procurar,
        /// mas parecisava que o SceneManager recebesse a chamada de draw antes do renderer começar a
        /// realmente desenhar algo, e como o scene manager é um draw game component, ele so 
        /// recebe essa chamada quando ocoore o base.Draw();
        using (Profiler.Scope("draw/scenes"))
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
            // and blend the lightmap on top in Renderer.DrawQueue(). Sized to
            // FinalTarget when one's active (editor viewport panel) rather than
            // the virtual viewport - otherwise this target's aspect ratio locks
            // to the OS window while the final blit's destination rect is
            // whatever the docked panel happens to be, and LightingSystem's
            // ApplyAfterWorld fullscreen-quads one onto the other, stretching
            // the whole scene to match. Matching the aspect here up front means
            // that blit is always a same-shape copy.
            var (sceneTargetWidth, sceneTargetHeight) = Renderer.FinalTarget is { } finalTarget
                ? (finalTarget.Width, finalTarget.Height)
                : (Viewport.VirtualWidth, Viewport.VirtualHeight);
            Renderer.EnsureSceneTarget(sceneTargetWidth, sceneTargetHeight);

            if (!ProfilerSweep.SkipWorld)
            {
                using var _ = Profiler.Scope("draw/ecs.submit");
                EntityManager.Draw(GameTime.DeltaTime);
            }
        }

        using (Profiler.Scope("draw/render.queue"))
            Renderer.DrawQueue();

        if (GameState == GameState.Running)
        {
            var lightingSystem = EntityManager.GetSystem<LightingSystem>();
            using (Profiler.GpuScope("draw/lighting.apply"))
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
        if (Renderer.FinalTarget is null && !ProfilerSweep.SkipUi)
        {
            using var _ = Profiler.GpuScope("draw/ui");
            InterfaceManager.Draw(GameTime.DeltaTime);
        }

        if (!Sweep.Running)
        {
            RenderStats.EndFrame();
            Profiler.EndFrame();
        }

        // Tick always runs - it's what drives the sweep itself, timed off its
        // own stopwatch rather than the frozen Profiler.
        Sweep.Tick();
        _lastDrawEnd = Stopwatch.GetTimestamp();
    }
}
