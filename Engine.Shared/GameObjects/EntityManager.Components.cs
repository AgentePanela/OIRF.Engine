using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Engine.Shared.Prototypes;

namespace Engine.Shared.GameObjects;

public sealed partial class EntityManager
{
    private Dictionary<EntityUid, Component>? TryGetPool(Type type)
    {
        if (_scene.Components.TryGetValue(type, out var pool))
            return pool;

        return null;
    }

    #region Components Api
    
    /// <summary>
    /// Add a component to a entity and return its instance.
    /// </summary>
    /// <exception cref="Exception">Entity already has that component.</exception>
    public T AddComp<T>(EntityUid uid) where T : Component, new()
    {
        var comp = _compFac.CreateInstance<T>() ?? throw new Exception("Unknown component.");
        AddComponentInstance(uid, comp);
        return comp;
    }

    /// <inheritdoc cref="AddComp{T}(EntityUid)"/>
    public Component? AddComponent(EntityUid uid, Type type)
    {
        var comp = _compFac.CreateInstance(type);
        if (comp is null)
            return null;

        AddComponentInstance(uid, comp);
        return comp;
    }

    private void AddComponentInstance(EntityUid uid, Component comp)
    {
        var type = comp.GetType();
        var pool = GetPool(type);

        if (pool.ContainsKey(uid))
            throw new Exception($"Entity {uid} already has {type.Name}");

        EventBus.RaiseEvent(uid, new CompInitEvent() { Component = comp });

        comp.Owner = uid;
        pool[uid] = comp;

        //comp.State = Component.CompState.Running;
        //EventBus.RaiseEvent(uid, new CompAddedEvent() { Component = comp });
    }

    private void ApplyComponentData(object target, Dictionary<string, object> data)
        => DataFieldConverter.ApplyByName(target, data);

    /// <summary>
    /// Ensures the entity has the selected component type.
    /// Creates and attaches one if missing.
    /// </summary>
    public T EnsureComp<T>(EntityUid uid) where T : Component, new()
    {
        if (!TryComp<T>(uid, out var comp))
            comp = AddComp<T>(uid);
        
        return comp;    
    }

    /// <summary>
    /// Get a component from an entity. Use <see cref="TryComp{Component}(EntityUid, out Component?)"/> 
    /// </summary>
    /// <returns>Returns null entity does not have that component.</returns>
    public T? GetComp<T>(EntityUid uid) where T : Component
    {
        var pool = GetPool<T>();

        if (!pool.TryGetValue(uid, out var comp))
            return null;

        return (T)comp;
    }

    /// <summary>
    /// Try get a component from an entity, will return false if does not have that component.
    /// </summary>
    public bool TryComp<T>(EntityUid uid, [NotNullWhen(true)]out T? comp) where T : Component
    {
        var pool = GetPool<T>();

        if (pool.TryGetValue(uid, out var found))
        {
            comp = (T)found;
            return true;
        }

        comp = null;
        return false;
    }

    /// <inheritdoc cref="TryComp{T}(EntityUid, out T?)"/>
    public bool TryComp(EntityUid uid, Type type, [NotNullWhen(true)]out Component? comp)
    {
        comp = default;
        var pool = GetPool(type);

        if (pool.TryGetValue(uid, out comp))
            return true;

        return false;
    }

    /// <summary>
    /// Returns a boolean based on if an entity has that component.
    /// </summary>
    public bool HasComp<T>(EntityUid uid) where T : Component
    {
        if (!_scene.Components.TryGetValue(typeof(T), out var pool))
            return false;

        return pool.ContainsKey(uid);
    }

    /// <summary>
    /// Marks a component to be removed in the next frame.
    /// </summary>
    public void RemComp<T>(EntityUid uid) where T : Component
    {
        RemComp(uid, typeof(T));
    }

    /// <inheritdoc cref="RemComp{T}(EntityUid)"/>
    public void RemComp(EntityUid uid, Type type)
    {
        var pool = TryGetPool(type);
        if (pool is null || !pool.TryGetValue(uid, out var comp))
            return;

        comp.RemoveComponent();
    }

    /// <summary>
    /// Get all components that the selected entity has.
    /// </summary>
    public List<Component>? GetEntityComps(EntityUid uid)
    {
        if (!HasEntity(uid, out _))
            return null;
    
        var result = new List<Component>();

        foreach (var pool in _scene.Components.Values)
        {
            if (!pool.TryGetValue(uid, out var comp))
                continue;
            result.Add(comp);
        }

        return result;
    }

    /// <summary>
    /// Get all entities with the component
    /// </summary>
    public IEnumerable<(EntityUid uid, T comp)> Query<T>() where T : Component
    {
        var pool = TryGetPool(typeof(T));

        if (pool is null)
            yield break;

        foreach (var (uid, comp) in pool)
            yield return (uid, (T)comp);
    }

    /// <summary>
    /// Get all entities with the selected components
    /// </summary>
    public IEnumerable<(EntityUid uid, T1 comp1, T2 comp2)> 
        Query<T1, T2>() where T1 : Component where T2 : Component
    {
        var pool1 = TryGetPool(typeof(T1));
        var pool2 = TryGetPool(typeof(T2));

        if (pool1 is null || pool2 is null)
            yield break;

        // get the comp with less entities
        var primary = pool1.Count <= pool2.Count ? pool1 : pool2;
        var secondary = primary == pool1 ? pool2 : pool1;

        foreach (var (uid, comp) in primary)
        {
            if (secondary.TryGetValue(uid, out var other))
            {
                if (primary == pool1)
                    yield return (uid, (T1)comp, (T2)other);
                else
                    yield return (uid, (T1)other, (T2)comp);
            }
        }
    }

    /// <summary>
    /// Get all entities with the selected components
    /// </summary>
    public IEnumerable<(EntityUid uid, T1, T2, T3)>
        Query<T1, T2, T3>() where T1 : Component where T2 : Component where T3 : Component
    {
        var p1 = TryGetPool(typeof(T1));
        var p2 = TryGetPool(typeof(T2));
        var p3 = TryGetPool(typeof(T3));

        if (p1 is null || p2 is null || p3 is null)
            yield break;

        // get the component with less entities
        var pools = new[] { p1, p2, p3 };
        var primary = pools.OrderBy(p => p.Count).First();

        foreach (var (uid, _) in primary)
        {
            if (p1.ContainsKey(uid) &&
                p2.ContainsKey(uid) &&
                p3.ContainsKey(uid))
            {
                yield return (
                    uid,
                    (T1)p1[uid],
                    (T2)p2[uid],
                    (T3)p3[uid]
                );
            }
        }
    }

    #endregion
}
