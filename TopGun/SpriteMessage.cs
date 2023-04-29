using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TopGun;

public enum SpriteMessageType : ushort
{
    CellLoop = 1,
    SubRects2,
    Unused3,
    CompToBackground,
    MoveCurve,
    MessageLoop,
    OffsetAndFlip,
    Hide,
    MoveLinear,
    DelayedMove,
    Delay,
    SetPos,
    SetPriority,
    SetRedraw,
    SetMotionDuration,
    SetCellAnimation,
    SetSpeed,
    ShowCell,
    FreeResources, // also unused
    ChangeScene,
    RunRootOp,
    RunScript,
    WaitForMovie,
    Proc266
}

public readonly struct SpriteMessage
{
    public readonly record struct Arg(int Value, bool IsIndirect, string Name = "");

    public int Offset { get; }
    public int EndOffset { get; }
    public SpriteMessageType Type { get; }
    public IReadOnlyList<Arg> Args { get; }
    private readonly byte[] data;
    public ReadOnlySpan<byte> Data => data;

    public string ToStringWithoutData()
    {
        var text = new StringBuilder();
        text.Append(Offset.ToString("X4"));
        text.Append(": ");
        text.Append(Type);

        bool firstArg = true;
        foreach (var arg in Args)
        {
            if (firstArg)
                firstArg = false;
            else
                text.Append(',');
            text.Append(' ');

            if (arg.Name != "")
            {
                text.Append(arg.Name);
                text.Append(": ");
            }

            text.Append(arg.IsIndirect ? '[' : '#');
            text.Append(arg.Value);
            if (arg.IsIndirect)
                text.Append(']');
        }

        return text.ToString();
    }

    public override string ToString()
    {
        var result = ToStringWithoutData();
        if (data.Any())
            result += " " + string.Join("", data.Select(d => d.ToString("X2")));
        return result;
    }

    public SpriteMessage(ref ReadOnlySpan<byte> script) : this(new SpanReader(script))
    {
        script = script[EndOffset..];
    }

    public SpriteMessage(SpanReader reader) : this(ref reader) { }

    public SpriteMessage(ref SpanReader reader)
    {
        Offset = reader.Position;
        Type = (SpriteMessageType)reader.ReadUShort();
        data = Array.Empty<byte>();
        switch(Type)
        {
            case SpriteMessageType.CellLoop:
                var cellStart = reader.ReadInt();
                var cellStop = reader.ReadInt();
                reader.ReadInt();
                var duration = reader.ReadInt();
                Args = new Arg[]
                {
                    new(cellStart, reader.ReadBool(), "cellStart"),
                    new(cellStop, reader.ReadBool(), "cellStop"),
                    new(duration, reader.ReadBool(), "duration")
                };
                reader.ReadBool();
                break;
            case SpriteMessageType.SubRects2:
                duration = reader.ReadInt();
                var cellIndexCount = reader.ReadInt();
                reader.ReadBool();
                var durationInd = reader.ReadBool();
                reader.ReadBool();
                var cellIndicesInd = reader.ReadByte();
                var args = new List<Arg>(cellIndexCount + 1)
                {
                    new(duration, durationInd, "duration")
                };
                int i;
                for (i = 0; i < cellIndexCount; i++)
                    args.Add(new(reader.ReadInt(), (cellIndicesInd & (1 << i)) > 0, "cellIndex" + i));
                Args = args;
                break;
            case SpriteMessageType.Unused3:
            case SpriteMessageType.CompToBackground:
            case SpriteMessageType.Hide:
            case SpriteMessageType.ChangeScene:
                Args = Array.Empty<Arg>();
                break;
            case SpriteMessageType.MoveCurve:
                duration = reader.ReadInt();
                var isRelative = reader.ReadByte();
                var point1XInd = reader.ReadBool();
                var point1YInd = reader.ReadBool();
                var point2XInd = reader.ReadBool();
                var point2YInd = reader.ReadBool();
                durationInd = reader.ReadBool();
                Args = new Arg[]
                {
                    new(duration, durationInd, "duration"),
                    new(isRelative, false, "isRelative"),
                    new(reader.ReadInt(), point1XInd, "point1X"),
                    new(reader.ReadInt(), point1YInd, "point1Y"),
                    new(reader.ReadInt(), point2XInd, "point2X"),
                    new(reader.ReadInt(), point2YInd, "point2Y"),
                };
                reader.ReadBytes(8 * 4);
                break;
            case SpriteMessageType.MessageLoop:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), false, "loopsRemaining"),
                    new(reader.ReadInt(), false, "loopCount"),
                    new(reader.ReadInt(), false, "jumpOffset")
                };
                break;
            case SpriteMessageType.OffsetAndFlip:
                var flipX = reader.ReadInt();
                var flipY = reader.ReadInt();
                var offsetXInd = reader.ReadBool();
                var offsetYInd = reader.ReadBool();
                var flipXInd = reader.ReadBool();
                var flipYInd = reader.ReadBool();
                var offsetX = reader.ReadInt();
                var offsetY = reader.ReadInt();
                Args = new Arg[]
                {
                    new(flipX, flipXInd, "flipX"),
                    new(flipY, flipYInd, "flipY"),
                    new(offsetX, offsetXInd, "offsetX"),
                    new(offsetY, offsetYInd, "offsetY"),
                };
                break;
            case SpriteMessageType.MoveLinear:
                duration = reader.ReadInt();
                isRelative = reader.ReadByte();
                var durationIsSpeed = reader.ReadBool();
                point1XInd = reader.ReadBool();
                point1YInd = reader.ReadBool();
                durationInd = reader.ReadBool();
                reader.ReadBool();
                Args = new Arg[]
                {
                    new(duration, durationInd, durationIsSpeed ? "speed" : "duration"),
                    new(isRelative, false, "isRelative"),
                    new(reader.ReadInt(), point1XInd, "targetX"),
                    new(reader.ReadInt(), point1YInd, "targetY"),
                };
                reader.ReadBytes(6 * 4);
                break;
            case SpriteMessageType.DelayedMove:
                isRelative = reader.ReadByte();
                point1XInd = reader.ReadBool();
                point1YInd = reader.ReadBool();
                reader.ReadBool();
                Args = new Arg[]
                {
                    new(isRelative, false, "isRelative"),
                    new(reader.ReadInt(), point1XInd, "targetX"),
                    new(reader.ReadInt(), point1YInd, "targetY"),
                };
                break;
            case SpriteMessageType.Delay:
                duration = reader.ReadInt();
                reader.ReadBool();
                durationInd = reader.ReadBool();
                Args = new Arg[]
                {
                    new(duration, durationInd, "duration")
                };
                break;
            case SpriteMessageType.SetPos:
                var targetX = reader.ReadInt();
                var targetY = reader.ReadInt();
                isRelative = reader.ReadByte();
                point1XInd = reader.ReadBool();
                point1YInd = reader.ReadBool();
                reader.ReadBool();
                Args = new Arg[]
                {
                    new(isRelative, false, "isRelative"),
                    new(targetX, point1XInd, "targetX"),
                    new(targetY, point1YInd, "targetY"),
                };
                break;
            case SpriteMessageType.SetPriority:
            case SpriteMessageType.SetRedraw:
            case SpriteMessageType.ShowCell: 
                Args = new Arg[]
                {
                    new(reader.ReadInt(), false)
                };
                break;
            case SpriteMessageType.SetMotionDuration:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), reader.ReadBool())
                };
                reader.ReadBool();
                break;
            case SpriteMessageType.SetCellAnimation:
                var nextCell = reader.ReadInt();
                cellStart = reader.ReadInt();
                cellStop = reader.ReadInt();
                Args = new Arg[]
                {
                    new(nextCell, reader.ReadBool(), "nextCell"),
                    new(cellStart, reader.ReadBool(), "cellStart"),
                    new(cellStop, reader.ReadBool(), "cellStop"),
                };
                reader.ReadBool();
                break;
            case SpriteMessageType.SetSpeed:
                var speed = reader.ReadInt();
                duration = reader.ReadInt();
                Args = new Arg[]
                {
                    new(speed, reader.ReadBool(), "speed"),
                    new(duration, reader.ReadBool(), "duration")
                };
                break;
            case SpriteMessageType.RunRootOp:
                var messageSize = reader.ReadInt();
                data = reader.ReadBytes(messageSize - 2 - 4).ToArray();
                Args = Array.Empty<Arg>();
                break;
            case SpriteMessageType.RunScript:
                var script = reader.ReadInt();
                var argCount = reader.ReadInt();
                args = new List<Arg>(argCount + 1)
                {
                    new(script, false, "script")
                };
                for (i = 0; i < argCount; i++)
                    args.Add(new(reader.ReadInt(), false, "arg" + i));
                for (; i < 6; i++)
                    reader.ReadInt();
                Args = args;
                break;
            case SpriteMessageType.WaitForMovie:
                var resIndex = reader.ReadInt();
                var unk1 = reader.ReadInt();
                var unk2 = reader.ReadByte();
                var unk3 = reader.ReadByte();
                reader.ReadInt();
                Args = new Arg[]
                {
                    new(resIndex, false, "resIndex"),
                    new(unk1, false, "unk1"),
                    new(unk2, false, "unk2"),
                    new(unk3, false, "unk3"),
                };
                break;
            case SpriteMessageType.Proc266:
                var sprite = reader.ReadInt();
                var flag = reader.ReadInt();
                Args = new Arg[]
                {
                    new(sprite, reader.ReadBool(), "sprite"),
                    new(flag, reader.ReadBool(), "flag")
                };
                break;
            default: throw new NotSupportedException($"Not supported message: {Type}");
        }
        EndOffset = reader.Position;
    }
}
