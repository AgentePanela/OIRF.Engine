using System;
using Engine.Shared.Configuration;

namespace Engine.Shared.Threading;

[CVarDefs]
public static class ThreadingCvars
{
    /// <summary>
    /// How many batches <see cref="IParallelManager"/> can run concurrently. Read once at boot.
    /// </summary>
    public static CVarDef<int> ParallelProcessCount
        = CVarDef.Create("threading.parallelprocesscount", Environment.ProcessorCount);
}
