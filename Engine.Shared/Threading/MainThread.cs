using System;
using System.Diagnostics;

namespace Engine.Shared.Threading;

/// <summary>
/// Tracks which thread the engine's main loop runs on and asserts calls stay on it.
/// </summary>
public static class MainThread
{
    private static int? _mainThreadId;

    /// <summary>
    /// Records the calling thread as the main thread. Call once at boot, before the game loop starts.
    /// </summary>
    public static void Capture()
    {
        _mainThreadId = Environment.CurrentManagedThreadId;
    }

    public static bool IsMainThread
        => _mainThreadId is null || _mainThreadId == Environment.CurrentManagedThreadId;

    /// <summary>
    /// Throws if called from a thread other than the captured main thread. No-op in Release builds.
    /// </summary>
    [Conditional("DEBUG")]
    public static void AssertMainThread()
    {
        if (!IsMainThread)
            throw new InvalidOperationException(
                $"This must be called from the main thread (called from thread {Environment.CurrentManagedThreadId}).");
    }
}
