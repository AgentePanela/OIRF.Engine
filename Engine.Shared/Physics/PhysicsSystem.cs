using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.Physics.Fixtures;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Physics;

// I need to say... I'm not good at math and had no idea how to make this
// so yes, fixtures and phyiscs are vibecoded, sorry.

/// <summary>
/// Handles velocity integration and collision resolution.
/// Listens to CollisionStartEvent fired by CollisionSystem and
/// applies the push - CollisionSystem never touches transforms.
/// </summary>
public sealed class PhysicsSystem : EntitySystem
{
    [Dependency] private readonly CollisionSystem _collision = default!;

    private readonly Dictionary<EntityUid, List<ActiveCollision>> _activeCollisions = new();
    private record ActiveCollision(EntityUid Other, Vector2 Penetration, bool IsHard);

    public override void Init()
    {
        base.Init();

        SubscribeEvent<PhysicsComponent, CollisionStartEvent>(OnCollisionStart);
        SubscribeEvent<PhysicsComponent, CollisionEndEvent>(OnCollisionEnd);
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        var query = GetEntitiesWithComp<TransformComponent, PhysicsComponent>();
        foreach (var (uid, transform, physics) in query)
        {
            if (physics.Static) 
                continue;

            if (_activeCollisions.TryGetValue(uid, out var collisions))
            {
                foreach (var col in collisions)
                {
                    if (!col.IsHard)
                        continue;

                    if (!_collision.TryGetPenetration(uid, col.Other, out var mtv))
                        continue;

                    bool otherIsStatic = TryComp<PhysicsComponent>(col.Other, out var otherPhysics)
                                        && otherPhysics.Static;

                    float factor = otherIsStatic ? 1f : 0.5f;
                    transform.Position += mtv * factor;

                    // Block velocity going further into the collision
                    // Only cancel if velocity is pointing INTO the collision (dot < 0)
                    var normal = Vector2.Normalize(mtv);
                    float dot  = Vector2.Dot(physics.Velocity, normal);
                    if (dot < 0f)
                        physics.Velocity -= normal * dot;
                }
            }
            if (physics.Velocity == Vector2.Zero) 
                continue;

            transform.Position += physics.Velocity * dt;
            physics.Velocity   *= 1f - Math.Clamp(physics.Friction * dt, 0f, 1f);

            if (physics.Velocity.LengthSquared() < 0.01f)
                physics.Velocity = Vector2.Zero;
        }
    }

    private void OnCollisionStart(EntityUid uid, PhysicsComponent physics, CollisionStartEvent ev)
    {
        if (!_activeCollisions.TryGetValue(uid, out var list))
        {
            list = new List<ActiveCollision>();
            _activeCollisions[uid] = list;
        }

        list.Add(new ActiveCollision(ev.Other, ev.PenetrationVector, ev.IsHard));
    }

    private void OnCollisionEnd(EntityUid uid, PhysicsComponent physics, CollisionEndEvent ev)
    {
        if (!_activeCollisions.TryGetValue(uid, out var list))
            return;

        list.RemoveAll(c => c.Other.Id == ev.Other.Id);

        if (list.Count == 0)
            _activeCollisions.Remove(uid);
    }

    private static Vector2 CancelAxis(Vector2 velocity, Vector2 penetration)
        => penetration.X != 0
            ? new Vector2(0, velocity.Y)
            : new Vector2(velocity.X, 0);
}
