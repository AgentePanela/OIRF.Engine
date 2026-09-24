using Engine.Client.Assets;
using Engine.Client.Audio;
using Engine.Client.GameObjects;
using Engine.Client.Graphics;
using Engine.Client.UI;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;
using Engine.Shared.Prototypes;
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
    public override Layout? Layout { get; } = null;
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
    [Dependency] protected IAudioManager _audio;
    [Dependency] protected RenderManager _renderer;
    [Dependency] protected EntityManager _entManager;
    public Color? BackgroundColor;
    public bool IsDisposed { get; private set; }
    public HashSet<EntityUid> OwnedEntities { get; } = new();

    /// <summary>
    /// Scene's own HUD/Overlay displayed after the game world.
    /// </summary>
    public abstract Layout? Layout { get; }

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

            if (Layout is not null)
                GameClient.InterfaceManager.RemoveChild(Layout);

            _entManager.WipeEntities(this);
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

        OnSceneStart();

        if (Layout is not null)
            GameClient.InterfaceManager.AddChild(Layout);
    }

    // ==== entities (owned by this scene, not global - see EntityManager for the raw API)

    /// <inheritdoc cref="EntityManager.CreateEmptyEntity(string?, IEntityScene?)"/>
    protected EntityUid CreateEmptyEntity(string? name = default)
        => _entManager.CreateEmptyEntity(name, this);

    /// <inheritdoc cref="EntityManager.CreateEntity(ProtoId{EntityPrototype}, IEntityScene?, string?)"/>
    protected EntityUid CreateEntity(ProtoId<EntityPrototype> protoId, string? nameOverride = null)
        => _entManager.CreateEntity(protoId, this, nameOverride);

    /// <inheritdoc cref="CreateEntity(ProtoId{EntityPrototype}, string?)"/>
    protected EntityUid CreateEntity(ProtoId<EntityPrototype> protoId, Vector2 pos, string? nameOverride = null)
    {
        var uid = CreateEntity(protoId, nameOverride);
        var trans = _entManager.EnsureComp<TransformComponent>(uid);
        trans.Position = pos;
        return uid;
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
