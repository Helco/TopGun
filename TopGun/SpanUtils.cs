using System;
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

    public static byte PopByte(ref ReadOnlySpan<byte> span) => PopStruct<byte>(ref span);
    public static bool PopBool(ref ReadOnlySpan<byte> span) => PopStruct<byte>(ref span) != 0;
    public static ushort PopUShort(ref ReadOnlySpan<byte> span) => PopStruct<ushort>(ref span);
    public static uint PopUInt(ref ReadOnlySpan<byte> span) => PopStruct<uint>(ref span);
    public static int PopInt(ref ReadOnlySpan<byte> span) => PopStruct<int>(ref span);

    public static ReadOnlySpan<byte> PopBytes(ref ReadOnlySpan<byte> span, int bytes)
    {
        var result = span[..bytes];
        span = span[bytes..];
        return result;
    }
}
