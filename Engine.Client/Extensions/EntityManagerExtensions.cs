using System.ComponentModel;
using Engine.Client.Graphics;
using Engine.Shared.GameObjects;

[EditorBrowsable(EditorBrowsableState.Never)]
internal static class EntityManagerExtensions
{
    internal static void DrawSystems(this EntityManager entityManager, in DrawContext ctx)
    {
        foreach (var system in entityManager.OrderedSystems)
        {
            if (system is not IEntityDrawSystem eds)
                continue;

            if (eds.FreezeDraw)
                continue;

            entityManager._systemTimer.Restart();
            eds.Draw(ctx);
            entityManager._systemTimer.Stop();
            entityManager._sysProff.Record(system.GetType().Name, 0.0, entityManager._systemTimer.Elapsed.TotalMilliseconds);
        }
    }
}