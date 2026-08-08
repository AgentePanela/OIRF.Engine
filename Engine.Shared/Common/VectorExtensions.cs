using Microsoft.Xna.Framework;

namespace Engine.Shared.Common;

public static class VectorExtensions
{
    public static Vector3 ToVector3(this Vector2 v)
        => new(v.X, 0f, v.Y);

    public static Vector2 ToVector2(this Vector3 v)
        => new(v.X, v.Z);
}
