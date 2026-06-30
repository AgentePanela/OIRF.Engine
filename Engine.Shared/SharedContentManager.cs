using System.Collections.Generic;
using System.Reflection;
using Engine.Shared.Assets;
using Engine.Shared.Configuration;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;
using Engine.Shared.Locale;
using Engine.Shared.Prototypes;

namespace Engine.Shared;

/// <summary>
/// Manages the content between Client and Server.
/// </summary>
public sealed class SharedContentManager
{
    public ContentType Type { get; private set; } = ContentType.Shared;
    private List<Assembly> _assemblies = new();
    private bool _inited = false;

    public void InitAsServer(Assembly[] assemblies)
    {
        if (_inited)
            throw new System.Exception("Shared Content manager is already inited!");

        Type = ContentType.Server;
        _assemblies.AddRange(assemblies);
        Init();
    }

    public void InitAsClient(Assembly[] assemblies)
    {
        if (_inited)
            throw new System.Exception("Shared Content manager is already inited!");

        Type = ContentType.Client;
        _assemblies.AddRange(assemblies);
        Init();
    }

    private void Init()
    {
        _assemblies.Add(Assembly.GetExecutingAssembly());
        IoCManager.Register<SharedResourceManager>();
        IoCManager.Register<IConfigurationManager, ConfigurationManager>();
        IoCManager.Register<ILocalizationManager, LocalizationManager>();
        IoCManager.Register<IPrototypeManager, PrototypeManager>();
        IoCManager.Register<EntityManager>();
        // add here ioc things

        IoCManager.AutoRegister(Assembly.GetExecutingAssembly());
        _inited = true;
    }

    internal void PostInit()
    {
        IoCManager.Resolve<IConfigurationManager>().Init();
        IoCManager.Resolve<IPrototypeManager>().Load();

    }

    public bool IsServer()
    {
        if (Type == ContentType.Server)
            return true;
        return false;
    }

    public bool IsClient()
    {
        if (Type == ContentType.Client)
            return true;
        return false;
    }

    public List<Assembly> GetAssemblies()
        => _assemblies;

    public enum ContentType
    {
        Server,
        Shared,
        Client,
    }
}
