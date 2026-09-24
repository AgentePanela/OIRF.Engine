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
public interface IComponentDeltaState<TFullState> : IComponentState where TFullState : IComponentState
{
    /// <summary>
    /// Merges this delta into <paramref name="full"/>, in place.
    /// </summary>
    void ApplyToFullState(TFullState full);

    /// <summary>
    /// Same as <see cref="ApplyToFullState"/>, but leaves <paramref name="full"/> untouched.
    /// </summary>
    TFullState CreateNewFullState(TFullState full);
}
