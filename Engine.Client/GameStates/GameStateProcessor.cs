using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Engine.Shared.GameStates;
using Engine.Shared.Timing;

namespace Engine.Client.GameStates;

/// <summary>
/// Holds the states as they come in and decides which one to apply this tick.
/// </summary>
public sealed class GameStateProcessor
{
    private readonly List<GameStateMessage> _queue = new();

    /// <summary>
    /// The last state that was applied.
    /// </summary>
    public GameTick LastAppliedTick { get; private set; } = GameTick.Zero;

    /// <summary>
    /// There are states waiting but none of them can be applied on top of what the client has.
    /// </summary>
    public bool NeedsFullState { get; private set; }

    public void Add(GameStateMessage msg)
    {
        // states are unreliable, so they arrive late and out of order
        if (msg.State.ToTick <= LastAppliedTick)
            return;

        _queue.Add(msg);
    }

    public bool TryGetNext([NotNullWhen(true)] out GameStateMessage? next)
    {
        next = null;

        foreach (var msg in _queue)
        {
            if (!CanApply(msg))
                continue;

            if (next is null || msg.State.ToTick > next.State.ToTick)
                next = msg;
        }

        NeedsFullState = next is null && _queue.Count > 0;
        return next is not null;
    }

    public void Applied(GameTick tick)
    {
        LastAppliedTick = tick;
        NeedsFullState = false;

        _queue.RemoveAll(msg => msg.State.ToTick <= tick);
    }

    /// <summary>
    /// Throws away everything buffered
    /// </summary>
    public void Reset()
    {
        _queue.Clear();
        LastAppliedTick = GameTick.Zero;
        NeedsFullState = false;
    }

    private bool CanApply(GameStateMessage msg)
        => msg.State.IsFullState || msg.State.FromTick == LastAppliedTick;
}
