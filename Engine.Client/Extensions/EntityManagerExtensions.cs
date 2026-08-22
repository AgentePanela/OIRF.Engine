using System;
using System.ComponentModel;
using Engine.Client;
using Engine.Client.Graphics;
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
            if (system is not EntityDrawSystem eds)
                continue;

            if (eds.FreezeDraw)
                continue;

            // charge whatever this system submits (and culls) to its own name
            var stats = GameClient.RenderStats;
            var submitsBefore = stats.Current(RenderCounter.Submits);
            stats.BeginSystem(type.Name);

            var allocBefore = GC.GetAllocatedBytesForCurrentThread();
            entityManager._systemTimer.Restart();
            eds.Draw(dt);
            entityManager._systemTimer.Stop();

            stats.EndSystem();
            entityManager._sysProff.RecordDraw(
                type.Name,
                entityManager._systemTimer.Elapsed.TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - allocBefore,
                (int)(stats.Current(RenderCounter.Submits) - submitsBefore));
        }
    }
}