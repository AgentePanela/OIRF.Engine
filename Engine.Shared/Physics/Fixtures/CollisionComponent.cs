using System;
using Engine.Shared.GameObjects;
using System.Collections.Generic;

namespace Engine.Shared.Physics.Fixtures;

[RegisterComponent("Collision"), NetworkedComponent, AutoDirty]
public sealed partial class CollisionComponent : Component
{
    [NetField] public partial Dictionary<string, CollisionFixture> Fixtures { get; set; } = new();

    [NetField] public partial bool Active { get; set; } = true;
 
    public CollisionFixture? GetFixture(string id)
        => Fixtures.GetValueOrDefault(id);
 
    public CollisionFixture AddFixture(string id, CollisionFixture fixture)
    {
        Fixtures[id] = fixture;
        Dirty(); // the dictionary is the same instance, so the generated setter never runs
        return fixture;
    }
 
    public bool RemoveFixture(string id)
    {
        if (!Fixtures.Remove(id))
            return false;

        Dirty();
        return true;
    }
}

[Serializable]
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
