using System.Collections.Generic;

namespace Engine.Shared.GameObjects;

/// <summary>
/// A world that can own entities.
/// </summary>
public interface IEntityScene
{
    /// <summary>
    /// EntityUids currently owned by this scene.
    /// </summary>
    public HashSet<EntityUid> OwnedEntities { get; }
}
