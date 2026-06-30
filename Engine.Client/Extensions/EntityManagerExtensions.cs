using System.ComponentModel;
using Engine.Shared.GameObjects;

[EditorBrowsable(EditorBrowsableState.Never)]
internal static class EntityManagerExtensions
{
    internal static void Draw(this EntityManager entityManager, float dt)
    {
        entityManager.DrawSystems(dt);
    }

    internal static void DrawSystems(this EntityManager entityManager, float dt)
    {
        foreach ((var type, var system) in entityManager.Systems)
        {
            entityManager._systemTimer.Restart();
            if (system is not EntityDrawSystem eds)
                continue;
            
            eds.Draw(dt);
            entityManager._systemTimer.Stop();
            entityManager._sysProff.Record(type.Name, 0.0, entityManager._systemTimer.Elapsed.TotalMilliseconds);
        }
            
    }
}