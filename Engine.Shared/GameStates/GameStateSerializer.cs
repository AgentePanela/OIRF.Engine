using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.Networking;
using Engine.Shared.Timing;
using Lidgren.Network;

namespace Engine.Shared.GameStates;

/// <summary>
/// The one place that knows what a <see cref="GameState"/> looks like on the wire. <para/>
/// A component payload carries no length of its own, which means nothing in a block can be skipped 
/// or kept around half-read.
/// </summary>
public static class GameStateSerializer
{
    /// <summary>
    /// Serializes a block into its own <see cref="EntityBlock.Data"/>, so every session that can see it writes the
    /// same bytes out without building them again.
    /// </summary>
    public static void WriteBlock(EntityBlock block, EntityManager entMan, ComponentFactory compFac)
    {
        var buffer = block.Data;
        buffer.LengthBits = 0;
        buffer.Position = 0;

        buffer.WriteVariableInt32(block.Entities.Count);
        foreach (var ent in block.Entities)
        {
            buffer.WriteVariableInt32(ent.NetEntity.Id);

            buffer.WriteVariableInt32(ent.Removed.Count);
            foreach (var netId in ent.Removed)
                buffer.WriteVariableInt32(netId);

            buffer.WriteVariableInt32(ent.Changes.Count);
            foreach (var change in ent.Changes)
            {
                buffer.WriteVariableInt32(change.NetId);

                if (change.State is not null)
                    NetFallbackHelpers.Write(buffer, change.State);
                else
                    change.Source!.WriteNetState(buffer, entMan);
            }
        }

        // generated states write single bits, so a block rarely ends on a byte boundary 
        buffer.WritePadBits();
    }

    public static void WriteHeader(NetBuffer buffer, GameState state)
    {
        buffer.WriteVariableUInt32(state.ToTick.Value);
        buffer.WriteVariableUInt32(state.FromTick.Value);

        buffer.WriteVariableInt32(state.Deletions.Count);
        foreach (var netEnt in state.Deletions)
            buffer.WriteVariableInt32(netEnt.Id);

        buffer.WriteVariableInt32(state.LeftView.Count);
        foreach (var netEnt in state.LeftView)
            buffer.WriteVariableInt32(netEnt.Id);

        buffer.WriteVariableInt32(state.Entering.Count);
        foreach (var entering in state.Entering)
        {
            buffer.WriteVariableInt32(entering.NetEntity.Id);
            buffer.Write(entering.ProtoId);
        }

        buffer.WriteVariableInt32(state.Blocks.Count);
    }

    /// <summary>
    /// Writes one block into a message, right after <see cref="WriteHeader"/> wrote how many there are.
    /// </summary>
    public static void WriteBlockInto(NetBuffer buffer, EntityBlock block)
    {
        buffer.Write((byte)block.Kind);
        buffer.WritePadBits();
        buffer.Write(block.Data.Data, 0, block.Data.LengthBytes);
    }

    public static void ReadHeader(NetBuffer buffer, GameState state, out int blockCount)
    {
        state.Reset();

        state.ToTick = new GameTick(buffer.ReadVariableUInt32());
        state.FromTick = new GameTick(buffer.ReadVariableUInt32());

        var deletions = buffer.ReadVariableInt32();
        for (var i = 0; i < deletions; i++)
            state.Deletions.Add(new NetEntity(buffer.ReadVariableInt32()));

        var leftView = buffer.ReadVariableInt32();
        for (var i = 0; i < leftView; i++)
            state.LeftView.Add(new NetEntity(buffer.ReadVariableInt32()));

        var entering = buffer.ReadVariableInt32();
        for (var i = 0; i < entering; i++)
            state.Entering.Add(new EnteringEntity(new NetEntity(buffer.ReadVariableInt32()), buffer.ReadString()));

        blockCount = buffer.ReadVariableInt32();
    }

    /// <summary>
    /// Walks the blocks, handing each entity and component to <paramref name="applier"/> as it goes.
    /// </summary>
    public static void ApplyBlocks(NetBuffer buffer, int blockCount, ComponentFactory compFac, IGameStateApplier applier)
    {
        for (var b = 0; b < blockCount; b++)
        {
            var kind = (EntityBlockKind)buffer.ReadByte();
            buffer.SkipPadBits();

            var entityCount = buffer.ReadVariableInt32();
            for (var e = 0; e < entityCount; e++)
            {
                var netEnt = new NetEntity(buffer.ReadVariableInt32());
                applier.BeginEntity(netEnt, kind);

                var removed = buffer.ReadVariableInt32();
                for (var r = 0; r < removed; r++)
                    applier.RemoveComponent(netEnt, buffer.ReadVariableInt32());

                var changes = buffer.ReadVariableInt32();
                for (var c = 0; c < changes; c++)
                {
                    var netId = buffer.ReadVariableInt32();
                    var type = compFac.GetTypeByNetId(netId);

                    // the connect-time component hash makes this impossible, so if it happens the hash lied and
                    // everything after this point in the payload is garbage - there is no length to skip over :(
                    if (type is null)
                        throw new GameStateDesyncException($"Unknown networked component id {netId}.");

                    if (compFac.IsManualState(type))
                        applier.ApplyComponentState(netEnt, netId, NetFallbackHelpers.Read<IComponentState>(buffer));
                    else
                        applier.ApplyComponentData(netEnt, netId, buffer);
                }
            }

            buffer.SkipPadBits();
        }
    }
}

/// <summary>
/// Receives what <see cref="GameStateSerializer.ApplyBlocks"/> reads out of a state.
/// </summary>
public interface IGameStateApplier
{
    void BeginEntity(NetEntity netEntity, EntityBlockKind kind);

    void RemoveComponent(NetEntity netEntity, int netId);

    void ApplyComponentState(NetEntity netEntity, int netId, IComponentState state);

    /// <summary>
    /// The buffer sits at the start of the component's payload and has to be read to its end.
    /// </summary>
    void ApplyComponentData(NetEntity netEntity, int netId, NetBuffer buffer);
}

public sealed class GameStateDesyncException(string message) : System.Exception(message);
