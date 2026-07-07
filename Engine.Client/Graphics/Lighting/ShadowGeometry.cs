using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Builds the occluder edge geometry for the shadow depth shader. Every
/// occluder AABB becomes 4 edges, every edge becomes a quad that the vertex
/// shader stretches across the shadow map row. The geometry doesn't depend
/// on the light, so it's built once per frame and drawn for every light.
/// Indices are a fixed 0,1,2 0,2,3 pattern owned by the LightingSystem.
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
    /// Fills the vertex array from the occluder list (16 verts per occluder).
    /// Returns the number of vertices written; stops early if the array
    /// runs out of room.
    /// </summary>
    public static int Build(
        IReadOnlyList<(Rectangle Bounds, TransformComponent Transform)> occluders,
        OccluderVertex[] destVertices)
    {
        int vIdx = 0;
        int vCap = destVertices.Length;

        for (int o = 0; o < occluders.Count; o++)
        {
            if (vIdx + 16 > vCap)
                break;

            var bounds = occluders[o].Bounds;

            float x0 = bounds.Left;
            float y0 = bounds.Top;
            float x1 = bounds.Right;
            float y1 = bounds.Bottom;

            // unrolled so we don't allocate an edge array per occluder
            for (int e = 0; e < 4; e++)
            {
                float ax, ay, bx, by;
                switch (e)
                {
                    case 0: ax = x0; ay = y0; bx = x1; by = y0; break; // top
                    case 1: ax = x1; ay = y0; bx = x1; by = y1; break; // right
                    case 2: ax = x1; ay = y1; bx = x0; by = y1; break; // bottom
                    default: ax = x0; ay = y1; bx = x0; by = y0; break; // left
                }

                var aPos = new Vector4(ax, ay, bx, by);
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(0, 0) };
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(1, 0) };
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(1, 1) };
                destVertices[vIdx++] = new OccluderVertex { aPos = aPos, subVertex = new Vector2(0, 1) };
            }
        }

        return vIdx;
    }
}
