using System;
using System.Collections.Generic;
using Engine.Shared.IoC;

namespace Engine.Shared.GameObjects;

public abstract class EntityEvent
{
    public EntityUid Uid = EntityUid.Empty;
    public bool Handled = false;
}

/// <summary>
/// Implements custom validation in a event.
/// </summary>
public interface IEntEventCustomValidation
{
    /// <summary>
    /// This will be called by raise event while trying to call all subscribed classes that have this event.
    /// </summary>
    public bool Validate(EntityEvent ev, EntityUid? uid = default, Component? comp = default);
}

/// <summary>
/// Manages the communication with systems using events (thats why it is called EventBus).
/// </summary>
public sealed class EventBus
{
    [Dependency] EntityManager _entityManager = default!;
    private readonly Dictionary<Type, List<Delegate>> _events = new();

    internal void Init()
    {
        IoCManager.ResolveDependencies(this);
    }

    /// <summary>
    /// Represents a event without reference of a entity or component, good for events like OnGameOver or OnGameStart
    /// </summary>
    public delegate void GlobalEventHandler<T>(T args) where T : EntityEvent;

    /// <summary>
    /// Subscribes to a global (a event without entity or component reference) event.
    /// The handler will be called whenever the event is raised,
    /// regardless of any specific entity.
    /// </summary>
    public void Subscribe<T>(GlobalEventHandler<T> handler) where T : EntityEvent
    {
        var type = typeof(T);

        if (!_events.TryGetValue(type, out var list))
        {
            list = new List<Delegate>();
            _events[type] = list;
        }

        list.Add(handler);
    }

    /// <summary>
    /// Represents a event with component and entity reference. This event will only be raised 
    /// if the raised entity has the required component.
    /// </summary>
    public delegate void EntityEventHandler<CompT, EventT>(EntityUid uid, CompT comp, EventT args)
        where CompT : Component where EventT : EntityEvent;

    /// <summary>
    /// Subscribes to an entity event that requires a specific component.
    /// The handler will only run if the target entity has the given component.
    /// </summary>
    /// <typeparam name="CompT">Component required on the entity.</typeparam>
    public void Subscribe<CompT, EventT>(EntityEventHandler<CompT, EventT> handler)
        where CompT : Component
        where EventT : EntityEvent
    {
        var type = typeof(EventT);

        if (!_events.TryGetValue(type, out var list))
        {
            list = new List<Delegate>();
            _events[type] = list;
        }

        list.Add(handler);
    }

    /// <summary>
    /// Raises a "global" event.
    /// All subscribers for this event type will be executed.
    /// </summary>
    public void RaiseEvent<T>(T ev) where T : EntityEvent
    {
        if (!_events.TryGetValue(typeof(T), out var list))
            return;

        foreach (var del in list)
        {
           if (del is GlobalEventHandler<T> action)
                ActionGlobalEvent(action, ev);

            if (ev.Handled)
                return;
        }
    }

    private void ActionGlobalEvent<T>(GlobalEventHandler<T> action, T ev) where T : EntityEvent
    {
        if (action is IEntEventCustomValidation validator)
            if (!validator.Validate(ev))
                return;
        
        action(ev);
    }

    /// <summary>
    /// Raises an event for a specific entity. <para/>
    /// <i>Handlers that require components will only run if the entity contains the required component.</i><para/>
    /// <strong>"Global" handlers will also receive the event if they reference the ent uid.</strong>
    /// </summary>
    public void RaiseEvent<T>(EntityUid uid, T ev) where T : EntityEvent
    {
        ev.Uid = uid;

        if (!_events.TryGetValue(typeof(T), out var list))
            return;

        foreach (var del in list)
        {
            switch (del)
            {
                // event only handler
                case GlobalEventHandler<T> action: // reach events without comp
                    ActionGlobalEvent(action, ev);
                    break;

                // Comp handler
                default:
                    var type = del.GetType().GenericTypeArguments;
                    if (type.Length != 2)
                        continue;

                    var compType = type[0];
                    Component comp;

                    if (ev is ComponentEvent cv)
                    {
                        comp = cv.Component;
                        if (compType != cv.Component.GetType()) // is trying to use another component in a component event
                            continue;                           // like <SpriteComponent, CompAddedEvent> while the comp added is Transfrom
                    }
                    else if (!_entityManager.TryComp(uid, compType, out comp!))
                        continue;
                    
                    if (ev is IEntEventCustomValidation validator)
                        if (!validator.Validate(ev, uid, comp))
                            continue;

                    del.DynamicInvoke(uid, comp, ev);

                    break;
            }

            if (ev.Handled)
                return;
        }
    }
}
