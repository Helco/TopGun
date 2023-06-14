using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TopGun;

public readonly struct Cell
{
    public uint Bitmap { get; }
    public int OffsetX { get; }
    public int OffsetY { get; }

    public Cell(ReadOnlySpan<byte> script) : this(new SpanReader(script)) { }

    public Cell(SpanReader reader) : this(ref reader) { }

    public Cell(ref SpanReader reader)
    {
        Bitmap = reader.ReadUInt();
        OffsetX = reader.ReadInt();
        OffsetY = reader.ReadInt();
    }

    public override string ToString() =>
        $"{Bitmap} @ {OffsetX},{OffsetY}";
}
