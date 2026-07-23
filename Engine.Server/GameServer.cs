using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Engine.Server.CVars;
using Engine.Shared;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.IoC;
using Engine.Shared.Locale;
using Engine.Shared.Prototypes;
using Engine.Shared.Storage;

namespace Engine.Server;

/// <summary>
/// Represents the current state of the server.
/// </summary>
public enum ServerState
{
    /// <summary>
    /// Server is initializing (IoC, shared systems, etc).
    /// </summary>
    Booting,

    /// <summary>
    /// Server is fully initialized and running the tick loop.
    /// </summary>
    Running,

    /// <summary>
    /// Server is shutting down.
    /// </summary>
    ShuttingDown,
}

/// <summary>
/// Headless game server. Manages the server tick loop
/// </summary>
public class GameServer : IDisposable
{
    private static GameServer? s_instance;

    /// <summary>
    /// Gets the current server instance.
    /// </summary>
    public static GameServer? Instance => s_instance;

    /// <summary>
    /// Server configuration options.
    /// </summary>
    public ServerOptions Options { get; }

    /// <summary>
    /// Current server state.
    /// </summary>
    public ServerState State { get; private set; } = ServerState.Booting;

    public EntityManager EntityManager { get; private set; } = default!;
    public IConfigurationManager ConfigManager { get; private set; } = default!;
    public IPrototypeManager Prototypes { get; private set; } = default!;
    public ILocalizationManager LocalizationManager { get; private set; } = default!;

    private readonly Stopwatch _tickWatch = new();
    private bool _running;
    private CancellationTokenSource? _cts;

    private EntityRoom? _room; // todo: RoomManager

    /// <summary>
    /// Creates a new headless server instance.
    /// And initialize all shared systems (IoC, ECS, Config, Prototypes, Storage).
    /// </summary>
    protected GameServer(ServerOptions options)
    {
        if (s_instance != null)
            throw new InvalidOperationException("Only a single ServerClient instance can be created.");

        s_instance = this;
        Options = options;

        Log.Debug("ServerState: Booting...");
        IoCManager.Register(new UserStorageManager(Options.DataPath, false));

        // Register and init shared content manager
        IoCManager.Register<SharedContentManager>();
        var sharedContent = IoCManager.Resolve<SharedContentManager>();

        var assemblies = Options.Assemblies.ToList();
        assemblies.Add(Assembly.GetExecutingAssembly());
        sharedContent.InitAsServer(assemblies.ToArray());

        EntityManager = IoCManager.Resolve<EntityManager>();
        ConfigManager = IoCManager.Resolve<IConfigurationManager>();
        Prototypes = IoCManager.Resolve<IPrototypeManager>();
        LocalizationManager = IoCManager.Resolve<ILocalizationManager>();

        IoCManager.AutoRegister(Assembly.GetExecutingAssembly());

        // Force default CVars
        ConfigManager.ForceDefaultValue(GameCVars.GameVersion, Options.Version);
        ConfigManager.ForceDefaultValue(ServerCVars.Port, Options.Port);
        ConfigManager.ForceDefaultValue(ServerCVars.ServerName, Options.ServerName);

        // Post-init shared systems
        PrototypesToIgnore();
        sharedContent.PostInit();
        Initialize();
        
        _room = new EntityRoom(); // todo: RoomManager
        EntityManager.ForceScene(_room);

        State = ServerState.Running;
        Log.Debug("ServerState: Loading > Running!");
    }

    /// <summary>
    /// Load all systems and others. (equivalent of LoadingScene in client but sync)
    /// </summary>
    protected virtual void Initialize()
    {
        IoCManager.Resolve<ComponentFactory>().LoadComponents();
                
        EntityManager.Init();
        EntityManager.RegisterSystems();
    }

    /// <summary>
    /// Add here your prototypes to be ignored via Prototypes.IgnorePrototypes([]);
    /// </summary>
    protected virtual void PrototypesToIgnore()
    {
        Prototypes.IgnorePrototypes(["inputMap"]); //todo: improve this
    }

    /// <summary>
    /// Start and run the blocking server tick loop.
    /// </summary>
    public void Run()
    {
        _cts = new CancellationTokenSource();
        _running = true;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _cts.Cancel();
        };

        var tickInterval = TimeSpan.FromSeconds(1.0 / Options.TickRate);
        long lastTickMs = 0;

        Log.Debug("============================================");
        Log.Debug("Server is now running! Press Ctrl+C to stop.");

        _tickWatch.Start();

        try
        {
            while (_running && !_cts.IsCancellationRequested)
            {
                var now = _tickWatch.ElapsedMilliseconds;
                var deltaTime = (now - lastTickMs) / 1000f;
                lastTickMs = now;

                Prototypes.Update();
                Update(deltaTime);

                // Sleep to maintain tick rate
                var elapsed = _tickWatch.ElapsedMilliseconds - now;
                var sleepMs = (int)(tickInterval.TotalMilliseconds - elapsed);
                if (sleepMs > 0)
                    Thread.Sleep(sleepMs);
            }
        }
        finally
        {
            OnShutdown();
        }
    }

    /// <summary>
    /// Called every server tick.
    /// Override to add custom server-side update logic.
    /// </summary>
    protected virtual void Update(float deltaTime)
    {
        // Update all entity systems
        EntityManager.Update(deltaTime);
        Prototypes.Update();
        LocalizationManager.Update();
    }

    /// <summary>
    /// Request a graceful server shutdown.
    /// </summary>
    public void Shutdown()
    {
        Log.Debug("Shutdown requested...");
        _running = false;
        _cts?.Cancel();
    }

    /// <summary>
    /// Called during shutdown. Override for custom cleanup.
    /// </summary>
    protected virtual void OnShutdown()
    {
        State = ServerState.ShuttingDown;
        Log.Debug("Server shutting down...");

        EntityManager.OnShutdown();

        if (Options.SaveConfigOnExit)
            ConfigManager.SaveConfig();

        Log.Debug("Server shut down.");
    }

    public void Dispose()
    {
        if (_running)
            Shutdown();

        _cts?.Dispose();
        s_instance = null;
    }
}
