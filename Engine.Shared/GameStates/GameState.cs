using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.Timing;

namespace Engine.Shared.GameStates;

// TODO: Maybe change to objects like RT?
// TODO: RT has no raw bytes anywhere in here - 
// TODO: every component, generated or manual, hands over an IComponentState object
// TODO: and the whole graph goes through the serializer. Different what we currently do.
// TODO: We keep bytes for the generated path because it allocates nothing and the delta granularity is 
// TODO: the whole component either way.

/// <summary>
/// Everything one session is told about the world in a single tick. Built by server
/// </summary>
public sealed class GameState
{
    /// <summary>
    /// The baseline this state was built against. GameTick.Zero means that the client has nothing
    /// so this is a full state.
    /// </summary>
    public GameTick FromTick;

    /// <summary>
    /// The tick this state describes.
    /// </summary>
    public GameTick ToTick;

    public bool IsFullState => FromTick == GameTick.Zero;

    /// <summary>
    /// Entities deleted on the server since <see cref="FromTick"/>.
    /// </summary>
    public readonly List<NetEntity> Deletions = new();

    /// <summary>
    /// The entities this session is seeing for the first time, with the prototype to build them from.
    /// </summary>
    public readonly List<EnteringEntity> Entering = new();

    public readonly List<EntityBlock> Blocks = new();

    public void Reset()
    {
        FromTick = GameTick.Zero;
        ToTick = GameTick.Zero;
        Deletions.Clear();
        Entering.Clear();
        Blocks.Clear();
    }
}

/// <summary>
/// An entity a session did not have yet.
/// </summary>
public readonly struct EnteringEntity(NetEntity netEntity, string protoId)
{
    public readonly NetEntity NetEntity = netEntity;
    public readonly string ProtoId = protoId;
}

/// <summary>
/// A group of entities inside a state: the globals are one, the session's room is another. It is what lets the client
/// drop only a room's entities when it leaves, without touching the globals.
/// </summary>
public sealed class EntityBlock
{
    public EntityBlockKind Kind;

    public readonly List<EntityState> Entities = new();

    public void Reset(EntityBlockKind kind)
    {
        Kind = kind;
        Entities.Clear();
    }
}

public enum EntityBlockKind : byte
{
    /// <summary>
    /// Entities with no scene/room.
    /// </summary>
    Global = 0,

    /// <summary>
    /// Entities owned by the room the session is in.
    /// </summary>
    Room = 1,
}

/// <summary>
/// One entity inside a block.
/// </summary>
public sealed class EntityState
{
    public NetEntity NetEntity;

    public readonly List<ComponentChange> Changes = new();

    /// <summary>
    /// ids of components the entity no longer has.
    /// </summary>
    public readonly List<int> Removed = new();

    public void Reset(NetEntity netEntity)
    {
        NetEntity = netEntity;
        Changes.Clear();
        Removed.Clear();
    }
}

/// <summary>
/// One component's data inside an <see cref="EntityState"/>. A manual component carries its <see cref="State"/>
/// object; a generated one carries the live component its bytes are written from.
/// </summary>
public readonly struct ComponentChange
{
    public readonly int NetId;

    /// <summary>
    /// Manual components only.
    /// </summary>
    public readonly IComponentState? State;

    /// <summary>
    /// Generated components only.
    /// </summary>
    public readonly Component? Source;

    /// <summary>
    /// Used when the session dont have the component already
    /// </summary>
    public readonly bool FullState;

    public ComponentChange(int netId, IComponentState state)
    {
        NetId = netId;
        State = state;
        Source = null;
        FullState = true;
    }

    public ComponentChange(int netId, Component source, bool fullState)
    {
        NetId = netId;
        State = null;
        Source = source;
        FullState = fullState;
    }
}
