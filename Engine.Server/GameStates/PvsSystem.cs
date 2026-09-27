using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.Networking;

namespace Engine.Server.GameStates;

/// <summary>
/// Controls and API for changing what entities can be seen by sessions.
/// </summary>
public sealed class PvsSystem : EntitySystem
{
    private static readonly HashSet<EntityUid> None = new();

    [Dependency] private readonly ServerGameStateSystem _states = default!;

    private readonly HashSet<EntityUid> _global = new();
    private readonly Dictionary<IEntityScene, HashSet<EntityUid>> _byScene = new();
    private readonly Dictionary<INetSession, HashSet<EntityUid>> _bySession = new();

    public override void Init()
    {
        base.Init();
        SubscribeEvent<EntityRemovedEvent>(OnEntityRemoved);
    }

    /// <summary>
    /// Makes <paramref name="uid"/> the only thing <paramref name="session"/> looks through.
    /// </summary>
    public void SetViewer(INetSession session, EntityUid uid)
    {
        var viewers = _states.GetSession(session).Viewers;
        viewers.Clear();
        viewers.Add(uid);
    }

    public void AddViewer(INetSession session, EntityUid uid)
        => _states.GetSession(session).Viewers.Add(uid);

    public void RemoveViewer(INetSession session, EntityUid uid)
        => _states.GetSession(session).Viewers.Remove(uid);

    public IReadOnlyCollection<EntityUid> GetViewers(INetSession session)
        => _states.GetSession(session).Viewers;

    /// <summary>
    /// Sent to every session.
    /// </summary>
    public void AddGlobalOverride(EntityUid uid) => _global.Add(uid);

    public void RemoveGlobalOverride(EntityUid uid) => _global.Remove(uid);

    /// <summary>
    /// Sent to every session in <paramref name="scene"/>.
    /// </summary>
    public void AddSceneOverride(IEntityScene scene, EntityUid uid) => GetOrAdd(_byScene, scene).Add(uid);

    public void RemoveSceneOverride(IEntityScene scene, EntityUid uid)
    {
        if (_byScene.TryGetValue(scene, out var set))
            set.Remove(uid);
    }

    /// <summary>
    /// Sent to one session.
    /// </summary>
    public void AddSessionOverride(INetSession session, EntityUid uid) => GetOrAdd(_bySession, session).Add(uid);

    public void RemoveSessionOverride(INetSession session, EntityUid uid)
    {
        if (_bySession.TryGetValue(session, out var set))
            set.Remove(uid);
    }

    public IReadOnlyCollection<EntityUid> GetGlobal() => _global;

    public IReadOnlyCollection<EntityUid> GetForScene(IEntityScene scene)
        => _byScene.TryGetValue(scene, out var set) ? set : None;

    public IReadOnlyCollection<EntityUid> GetForSession(INetSession session)
        => _bySession.TryGetValue(session, out var set) ? set : None;

    public void ClearSession(INetSession session) => _bySession.Remove(session);

    
    private void OnEntityRemoved(EntityRemovedEvent ev)
    {
        // Drops a deleted entity from every scope
        _global.Remove(ev.Uid); 

        foreach (var (_, set) in _byScene)
            set.Remove(ev.Uid);

        foreach (var (_, set) in _bySession)
            set.Remove(ev.Uid);
    }

    private static HashSet<EntityUid> GetOrAdd<TKey>(Dictionary<TKey, HashSet<EntityUid>> map, TKey key) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var set))
            map[key] = set = new HashSet<EntityUid>();

        return set;
    }
}
