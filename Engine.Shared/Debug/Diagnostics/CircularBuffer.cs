using System;

namespace Engine.Shared.Debug.Diagnostics;

/// <summary>
/// stores time info of each system per 60 frames. this just exist to not create a lot of objects.
/// </summary>
/// <typeparam name="T"></typeparam>
internal sealed class CircularBuffer<T> where T : struct
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    public int Capacity => _buffer.Length;
    public int Count => _count;

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _buffer = new T[capacity];
    }

    public void Add(T value)
    {
        _buffer[_head] = value;
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length)
            _count++;
    }

    /// <summary>
    /// Iterate samples from oldest to newest.
    /// </summary>
    public ReadOnlySpan<T> AsSpan()
         => _buffer.AsSpan(0, _count);


    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new IndexOutOfRangeException();

            // Oldest sample is at (_head - _count + buffer.Length) % buffer.Length
            int start = (_head - _count + _buffer.Length) % _buffer.Length;
            return _buffer[(start + index) % _buffer.Length];
        }
    }
}

internal static class CircularBufferExtensions
{
    public static double Average(this CircularBuffer<double> buffer)
    {
        if (buffer.Count == 0)
            return 0.0;

        double sum = 0.0;
        for (int i = 0; i < buffer.Count; i++)
            sum += buffer[i];

        return sum / buffer.Count;
    }

    public static double Max(this CircularBuffer<double> buffer)
    {
        if (buffer.Count == 0)
            return 0.0;

        double max = double.MinValue;
        for (int i = 0; i < buffer.Count; i++)
        {
            double v = buffer[i];
            if (v > max) max = v;
        }
        return max;
    }
}
