using System.Collections.Generic;
using System.Reflection;
using Engine.Shared.Assets;
using Engine.Shared.Audio;
using Engine.Shared.Configuration;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;
using Engine.Shared.Locale;
using Engine.Shared.Networking;
using Engine.Shared.Prototypes;
using Engine.Shared.Timing;

namespace Engine.Shared;

/// <summary>
/// Manages the content between Client and Server.
/// </summary>
public sealed class SharedContentManager
{
    public ContentSide Type { get; private set; } = ContentSide.Shared;
    private List<Assembly> _assemblies = new();
    private bool _inited = false;

    public void InitAsServer(Assembly[] assemblies)
    {
        if (_inited)
            throw new System.Exception("Shared Content manager is already inited!");

        Type = ContentSide.Server;
        _assemblies.AddRange(assemblies);
        Init();
    }

    public void InitAsClient(Assembly[] assemblies)
    {
        if (_inited)
            throw new System.Exception("Shared Content manager is already inited!");

        Type = ContentSide.Client;
        _assemblies.AddRange(assemblies);
        Init();
    }

    private void Init()
    {
        _assemblies.Add(Assembly.GetExecutingAssembly());
        IoCManager.Register<SharedResourceManager>();
        IoCManager.Register<SharedAudioManifest>();
        IoCManager.Register<IConfigurationManager, ConfigurationManager>();
        IoCManager.Register<ILocalizationManager, LocalizationManager>();
        IoCManager.Register<IPrototypeManager, PrototypeManager>();
        IoCManager.Register<IGameTiming, GameTiming>();
        IoCManager.Register<EntityManager>();
        IoCManager.Register<INetManager, NetManager>();
        // add here ioc things

        IoCManager.AutoRegister(Assembly.GetExecutingAssembly());
        _inited = true;
    }

    internal void PostInit()
    {
        IoCManager.Resolve<IConfigurationManager>().Init();
        IoCManager.Resolve<IPrototypeManager>().Load();
        IoCManager.Resolve<SharedAudioManifest>().Load();
    }

    public bool IsServer()
    {
        if (Type == ContentSide.Server)
            return true;
        return false;
    }

    public bool IsClient()
    {
        if (Type == ContentSide.Client)
            return true;
        return false;
    }

    public List<Assembly> GetAssemblies()
        => _assemblies;
}

public enum ContentSide
{
    Server,
    Shared,
    Client,
}