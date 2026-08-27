using System;
using System.Collections.Generic;
using System.Diagnostics;
using Engine.Shared.GameObjects;
using Engine.Shared.Lighting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

public sealed partial class LightingSystem
{
    // occluder edge geometry. Each shadow light gets its own range holding
    // only the occluders inside its radius, all packed into one buffer and
    // drawn with a per-light baseVertex. Capped at 4096 occluders per light
    // by the 16 bit index range
    private const int MaxOccluderCap = 4096;

    // ~6 MB of vertices. Beyond this the remaining lights render unshadowed
    private const int MaxShadowVertices = 1 << 18;

    private int _occluderCapacity = 256;
    private ShadowGeometry.OccluderVertex[] _shadowVerts = new ShadowGeometry.OccluderVertex[256 * 16];
    private DynamicVertexBuffer? _shadowVB;
    private int _shadowVBCapacity;
    private IndexBuffer? _shadowIB;

    // one entry per shadow-casting light, in the order RenderShadowMap and
    // DrawRadialLights walk _lights, so the shadow map row index lines up
    private struct ShadowRange
    {
        public Vector2 LightPos;
        public float LightRadius;
        public int VertexOffset;
        public int TriCount;
        public bool NeedsWrapPass;
    }
    private readonly List<ShadowRange> _shadowRanges = new();

    // per-light shadow geometry cache, keyed by the light entity. Valid as
    // long as the light hasn't moved/resized and the occluder set hasn't
    // changed since it was built (see _occluderGeneration), letting
    // BuildShadowGeometry skip re-walking that light's occluders entirely
    private struct ShadowLightCache
    {
        public int Generation;
        public Vector2 LightPos;
        public float LightRadius;
        public ShadowGeometry.OccluderVertex[] Vertices;
        public int VertexCount;
        public bool NeedsWrapPass;
    }
    private readonly Dictionary<EntityUid, ShadowLightCache> _shadowLightCache = new();

    private readonly List<int> _scratchRectOccluders = new();

    // edge-adjacency buckets for entity-occluder culling, keyed by the shared
    // touching coordinate. Rebuilt every frame since _occluders is too
    private readonly Dictionary<int, List<(int Lo, int Hi, int Idx)>> _edgeTop = new();
    private readonly Dictionary<int, List<(int Lo, int Hi, int Idx)>> _edgeBottom = new();
    private readonly Dictionary<int, List<(int Lo, int Hi, int Idx)>> _edgeLeft = new();
    private readonly Dictionary<int, List<(int Lo, int Hi, int Idx)>> _edgeRight = new();

    // the bucket lists are handed back here instead of being dropped for the
    // GC - there's one per distinct edge coordinate, every frame
    private readonly Stack<List<(int Lo, int Hi, int Idx)>> _edgeListPool = new();

    // reused every frame to avoid allocations
    private readonly List<ShadowGeometry.ShadowOccluder> _occluders = new();

    private Rectangle _lastOccluderBounds;
    private bool _occludersDirty = true;

    // bumped every time CollectOccluders actually rebuilds _occluders (either
    // an occluder changed or the camera moved far enough to need new ones) -
    // invalidates every light's ShadowLightCache entry at once, conservatively
    private int _occluderGeneration;

    private bool _warnedShadowCap;

    // LESS so overlapping occluder slices keep the closest distance
    private static readonly DepthStencilState ShadowDepthState = new DepthStencilState
    {
        DepthBufferEnable       = true,
        DepthBufferWriteEnable  = true,
        DepthBufferFunction     = CompareFunction.Less,
    };

    // parameter lookups by name are dictionary hits, cache them once
    private EffectParameter? _sdLightPos, _sdLightRadius, _sdWrapPass;

    private readonly Stopwatch _buildTimer = new();

    // shadow pass broken into its three parts, so it's possible to tell
    // geometry building from target setup from the per-light draws
    private double _shadowBuildMs;
    private double _shadowSetupMs;
    private double _shadowDrawMs;

