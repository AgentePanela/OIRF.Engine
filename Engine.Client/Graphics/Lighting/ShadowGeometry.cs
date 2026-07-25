using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Builds the occluder edge geometry for the shadow depth shader. Every
/// occluder AABB becomes 4 edges, every edge becomes a quad that the vertex
/// shader stretches across the shadow map row. Built once per shadow light,
/// with the occluders that light can't reach culled out, into a shared array
/// the LightingSystem draws as per-light ranges. Indices are a fixed
/// 0,1,2 0,2,3 pattern owned by the LightingSystem.
/// </summary>
internal static class ShadowGeometry
{
    /// <summary>
    /// aPos.xy = endpoint A, aPos.zw = endpoint B (world space).
    /// subVertex.x picks the endpoint (0/1), subVertex.y the row side (0/1).
    /// </summary>
    public struct OccluderVertex
    {
        public Vector4 aPos;
        public Vector2 subVertex;

        public const int SizeInBytes = 24;

        public static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector4, VertexElementUsage.Position, 0),
            new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));
    }

    /// <summary>
    /// An occluder AABB plus which of its 4 edges are interior seams shared with a
    /// touching neighbor. Blocked edges are skipped so adjacent occluders (e.g. two
    /// wall segments placed edge to edge) don't cast a shadow onto each other's seam.
    /// </summary>
    public struct ShadowOccluder
    {
        public Rectangle Bounds;
        public TransformComponent Transform;
        public bool BlockedTop, BlockedRight, BlockedBottom, BlockedLeft;
    }

    /// <summary>
    /// Outcome of a <see cref="Build"/> call for one light.
    /// </summary>
    public struct BuildResult
    {
        /// <summary>Vertices written, starting at the requested offset.</summary>
        public int VertexCount;

        /// <summary>
        /// True when at least one edge straddles the ±π seam, so the shadow
        /// pass has to run its second (wrap) pass for this light. False for
        /// the common case, which halves the light's draw calls.
        /// </summary>
        public bool NeedsWrapPass;

        /// <summary>The array ran out of room and occluders were dropped.</summary>
        public bool Truncated;
    }

    /// <summary>
    /// Fills the vertex array from the occluder list (up to 16 verts per occluder,
    /// fewer for occluders with blocked edges), skipping occluders outside the
    /// light's reach. Writes at <paramref name="destOffset"/> and never past the
    /// end of the array.
    /// </summary>
    public static BuildResult Build(
        IReadOnlyList<ShadowOccluder> occluders,
        int occluderCount,
        OccluderVertex[] destVertices,
        int destOffset,
        Vector2 lightPos,
        float lightRadius)
    {
        int vIdx = destOffset;
        int vCap = destVertices.Length;
        bool needsWrap = false;
        bool truncated = false;
        float radiusSq = lightRadius * lightRadius;

        for (int o = 0; o < occluderCount; o++)
        {
            if (vIdx + 16 > vCap)
            {
                truncated = true;
                break;
            }

            var occluder = occluders[o];
            var bounds = occluder.Bounds;

            float x0 = bounds.Left;
            float y0 = bounds.Top;
            float x1 = bounds.Right;
            float y1 = bounds.Bottom;

            // nearest point on the AABB to the light - outside the radius means
            // this occluder can't darken anything this light lights
            float nx = MathHelper.Clamp(lightPos.X, x0, x1);
            float ny = MathHelper.Clamp(lightPos.Y, y0, y1);
            float dx = lightPos.X - nx;
            float dy = lightPos.Y - ny;
            if (dx * dx + dy * dy > radiusSq)
                continue;

            // unrolled so we don't allocate an edge array per occluder
            for (int e = 0; e < 4; e++)
            {
                float ax, ay, bx, by;
                bool blocked;
                switch (e)
                {
                    case 0: ax = x0; ay = y0; bx = x1; by = y0; blocked = occluder.BlockedTop; break; // top
                    case 1: ax = x1; ay = y0; bx = x1; by = y1; blocked = occluder.BlockedRight; break; // right
                    case 2: ax = x1; ay = y1; bx = x0; by = y1; blocked = occluder.BlockedBottom; break; // bottom
                    default: ax = x0; ay = y1; bx = x0; by = y0; blocked = occluder.BlockedLeft; break; // left
                }

                if (blocked) continue;

                if (!needsWrap)
                    needsWrap = EdgeWraps(ax, ay, bx, by, lightPos);

                var aPos = new Vector4(ax, ay, bx, by);
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(0, 0) };
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(1, 0) };
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(1, 1) };
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(0, 1) };
            }
        }

        return new BuildResult
        {
            VertexCount = vIdx - destOffset,
            NeedsWrapPass = needsWrap,
            Truncated = truncated,
        };

    }

    // Mirrors the span test in ShadowDepth.fx's vertex shader - same atan2
    // expression, same normalization - so the CPU and the GPU agree on which
    // edges need the wrap pass. Missing one would drop a shadow slice, so the
    // threshold sits just under the shader's 1.0: erring towards running an
    // unnecessary wrap pass is free, erring the other way is a visible hole.
    private const float WrapThreshold = 0.999f;

    private static bool EdgeWraps(float ax, float ay, float bx, float by, Vector2 lightPos)
    {
        float angleA = MathF.Atan2(ay - lightPos.Y, -(ax - lightPos.X)) / MathF.PI;
        float angleB = MathF.Atan2(by - lightPos.Y, -(bx - lightPos.X)) / MathF.PI;
        float span = angleB - angleA;
        return span > WrapThreshold || span < -WrapThreshold;
    }
}
