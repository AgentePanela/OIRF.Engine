using Engine.Client.Assets;
using Engine.Client.GameObjects;
using Engine.Client.Graphics;
using Engine.Client.UI;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using System;
using System.Collections.Generic;

namespace Engine.Client.Scenes;

/// <summary>
/// The default scene class used by SceneLoader
/// </summary>
internal class DefaultScene : Scene
{
    public override UICanvas? DefaultCanvas { get; protected set; } = null;
}

public abstract class Scene : IEntityScene, IDisposable
{
    /// <summary>
    /// Gets the game client instance.
    /// </summary>
    protected GameClient _game;
    protected ContentManager _content { get; private set; }
    [Dependency] protected SceneManager _scene;
    [Dependency] protected IAssetManager _asset;
    [Dependency] protected RenderManager _renderer;
    [Dependency] protected EntityManager _entManager;
    public Color? BackgroundColor;
    public bool IsDisposed { get; private set; }
    public Dictionary<EntityUid, Entity> Entities { get; private set; } = new();
    public int EntUidIndex { get; set; } = 0;
    public Dictionary<Type, Dictionary<EntityUid, Component>> Components { get; private set; } = new();

    public abstract UICanvas? DefaultCanvas { get; protected set; }

    public Scene()
    {
    }
    // Finalizer, called when object is cleaned up by garbage collector.
    ~Scene() => Dispose(false);

    /// <summary>
    /// Disposes of this scene.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes of this scene.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        if (disposing)
        {
            OnSceneEnd(); // Run this first!!
            Components.Clear();
            Entities.Clear();
            //UnloadContent();
            //Content.Dispose();
        }
        IsDisposed = true; 
    }

    internal void Initialize()
    {
        IoCManager.ResolveDependencies(this);
        _content = GameClient.Content;
        _game = GameClient.Instance;
        _entManager.ForceScene(this);

        BootstrapLoadedEntities();

        OnSceneStart();
    }

    internal Entity CreateLoadedEntity(string? name = default)
    {
        var uid = new EntityUid(++EntUidIndex);
        var entity = string.IsNullOrWhiteSpace(name)
            ? new Entity(uid)
            : new Entity(uid, name);

        entity.SetScene(this);
        Entities[uid] = entity;
        return entity;
    }

    internal void AttachLoadedComponent(EntityUid uid, Component comp)
    {
        if (!Entities.ContainsKey(uid))
            throw new InvalidOperationException($"Cannot attach component '{comp.GetType().Name}' to unknown entity '{uid}'.");

        if (!Components.TryGetValue(comp.GetType(), out var pool))
        {
            pool = new Dictionary<EntityUid, Component>();
            Components[comp.GetType()] = pool;
        }

        if (pool.ContainsKey(uid))
            throw new InvalidOperationException($"Entity '{uid}' already has component '{comp.GetType().Name}'.");

        comp.Owner = uid;
        comp.State = Component.CompState.Running;
        pool[uid] = comp;
    }

    private void BootstrapLoadedEntities()
    {
        if (Entities.Count == 0)
            return;

        foreach (var (uid, _) in Entities)
        {
            foreach (var pool in Components.Values)
            {
                if (!pool.TryGetValue(uid, out var comp))
                    continue;

                _entManager.EventBus.RaiseEvent(uid, new CompAddedEvent()
                {
                    Component = comp
                });
            }

            _entManager.EventBus.RaiseEvent(uid, new EntityAddedEvent());
        }
    }

    /// <summary>
    /// Happens after scene starts, where all entities where loaded and positioned.
    /// All helper instances like IAssetManager are already filled.
    /// </summary>
    public virtual void OnSceneStart()
    {
        
    }

    /// <summary>
    /// Called before draw calls on each tick.
    /// </summary>
    /// <param name="dt">Time variation with the last tick.</param>
    public virtual void Update(float dt)
    {
        
    }

    public virtual void Draw(float dt)
    {
        
    }

    /// <summary>
    /// This happends before the disponsing process where all entities are still active.
    /// </summary>
    public virtual void OnSceneEnd()
    {
        
    }

}