    private int CountShadowLights()
    {
        int count = 0;
        foreach (var entry in _lights)
        {
            if (!entry.CastShadows) continue;
            if (++count >= _lighting.MaxShadowcastingLights) return count;
        }
        return count;
    }

    private static int RoundUpPow2(int value)
    {
        int result = 1;
        while (result < value)
            result *= 2;
        return result;
    }

    // extra slack kept around the minimum required bounds whenever we
    // (re)collect, so a camera pan within the margin reuses last frame's
    // occluder list instead of forcing a full rescan every frame it moves
    private const float OccluderCacheMarginFactor = 0.5f;

    private void CollectOccluders()
    {
        // an occluder within a light radius of the view can still push a
        // shadow into the view, so pad the culling bounds by the biggest one
        var bounds = _camera.ViewportBounds;
        int pad = (int)MathF.Ceiling(_maxLightRadius);
        bounds.Inflate(pad, pad);

        // the cache stays valid until the camera drifts outside the (more
        // generously padded) region it was last collected for, not just on
        // an exact match - a small pan reuses last frame's occluder list
        if (!_occludersDirty && _lastOccluderBounds.Contains(bounds))
            return;

        _occluders.Clear();
        _scratchRectOccluders.Clear();

        var collectBounds = bounds;
        collectBounds.Inflate(
            (int)MathF.Ceiling(bounds.Width * OccluderCacheMarginFactor),
            (int)MathF.Ceiling(bounds.Height * OccluderCacheMarginFactor));

        foreach (var (uid, occluder, transform) in GetEntitiesWithComp<OccluderComponent, TransformComponent>())
        {
            // this walks every occluder in the scene, so reject the far ones
            // off the component alone - resolving a sprite occluder's bounds
            // costs a component lookup and sometimes an atlas hit
            if (!_occlusionSys.MayOverlap(occluder, transform, collectBounds)) continue;

            var b = _occlusionSys.GetOccluderBounds(uid, occluder, transform, _entManager);
            if (b.Width <= 0 || b.Height <= 0) continue;
            if (!collectBounds.Intersects(b)) continue;

            _occluders.Add(new ShadowGeometry.ShadowOccluder { Bounds = b, Transform = transform });

            // only true rectangles participate in edge culling - a circle's
            // silhouette curves inward from its AABB corners, so culling it
            // like a rectangle could hide a real gap in the shadow
            if (occluder.Shape == OccluderShape.Rectangle)
                _scratchRectOccluders.Add(_occluders.Count - 1);
        }
        CullTouchingEntityEdges(_scratchRectOccluders);

        _lastOccluderBounds = collectBounds;
        _occludersDirty = false;

        // the occluder set (or just its collected membership) may have
        // changed, so every light's cached shadow geometry needs a rebuild
        _occluderGeneration++;
    }

    private void OnOccluderAdded(EntityUid uid, OccluderComponent comp, CompAddedEvent args)
        => _occludersDirty = true;

    private void OnOccluderRemoved(EntityUid uid, OccluderComponent comp, CompRemovedEvent args)
        => _occludersDirty = true;

    private void OnOccluderMoved(EntityUid uid, OccluderComponent comp, MoveEvent args)
        => _occludersDirty = true;

    private void OnPointLightRemoved(EntityUid uid, PointLightComponent comp, CompRemovedEvent args)
        => _shadowLightCache.Remove(uid);

    private void OnSpotLightRemoved(EntityUid uid, SpotLightComponent comp, CompRemovedEvent args)
        => _shadowLightCache.Remove(uid);

