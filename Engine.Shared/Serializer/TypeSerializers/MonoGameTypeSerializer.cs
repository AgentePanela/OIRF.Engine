using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using NetSerializer;

namespace Engine.Shared.Serializer.TypeSerializers;

internal sealed class MonoGameTypeSerializer : IStaticTypeSerializer
{
    private static readonly Dictionary<Type, (string Write, string Read)> MethodNames = new()
    {
        [typeof(Vector2)] = (nameof(WriteVector2), nameof(ReadVector2)),
        [typeof(Vector3)] = (nameof(WriteVector3), nameof(ReadVector3)),
        [typeof(Vector4)] = (nameof(WriteVector4), nameof(ReadVector4)),
        [typeof(Point)] = (nameof(WritePoint), nameof(ReadPoint)),
        [typeof(Rectangle)] = (nameof(WriteRectangle), nameof(ReadRectangle)),
        [typeof(Color)] = (nameof(WriteColor), nameof(ReadColor)),
    };

    public bool Handles(Type type) => MethodNames.ContainsKey(type);

    public IEnumerable<Type> GetSubtypes(Type type) => Type.EmptyTypes;

    public MethodInfo GetStaticWriter(Type type)
        => typeof(MonoGameTypeSerializer).GetMethod(MethodNames[type].Write, BindingFlags.Public | BindingFlags.Static)!;

    public MethodInfo GetStaticReader(Type type)
        => typeof(MonoGameTypeSerializer).GetMethod(MethodNames[type].Read, BindingFlags.Public | BindingFlags.Static)!;

    public static void WriteVector2(Stream stream, Vector2 value)
    {
        WriteSingle(stream, value.X);
        WriteSingle(stream, value.Y);
    }

    public static void ReadVector2(Stream stream, out Vector2 value)
    {
        ReadSingle(stream, out var x);
        ReadSingle(stream, out var y);
        value = new Vector2(x, y);
    }

    public static void WriteVector3(Stream stream, Vector3 value)
    {
        WriteSingle(stream, value.X);
        WriteSingle(stream, value.Y);
        WriteSingle(stream, value.Z);
    }

    public static void ReadVector3(Stream stream, out Vector3 value)
    {
        ReadSingle(stream, out var x);
        ReadSingle(stream, out var y);
        ReadSingle(stream, out var z);
        value = new Vector3(x, y, z);
    }

    public static void WriteVector4(Stream stream, Vector4 value)
    {
        WriteSingle(stream, value.X);
        WriteSingle(stream, value.Y);
        WriteSingle(stream, value.Z);
        WriteSingle(stream, value.W);
    }

    public static void ReadVector4(Stream stream, out Vector4 value)
    {
        ReadSingle(stream, out var x);
        ReadSingle(stream, out var y);
        ReadSingle(stream, out var z);
        ReadSingle(stream, out var w);
        value = new Vector4(x, y, z, w);
    }

    public static void WritePoint(Stream stream, Point value)
    {
        WriteInt32(stream, value.X);
        WriteInt32(stream, value.Y);
    }

    public static void ReadPoint(Stream stream, out Point value)
    {
        ReadInt32(stream, out var x);
        ReadInt32(stream, out var y);
        value = new Point(x, y);
    }

    public static void WriteRectangle(Stream stream, Rectangle value)
    {
        WriteInt32(stream, value.X);
        WriteInt32(stream, value.Y);
        WriteInt32(stream, value.Width);
        WriteInt32(stream, value.Height);
    }

    public static void ReadRectangle(Stream stream, out Rectangle value)
    {
        ReadInt32(stream, out var x);
        ReadInt32(stream, out var y);
        ReadInt32(stream, out var width);
        ReadInt32(stream, out var height);
        value = new Rectangle(x, y, width, height);
    }

    public static void WriteColor(Stream stream, Color value)
    {
        stream.WriteByte(value.R);
        stream.WriteByte(value.G);
        stream.WriteByte(value.B);
        stream.WriteByte(value.A);
    }

    public static void ReadColor(Stream stream, out Color value)
        => value = new Color(ReadByteChecked(stream), ReadByteChecked(stream), ReadByteChecked(stream), ReadByteChecked(stream));

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, value);
        stream.Write(bytes);
    }

    private static void ReadInt32(Stream stream, out int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        ReadExact(stream, bytes);
        value = BitConverter.ToInt32(bytes);
    }

    private static void WriteSingle(Stream stream, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, value);
        stream.Write(bytes);
    }

    private static void ReadSingle(Stream stream, out float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        ReadExact(stream, bytes);
        value = BitConverter.ToSingle(bytes);
    }

    private static int ReadByteChecked(Stream stream)
    {
        var b = stream.ReadByte();
        if (b < 0)
            throw new EndOfStreamException();

        return b;
    }

    private static void ReadExact(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer.Slice(read));
            if (n == 0)
                throw new EndOfStreamException();

            read += n;
        }
    }
}
