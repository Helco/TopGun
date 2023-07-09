using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TopGun;

internal static unsafe class SpanUtils
{
    public static T PopStruct<T>(ref ReadOnlySpan<byte> span) where T : unmanaged
    {
        var result = MemoryMarshal.Cast<byte, T>(span)[0];
        span = span[sizeof(T)..];
        return result;
    }

    public static void PushStruct<T>(ref Span<byte> span, T value) where T : unmanaged
    {
        MemoryMarshal.Cast<byte, T>(span)[0] = value;
        span = span[sizeof(T)..];
    }

    public static byte PopByte(ref ReadOnlySpan<byte> span) => PopStruct<byte>(ref span);
    public static bool PopBool(ref ReadOnlySpan<byte> span) => PopStruct<byte>(ref span) != 0;
    public static ushort PopUShort(ref ReadOnlySpan<byte> span) => PopStruct<ushort>(ref span);
    public static uint PopUInt(ref ReadOnlySpan<byte> span) => PopStruct<uint>(ref span);
    public static int PopInt(ref ReadOnlySpan<byte> span) => PopStruct<int>(ref span);

    public static void PushByte(ref Span<byte> span, byte value) => PushStruct(ref span, value);

    public static ReadOnlySpan<byte> PopBytes(ref ReadOnlySpan<byte> span, int bytes)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        var result = span[..bytes];
        span = span[bytes..];
        return result;
    }

    public static T RemoveLast<T>(this IList<T> list)
    {
        if (list.Count == 0)
            throw new ArgumentException("Given list is empty", nameof(list));
        var element = list[list.Count - 1];
        list.RemoveAt(list.Count - 1);
        return element;
    }

    public static void ShiftElement<T>(this T[] array, int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex) return;
        if (oldIndex < 0 || oldIndex >= array.Length) throw new ArgumentOutOfRangeException(nameof(oldIndex));
        if (newIndex < 0 || newIndex >= array.Length) throw new ArgumentOutOfRangeException(nameof(newIndex));

        T element = array[oldIndex];
        if (newIndex < oldIndex)
            Array.Copy(array, newIndex, array, newIndex + 1, oldIndex - newIndex);
        else
            Array.Copy(array, newIndex, array, newIndex - 1, newIndex - oldIndex);
        array[newIndex] = element;
    }
}

public unsafe ref struct SpanReader
{
    public readonly ReadOnlySpan<byte> totalBuffer;
    public ReadOnlySpan<byte> RestBuffer { get; private set; }
    private int position;

    public SpanReader(ReadOnlySpan<byte> buffer)
    {
        totalBuffer = RestBuffer = buffer;
        position = 0;
    }

    public bool EndOfSpan => RestBuffer.IsEmpty;

    public int Position
    {
        get => position;
        set
        {
            if (value < 0 || value > totalBuffer.Length)
                throw new ArgumentOutOfRangeException(nameof(value));
            position = value;
            RestBuffer = totalBuffer[value..];
        }
    }

    public int Size => totalBuffer.Length;
    public int SizeLeft => RestBuffer.Length;

    public T ReadStruct<T>() where T : unmanaged
    {
        var result = MemoryMarshal.Cast<byte, T>(RestBuffer)[0];
        RestBuffer = RestBuffer[sizeof(T)..];
        position += sizeof(T);
        return result;
    }

    public ReadOnlySpan<byte> ReadBytes(int bytes)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        var result = RestBuffer[..bytes];
        RestBuffer = RestBuffer[bytes..];
        position += bytes;
        return result;
    }

    public byte ReadByte() => ReadStruct<byte>();
    public bool ReadBool() => ReadStruct<byte>() != 0;
    public ushort ReadUShort() => ReadStruct<ushort>();
    public uint ReadUInt() => ReadStruct<uint>();
    public int ReadInt() => ReadStruct<int>();
}