    // an entity occluder's edge is an interior seam - and gets culled - when
    // another occluder's opposite-facing edge sits exactly on it and fully
    // spans it (e.g. two wall segments placed edge to edge)
    private void CullTouchingEntityEdges(List<int> indices)
    {
        if (indices.Count < 2) return;

        RecycleEdgeBuckets(_edgeTop);
        RecycleEdgeBuckets(_edgeBottom);
        RecycleEdgeBuckets(_edgeLeft);
        RecycleEdgeBuckets(_edgeRight);

        foreach (var i in indices)
        {
            var b = _occluders[i].Bounds;
            Bucket(_edgeTop, b.Top, b.Left, b.Right, i);
            Bucket(_edgeBottom, b.Bottom, b.Left, b.Right, i);
            Bucket(_edgeLeft, b.Left, b.Top, b.Bottom, i);
            Bucket(_edgeRight, b.Right, b.Top, b.Bottom, i);
        }

        foreach (var i in indices)
        {
            var occ = _occluders[i];
            var b = occ.Bounds;
            occ.BlockedTop    = Covered(_edgeBottom, b.Top,    b.Left, b.Right, i);
            occ.BlockedBottom = Covered(_edgeTop,    b.Bottom, b.Left, b.Right, i);
            occ.BlockedLeft   = Covered(_edgeRight,  b.Left,   b.Top,  b.Bottom, i);
            occ.BlockedRight  = Covered(_edgeLeft,   b.Right,  b.Top,  b.Bottom, i);
            _occluders[i] = occ;
        }
    }

    private void RecycleEdgeBuckets(Dictionary<int, List<(int Lo, int Hi, int Idx)>> map)
    {
        foreach (var list in map.Values)
        {
            list.Clear();
            _edgeListPool.Push(list);
        }
        map.Clear();
    }

