using System;
using System.Threading;
using Engine.Shared.Configuration;
using Engine.Shared.IoC;

namespace Engine.Shared.Threading;

public sealed class ParallelManager : IParallelManager
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public int ParallelProcessCount { get; private set; } = 1;

    public ParallelManager()
    {
        IoCManager.ResolveDependencies(this);

        _cfg.Subs(ThreadingCvars.ParallelProcessCount, v => ParallelProcessCount = Math.Max(1, v));
    }

    public void ProcessNow(IJob job)
    {
        using var countdown = QueueJob(job);
        countdown.Wait();
    }

    public WaitHandle Process(IJob job)
        => QueueJob(job).WaitHandle;

    public void ProcessNow(IParallelRangeJob job, int amount)
    {
        using var countdown = QueueRange(job, amount);
        countdown.Wait();
    }

    public void ProcessSerialNow(IParallelRangeJob job, int amount)
    {
        if (amount > 0)
            job.ExecuteRange(0, amount);
    }

    public WaitHandle Process(IParallelRangeJob job, int amount)
        => QueueRange(job, amount).WaitHandle;

    private static CountdownEvent QueueJob(IJob job)
    {
        var countdown = new CountdownEvent(1);
        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            state.job.Execute();
            state.countdown.Signal();
        }, (job, countdown), preferLocal: true);
        return countdown;
    }

    // splits the range into batches and queues one work item per batch; batches below
    // MinimumBatchParallel just run inline, no thread pool involved.
    private CountdownEvent QueueRange(IParallelRangeJob job, int amount)
    {
        if (amount <= 0)
            return new CountdownEvent(0);

        var batchSize = Math.Max(1, job.BatchSize);
        var batchCount = (amount + batchSize - 1) / batchSize;

        if (batchCount < job.MinimumBatchParallel || ParallelProcessCount <= 1)
        {
            job.ExecuteRange(0, amount);
            return new CountdownEvent(0);
        }

        var countdown = new CountdownEvent(batchCount);
        for (var b = 0; b < batchCount; b++)
        {
            var start = b * batchSize;
            var end = Math.Min(start + batchSize, amount);
            ThreadPool.UnsafeQueueUserWorkItem(static state =>
            {
                state.job.ExecuteRange(state.start, state.end);
                state.countdown.Signal();
            }, (job, start, end, countdown), preferLocal: true);
        }

        return countdown;
    }
}
