using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Engine.Shared.Prototypes;
using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects;

public sealed partial class EntityManager
{
    /// <summary>
    /// Get the current entity count.
    /// </summary>
    public int GetEntityCount()
    {
        return _scene.Entities.Count;
    }

    /// <summary>
    /// Create an empty entity in the current scene. <para/>
    /// "i" exists just to shutup the compiler.
    /// </summary>
    internal Entity CreateEmptyEntity(string? name = default, bool i = true)
    {
        var uid = new EntityUid(_scene.EntUidIndex);
        var ent = new Entity(uid, name ?? string.Empty);
        ent.SetScene(_scene);
        _scene.Entities.Add(uid, ent);
        _scene.EntUidIndex++;
        EventBus.RaiseEvent(uid, new EntityInitEvent());
        return ent;
    }

    /// <summary>
    /// Create an empty entity in the current scene.
    /// </summary>
    public EntityUid CreateEmptyEntity(string? name = default)
    {
        Entity ent = CreateEmptyEntity(name, true);
        EventBus.RaiseEvent(ent.Uid, new EntityAddedEvent());
        return ent.Uid;
    }

    /// <summary>
    /// Create a entity using a prototype as reference.
    /// </summary>
    public EntityUid CreateEntity(ProtoId<EntityPrototype> protoId, string? nameOverride = null)
    {
        var proto = _proto.Index(protoId);

        if (proto is IInheritingPrototype inh && inh.Abstract)
            throw new Exception($"Prototype '{proto.ID}' is abstract and cannot be spawned.");

        var uid = CreateEmptyEntity(nameOverride ?? proto.Name ?? proto.ID);

        foreach (var entry in proto.Components.Values)
        {
            var comp = _compFac.CreateInstanceFromSanitized(entry.Type)
                ?? _compFac.CreateInstance(entry.Type)
                ?? null; //throw new Exception($"Unknown component '{entry.Type}' in prototype '{proto.ID}'.");

            if (comp == null) // since we now have server and client, we should just ignore the unknow comps :p
                continue;

            ApplyComponentData(comp, entry.Data);
            AddComponentInstance(uid, comp);
        }

        EventBus.RaiseEvent(uid, new EntityAddedEvent());
        return uid;
    }

    /// <inheritdoc cref="CreateEntity(ProtoId{EntityPrototype}, string?)"/>
    public EntityUid CreateEntity(ProtoId<EntityPrototype> protoId, Vector2 pos, string? nameOverride = null)
    {
        var uid = CreateEntity(protoId, nameOverride);
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
        if (!_scene.Entities.TryGetValue(uid, out ent))
            return false;

        return true;
    }

    /// <summary>
    /// Get a entity by it uid. Returns false if does not exist.
    /// </summary>
    public Entity? GetEntity(EntityUid uid)
    {
        if (!_scene.Entities.TryGetValue(uid, out var ent))
            return null;

        return ent;
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
    /// Creates a copy of an existing entity, copying its metadata and compononents
    /// </summary>
    public EntityUid CloneEntity(EntityUid source)
    {
        if (!HasEntity(source, out var srcEnt))
            return EntityUid.Empty;

        var newEnt = CreateEmptyEntity(srcEnt.Name, true);

        foreach (var comp in GetEntityComps(source) ?? [])
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
}
