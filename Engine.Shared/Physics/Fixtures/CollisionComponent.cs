using Engine.Shared.GameObjects;
using System.Collections.Generic;

namespace Engine.Shared.Physics.Fixtures;

[RegisterComponent("Collision")]
public sealed class CollisionComponent : Component
{
    public Dictionary<string, CollisionFixture> Fixtures { get; set; } = new();

    public bool Active { get; set; } = true;
 
    public CollisionFixture? GetFixture(string id)
        => Fixtures.GetValueOrDefault(id);
 
    public CollisionFixture AddFixture(string id, CollisionFixture fixture)
    {
        Fixtures[id] = fixture;
        return fixture;
    }
 
    public bool RemoveFixture(string id)
        => Fixtures.Remove(id);
}

public sealed class CollisionFixture
{
    public CollisionShape Shape { get; set; } = new BoxShape();
 
    /// <summary>
    /// Layers this fixture belongs to.
    /// </summary>
    public HashSet<string> Layers { get; set; } = new();
 
    /// <summary>
    /// Layers this fixture collides with.
    /// </summary>
    public HashSet<string> Masks  { get; set; } = new();
 
    /// <summary>
    /// If false, raises events but does not block movement physically.
    /// </summary>
    public bool Hard { get; set; } = true;
}
