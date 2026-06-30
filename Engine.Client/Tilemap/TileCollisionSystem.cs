using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.Physics;
using Engine.Shared.Physics.Fixtures;
using Microsoft.Xna.Framework;

namespace Engine.Client.Tilemap;

/// <summary>
/// Resolves entity-vs-tile collisions every frame
/// Entities with a <see cref="PhysicsComponent"/> and <see cref="CollisionComponent"/> get collied by tiles with solid: true
/// </summary>
public sealed class TileCollisionSystem : EntitySystem
{
    [Dependency] private readonly TilemapSystem _tilemap = default!;

    // reused per-frame to avoid allocations
    private readonly List<Rectangle> _solidTiles = new();

    public override void Update(float dt)
    {
        base.Update(dt);

        var tilemaps = GetEntitiesWithComp<TilemapComponent, TransformComponent>();
        var entities = GetEntitiesWithComp<TransformComponent, PhysicsComponent, CollisionComponent>();
        foreach ((var uid, var transform, var physics, var collision) in entities)
        {
            if (physics.Static || !collision.Active)
                continue;

            foreach ((_, var tileComp, var tileTrans) in tilemaps)
                ResolveAgainstTilemap(transform, physics, collision, tileComp, tileTrans);
        }
    }

    private void ResolveAgainstTilemap(
        TransformComponent transform,
        PhysicsComponent physics,
        CollisionComponent collision,
        TilemapComponent tileComp,
        TransformComponent tileTrans)
    {
        // build entity AABB from all fixtures
        float eMinX = float.MaxValue, eMinY = float.MaxValue;
        float eMaxX = float.MinValue, eMaxY = float.MinValue;

        foreach ((_, var fixture) in collision.Fixtures)
        {
            if (!fixture.Hard)
                continue;

            fixture.Shape.GetAABB(transform.Position,
                out float fx0, out float fy0, out float fx1, out float fy1);

            if (fx0 < eMinX) eMinX = fx0;
            if (fy0 < eMinY) eMinY = fy0;
            if (fx1 > eMaxX) eMaxX = fx1;
            if (fy1 > eMaxY) eMaxY = fy1;
        }
        if (eMinX >= eMaxX || eMinY >= eMaxY)
            return;

        var searchArea = new Rectangle(
            (int)eMinX - 1, (int)eMinY - 1,
            (int)(eMaxX - eMinX) + 2, (int)(eMaxY - eMinY) + 2);

        _tilemap.GetSolidTilesInArea(tileComp, tileTrans, searchArea, _solidTiles);
        foreach (var tileRect in _solidTiles)
        {
            float exMin = float.MaxValue, eyMin = float.MaxValue;
            float exMax = float.MinValue, eyMax = float.MinValue;

            foreach ((_, var fixture) in collision.Fixtures)
            {
                if (!fixture.Hard) continue;
                fixture.Shape.GetAABB(transform.Position,
                    out float fx0, out float fy0, out float fx1, out float fy1);
                if (fx0 < exMin) exMin = fx0;
                if (fy0 < eyMin) eyMin = fy0;
                if (fx1 > exMax) exMax = fx1;
                if (fy1 > eyMax) eyMax = fy1;
            }

            float tMinX = tileRect.Left;
            float tMinY = tileRect.Top;
            float tMaxX = tileRect.Right;
            float tMaxY = tileRect.Bottom;

            if (exMax <= tMinX || exMin >= tMaxX || eyMax <= tMinY || eyMin >= tMaxY)
                continue;

            // compute penetration
            float overlapLeft  = exMax - tMinX;
            float overlapRight = tMaxX - exMin;
            float overlapTop   = eyMax - tMinY;
            float overlapBottom = tMaxY - eyMin;
            float minOverlapX = (overlapLeft < overlapRight) ? -overlapLeft : overlapRight;
            float minOverlapY = (overlapTop < overlapBottom) ? -overlapTop  : overlapBottom;

            Vector2 mtv;
            if (Math.Abs(minOverlapX) < Math.Abs(minOverlapY))
                mtv = new Vector2(minOverlapX, 0);
            else
                mtv = new Vector2(0, minOverlapY);

            transform.Position += mtv;

            // cancel velocity
            var normal = Vector2.Normalize(mtv);
            float dot = Vector2.Dot(physics.Velocity, normal);
            if (dot < 0f)
                physics.Velocity -= normal * dot;
        }
    }
}
