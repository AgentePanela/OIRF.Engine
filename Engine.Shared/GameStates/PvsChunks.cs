using System;
using Microsoft.Xna.Framework;

namespace Engine.Shared.GameStates;

public static class PvsChunks
{
    public static Point ToChunk(Vector2 position, int chunkSize)
        => new((int)MathF.Floor(position.X / chunkSize), (int)MathF.Floor(position.Y / chunkSize));

    /// <summary>
    /// Squared distance from <paramref name="point"/> to the nearest point of a chunk, zero when inside it.
    /// </summary>
    public static float DistanceSqToChunk(Vector2 point, int chunkX, int chunkY, int chunkSize)
    {
        float minX = chunkX * chunkSize, minY = chunkY * chunkSize;
        float maxX = minX + chunkSize, maxY = minY + chunkSize;

        var dx = MathF.Max(0f, MathF.Max(minX - point.X, point.X - maxX));
        var dy = MathF.Max(0f, MathF.Max(minY - point.Y, point.Y - maxY));

        return dx * dx + dy * dy;
    }
}
