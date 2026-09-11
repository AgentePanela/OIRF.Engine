using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Engine.Shared.Prototypes;
using Engine.Shared.Threading;
using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects;

public sealed partial class EntityManager
{
    /// <summary>
    /// Get the current entity count (every entity - global and scene/room-owned alike).
    /// </summary>
    public int GetEntityCount()
    {
        return _entities.Count;
    }

    /// <summary>
    /// Create a global empty entity, not owned by any scene/room. <para/>
    /// "i" exists just to shutup the compiler.
    /// </summary>
    internal Entity CreateEmptyEntity(string? name = default, bool i = true)
        => CreateEmptyEntityOwned(name, null);

    private Entity CreateEmptyEntityOwned(string? name, IEntityScene? owner)
    {
        MainThread.AssertMainThread();

        var uid = new EntityUid(_nextUid++);
        var ent = new Entity(uid, name ?? string.Empty);
        if (owner is not null)
        {
            ent.SetScene(owner);
            owner.OwnedEntities.Add(uid);
        }

        if (!_entities.TryAdd(uid, ent))
            throw new Exception($"Entity {uid} already exists.");

        EventBus.RaiseEvent(uid, new EntityInitEvent());
        return ent;
    }

    /// <summary>
    /// Create a global empty entity, not owned by any scene/room. Use the
    /// <see cref="CreateEmptyEntity(string?, IEntityScene?)"/> overload to tie it to one.
    /// </summary>
    public EntityUid CreateEmptyEntity(string? name = default)
    {
        Entity ent = CreateEmptyEntity(name, true);
        EventBus.RaiseEvent(ent.Uid, new EntityAddedEvent());
        return ent.Uid;
    }

    /// <summary>
    /// Create an empty entity owned by <paramref name="owner"/>, or global if null.
    /// </summary>
    public EntityUid CreateEmptyEntity(string? name, IEntityScene? owner)
    {
        Entity ent = CreateEmptyEntityOwned(name, owner);
        EventBus.RaiseEvent(ent.Uid, new EntityAddedEvent());
        return ent.Uid;
    }

    /// <summary>
    /// Create a global entity (not owned by any scene/room) using a prototype as reference.
    /// Use the <see cref="CreateEntity(ProtoId{EntityPrototype}, IEntityScene?, string?)"/>
    /// overload to tie it to one.
    /// </summary>
    public EntityUid CreateEntity(ProtoId<EntityPrototype> protoId, string? nameOverride = null)
        => CreateEntity(protoId, null, nameOverride);

    /// <summary>
    /// Create a entity using a prototype as reference, owned by <paramref name="owner"/>
    /// (or global if null).
    /// </summary>
    public EntityUid CreateEntity(ProtoId<EntityPrototype> protoId, IEntityScene? owner, string? nameOverride = null)
    {
        var proto = _proto.Index(protoId);

        if (proto is IInheritingPrototype inh && inh.Abstract)
            throw new Exception($"Prototype '{proto.ID}' is abstract and cannot be spawned.");

        var ent = CreateEmptyEntityOwned(nameOverride ?? proto.Name ?? proto.ID, owner);
        ent.SetId(protoId);

        foreach (var entry in proto.Components.Values)
        {
            var comp = _compFac.CreateInstanceFromSanitized(entry.Type)
                ?? _compFac.CreateInstance(entry.Type)
                ?? null; //throw new Exception($"Unknown component '{entry.Type}' in prototype '{proto.ID}'.");

            if (comp == null) // since we now have server and client, we should just ignore the unknow comps :p
                continue;

            ApplyComponentData(comp, entry.Data);
            AddComponentInstance(ent.Uid, comp);
        }

        EventBus.RaiseEvent(ent.Uid, new EntityAddedEvent());
        return ent.Uid;
    }

    /// <inheritdoc cref="CreateEntity(ProtoId{EntityPrototype}, string?)"/>
    public EntityUid CreateEntity(ProtoId<EntityPrototype> protoId, Vector2 pos, string? nameOverride = null)
        => CreateEntity(protoId, null, pos, nameOverride);

    /// <inheritdoc cref="CreateEntity(ProtoId{EntityPrototype}, IEntityScene?, string?)"/>
    public EntityUid CreateEntity(ProtoId<EntityPrototype> protoId, IEntityScene? owner, Vector2 pos, string? nameOverride = null)
    {
        var uid = CreateEntity(protoId, owner, nameOverride);
        var trans = EnsureComp<TransformComponent>(uid);
        trans.Position = pos;
        return uid;
    }

    /// <summary>
    /// Returns if a entity exist in the current scene.
    /// </summary>
    /// <param name="ent">The entity instance.</param>
    public bool HasEntity(EntityUid uid, [NotNullWhen(true)]out Entity? ent)
    {
        ent = default;
        if (!_entities.TryGetValue(uid, out ent))
            return false;

        return true;
    }

    /// <summary>
    /// Get a entity by it uid. Returns false if does not exist.
    /// </summary>
    public Entity? GetEntity(EntityUid uid)
    {
        if (!_entities.TryGetValue(uid, out var ent))
            return null;

        return ent;
    }

    /// <summary>
    /// Get the loaded entity list.
    /// </summary>
    public List<EntityUid> GetEntities()
    {
        return _entities.Keys.ToList();
    }

    /// <summary>
    /// Get the entities owned by a specific scene/room.
    /// </summary>
    public IReadOnlyCollection<EntityUid> GetEntitiesInScene(IEntityScene scene)
    {
        return scene.OwnedEntities.ToList();
    }

    /// <summary>
    /// Marks the entity to be deleted in the next frame.
    /// </summary>
    public void DeleteEntity(EntityUid uid)
    {
        if (!HasEntity(uid, out var ent))
            return;

        ent.Delete();
    }

    /// <summary>
    /// Creates a copy of an existing entity, copying its metadata and compononents. The
    /// clone is owned by the same scene/room as <paramref name="source"/> (global if
    /// <paramref name="source"/> is global).
    /// </summary>
    public EntityUid CloneEntity(EntityUid source)
    {
        if (!HasEntity(source, out var srcEnt))
            return EntityUid.Empty;

        return RestoreEntity(srcEnt.Name, GetEntityComps(source) ?? [], srcEnt.Id, srcEnt.Scene);
    }

    /// <summary>
    /// Creates a new entity from a list of components and them copy their values via
    /// reflection. Global (not owned by any scene/room) unless <paramref name="owner"/>
    /// is given.
    /// </summary>
    public EntityUid RestoreEntity(string name, IReadOnlyList<Component> snapshot, ProtoId<EntityPrototype>? proto = null, IEntityScene? owner = null)
    {
        var newEnt = CreateEmptyEntityOwned(name, owner);
        if (proto is not null)
            newEnt.SetId(proto.Value);

        foreach (var comp in snapshot)
        {
            var newComp = _compFac.CreateInstance(comp.GetType());
            if (newComp is null)
                continue;

            DataFieldConverter.CopyByReflection(comp, newComp);
            AddComponentInstance(newEnt.Uid, newComp);
        }

        EventBus.RaiseEvent(newEnt.Uid, new EntityAddedEvent());
        return newEnt.Uid;
    }

    /// <summary>
    /// Wipes entities through the same deferred path as a normal <see cref="DeleteEntity"/> -
    /// queued, on the next Update.
    /// </summary>
    public void WipeEntities(IEntityScene? scene = null)
    {
        if (scene is null)
        {
            _wipeAllQueued = true;
            return;
        }

        foreach (var uid in scene.OwnedEntities.ToList())
            DeleteEntity(uid);
    }

}
