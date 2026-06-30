using System;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Physics.Fixtures;

public sealed class CollisionStartEvent : EntityEvent
{
    public EntityUid Other { get; }
    public string SelfFixtureId { get; }
    public string OtherFixtureId { get; }
 
    /// <summary>
    /// Vector that pushes Self away from Other to resolve the penetration.
    /// Zero if the shape pair has no resolution implemented (e.g. polygon).
    /// </summary>
    public Vector2 PenetrationVector { get; }

    public bool IsHard { get; }
 
    public CollisionStartEvent(EntityUid other, string selfFixture, string otherFixture, Vector2 penetration, bool hard)
    {
        Other = other;
        SelfFixtureId = selfFixture;
        OtherFixtureId = otherFixture;
        PenetrationVector = penetration;
        IsHard = hard;
    }
}

[Obsolete]
public sealed class CollisionStayEvent : EntityEvent
{
    public EntityUid Other { get; }
    public string SelfFixtureId  { get; }
    public string OtherFixtureId { get; }
    public Vector2 PenetrationVector { get; }

    public CollisionStayEvent(EntityUid other, string selfFixture, string otherFixture, Vector2 penetration)
    {
        Other = other;
        SelfFixtureId = selfFixture;
        OtherFixtureId = otherFixture;
        PenetrationVector = penetration;
    }
}
 
public sealed class CollisionEndEvent : EntityEvent
{
    public EntityUid Other { get; }
    public string SelfFixtureId { get; }
    public string OtherFixtureId { get; }
 
    public CollisionEndEvent(EntityUid other, string selfFixture, string otherFixture)
    {
        Other = other;
        SelfFixtureId = selfFixture;
        OtherFixtureId = otherFixture;
    }
}
