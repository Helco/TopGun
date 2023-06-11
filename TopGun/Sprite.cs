using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp.PixelFormats;

namespace TopGun;

public enum SpritePickableMode : byte
{
    AlwaysPickable,
    PickableIfVisible,
    NeverPickable
}

public readonly struct Sprite
{
    public int Level { get; }
    public bool IsRectPickable { get; }
    public bool IsTopMost { get; }
    public SpritePickableMode PickableMode { get; }
    public bool IsScrollable { get; }
    public bool IsClickable { get; }
    public int ClickScriptIndex { get; }
    public int ClickScriptArg { get; }
    public bool IsDraggable { get; }
    public int DragScriptIndex { get; }
    public IReadOnlyList<uint> Resources { get; }
    public IReadOnlyList<Rgb24> Palette { get; }

    public Sprite(ReadOnlySpan<byte> script) : this(new SpanReader(script)) { }

    public Sprite(SpanReader reader) : this(ref reader) { }

    public Sprite(ref SpanReader reader)
    {
        ClickScriptIndex = reader.ReadInt();
        ClickScriptArg = reader.ReadInt();
        var resources = new uint [reader.ReadInt()];
        reader.ReadBytes(4);
        DragScriptIndex = reader.ReadInt();
        reader.ReadBytes(4);
        var palette = new Rgb24[reader.ReadInt()];
        Level = reader.ReadInt();
        IsClickable = reader.ReadBool();
        IsRectPickable = reader.ReadBool();
        IsDraggable = reader.ReadBool();
        IsTopMost = reader.ReadBool();
        PickableMode = (SpritePickableMode)reader.ReadByte();
        IsScrollable = reader.ReadBool();
        Resources = resources;
        Palette = palette;

        for (int i = 0; i < resources.Length; i++)
            resources[i] = reader.ReadUInt();
        if (resources.Length < 8)
            reader.ReadBytes(sizeof(uint) * (8 - resources.Length));
        for (int i = 0; i < palette.Length; i++)
        {
            palette[i].R = reader.ReadByte();
            palette[i].G = reader.ReadByte();
            palette[i].B = reader.ReadByte();
            reader.ReadByte();
        }
    }

    public string ToStringWithoutResources()
    {
        var result = new StringBuilder();
        result.AppendLine($"Level: {Level}");
        result.AppendLine($"RectPickable: {IsRectPickable} ({PickableMode})");
        result.AppendLine($"IsTopMost: {IsTopMost}");
        result.AppendLine($"IsScrollable: {IsScrollable}");
        result.AppendLine($"Clickable: {IsClickable} run {ClickScriptIndex} with {ClickScriptArg}");
        result.AppendLine($"Draggable: {IsDraggable} run {DragScriptIndex}");
        if (Palette.Any())
            result.AppendLine($"Palette: " + string.Join(", ", Palette));
        return result.ToString();
    }

    public override string ToString()
    {
        var result = new StringBuilder();
        result.Append(ToStringWithoutResources());
        result.AppendLine("Resources: " + string.Join(", ", Resources));
        return result.ToString();
    }
}