    private void Bucket(Dictionary<int, List<(int Lo, int Hi, int Idx)>> map, int key, int lo, int hi, int idx)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = _edgeListPool.Count > 0 ? _edgeListPool.Pop() : new List<(int, int, int)>();
        list.Add((lo, hi, idx));
    }

    private static bool Covered(Dictionary<int, List<(int Lo, int Hi, int Idx)>> opposite, int key, int lo, int hi, int selfIdx)
    {
        if (!opposite.TryGetValue(key, out var list)) return false;
        foreach (var (nlo, nhi, idx) in list)
        {
            if (idx != selfIdx && nlo <= lo && nhi >= hi) return true;
        }
        return false;
    }

    private void RenderShadowMap()
    {
        _shadowBuildMs = _shadowSetupMs = _shadowDrawMs = 0;

        if (!_shadowMap.Usable)
        {
            _shadowRanges.Clear();
            return;
        }

        var shadowMap = _shadowMap.Target!;
        var depthEffect = _shadowDepthEffect!;

        _buildTimer.Restart();
        BuildShadowGeometry();
        _buildTimer.Stop();
        _shadowBuildMs = _buildTimer.Elapsed.TotalMilliseconds;

        // no shadow-casting light this frame means nothing will ever sample
        // the shadow map, so skip the bind/clear entirely instead of paying
        // for it with nothing to draw
        if (_shadowRanges.Count == 0)
            return;

        // clear color = "no occluder", depth = far so the LESS test accepts the first write
        _buildTimer.Restart();
        GameClient.GraphicsDevice.SetRenderTarget(shadowMap);
        GameClient.GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer,
            new Color(255, 255, 0, 255), 1f, 0);
        _buildTimer.Stop();
        _shadowSetupMs = _buildTimer.Elapsed.TotalMilliseconds;

        // ranges exist but produced no vertices (occluders too far from every
        // light) - the clear above already made those rows read as unshadowed
        if (_shadowVB is null || _shadowIB is null)
            return;

        _buildTimer.Restart();

        var prevBlend = GameClient.GraphicsDevice.BlendState;
        var prevDepth = GameClient.GraphicsDevice.DepthStencilState;
        GameClient.GraphicsDevice.BlendState = BlendState.Opaque;
        GameClient.GraphicsDevice.DepthStencilState = ShadowDepthState;
        GameClient.GraphicsDevice.SetVertexBuffer(_shadowVB);
        GameClient.GraphicsDevice.Indices = _shadowIB;

        for (int shadowIdx = 0; shadowIdx < _shadowRanges.Count; shadowIdx++)
        {
            var range = _shadowRanges[shadowIdx];
            if (range.TriCount == 0)
                continue;

            GameClient.GraphicsDevice.Viewport = new Viewport(0, shadowIdx, shadowMap.Width, 1);
            _sdLightPos?.SetValue(range.LightPos);
            _sdLightRadius?.SetValue(range.LightRadius);

            // pass 0 = normal range, pass 1 = tail that wraps around the +-pi
            // seam. Skipped entirely when no edge of this light's geometry
            // straddles the seam, which is the usual case
            int passes = range.NeedsWrapPass ? 2 : 1;
            for (int wrapPass = 0; wrapPass < passes; wrapPass++)
            {
                _sdWrapPass?.SetValue((float)wrapPass);
                foreach (var pass in depthEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    // baseVertex offsets into this light's slice, so the index
                    // buffer stays a single 0-based quad pattern
                    GameClient.GraphicsDevice.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList, range.VertexOffset, 0, range.TriCount);
                }
            }
        }

        GameClient.GraphicsDevice.SetVertexBuffer(null);
        GameClient.GraphicsDevice.Indices = null;
        GameClient.GraphicsDevice.BlendState = prevBlend;
        GameClient.GraphicsDevice.DepthStencilState = prevDepth;

        _buildTimer.Stop();
        _shadowDrawMs = _buildTimer.Elapsed.TotalMilliseconds;
    }

    // builds one geometry range per shadow-casting light, holding only the
    // occluders that light actually reaches, and uploads them all in one go
    private void BuildShadowGeometry()
    {
        _shadowRanges.Clear();

        int count = Math.Min(_occluders.Count, MaxOccluderCap);
        if (count == 0) return;

        EnsureOccluderCapacity(count);

        int vIdx = 0;

        // the map is rounded up to a power of two and never shrinks, so the
        // budget is the cvar, not however many rows happen to be allocated
        int rows = Math.Min(_shadowMap.Height, _lighting.MaxShadowcastingLights);

        foreach (var entry in _lights)
        {
            if (!entry.CastShadows) continue;
            if (_shadowRanges.Count >= rows)
            {
                if (!_warnedShadowCap)
                {
                    Log.Warn(
                        $"LightingSystem: shadow cap ({rows}) reached, extra lights render " +
                        "without shadows. Raise MaxShadowcastingLights or cull more lights.");
                    _warnedShadowCap = true;
                }
                break;
            }

            int vertexCount;
            bool needsWrapPass;

            // this light's shadow shape only depends on its own pos/radius
            // and the occluders in reach - if none of that changed since it
            // was last built, reuse the cached vertices instead of redoing
            // the per-occluder distance test + edge decomposition
            if (_shadowLightCache.TryGetValue(entry.Uid, out var cached)
                && cached.Generation == _occluderGeneration
                && cached.LightPos == entry.WorldPos
                && cached.LightRadius == entry.Comp.Radius)
            {
                vertexCount = cached.VertexCount;
                needsWrapPass = cached.NeedsWrapPass;

                while (vIdx + vertexCount > _shadowVerts.Length && GrowShadowVerts()) { }

                if (vIdx + vertexCount > _shadowVerts.Length)
                {
                    // global vertex budget exhausted - drop this light's
                    // shadow for one frame rather than overrun the buffer
                    vertexCount = 0;
                    needsWrapPass = false;
                }
                else
                {
                    Array.Copy(cached.Vertices, 0, _shadowVerts, vIdx, vertexCount);
                }
            }
            else
            {
                ShadowGeometry.BuildResult result;
                while (true)
                {
                    result = ShadowGeometry.Build(
                        _occluders, count, _shadowVerts, vIdx, entry.WorldPos, entry.Comp.Radius);

                    // the array only fills up once per scene, then it's reused
                    if (!result.Truncated || !GrowShadowVerts())
                        break;
                }

                vertexCount = result.VertexCount;
                needsWrapPass = result.NeedsWrapPass;

                // stash a copy for next frame - the shared _shadowVerts slot
                // gets overwritten every frame, so this light needs its own
                if (!_shadowLightCache.TryGetValue(entry.Uid, out var slot) || slot.Vertices.Length < vertexCount)
                    slot.Vertices = new ShadowGeometry.OccluderVertex[vertexCount];
                Array.Copy(_shadowVerts, vIdx, slot.Vertices, 0, vertexCount);
                slot.Generation = _occluderGeneration;
                slot.LightPos = entry.WorldPos;
                slot.LightRadius = entry.Comp.Radius;
                slot.VertexCount = vertexCount;
                slot.NeedsWrapPass = needsWrapPass;
                _shadowLightCache[entry.Uid] = slot;
            }

            _shadowRanges.Add(new ShadowRange
            {
                LightPos = entry.WorldPos,
                LightRadius = entry.Comp.Radius,
                VertexOffset = vIdx,
                TriCount = vertexCount / 4 * 2,
                NeedsWrapPass = needsWrapPass,
            });

            vIdx += vertexCount;
        }

        if (vIdx == 0) return;

        EnsureShadowVertexBuffer(vIdx);
        _shadowVB!.SetData(_shadowVerts, 0, vIdx, SetDataOptions.Discard);
    }

    // the index buffer is sized for the worst-case single light, since
    // baseVertex makes every range index from 0
    private void EnsureOccluderCapacity(int occluderCount)
    {
        if (occluderCount <= _occluderCapacity && _shadowIB is not null)
            return;

        while (_occluderCapacity < occluderCount)
            _occluderCapacity *= 2;
        _occluderCapacity = Math.Min(_occluderCapacity, MaxOccluderCap);

        _shadowIB?.Dispose();
        _shadowIB = new IndexBuffer(GameClient.GraphicsDevice,
            IndexElementSize.SixteenBits, _occluderCapacity * 24, BufferUsage.WriteOnly);
        _shadowIB.SetData(BuildQuadIndices(_occluderCapacity * 4));
    }

    private bool GrowShadowVerts()
    {
        if (_shadowVerts.Length >= MaxShadowVertices)
            return false;

        int size = Math.Min(_shadowVerts.Length * 2, MaxShadowVertices);
        Array.Resize(ref _shadowVerts, size);
        return true;
    }

    private void EnsureShadowVertexBuffer(int vertexCount)
    {
        if (_shadowVB is not null && _shadowVBCapacity >= vertexCount)
            return;

        _shadowVBCapacity = Math.Max(_shadowVBCapacity, 256 * 16);
        while (_shadowVBCapacity < vertexCount)
            _shadowVBCapacity *= 2;

        _shadowVB?.Dispose();
        _shadowVB = new DynamicVertexBuffer(GameClient.GraphicsDevice,
            ShadowGeometry.OccluderVertex.Declaration, _shadowVBCapacity, BufferUsage.WriteOnly);
    }

    // 0,1,2 0,2,3 for every quad. Built once per capacity, the pattern
    // never changes
    private static short[] BuildQuadIndices(int quadCount)
    {
        var indices = new short[quadCount * 6];
        for (int q = 0; q < quadCount; q++)
        {
            int v = q * 4;
            int i = q * 6;
            indices[i]     = (short)v;
            indices[i + 1] = (short)(v + 1);
            indices[i + 2] = (short)(v + 2);
            indices[i + 3] = (short)v;
            indices[i + 4] = (short)(v + 2);
            indices[i + 5] = (short)(v + 3);
        }
        return indices;
    }
}
