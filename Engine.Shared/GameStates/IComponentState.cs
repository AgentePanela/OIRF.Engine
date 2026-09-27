namespace Engine.Shared.GameStates;

/// <summary>
/// The state of a component that builds its own, instead of letting the generator do it.
/// </summary>
public interface IComponentState
{
}

/// <summary>
/// A state that only carries what changed since some tick.
/// </summary>
public interface IComponentDeltaState : IComponentState
{
    /// <summary>
    /// The new full state, from this delta and what the peer already had. Returns null when there is nothing to merge onto.
    /// </summary>
    IComponentState? ApplyToFull(IComponentState? full);
}

/// <inheritdoc cref="IComponentDeltaState"/>
public interface IComponentDeltaState<TFullState> : IComponentDeltaState where TFullState : IComponentState
{
    /// <summary>
    /// Merges this delta with <paramref name="full"/> into a new state, leaving <paramref name="full"/> untouched.
    /// </summary>
    TFullState CreateNewFullState(TFullState full);

    IComponentState? IComponentDeltaState.ApplyToFull(IComponentState? full)
        => full is TFullState typed ? CreateNewFullState(typed) : null;
}
