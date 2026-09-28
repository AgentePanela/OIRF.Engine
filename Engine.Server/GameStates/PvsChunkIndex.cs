using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Microsoft.Xna.Framework;

namespace Engine.Server.GameStates;

public sealed class PvsChunkIndex
{
    /// <summary>
    /// Side of a chunk in world units. Set from <c>net.pvs-chunk-size</c>.
    /// </summary>
    public int ChunkSize = 512;

    private readonly Dictionary<IEntityScene, SceneChunks> _scenes = new();
    private readonly SceneChunks _globals = new(GlobalSceneId);
    private int _nextSceneId = GlobalSceneId + 1;

    private const int GlobalSceneId = 0;

    /// <summary>
    /// Empties every bucket, keeping them (and their lists) around for this tick rebuild.
    /// </summary>
    public void Clear()
    {
        _globals.Clear();
        foreach (var (_, scene) in _scenes)
            scene.Clear();
    }

    public void Add(IEntityScene? scene, EntityUid uid, Vector2 position)
    {
        var target = scene is null ? _globals : GetOrAddScene(scene);
        target.Add(PvsChunks.ToChunk(position, ChunkSize), uid);
    }

    /// <summary>
    /// Appends every chunk of <paramref name="scene"/> whose area is within <paramref name="range"/> of
    /// <paramref name="center"/>, each with how far it is, so the caller can go from the closest outwards.
    /// </summary>
    public void GetChunksInRange(IEntityScene? scene, Vector2 center, float range, List<PvsChunkHit> output)
    {
        var target = scene is null ? _globals : (_scenes.TryGetValue(scene, out var found) ? found : null);
        if (target is null)
            return;

        var min = PvsChunks.ToChunk(new Vector2(center.X - range, center.Y - range), ChunkSize);
        var max = PvsChunks.ToChunk(new Vector2(center.X + range, center.Y + range), ChunkSize);
        var rangeSq = range * range;

        for (var cx = min.X; cx <= max.X; cx++)
        for (var cy = min.Y; cy <= max.Y; cy++)
        {
            if (!target.Chunks.TryGetValue((cx, cy), out var entities) || entities.Count == 0)
                continue;

            // the corner chunks of the square are further than the radius
            var distSq = PvsChunks.DistanceSqToChunk(center, cx, cy, ChunkSize);
            if (distSq > rangeSq)
                continue;

            output.Add(new PvsChunkHit(new PvsChunkKey(target.Id, cx, cy), entities, distSq));
        }
    }

    private SceneChunks GetOrAddScene(IEntityScene scene)
    {
        if (!_scenes.TryGetValue(scene, out var found))
            _scenes[scene] = found = new SceneChunks(_nextSceneId++);

        return found;
    }

    private sealed class SceneChunks(int id)
    {
        public readonly int Id = id;
        public readonly Dictionary<(int X, int Y), List<EntityUid>> Chunks = new();

        public void Clear()
        {
            foreach (var (_, entities) in Chunks)
                entities.Clear();
        }

        public void Add(Point chunk, EntityUid uid)
        {
            var key = (chunk.X, chunk.Y);
            if (!Chunks.TryGetValue(key, out var entities))
                Chunks[key] = entities = new List<EntityUid>();

            entities.Add(uid);
        }
    }
}

/// <summary>
/// Identifies a chunk across every scene
/// </summary>
public readonly struct PvsChunkKey(int sceneId, int x, int y) : IEquatable<PvsChunkKey>
{
    public readonly int SceneId = sceneId;
    public readonly int X = x;
    public readonly int Y = y;

    public bool Equals(PvsChunkKey other) => SceneId == other.SceneId && X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is PvsChunkKey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(SceneId, X, Y);
    public override string ToString() => $"{SceneId}:{X},{Y}";
}

/// <summary>
/// A chunk a viewer can see, and how far it is from it.
/// </summary>
public readonly struct PvsChunkHit(PvsChunkKey key, List<EntityUid> entities, float distanceSq)
{
    public readonly PvsChunkKey Key = key;
    public readonly List<EntityUid> Entities = entities;
    public readonly float DistanceSq = distanceSq;
}
