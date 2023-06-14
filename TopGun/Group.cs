using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TopGun;

public readonly struct Group
{
    public IReadOnlyList<uint> Children { get; }

    public Group(ref SpanReader reader) : this(reader.RestBuffer)
    {
        reader.Position = reader.Size;
    }

    public Group(SpanReader reader) : this(reader.RestBuffer) { }

    public Group(ReadOnlySpan<byte> children)
    {
        Children = MemoryMarshal.Cast<byte, uint>(children).ToArray();
    }

    public override string ToString() => "Children: " + string.Join(", ", Children);
}
