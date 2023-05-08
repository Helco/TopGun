using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;

using static TopGun.SpanUtils;
using System.Net.WebSockets;
using System.Security.Cryptography;

namespace TopGun;

[Flags]
public enum BitmapFlags : uint
{
    SimpleRLE = 0x80
}

public unsafe class Bitmap
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Header32
    {
        public int Width;
        public int Height;
        public BitmapFlags Flags;
        public int Unk1;
        public int Unk2;
        public int Unk3;
    }

    private byte[] data;

    public int Width { get; }
    public int Height { get; }
    public BitmapFlags Flags { get; private set; }
    public ReadOnlySpan<byte> Data => data;

    public int AlignedWidth => (Width + 3) / 4 * 4;
    public int UnalignedRest => AlignedWidth - Width;

    public static bool IsSimpleRLE(ReadOnlySpan<byte> data) => (data[8] & 0x80) != 0;

    public Bitmap(string path) : this(File.ReadAllBytes(path)) { }

    public Bitmap(ReadOnlySpan<byte> data)
    {
        var header = MemoryMarshal.Cast<byte, Header32>(data)[0];
        data = data[sizeof(Header32)..];

        Width = header.Width;
        Height = header.Height;
        Flags = header.Flags;
        this.data = data.ToArray();
    }

    public void Expand()
    {
        if (Flags.HasFlag(BitmapFlags.SimpleRLE))
            ExpandSimpleRLE();
        else
            ExpandRLEwithLZW();
    }

    private void ExpandSimpleRLE()
    {
        var source = Data[(2 * Height)..];
        var newData = new byte[AlignedWidth * Height];
        var dest = newData.AsSpan();
        do
        {
            while (true)
            {
                var packetHeader = source[0];
                source = source[1..];
                if (packetHeader == 0)
                    break;
                else if (packetHeader < 0x80) // repeat packet
                {
                    dest.Slice(0, packetHeader).Fill(source[0]);
                    dest = dest[packetHeader..];
                    source = source[1..];
                }
                else // copy packet
                {
                    var size = (byte)~packetHeader;
                    if (size == 0)
                    {
                        size = source[0];
                        source = source[1..];
                    }
                    source.Slice(0, size).CopyTo(dest);
                    source = source[size..];
                    dest = dest[size..];
                }
            }
            dest = dest[UnalignedRest..];
        } while (source[0] != 0);

        if (source.Length != 1)
            throw new InvalidDataException("Unexpected data at end of source");
        if (dest.Length != 0)
            throw new InvalidDataException($"{dest.Length} pixels were not expanded");

        data = newData;
        Flags &= ~BitmapFlags.SimpleRLE;
    }

    private void ExpandRLEwithLZW()
    {
        var newData = new byte[AlignedWidth * Height];
        var source = Data;
        var dest = newData.AsSpan();

        while(true)
        {
            var packetHeader = PopUShort(ref source);
            var packetSize = packetHeader & 0x1fff;
            var packetType = packetHeader & 0xe000;
            if (packetType == 0xE000)
                break;
            switch(packetType)
            {
                case 0x0000:
                    source[..packetSize].CopyTo(dest);
                    dest = dest[packetSize..];
                    break;
                case 0x4000: ExpandRLEwithLZWPacket(10, ref dest, source[..packetSize]); break;
                case 0x6000: ExpandRLEwithLZWPacket(11, ref dest, source[..packetSize]); break;
                case 0x8000: ExpandRLEwithLZWPacket(12, ref dest, source[..packetSize]); break;
                default: throw new InvalidDataException($"Invalid packet type {packetType:X4}");
            }
            source = source[packetSize..];

        }
        if (!dest.IsEmpty)
            throw new InvalidDataException($"{dest.Length} pixels were not expanded");

        data = newData;
    }

    private void ExpandRLEwithLZWPacket(int bits, ref Span<byte> dest, ReadOnlySpan<byte> source)
    {
        while(!source.IsEmpty)
        {
            var subPacketSize = PopUShort(ref source);
            ExpandRLEwithLZWSubPacket(bits, ref dest, source[..subPacketSize]);
            source = source[subPacketSize..];
        }
    }

    private ref struct SymbolReader
    {
        private readonly uint bits;
        private ReadOnlySpan<byte> source;
        private uint nextBits;
        private uint bitsLeft;

        public SymbolReader(uint bits, ReadOnlySpan<byte> source)
        {
            this.bits = bits;
            this.source = source;
            nextBits = 0;
            bitsLeft = 0;
        }

        public ushort NextSymbol()
        {
            while (bitsLeft < bits)
            {
                nextBits <<= 8;
                nextBits |= PopByte(ref source);
                bitsLeft += 8;
            }
            bitsLeft -= bits;
            ushort result = (ushort)(nextBits >> (int)bitsLeft);
            nextBits &= (uint)(1 << (int)bitsLeft) - 1;
            return result;
        }

        public ushort EndSymbol => (ushort)((1 << (int)bits) - 1);
        public ushort MaxSymbols => (ushort)((1 << (int)bits) - 1);
    }

    private readonly record struct Symbol(byte data, byte length, ushort lastSymbol);

    private void ExpandRLEwithLZWSubPacket(int bits, ref Span<byte> dest, ReadOnlySpan<byte> source)
    {
        var symbols = new List<Symbol>(512);
        for (int i = 0; i < 256; i++)
            symbols.Add(new Symbol(0, 0, 0));
        var symbolReader = new SymbolReader((uint)bits, source);

        ushort symbol = symbolReader.NextSymbol();
        PushByte(ref dest, (byte)symbol);
        ushort prevSymbol = symbol;
        byte lastData = (byte)symbol;
        byte newLastData;

        while (true)
        {
            symbol = symbolReader.NextSymbol();
            if (symbol == symbolReader.EndSymbol)
                break;
            else if (symbol < symbols.Count)
                PushSymbolData(ref dest, symbol, out newLastData);
            else
            {
                PushSymbolData(ref dest, prevSymbol, out newLastData);
                PushByte(ref dest, lastData);
            }
            lastData = newLastData;

            if (symbols.Count < symbolReader.MaxSymbols)
                symbols.Add(new(lastData, (byte)(symbols[prevSymbol].length + 1), prevSymbol));
            prevSymbol = symbol;
        }

        void PushSymbolData(ref Span<byte> dest, ushort symbol, out byte lastData)
        {
            var length = symbols[symbol].length;
            for (int i = 0; i < length; i++)
            {
                var curSymbol = symbols[symbol];
                dest[length - i] = curSymbol.data;
                symbol = curSymbol.lastSymbol;
            }
            lastData = dest[0] = (byte)symbol;
            dest = dest[(length + 1)..];
        }
    }

}
