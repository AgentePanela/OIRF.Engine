using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Builds the per-frame occluder EDGE geometry fed to the shadow depth
/// shader. For each occluder AABB we emit 4 edges. Each edge gets 4
/// sub-vertices (the corners of the angular strip in the shadow map).
/// The vertex shader picks the right one based on subVertex.xy:
///
///   subVertex.x = 0 → endpoint A,  1 → endpoint B
///   subVertex.y = 0 → top of the current shadow row,  1 → bottom
///
/// 4 edges × 4 verts = 16 vertices per occluder, 24 indices (6 tris per edge).
/// </summary>
internal static class ShadowGeometry
{
    /// <summary>
    /// Vertex format: aPos.xy = endpoint A, aPos.zw = endpoint B,
    /// subVertex.xy = (endpoint 0/1, near/far).
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
    /// Write per-occluder edge vertex data into <paramref name="destVertices"/>
    /// and per-triangle indices into <paramref name="destIndices"/>.
    /// Returns the number of indices written (multiple of 3).
    /// <paramref name="vertexCount"/> is set to the number of vertices actually
    /// written — pass it to DrawUserIndexedPrimitives instead of the full array
    /// length to avoid uploading stale data from prior frames.
    /// Stops at the destArrays bound if there isn't enough room.
    /// </summary>
    public static int Build(
        IReadOnlyList<(Rectangle Bounds, TransformComponent Transform)> occluders,
        OccluderVertex[] destVertices,
        short[] destIndices,
        out int vertexCount)
    {
        int vIdx = 0;
        int iIdx = 0;
        int vCap = destVertices.Length;
        int iCap = destIndices.Length;

        for (int o = 0; o < occluders.Count; o++)
        {
            var bounds = occluders[o].Bounds;

            // 4 AABB corners.
            float x0 = bounds.Left;
            float y0 = bounds.Top;
            float x1 = bounds.Right;
            float y1 = bounds.Bottom;

            // 4 edges (TL→TR, TR→BR, BR→BL, BL→TL). Hand-unrolled
            // (instead of an inner (A,B)[] loop) so we don't allocate a
            // 4-element tuple array per occluder per frame.
            for (int e = 0; e < 4; e++)
            {
                if (vIdx + 4 > vCap || iIdx + 6 > iCap)
                {
                    vertexCount = 0;
                    return iIdx;
                }

                float ax, ay, bx, by;
                switch (e)
                {
                    case 0: ax = x0; ay = y0; bx = x1; by = y0; break; // TL→TR
                    case 1: ax = x1; ay = y0; bx = x1; by = y1; break; // TR→BR
                    case 2: ax = x1; ay = y1; bx = x0; by = y1; break; // BR→BL
                    default: ax = x0; ay = y1; bx = x0; by = y0; break; // BL→TL
                }

                int vBase = vIdx;

                // subVertex = (endpoint 0/1, near/far)
                //   (0, 0) → A/top
                //   (1, 0) → B/top
                //   (1, 1) → B/bottom
                //   (0, 1) → A/bottom
                var aPos = new Vector4(ax, ay, bx, by);
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(0, 0) };
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(1, 0) };
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(1, 1) };
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(0, 1) };

                // Two triangles forming the quad (CCW): 0,1,2 and 0,2,3.
                short v0 = (short)vBase;
                short v1 = (short)(vBase + 1);
                short v2 = (short)(vBase + 2);
                short v3 = (short)(vBase + 3);

                destIndices[iIdx++] = v0;
                destIndices[iIdx++] = v1;
                destIndices[iIdx++] = v2;

                destIndices[iIdx++] = v0;
                destIndices[iIdx++] = v2;
                destIndices[iIdx++] = v3;
            }
        }

        vertexCount = vIdx;
        return iIdx;
    }
}
