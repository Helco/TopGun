using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static TopGun.SpanUtils;

namespace TopGun;

public readonly struct ScriptRootInstruction
{
    public enum ArgType
    {
        Value,
        Indirect,
        InstructionOffset
    }

    public readonly record struct Arg(int Value, ArgType Type, string Name="")
    {
        public Arg(int Value, bool IsIndirect, string Name = "") :
            this(Value, IsIndirect ? ArgType.Indirect : ArgType.Value, Name)
        {
        }
    }

    public int Offset { get; }
    public int EndOffset { get; }
    public ScriptOp Op { get; }
    public IReadOnlyList<Arg> Args { get; }
    public string? StringArg { get; }

    private readonly byte[] data;
    public ReadOnlySpan<byte> Data => data;

    public string ToStringWithoutData()
    {
        var text = new StringBuilder();
        text.Append(Offset.ToString("D4"));
        text.Append(": ");
        text.Append(Op);

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

            switch(arg.Type)
            {
                case ArgType.Value:
                    text.Append('#');
                    text.Append(arg.Value); 
                    break;
                case ArgType.Indirect:
                    text.Append('[');
                    text.Append(arg.Value);
                    text.Append(']');
                    break;
                case ArgType.InstructionOffset:
                    text.Append('$');
                    text.Append((Offset + arg.Value).ToString("D4"));
                    break;
                default: throw new NotImplementedException();
            }
        }

        if (StringArg != null)
        {
            text.Append(" \"");
            text.Append(StringArg);
            text.Append('\"');
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

    public ScriptRootInstruction(ref ReadOnlySpan<byte> script) : this(new SpanReader(script))
    {
        script = script[EndOffset..];
    }

    public ScriptRootInstruction(SpanReader reader) : this(ref reader) { }

    public ScriptRootInstruction(ref SpanReader reader)
    {
        Offset = reader.Position;
        Op = (ScriptOp)reader.ReadUShort();
        data = Array.Empty<byte>();
        bool valueInd;
        switch(Op)
        {
            case ScriptOp.RunMessage:
                var resIndex = reader.ReadInt();
                var resIndexInd = reader.ReadBool();
                var indirectArgMask = reader.ReadByte();
                var localScopeSize = reader.ReadByte();
                var argCount = reader.ReadByte();
                var args = new List<Arg>(2 + argCount)
                {
                    new(resIndex, resIndexInd, "resIndex"),
                    new(localScopeSize, false, "localScope")
                };
                for (int i = 0; i < argCount; i++)
                    args.Add(new(reader.ReadInt(), (indirectArgMask & (1 << i)) > 0));
                Args = args;
                break;

            case ScriptOp.Nop:
            case ScriptOp.ChangeSceneToTmpString:
            case ScriptOp.Exit:
            case ScriptOp.BackupIni:
                Args = Array.Empty<Arg>(); break;

            case ScriptOp.SetReg3E43:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), reader.ReadBool())
                };
                reader.ReadByte(); // unused
                break;

            case ScriptOp.CalcAngle:
                var target = reader.ReadInt();
                var x1 = reader.ReadInt();
                var y1 = reader.ReadInt();
                var x2 = reader.ReadInt();
                var y2 = reader.ReadInt();
                Args = new Arg[]
                {
                    new(target, false, "target"),
                    new(x1, reader.ReadBool(), "x1"),
                    new(y1, reader.ReadBool(), "y1"),
                    new(x2, reader.ReadBool(), "x2"),
                    new(y2, reader.ReadBool(), "y2")
                };
                break;

            case ScriptOp.SpriteBreakLoops:
                var sprite = reader.ReadInt();
                var toggle = reader.ReadByte();
                Args = new Arg[]
                {
                    new(sprite, reader.ReadBool(), "sprite"),
                    new(toggle, false)
                };
                break;

            case ScriptOp.BrowseEvents14:
                var rctX1 = reader.ReadInt();
                var rctY1 = reader.ReadInt();
                var rctX2 = reader.ReadInt();
                var rctY2 = reader.ReadInt();
                var unk1 = reader.ReadInt();
                var unk2 = reader.ReadInt();
                var unk3 = reader.ReadInt();
                var unk4 = reader.ReadInt();
                var unk5 = reader.ReadInt();
                var flag = reader.ReadByte();
                reader.ReadByte();
                var rctFlags = reader.ReadByte();
                reader.ReadByte();
                Args = new Arg[]
                {
                    new(rctX1, (rctFlags & 1) > 0, "rctX1"),
                    new(rctY1, (rctFlags & 2) > 0, "rctY1"),
                    new(rctX2, (rctFlags & 4) > 0, "rctX2"),
                    new(rctY2, (rctFlags & 8) > 0, "rctY2"),
                    new(unk1, reader.ReadBool()),
                    new(unk2, reader.ReadBool()),
                    new(unk3, reader.ReadBool()),
                    new(unk4, reader.ReadBool()),
                    new(unk5, false),
                    new(flag, false, "flag")
                };
                break;

            case ScriptOp.ClickRects16:
                unk1 = reader.ReadInt();
                unk2 = reader.ReadInt();
                unk3 = reader.ReadInt();
                rctX1 = reader.ReadInt();
                rctY1 = reader.ReadInt();
                rctX2 = reader.ReadInt();
                rctY2 = reader.ReadInt();
                var flag1 = reader.ReadByte();
                var flag2 = reader.ReadByte();
                var flag3 = reader.ReadByte();
                var unk1Ind = reader.ReadBool();
                var unk2Ind = reader.ReadBool();
                var unk3Ind = reader.ReadBool();
                rctFlags = reader.ReadByte();
                reader.ReadByte();
                Args = new Arg[]
                {
                    new(unk1, unk1Ind),
                    new(unk2, unk2Ind),
                    new(unk3, unk3Ind),
                    new(rctX1, (rctFlags & 1) > 0, "rctX1"),
                    new(rctY1, (rctFlags & 2) > 0, "rctY1"),
                    new(rctX2, (rctFlags & 4) > 0, "rctX2"),
                    new(rctY2, (rctFlags & 8) > 0, "rctY2"),
                    new(flag1, false, "flag1"),
                    new(flag2, false, "flag2"),
                    new(flag3, false, "flag3")
                };
                break;

            case ScriptOp.SpriteSetClipBox:
                rctX1 = reader.ReadInt();
                rctY1 = reader.ReadInt();
                rctX2 = reader.ReadInt();
                rctY2 = reader.ReadInt();
                rctFlags = reader.ReadByte();
                reader.ReadByte();
                Args = new Arg[]
                {
                    new(rctX1, (rctFlags & 1) > 0, "rctX1"),
                    new(rctY1, (rctFlags & 2) > 0, "rctY1"),
                    new(rctX2, (rctFlags & 4) > 0, "rctX2"),
                    new(rctY2, (rctFlags & 8) > 0, "rctY2"),
                };
                break;

            case ScriptOp.SpriteCombToBackground:
                sprite = reader.ReadInt();
                flag = reader.ReadByte();
                Args = new Arg[]
                {
                    new(sprite, reader.ReadBool(), "sprite"),
                    new(flag, false)
                };
                break;

            case ScriptOp.SetHitDetectTrigger23:
                unk1 = reader.ReadInt();
                unk2 = reader.ReadInt();
                unk3 = reader.ReadInt();
                unk4 = reader.ReadInt();
                unk5 = reader.ReadInt();
                flag1 = reader.ReadByte();
                flag2 = reader.ReadByte();
                flag3 = reader.ReadByte();
                var flag4 = reader.ReadByte();
                var flag5 = reader.ReadByte();
                var unk4Ind = reader.ReadBool(); // yes wrong order. :(
                var unk5Ind = reader.ReadBool();
                unk3Ind = reader.ReadBool();
                unk1Ind = reader.ReadBool();
                unk2Ind = reader.ReadBool();
                Args = new Arg[]
                {
                    new(unk1, unk1Ind, "unk1"),
                    new(unk2, unk2Ind, "unk2"),
                    new(unk3, unk3Ind, "unk3"),
                    new(unk4, unk4Ind, "unk4"),
                    new(unk5, unk5Ind, "unk5"),
                    new(flag1, false, "flag1"),
                    new(flag2, false, "flag2"),
                    new(flag3, false, "flag3"),
                    new(flag4, false, "flag4"),
                    new(flag5, false, "flag5"),
                };
                break;

            case ScriptOp.Cosine:
            case ScriptOp.Sine:
                target = reader.ReadInt();
                var value = reader.ReadInt();
                Args = new Arg[]
                {
                    new(target, false, "target"),
                    new(value, reader.ReadBool(), "value")
                };
                reader.ReadByte();
                break;

            case ScriptOp.SetCursor:
                Args = new Arg[]
                {
                    new(reader.ReadByte(), false)
                };
                reader.ReadByte();
                break;

            case ScriptOp.SetCursorPos28:
                var x = reader.ReadInt();
                var y = reader.ReadInt();
                Args = new Arg[]
                {
                    new(x, reader.ReadBool()),
                    new(y, reader.ReadBool())
                };
                break;

            case ScriptOp.DebugStr:
                value = reader.ReadInt();
                var stringId = reader.ReadInt();
                Args = new Arg[]
                {
                    new(stringId, reader.ReadBool(), "arg"),
                    new(value, false, "stringId")
                };
                reader.ReadBytes(1);
                break;

            case ScriptOp.DeleteIniSection:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), false, "stringId")
                };
                break;

            case ScriptOp.PickedSprite43:
                unk1 = reader.ReadInt();
                unk2 = reader.ReadInt();
                unk3 = reader.ReadInt();
                flag = reader.ReadByte();
                Args = new Arg[]
                {
                    new(unk1, reader.ReadBool()),
                    new(unk2, reader.ReadBool()),
                    new(unk3, reader.ReadBool()),
                    new(flag, false, "flag"),
                };
                break;

            case ScriptOp.StartTextInput:
                unk1 = reader.ReadInt();
                unk2 = reader.ReadInt();
                unk3 = reader.ReadInt();
                data = reader.ReadBytes(6).ToArray();
                Args = new Arg[]
                {
                    new(unk1, reader.ReadBool()),
                    new(unk2, reader.ReadBool()),
                    new(unk3, reader.ReadBool()),
                };
                reader.ReadByte();
                break;

            case ScriptOp.ChangeScene54:
                var nameBytes = reader.ReadBytes(256);
                flag1 = reader.ReadByte();
                flag2 = reader.ReadByte();
                StringArg = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
                Args = new Arg[]
                {
                    new(flag1, false),
                    new(flag2, false),
                };
                break;

            case ScriptOp.SetTmpString:
                StringArg = Encoding.UTF8.GetString(reader.ReadBytes(256)).TrimEnd('\0');
                Args = Array.Empty<Arg>();
                break;

            case ScriptOp.Fade:
            case ScriptOp.StopFade:
            case ScriptOp.SetCallScriptProcs62:
            case ScriptOp.SetErrFile:
                value = reader.ReadInt();
                Args = new Arg[]
                {
                    new(value, false)
                };
                break;

            case ScriptOp.SpriteGetBounds63:
                sprite = reader.ReadInt();
                var spriteInd = reader.ReadBool();
                reader.ReadByte();
                Args = new Arg[]
                {
                    new(sprite, spriteInd, "sprite"),
                    new(reader.ReadInt(), false),
                    new(reader.ReadInt(), false),
                    new(reader.ReadInt(), false),
                    new(reader.ReadInt(), false)
                };
                break;

            case ScriptOp.SpriteGetInfo64:
                sprite = reader.ReadInt();
                spriteInd = reader.ReadBool();
                reader.ReadByte();
                Args = new Arg[]
                {
                    new(sprite, spriteInd, "sprite"),
                    new(reader.ReadInt(), false),
                    new(reader.ReadInt(), false),
                    new(reader.ReadInt(), false),
                    new(reader.ReadInt(), false),
                    new(reader.ReadInt(), false),
                    new(reader.ReadInt(), false),
                    new(reader.ReadInt(), false),
                    new(reader.ReadInt(), false),
                    new(reader.ReadInt(), false)
                };
                break;

            case ScriptOp.GetDate:
                var dayOfWeek = reader.ReadInt();
                var month = reader.ReadInt();
                var day = reader.ReadInt();
                var year = reader.ReadInt();
                Args = new Arg[]
                {
                    new(dayOfWeek, false, "dayOfWeek"),
                    new(month, false, "month"),
                    new(day, false, "day"),
                    new(year, false, "year"),
                };
                break;

            case ScriptOp.GetLineIntersect:
                var targetX = reader.ReadInt();
                var targetY = reader.ReadInt();
                x1 = reader.ReadInt();
                y1 = reader.ReadInt();
                x2 = reader.ReadInt();
                y2 = reader.ReadInt();
                var x3 = reader.ReadInt();
                var y3 = reader.ReadInt();
                var x4 = reader.ReadInt();
                var y4 = reader.ReadInt();
                Args = new Arg[]
                {
                    new(targetX, false, "targetX"),
                    new(targetY, false, "targetY"),
                    new(x1, reader.ReadBool(), "x1"),
                    new(y1, reader.ReadBool(), "y1"),
                    new(x2, reader.ReadBool(), "x2"),
                    new(y2, reader.ReadBool(), "y2"),
                    new(x3, reader.ReadBool(), "x3"),
                    new(y3, reader.ReadBool(), "y3"),
                    new(x4, reader.ReadBool(), "x4"),
                    new(y4, reader.ReadBool(), "y4"),
                };
                break;

            case ScriptOp.SpriteGetPos:
                sprite = reader.ReadInt();
                spriteInd = reader.ReadBool();
                reader.ReadByte();
                Args = new Arg[]
                {
                    new(sprite, spriteInd, "sprite"),
                    new(reader.ReadInt(), false),
                    new(reader.ReadInt(), false),
                };
                break;

            case ScriptOp.GetClock:
                var hour = reader.ReadInt();
                var minute = reader.ReadInt();
                var second = reader.ReadInt();
                var tenthSecond = reader.ReadInt();
                Args = new Arg[]
                {
                    new(hour, false, "hour"),
                    new(minute, false, "minute"),
                    new(second, false, "second"),
                    new(tenthSecond, false, "tenthSecond"),
                };
                break;

            case ScriptOp.RunScriptIf:
                var then = reader.ReadInt();
                var @else = reader.ReadInt();
                var hasElse = reader.ReadByte();
                var thenInd = reader.ReadBool();
                var elseInd = reader.ReadBool();
                reader.ReadByte();
                var left = reader.ReadInt();
                var right = reader.ReadInt();
                var condOp = reader.ReadByte();
                var leftInd = reader.ReadBool();
                var rightInd = reader.ReadBool();
                reader.ReadByte();
                Args = new Arg[]
                {
                    new(then, thenInd, "then"),
                    new(@else, elseInd, "else"),
                    new(hasElse, false, "hasElse"),
                    new(left, leftInd, "left"),
                    new(right, rightInd, "right"),
                    new(condOp, false, "condOp")
                };
                break;

            case ScriptOp.JumpIf:
                var jumpSize = reader.ReadInt();
                left = reader.ReadInt();
                right = reader.ReadInt();
                condOp = reader.ReadByte();
                leftInd = reader.ReadBool();
                rightInd = reader.ReadBool();
                reader.ReadByte();
                Args = new Arg[]
                {
                    new(jumpSize, ArgType.InstructionOffset, "jumpSize"),
                    new(left, leftInd, "left"),
                    new(right, rightInd, "right"),
                    new(condOp, false, "condOp")
                };
                break;

            case ScriptOp.JumpIfCalc:
            case ScriptOp.JumpIfCalc_dup:
                @else = reader.ReadInt();
                then = reader.ReadInt();
                if (then <= 10 && @else <= 10)
                    throw new NotSupportedException("Cannot figure out size of JumpIfCalc root op");
                else if (then <= 0) data = reader.ReadBytes(@else - 10).ToArray();
                else if (@else <= 0) data = reader.ReadBytes(then - 10).ToArray();
                else data = reader.ReadBytes(Math.Min(then, @else) - 10).ToArray(); // in original there is only else regarded
                Args = new Arg[]
                {
                    new(then, ArgType.InstructionOffset, "then"),
                    new(@else, ArgType.InstructionOffset, "else")
                };
                break;

            case ScriptOp.RunScriptIfResLoaded:
                resIndex = reader.ReadInt();
                var scriptIndex = reader.ReadInt();
                Args = new Arg[]
                {
                    new(resIndex, reader.ReadBool(), "resIndex"),
                    new(scriptIndex, reader.ReadBool(), "scriptIndex")
                };
                break;

            case ScriptOp.BufferCDC_94:
                unk1 = reader.ReadInt();
                unk2 = reader.ReadInt();
                unk3 = reader.ReadInt();
                unk4 = reader.ReadInt();
                unk5 = reader.ReadInt();
                flag1 = reader.ReadByte();
                flag2 = reader.ReadByte();
                flag3 = reader.ReadByte();
                unk1Ind = reader.ReadBool();
                unk2Ind = reader.ReadBool();
                unk3Ind = reader.ReadBool();
                Args = new Arg[]
                {
                    new(unk1, unk1Ind, "unk1"),
                    new(unk2, unk2Ind, "unk2"),
                    new(unk3, unk3Ind, "unk3"),
                    new(unk4, false, "unk4"),
                    new(unk5, false, "unk5"),
                    new(flag1, false, "flag1"),
                    new(flag2, false, "flag2"),
                    new(flag3, false, "flag3"),
                };
                break;

            case ScriptOp.BufferCDC_96:
                unk1 = reader.ReadInt();
                unk2 = reader.ReadInt();
                flag1 = reader.ReadByte();
                flag2 = reader.ReadByte();
                flag3 = reader.ReadByte();
                unk1Ind = reader.ReadBool();
                unk2Ind = reader.ReadBool();
                reader.ReadByte();
                Args = new Arg[]
                {
                    new Arg(unk1, unk1Ind, "unk1"),
                    new Arg(unk2, unk2Ind, "unk2"),
                    new Arg(flag1, false, "flag1"),
                    new Arg(flag2, false, "flag2"),
                    new Arg(flag3, false, "flag3"),
                };
                break;

            case ScriptOp.BufferCDC_97:
                unk1 = reader.ReadInt();
                unk2 = reader.ReadInt();
                Args = new Arg[]
                {
                    new(unk1, reader.ReadBool()),
                    new(unk2, reader.ReadBool()),
                };
                break;

            case ScriptOp.BufferCDC_99:
                unk1 = reader.ReadInt();
                unk2 = reader.ReadInt();
                unk3 = reader.ReadInt();
                unk4 = reader.ReadInt();
                Args = new Arg[]
                {
                    new(unk1, reader.ReadBool()),
                    new(unk2, false, "out1"),
                    new(unk3, false, "out2"),
                    new(unk4, false, "out3"),
                };
                reader.ReadByte();
                break;

            case ScriptOp.SetBuffer3E5_101:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), false, "target"),
                    new(reader.ReadInt(), reader.ReadBool(), "value")
                };
                reader.ReadByte();
                break;

            case ScriptOp.Jump:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), ArgType.InstructionOffset, "jumpSize")
                };
                break;

            case ScriptOp.GetKeyState:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), false, "target"),
                    new(reader.ReadInt(), reader.ReadBool(), "key")
                };
                reader.ReadByte();
                break;

            case ScriptOp.SetReg3EE7_107:
                unk1 = reader.ReadInt();
                unk2 = reader.ReadInt();
                Args = new Arg[]
                {
                    new(unk1, reader.ReadBool(), "unk1"),
                    new(unk2, reader.ReadBool(), "unk2"),
                    new(reader.ReadByte(), false, "flag1"),
                    new(reader.ReadByte(), false, "flag2"),
                    new(reader.ReadByte(), false, "flag3"),
                    new(reader.ReadByte(), false, "flag4"),
                    new(reader.ReadByte(), false, "flag5"),
                };
                reader.ReadByte();
                break;

            case ScriptOp.DeleteTimer:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), reader.ReadBool())
                };
                reader.ReadByte();
                break;

            case ScriptOp.SpriteSetLevel:
                resIndex = reader.ReadInt();
                unk1 = reader.ReadInt();
                Args = new Arg[]
                {
                    new(resIndex, reader.ReadBool(), "resIndex"),
                    new(unk1, reader.ReadBool(), "unk1")
                };
                break;

            case ScriptOp.ClearTopMostSpriteNextFrame:
            case ScriptOp.Set3EF7_120:
            case ScriptOp.Set3F0B_138:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), reader.ReadBool())
                };
                reader.ReadByte();
                break;

            case ScriptOp.LoadResource:
            case ScriptOp.FreeResource:
            case ScriptOp.LoadPaletteResource:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), reader.ReadBool())
                };
                reader.ReadByte();
                break;

            case ScriptOp.AudioMute:
            case ScriptOp.AudioPlayCDTrack:
                Args = new Arg[]
                {
                    new(reader.ReadUShort(), false)
                };
                break;

            case ScriptOp.SpriteOffset:
                sprite = reader.ReadInt();
                x = reader.ReadInt();
                y = reader.ReadInt();
                Args = new Arg[]
                {
                    new(sprite, reader.ReadBool(), "sprite"),
                    new(x, reader.ReadBool(), "x"),
                    new(y, reader.ReadBool(), "y")
                };
                reader.ReadByte();
                break;

            case ScriptOp.AudioPlayMidi:
                unk1 = reader.ReadInt();
                unk1Ind = reader.ReadBool();
                reader.ReadByte();
                flag1 = reader.ReadByte();
                flag2 = reader.ReadByte();
                flag3 = reader.ReadByte();
                unk2Ind = reader.ReadBool();
                unk2 = reader.ReadInt();
                Args = new Arg[]
                {
                    new(unk1, unk1Ind, "unk1"),
                    new(flag1, false, "flag1"),
                    new(flag2, false, "flag2"),
                    new(flag3, false, "flag3"),
                    new(unk2, unk2Ind, "unk2"),
                };
                break;

            case ScriptOp.AudioPlayWave145:
                unk1 = reader.ReadInt();
                unk1Ind = reader.ReadBool();
                reader.ReadByte();
                unk2 = reader.ReadInt();
                unk3 = reader.ReadInt();
                unk4 = reader.ReadInt();
                unk5 = reader.ReadInt();
                var unk6 = reader.ReadInt();
                var unk7 = reader.ReadInt();
                flag1 = reader.ReadByte();
                flag2 = reader.ReadByte();
                flag3 = reader.ReadByte();
                var unk7Ind = reader.ReadBool();
                var unk8 = reader.ReadInt();
                Args = new Arg[]
                {
                    new (unk1, unk1Ind, "unk1"),
                    new (unk2, false, "unk2"),
                    new (unk3, false, "unk3"),
                    new (unk4, false, "unk4"),
                    new (unk5, false, "unk5"),
                    new (unk6, false, "unk6"),
                    new (flag1, false, "flag1"),
                    new (flag2, false, "flag2"),
                    new (flag3, false, "flag3"),
                    new (unk7, unk7Ind, "unk7"),
                    new (unk8, false, "unk8"),
                };
                break;

            case ScriptOp.Post:
                unk1 = reader.ReadInt();
                unk2 = reader.ReadInt();
                unk3 = reader.ReadInt();
                Args = new Arg[]
                {
                    new(unk1, reader.ReadBool()),
                    new(unk2, reader.ReadBool()),
                    new(unk3, reader.ReadBool()),
                };
                reader.ReadByte();
                break;

            case ScriptOp.RandomValue:
                target = reader.ReadInt();
                left = reader.ReadInt();
                right = reader.ReadInt();
                Args = new Arg[]
                {
                    new(target, false, "target"),
                    new(left, reader.ReadBool(), "left"),
                    new(right, reader.ReadBool(), "right")
                };
                break;

            case ScriptOp.RunRandomOf:
            case ScriptOp.RunNextOf:
                var scriptCount = reader.ReadByte();
                var except = reader.ReadByte();
                var runArrayOp = reader.ReadByte();
                var indFlags = reader.ReadByte();
                if (runArrayOp != 0)
                {
                    Args = new Arg[]
                    {
                        new(except, false, "except"),
                        new(runArrayOp, false, "runArrayOp"),
                        new(reader.ReadInt(), indFlags != 0, "arrayIndex")
                    };
                    reader.ReadBytes(5 * sizeof(int));
                }
                else
                {
                    Args = new Arg[]
                    {
                        new(except, false, "except"),
                        new(runArrayOp, false, "runArrayOp"),
                        new(scriptCount, false, "scriptCount"),
                        new(reader.ReadInt(), (indFlags & (1 << 0)) != 0),
                        new(reader.ReadInt(), (indFlags & (1 << 1)) != 0),
                        new(reader.ReadInt(), (indFlags & (1 << 2)) != 0),
                        new(reader.ReadInt(), (indFlags & (1 << 3)) != 0),
                        new(reader.ReadInt(), (indFlags & (1 << 4)) != 0),
                        new(reader.ReadInt(), (indFlags & (1 << 5)) != 0)
                    };
                }
                break;

            case ScriptOp.ReadIni:
            case ScriptOp.WriteIni:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), false, "value"),
                    new(reader.ReadInt(), false, "key"),
                    new(reader.ReadInt(), false, "topic"),
                    new(reader.ReadByte(), false, "encrypted"),
                    new(reader.ReadByte(), false, "isString"),
                };
                break;

            case ScriptOp.Return:
            case ScriptOp.RunCalc:
                Args = Array.Empty<Arg>();
                data = reader.ReadBytes(reader.ReadInt() - 2 - 4).ToArray();
                break;

            case ScriptOp.Animate:
                var animType = reader.ReadInt();
                var highResIndex = reader.ReadInt();
                var lowResIndex = reader.ReadInt();
                var highResInd = reader.ReadBool();
                var lowResInd = reader.ReadBool();
                var bkgIdx = reader.ReadByte();
                reader.ReadByte();
                var preAnimType = reader.ReadInt();
                var preAnimArg0 = reader.ReadInt();
                var preAnimArg1 = reader.ReadInt();
                Args = new Arg[]
                {
                    new(animType, false, "type"),
                    new(highResIndex, highResInd, "highRes"),
                    new(lowResIndex, lowResInd, "lowRes"),
                    new(bkgIdx, false, "bkgIdx"),
                    new(preAnimType, false, "preType"),
                    new(preAnimArg0, false, "preArg0"),
                    new(preAnimArg1, false, "preArg1")
                };
                break;

            case ScriptOp.SimpleCalc:
                target = reader.ReadInt();
                var opCount = reader.ReadInt();
                args = new List<Arg>(1 + opCount * 3)
                {
                    new(target, false, "target")
                };
                for (int i = 0; i < opCount; i++)
                {
                    value = reader.ReadInt();
                    var op = reader.ReadByte();
                    reader.ReadByte();
                    var negateValue = reader.ReadByte();
                    valueInd = reader.ReadBool();
                    args.Add(new(op, false, "op" + i));
                    args.Add(new(negateValue, false, "neg" + i));
                    args.Add(new(value, valueInd, "value" + i));
                }
                Args = args;
                reader.ReadBytes(8 * (3 - opCount));
                break;

            case ScriptOp.SpriteChangePalette179:
                var offset = reader.ReadInt();
                var count = reader.ReadInt();
                var reset = reader.ReadByte();
                var offsetInd = reader.ReadBool();
                var countInd = reader.ReadBool();
                var r = reader.ReadByte();
                var g = reader.ReadByte();
                var b = reader.ReadByte();
                reader.ReadByte();
                Args = new Arg[]
                {
                    new(offset, offsetInd, "offset"),
                    new(count, countInd, "count"),
                    new(reset, false, "reset"),
                    new(r, false, "r"),
                    new(g, false, "g"),
                    new(b, false, "b")
                };
                break;

            case ScriptOp.ExtractFile:
                resIndex = reader.ReadInt();
                target = reader.ReadInt();
                flag = reader.ReadByte();
                resIndexInd = reader.ReadBool();
                Args = new Arg[]
                {
                    new(resIndex, resIndexInd, "resIndex"),
                    new(target, false, "targetName"),
                    new(flag, false, "setAsWallpaper")
                };
                break;

            case ScriptOp.SpriteSetPos:
                sprite = reader.ReadInt();
                x = reader.ReadInt();
                y = reader.ReadInt();
                Args = new Arg[]
                {
                    new(sprite, reader.ReadBool(), "sprite"),
                    new(x, reader.ReadBool(), "x"),
                    new(y, reader.ReadBool(), "y")
                };
                reader.ReadByte();
                break;

            case ScriptOp.SetQueueAndHide:
                sprite = reader.ReadInt();
                var queue = reader.ReadInt();
                flag = reader.ReadByte();
                Args = new Arg[]
                {
                    new(sprite, reader.ReadBool(), "sprite"),
                    new(queue, reader.ReadBool(), "queue"),
                    new(flag, false, "hideSprite")
                };
                reader.ReadByte();
                break;

            case ScriptOp.SetString:
                target = reader.ReadInt();
                stringId = reader.ReadInt();
                reader.ReadByte();
                var stringIdInd = reader.ReadBool();
                var formatCount = reader.ReadByte();
                reader.ReadBytes(3);
                args = new List<Arg>(2 + formatCount * 2)
                {
                    new(target, false, "target"),
                    new(stringId, stringIdInd, "string")
                };
                for (int i = 0; i < formatCount; i++)
                {
                    value = reader.ReadInt();
                    flag = reader.ReadByte();
                    valueInd = reader.ReadBool();
                    args.Add(new(value, valueInd && flag != 0, "value" + i));
                    args.Add(new(flag, false, "isNumber" + i));
                }
                reader.ReadBytes(6 * (6 - formatCount));
                Args = args;
                break;

            case ScriptOp.SetText:
                target = reader.ReadInt();
                value = reader.ReadInt();
                stringId = reader.ReadInt();
                Args = new Arg[]
                {
                    new(target, reader.ReadBool(), "target"),
                    new(value, reader.ReadBool(), "value"),
                    new(stringId, reader.ReadBool(), "string"),
                    new(reader.ReadByte(), false, "useValue")
                };
                break;

            case ScriptOp.SetTextNum195:
                target = reader.ReadInt();
                value = reader.ReadInt();
                Args = new Arg[]
                {
                    new(target, reader.ReadBool(), "target"),
                    new(value, reader.ReadBool(), "value")
                };
                break;

            case ScriptOp.SetTimer:
                target = reader.ReadInt();
                scriptIndex = reader.ReadInt();
                var duration = reader.ReadInt();
                var durationInd = reader.ReadBool();
                var scriptIndexInd = reader.ReadBool();
                durationInd &= reader.ReadBool();
                var repeats = reader.ReadByte();
                Args = new Arg[]
                {
                    new(target, false, "target"),
                    new(scriptIndex, scriptIndexInd, "script"),
                    new(duration, durationInd, "duration"),
                    new(repeats, false, "repeats")
                };
                break;

            case ScriptOp.AudioSetVolume:
                flag1 = reader.ReadByte();
                flag2 = reader.ReadByte();
                valueInd = reader.ReadBool();
                reader.ReadByte();
                value = reader.ReadInt();
                Args = new Arg[]
                {
                    new(flag1, false, "flag1"),
                    new(flag2, false, "flag2"),
                    new(value, valueInd, "value"),
                };
                break;

            case ScriptOp.AudioStopCD:
                Args = Array.Empty<Arg>();
                reader.ReadUShort();
                break;

            case ScriptOp.AudioStopMidi:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), reader.ReadBool(), "target")
                };
                reader.ReadByte();
                break;

            case ScriptOp.AudioStopWave:
                target = reader.ReadInt();
                var targetInd = reader.ReadBool();
                reader.ReadByte();
                value = reader.ReadInt();
                Args = new Arg[]
                {
                    new(target, targetInd, "target"),
                    new(value, false, "value")
                };
                break;

            case ScriptOp.StringCompare:
            case ScriptOp.StringCompareI:
                target = reader.ReadInt();
                left = reader.ReadInt();
                right = reader.ReadInt();
                Args = new Arg[]
                {
                    new(target, false, "target"),
                    new(left, false, "left"),
                    new(right, false, "right"),
                };
                break;

            case ScriptOp.RunArrayOp217:
                resIndex = reader.ReadInt();
                var fallback = reader.ReadInt();
                var indexVar = reader.ReadInt();
                flag1 = reader.ReadByte();
                flag2 = reader.ReadByte();
                resIndexInd = reader.ReadBool();
                var fallbackInd = reader.ReadBool();
                Args = new Arg[]
                {
                    new(resIndex, resIndexInd, "array"),
                    new(indexVar, false, "indexVar"),
                    new(fallback, fallbackInd, "fallback"),
                    new(flag1, false, "useFallback"),
                    new(flag2, false, "incrementIndex")
                };
                break;

            case ScriptOp.Switch:
                value = reader.ReadInt();
                var offsetToCases = reader.ReadInt();
                var defaultJump = reader.ReadInt();
                var caseCount = reader.ReadUShort();
                reader.ReadByte();
                valueInd = reader.ReadBool();
                var caseScript = reader.RestBuffer[(offsetToCases - 18)..];
                args = new List<Arg>(2 + caseCount * 3)
                {
                    new(value, valueInd, "value"),
                    new(defaultJump, false, "defaultJump")
                };
                for (int i = 0; i < caseCount; i++)
                {
                    PopUShort(ref caseScript); // should always be Switch to act as noop
                    var compare = PopInt(ref caseScript);
                    var jump = PopInt(ref caseScript);
                    var compareInd = PopByte(ref caseScript) != 0;
                    PopByte(ref caseScript);
                    args.Add(new(compare, compareInd, "compare" + i));
                    args.Add(new(jump, ArgType.InstructionOffset, "jump" + i));
                }
                Args = args;
                break;

            case ScriptOp.CalcSwitch:
                var offsetToFirstBody = reader.ReadInt();
                offsetToCases = reader.ReadInt();
                defaultJump = reader.ReadInt();
                caseCount = reader.ReadUShort();
                data = reader.ReadBytes(offsetToFirstBody - 16).ToArray();
                caseScript = reader.RestBuffer[(offsetToCases - offsetToFirstBody)..];
                args = new List<Arg>(1 + caseCount * 3)
                {
                    new(defaultJump, false, "defaultJump")
                };
                for (int i = 0; i < caseCount; i++)
                {
                    PopUShort(ref caseScript); // should always be Switch to act as noop
                    var compare = PopInt(ref caseScript);
                    var jump = PopInt(ref caseScript);
                    var compareInd = PopByte(ref caseScript) != 0;
                    PopByte(ref caseScript);
                    args.Add(new(compare, compareInd, "compare" + i));
                    args.Add(new(jump, ArgType.InstructionOffset, "jump" + i));
                }
                Args = args;
                break;

            case ScriptOp.Case:
                reader.ReadBytes(10);
                Args = Array.Empty<Arg>();
                break;

            case ScriptOp.SpriteSwap222:
                sprite = reader.ReadInt();
                var sprite2 = reader.ReadInt();
                queue = reader.ReadInt();
                flag = reader.ReadByte();
                spriteInd = reader.ReadBool();
                var sprite2Ind = reader.ReadBool();
                var queueInd = reader.ReadBool();
                Args = new Arg[]
                {
                    new(sprite, spriteInd, "sprite1"),
                    new(sprite2, sprite2Ind, "sprite2"),
                    new(queue, queueInd, "queue"),
                    new(flag, false, "freeFlag")
                };
                break;

            case ScriptOp.Math224:
                targetX = reader.ReadInt();
                targetY = reader.ReadInt();
                unk1 = reader.ReadInt();
                unk2 = reader.ReadInt();
                unk3 = reader.ReadInt();
                unk4 = reader.ReadInt();
                unk5 = reader.ReadInt();
                unk6 = reader.ReadInt();
                unk7 = reader.ReadInt();
                unk1Ind = reader.ReadBool();
                unk2Ind = reader.ReadBool();
                unk3Ind = reader.ReadBool();
                unk4Ind = reader.ReadBool();
                unk5Ind = reader.ReadBool();
                var unk6Ind = reader.ReadBool();
                unk7Ind = reader.ReadBool();
                reader.ReadByte();
                Args = new Arg[]
                {
                    new(targetX, false, "targetX"),
                    new(targetY, false, "targetY"),
                    new(unk1, unk1Ind),
                    new(unk2, unk2Ind),
                    new(unk3, unk3Ind),
                    new(unk4, unk4Ind),
                    new(unk5, unk5Ind),
                    new(unk6, unk6Ind),
                    new(unk7, unk7Ind),
                };
                break;

            case ScriptOp.SpriteIsVisible:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), false, "target"),
                    new(reader.ReadInt(), reader.ReadBool(), "sprite")
                };
                reader.ReadByte();
                break;

            case ScriptOp.SetMapTransform:
                var zoom = reader.ReadInt();
                var offsetX = reader.ReadInt();
                var offsetY = reader.ReadInt();
                Args = new Arg[]
                {
                    new(zoom, reader.ReadBool(), "zoom"),
                    new(offsetX, reader.ReadBool(), "offsetX"),
                    new(offsetY, reader.ReadBool(), "offsetY")
                };
                reader.ReadByte();
                break;

            default: throw new NotSupportedException($"Not supported operation: {Op}");
        }
        EndOffset = reader.Position;
    }
}
