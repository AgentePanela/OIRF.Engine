using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Builds the occluder edge geometry for the shadow depth shader. Every
/// occluder AABB becomes 4 edges, every edge becomes a quad (4 verts, 6
/// indices) that the vertex shader stretches across the shadow map row.
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
    /// Fills the vertex/index arrays from the occluder list. Returns the
    /// number of indices written; <paramref name="vertexCount"/> gets the
    /// number of vertices. Stops early if the arrays run out of room.
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

            float x0 = bounds.Left;
            float y0 = bounds.Top;
            float x1 = bounds.Right;
            float y1 = bounds.Bottom;

            // unrolled so we don't allocate an edge array per occluder
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
                    case 0: ax = x0; ay = y0; bx = x1; by = y0; break; // top
                    case 1: ax = x1; ay = y0; bx = x1; by = y1; break; // right
                    case 2: ax = x1; ay = y1; bx = x0; by = y1; break; // bottom
                    default: ax = x0; ay = y1; bx = x0; by = y0; break; // left
                }

                int vBase = vIdx;

                var aPos = new Vector4(ax, ay, bx, by);
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(0, 0) };
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(1, 0) };
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(1, 1) };
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(0, 1) };

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
