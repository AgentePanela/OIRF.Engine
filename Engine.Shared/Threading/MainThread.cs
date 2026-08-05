using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Engine.Shared.Threading;

/// <summary>
/// Tracks which thread the engine's main loop runs on and asserts calls stay on it.
/// </summary>
public static class MainThread
{
    private static int? _mainThreadId;
    private static readonly ConcurrentDictionary<int, byte> _safeThreads = new();

    /// <summary>
    /// Records the calling thread as the main thread. Call once at boot, before the game loop starts.
    /// </summary>
    public static void Capture()
    {
        _mainThreadId = Environment.CurrentManagedThreadId;
    }

    public static bool IsMainThread
        => _mainThreadId is null || _mainThreadId == Environment.CurrentManagedThreadId;

    public static bool IsSafeThread
        => _safeThreads.ContainsKey(Environment.CurrentManagedThreadId);

    /// <summary>
    /// Throws if called from a thread that's neither the captured main thread nor a registered
    /// <see cref="SafeThreads"/> thread. No-op in Release builds.
    /// </summary>
    [Conditional("DEBUG")]
    public static void AssertMainThread()
    {
        if (!IsMainThread && !IsSafeThread)
            throw new InvalidOperationException(
                $"This must be called from the main thread (called from thread {Environment.CurrentManagedThreadId}).");
    }

    /// <summary>
    /// Lets a background thread opt into passing <see cref="AssertMainThread"/>, for cases where
    /// it's provably the sole thread touching ECS state for a bounded stretch of work (e.g. boot).
    /// Always register/unregister the calling thread's own id from inside that thread — a thread id
    /// captured from outside (e.g. a Task's own Id, or before the work actually starts) doesn't match
    /// what AssertMainThread checks. Wrap in try/finally: leaving a thread registered after its work
    /// is done risks a later, unrelated ThreadPool work item reusing that same managed thread id and
    /// silently passing checks it shouldn't.
    /// </summary>
    public static class SafeThreads
    {
        public static void Add(int threadId) => _safeThreads[threadId] = 0;
        public static void Remove(int threadId) => _safeThreads.TryRemove(threadId, out _);
    }
}
