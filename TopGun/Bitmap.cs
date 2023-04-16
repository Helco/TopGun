using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;

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

    public bool IsCompressed => Flags.HasFlag(BitmapFlags.SimpleRLE);
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

}
