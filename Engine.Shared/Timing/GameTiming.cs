namespace Engine.Shared.Timing;

/// <inheritdoc cref="IGameTiming"/>
public sealed class GameTiming : IGameTiming
{
    public GameTick CurTick { get; private set; } = GameTick.First;

    public float TickRate { get; private set; } = 60f;

    public float TickPeriod => TickRate > 0f ? 1f / TickRate : 0f;

    public float DeltaTime { get; private set; }

    public double TotalTime { get; private set; }

    public int Fps { get; private set; }

    private int _frameCount;
    private double _fpsElapsed;

    void IGameTiming.SetTickRate(float tickRate)
    {
        TickRate = tickRate;
    }

    GameTick IGameTiming.AdvanceTick()
    {
        CurTick = new GameTick(CurTick.Value + 1);
        return CurTick;
    }

    void IGameTiming.SetTick(GameTick tick)
    {
        CurTick = tick;
    }

    void IGameTiming.UpdateDeltaTime(float deltaSeconds)
    {
        DeltaTime = deltaSeconds;
        TotalTime += deltaSeconds;
    }

    void IGameTiming.UpdateFPS(float deltaSeconds)
    {
        _frameCount++;
        _fpsElapsed += deltaSeconds;

        if (_fpsElapsed >= 1.0)
        {
            Fps = _frameCount;
            _frameCount = 0;
            _fpsElapsed -= 1.0;
        }
    }
}
