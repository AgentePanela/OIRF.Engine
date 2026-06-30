using Microsoft.Xna.Framework;

/// <summary>
/// Immutable per-frame time snapshot used to drive game logic.
/// Find it in <code>GameClient.GameTime</code>
/// </summary>
// todo: static class maybe?
public sealed class GTime
{
    public int FrameCount { get; private set; }
    public int Fps { get; private set; }
    public double TotalTime { get; private set; }
    public float DeltaTime { get; private set; }
    private double _elapsedTime;

    internal void UpdateDelta(GameTime gt)
    {
        DeltaTime = (float)gt.ElapsedGameTime.TotalSeconds;
    }

    internal void UpdateFps(GameTime gt)
    {
        FrameCount++;
        _elapsedTime += gt.ElapsedGameTime.TotalSeconds;
        TotalTime += DeltaTime;

        if (_elapsedTime >= 1.0)
        {
            Fps = FrameCount;
            FrameCount = 0;
            _elapsedTime -= 1.0;
        }
    }
}
