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

    private unsafe ref struct SymbolReader
    {
        private uint bits;
        private uint bitsForNextTime;
        private uint bitsLeft;
        private int nextMaskI;
        private ReadOnlySpan<byte> source;
        private fixed ushort bitsLeftMasks[8];

        public SymbolReader(uint bits, ReadOnlySpan<byte> source)
        {
            this.source = source;
            this.bits = bits < 9 || bits > 12 ? 11 : bits;
            nextMaskI = 0;
            bitsForNextTime = 0;
            bitsLeft = 0;
            switch(bits)
            {
                case 9:
                    bitsLeftMasks[0] = 0x0000;
                    bitsLeftMasks[1] = 0x7F00;
                    bitsLeftMasks[2] = 0x3F00;
                    bitsLeftMasks[3] = 0x1F00;
                    bitsLeftMasks[4] = 0x0F00;
                    bitsLeftMasks[5] = 0x0700;
                    bitsLeftMasks[6] = 0x0300;
                    bitsLeftMasks[7] = 0x0100;
                    break;
                case 10:
                    bitsLeftMasks[0] = 0x0000;
                    bitsLeftMasks[1] = 0x3F00;
                    bitsLeftMasks[2] = 0x0F00;
                    bitsLeftMasks[3] = 0x0300;
                    bitsLeftMasks[4] = 0x0000;
                    bitsLeftMasks[5] = 0x3F00;
                    bitsLeftMasks[6] = 0x0F00;
                    bitsLeftMasks[7] = 0x0300;
                    break;
                case 11:
                    bitsLeftMasks[0] = 0x0000;
                    bitsLeftMasks[1] = 0x1F00;
                    bitsLeftMasks[2] = 0x0300;
                    bitsLeftMasks[3] = 0x7F00;
                    bitsLeftMasks[4] = 0x0F00;
                    bitsLeftMasks[5] = 0x0100;
                    bitsLeftMasks[6] = 0x3F00;
                    bitsLeftMasks[7] = 0x0700;
                    break;
                case 12:
                    bitsLeftMasks[0] = 0x0000;
                    bitsLeftMasks[1] = 0x0F00;
                    bitsLeftMasks[2] = 0x0000;
                    bitsLeftMasks[3] = 0x0F00;
                    bitsLeftMasks[4] = 0x0000;
                    bitsLeftMasks[5] = 0x0F00;
                    bitsLeftMasks[6] = 0x0000;
                    bitsLeftMasks[7] = 0x0F00;
                    break;
                default: throw new InvalidProgramException();
            }
        }

        public ushort NextSymbol()
        {
            uint symbol = bitsLeftMasks[nextMaskI] & (bitsForNextTime << 8);
            nextMaskI = (nextMaskI + 1) % 8;
            symbol |= PopByte(ref source);
            bitsLeft += 8;
            if (bitsLeft < bits)
            {
                symbol <<= 8;
                symbol |= PopByte(ref source);
                bitsLeft += 8;
            }
            bitsLeft -= bits;
            bitsForNextTime = symbol;
            symbol >>= (int)bitsLeft;
            return (ushort)symbol;
        }

        public ushort EndSymbol => (ushort)((1 << (int)bits) - 1);
        public int MaxSymbols => EndSymbol - 1;
    }

    private const int LZWDictionarySize = 5021;

    private void ExpandRLEwithLZWSubPacket(int bits, ref Span<byte> dest, ReadOnlySpan<byte> source)
    {
        var symbolReader = new SymbolReader((uint)bits, source);
        var symbolLengths = new byte[LZWDictionarySize];
        var symbolDatas = new byte[LZWDictionarySize];
        var prevDataIndices = new ushort[LZWDictionarySize];
        var symbolCount = 256;

        ushort symbol = symbolReader.NextSymbol();
        if (symbol > 255)
            throw new InvalidDataException("First symbol cannot be greater than 255");
        PushByte(ref dest, (byte)symbol);
        ushort prevSymbol = symbol;
        byte lastData = (byte)symbol;

        while(true)
        {
            symbol = symbolReader.NextSymbol();
            if (symbol == symbolReader.EndSymbol)
                break;

            byte newLastData;
            if (symbol < symbolCount)
            {
                PushSymbolData(ref dest, symbol, out newLastData);
            }
            else
            {
                PushSymbolData(ref dest, prevSymbol, out newLastData);
                PushByte(ref dest, lastData);
            }
            lastData = newLastData;

            if (symbolCount <= symbolReader.MaxSymbols)
            {
                symbolDatas[symbolCount] = lastData;
                symbolLengths[symbolCount] = (byte)(symbolLengths[prevSymbol] + 1);
                prevDataIndices[symbolCount] = prevSymbol;
                symbolCount++;
            }
            prevSymbol = symbol;
        }

        void PushSymbolData(ref Span<byte> dest, ushort symbol, out byte lastData)
        {
            if (symbol < 256)
            {
                PushByte(ref dest, (byte)symbol);
                lastData = (byte)symbol;
                return;
            }

            var length = symbolLengths[symbol];
            for (int i = 0; i < length; i++)
            {
                dest[length - i] = symbolDatas[symbol];
                symbol = prevDataIndices[symbol];
            }
            //if (symbol > 255)
                //throw new InvalidDataException("Last symbol in string cannot be greather than 255");
            lastData = dest[0] = (byte)symbol;
            dest = dest[(length + 1)..];
        }
    }

}
