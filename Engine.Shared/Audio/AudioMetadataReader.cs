using System;
using System.IO;

namespace Engine.Shared.Audio;

/// <summary>
/// Reads Duration/SampleRate/Channels straight from an Ogg Vorbis container's page headers.
/// </summary>
public static class AudioMetadataReader
{
    public static bool TryRead(Stream stream, out AudioMetadata metadata)
    {
        metadata = default;

        if (!TryReadPageHeader(stream, out var granule, out var bodyLength))
            return false;

        Span<byte> idHeader = stackalloc byte[Math.Min(bodyLength, 30)];
        if (!ReadFully(stream, idHeader))
            return false;

        if (idHeader.Length < 16 || idHeader[0] != 1 ||
            idHeader[1] != (byte)'v' || idHeader[2] != (byte)'o' || idHeader[3] != (byte)'r' ||
            idHeader[4] != (byte)'b' || idHeader[5] != (byte)'i' || idHeader[6] != (byte)'s')
            return false; // first page isn't a Vorbis identification header

        int channels = idHeader[11];
        int sampleRate = BitConverter.ToInt32(idHeader.Slice(12, 4));
        if (sampleRate <= 0 || !Skip(stream, bodyLength - idHeader.Length))
            return false;

        long lastGranule = granule;
        while (TryReadPageHeader(stream, out granule, out bodyLength))
        {
            if (!Skip(stream, bodyLength))
                break;
            lastGranule = granule;
        }

        metadata = new AudioMetadata(TimeSpan.FromSeconds(lastGranule / (double)sampleRate), sampleRate, channels);
        return true;
    }


    // Reads one Ogg page's fixed header + segment table.
    private static bool TryReadPageHeader(Stream stream, out long granule, out int bodyLength)
    {
        granule = 0;
        bodyLength = 0;

        Span<byte> header = stackalloc byte[27];
        if (!ReadFully(stream, header))
            return false;

        if (header[0] != (byte)'O' || header[1] != (byte)'g' || header[2] != (byte)'g' || header[3] != (byte)'S')
            return false; // not a valid/aligned Ogg stream

        granule = BitConverter.ToInt64(header.Slice(6, 8));
        int segmentCount = header[26];

        Span<byte> segments = stackalloc byte[segmentCount];
        if (!ReadFully(stream, segments))
            return false;

        foreach (var seg in segments)
            bodyLength += seg;

        return true;
    }

    private static bool ReadFully(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer.Slice(total));
            if (read == 0)
                return false;
            total += read;
        }
        return true;
    }

    private static bool Skip(Stream stream, int count)
    {
        if (count <= 0)
            return true;

        if (stream.CanSeek)
        {
            if (stream.Position + count > stream.Length)
                return false;
            stream.Seek(count, SeekOrigin.Current);
            return true;
        }

        Span<byte> scratch = stackalloc byte[Math.Min(count, 4096)];
        int remaining = count;
        while (remaining > 0)
        {
            int chunk = Math.Min(remaining, scratch.Length);
            if (!ReadFully(stream, scratch.Slice(0, chunk)))
                return false;
            remaining -= chunk;
        }
        return true;
    }
}

/// <summary>
/// Metadata read straight from an audio file's container/header.
/// </summary>
public readonly struct AudioMetadata
{
    public readonly TimeSpan Duration;
    public readonly int SampleRate;
    public readonly int Channels;

    public AudioMetadata(TimeSpan duration, int sampleRate, int channels)
    {
        Duration = duration;
        SampleRate = sampleRate;
        Channels = channels;
    }
}