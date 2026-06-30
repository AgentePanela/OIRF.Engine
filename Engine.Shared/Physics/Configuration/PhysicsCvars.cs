using Engine.Shared.Configuration;

namespace Engine.Shared.Physics.Configuration;

[CVarDefs]
public static class PhysicsCvars
{
    public static CVarDef<bool> CollisionMask = CVarDef.Create("physics.collisionmask", false);

    /// <summary>
    /// Change this to the game avreage collision entity size.
    /// </summary>
    public static CVarDef<int> CellSize
        = CVarDef.Create("physics.cellsize", 128);
}
