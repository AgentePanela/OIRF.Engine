using Engine.Shared.IoC;
using Engine.Shared.Prototypes;
using System.Text.Json.Serialization;

namespace Engine.Shared.GameObjects;

/// <summary>
/// Represents a unique entity within a <see cref="IEntityScene"/>.
/// 
/// An entity is an identifier that groups components together.
/// It does not contain behavior or game logic by itself.
/// </summary>
public sealed class Entity
{
    /// <summary>
    /// The current scene this entity is in.
    /// </summary>
    [JsonIgnore] public IEntityScene? Scene { get; private set; }

    /// <summary>
    /// The name of the entity.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The unique identifier of the entity used in most entity based functions.
    /// </summary>
    public EntityUid Uid { get; private set; } = EntityUid.Empty;

    /// <summary>
    /// The prototype id that this entity has used while being created.
    /// </summary>
    public ProtoId<EntityPrototype> Id { get; private set; } = new ProtoId<EntityPrototype>();

    /// <summary>
    /// Marks if this entity will be deleted in the next tick.
    /// </summary>
    [JsonIgnore] public bool Deleting { get; private set; } = false;

    internal Entity(EntityUid uid)
    {
        Uid = uid;
    }

    internal Entity(EntityUid uid, string name)
    {
        Name = name;
        Uid = uid;
    }

    internal Entity(EntityUid uid, string name, ProtoId<EntityPrototype> proto)
    {
        Name = name;
        Uid = uid;
        Id = proto;
    }

    internal void SetScene(IEntityScene scene)
        => Scene = scene;

    internal void SetId(ProtoId<EntityPrototype> proto)
        => Id = proto;

    /// <summary>
    /// Marks this entity to be in Deleting state.
    /// EntityManager will delete this entity and their components in the next frame.
    /// </summary>
    public void Delete()
    {
        Deleting = true;
        IoCManager.Resolve<EntityManager>().EntitiesToRemove.Add(Uid);
    }
}

/// <summary>
/// <strong>ONLY USE THIS IF YOU KNOW WHAT U ARE DOING</strong><para/>
/// Called when a entity is right about to be added to the scene. Entity default components is still not avaible.<para/>
/// For normal usage see <seealso cref="EntityAddedEvent"/>
/// </summary>
public sealed class EntityInitEvent : EntityEvent
{
}

/// <summary>
/// Called when a entity is added to the scene. All entity components are already avaible and ready.
/// </summary>
public sealed class EntityAddedEvent : EntityEvent
{
}

/// <summary>
/// Called right before the entity removal. Entity components are still avaible.
/// </summary>
public sealed class EntityRemovedEvent : EntityEvent
{
}
