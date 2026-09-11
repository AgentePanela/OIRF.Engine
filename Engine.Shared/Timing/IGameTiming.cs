using System;

namespace Engine.Shared.Timing;

/// <summary>
/// Tracks time for the game simulation.
/// </summary>
public interface IGameTiming
{
    /// <summary>
    /// The tick currently being simulated by server.
    /// </summary>
    GameTick CurTick { get; }

    /// <summary>
    /// How many ticks the simulation runs per second.
    /// </summary>
    float TickRate { get; }

    /// <summary>
    /// Duration of a single tick, in seconds (1 / <see cref="TickRate"/>).
    /// </summary>
    float TickPeriod { get; }

    /// <summary>
    /// Real seconds elapsed since the previous Update.
    /// </summary>
    float DeltaTime { get; }

    /// <summary>
    /// Total real seconds elapsed since Update started running.
    /// </summary>
    double TotalTime { get; }

    /// <summary>
    /// How many Update calls happened during the last full second (CLIENT ONLY)
    /// </summary>
    int Fps { get; }

    /// <summary>
    /// Configures how many ticks run per second.
    /// </summary>
    internal void SetTickRate(float tickRate);

    /// <summary>
    /// Moves to the next tick.
    /// </summary>
    internal GameTick AdvanceTick();

    /// <summary>
    /// Forcibly sets the current tick.
    /// </summary>
    internal void SetTick(GameTick tick);

    internal void UpdateDeltaTime(float deltaSeconds);

    internal void UpdateFPS(float deltaSeconds);
}

public readonly struct GameTick : IEquatable<GameTick>, IComparable<GameTick>, ISpanFormattable
{
    public readonly uint Value;

    /// <summary>
    /// Tick zero. Nothing has simulated yet.
    /// </summary>
    public static readonly GameTick Zero = new(0);

    /// <summary>
    /// The first real simulation tick.
    /// </summary>
    public static readonly GameTick First = new(1);

    /// <summary>
    /// The highest possible tick. Used as a "never" sentinel.
    /// </summary>
    public static readonly GameTick MaxValue = new(uint.MaxValue);

    public GameTick(uint value)
    {
        Value = value;
    }

    public static GameTick operator +(GameTick tick, uint value) => new(tick.Value + value);
    public static GameTick operator -(GameTick tick, uint value) => new(tick.Value - value);

    public static uint operator -(GameTick left, GameTick right) => left.Value - right.Value;

    public static bool operator ==(GameTick left, GameTick right) => left.Value == right.Value;
    public static bool operator !=(GameTick left, GameTick right) => left.Value != right.Value;
    public static bool operator >(GameTick left, GameTick right) => left.Value > right.Value;
    public static bool operator <(GameTick left, GameTick right) => left.Value < right.Value;
    public static bool operator >=(GameTick left, GameTick right) => left.Value >= right.Value;
    public static bool operator <=(GameTick left, GameTick right) => left.Value <= right.Value;

    public int CompareTo(GameTick other) => Value.CompareTo(other.Value);

    public bool Equals(GameTick other) => Value == other.Value;

    public override bool Equals(object? obj)
    {
        return obj is GameTick other && Equals(other);
    }

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return Value.ToString(format, formatProvider);
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        return Value.TryFormat(destination, out charsWritten, format, provider);
    }
}