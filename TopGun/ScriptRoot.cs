using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static TopGun.SpanUtils;

namespace TopGun;

public enum ScriptRootOp : ushort
{
    RunMessage = 1,
    Nop,
    SetReg = 4,
    CalcAngle = 11,
    SpriteBreakLoops = 13,
    BrowseEvents14,
    ClickRects16 = 16,
    SpriteSetClipBox = 17,
    SpriteCombToBackground = 20,
    Sub4_23 = 23,
    Cosine = 26,
    SetCursor,
    SetCursorPos,
    DebugStr = 30,
    DeleteIniSection = 35,
    CursorPos43 = 43,
    Text44,
    ChangeScene54 = 54,
    CloseWindow57 = 57,
    SetTmpString = 58,
    BkgTransparent60 = 60,
    BkgTransparent61,
    SetCallScriptProcs62,
    SpriteGetBounds = 63,
    SpriteGetInfo,
    GetDate = 66,
    GetLineIntersect = 72,
    SpriteGetPos = 77,
    GetClock = 83,
    RunScriptIf = 89,
    JumpIf,
    JumpIfCalc,
    RunScriptIfResLoaded = 92,
    BufferCDC_94 = 94,
    BufferCDC_96 = 96,
    BufferCDC_97 = 97,
    BufferCDC_99 = 99,
    SetBuffer3E5_101 = 101,
    Jump = 104,
    Set1943_107 = 107,
    GetKeyState = 106,
    DeleteTimer = 109,
    SpriteSetLevel = 113,
    SetErrFile = 114,
    Send4C8_116 = 116,
    LoadResource = 117,
    Set3EF7_120 = 120,
    AudioMute = 122,
    SpriteOffset = 124,
    Set3F0B_138 = 138,
    AudioPlayCDTrack = 142,
    AudioPlayMidi,
    AudioPlayWave = 145,
    Post = 149,
    RandomValue = 153,
    RunRandomOf,
    ReadIni = 156,
    Return = 161,
    Exit = 162,
    BackupIni = 163,
    Animate = 164,
    RunNextOf = 167,
    SimpleCalc = 169,
    ComplexCalc = 175,
    SpriteChangePalette = 179,
    LoadPaletteResource = 180,
    ExtractFile = 183,
    SpriteSetPos = 184,
    SpriteSetQueue = 185,
    SetString = 192,
    SetText = 194,
    SetTextNum195 = 195,
    SetTimer = 196,
    AudioSetVolume = 198,
    Sine = 202,
    AudioStopCD = 210,
    AudioStopMidi,
    AudioStopWave,
    StringCompare,
    StringCompareI,
    RunArrayOp = 217,
    Switch,
    CalcSwitch,
    SpriteSwap222 = 222,
    FreeResource = 223,
    Math224 = 224,
    SpriteIsVisible = 225,
    JumpIfCalc_alt = 227, // seems to be actually duplicated
    WriteIni = 228,
    SetMapTransform = 230
}

public readonly struct ScriptRootInstruction
{
    public readonly record struct Arg(int Value, bool IsIndirect, string Name="");

    public ScriptRootOp Op { get; }
    public IReadOnlyList<Arg> Args { get; }
    public string? StringArg { get; }

    private readonly byte[] data;
    public ReadOnlySpan<byte> Data => data;

    public string ToStringWithoutData()
    {
        var text = new StringBuilder();
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

            text.Append(arg.IsIndirect ? '[' : '#');
            text.Append(arg.Value);
            if (arg.IsIndirect)
                text.Append(']');
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

    public ScriptRootInstruction(ref ReadOnlySpan<byte> script)
    {
        Op = (ScriptRootOp)PopUShort(ref script);
        data = Array.Empty<byte>();
        bool valueInd;
        switch(Op)
        {
            case ScriptRootOp.RunMessage:
                var resIndex = PopInt(ref script);
                var resIndexInd = PopBool(ref script);
                var indirectArgMask = PopByte(ref script);
                var localScopeSize = PopByte(ref script);
                var argCount = PopByte(ref script);
                var args = new List<Arg>(2 + argCount)
                {
                    new(resIndex, resIndexInd, "resIndex"),
                    new(localScopeSize, false, "localScope")
                };
                for (int i = 0; i < argCount; i++)
                    args.Add(new(PopInt(ref script), (indirectArgMask & (1 << i)) > 0));
                Args = args;
                break;

            case ScriptRootOp.Nop:
            case ScriptRootOp.CloseWindow57:
            case ScriptRootOp.Exit:
            case ScriptRootOp.BackupIni:
                Args = Array.Empty<Arg>(); break;

            case ScriptRootOp.SetReg:
                Args = new Arg[]
                {
                    new(PopInt(ref script), PopBool(ref script))
                };
                PopByte(ref script); // unused
                break;

            case ScriptRootOp.CalcAngle:
                var target = PopInt(ref script);
                var x1 = PopInt(ref script);
                var y1 = PopInt(ref script);
                var x2 = PopInt(ref script);
                var y2 = PopInt(ref script);
                Args = new Arg[]
                {
                    new(target, false, "target"),
                    new(x1, PopBool(ref script), "x1"),
                    new(y1, PopBool(ref script), "y1"),
                    new(x2, PopBool(ref script), "x2"),
                    new(y2, PopBool(ref script), "y2")
                };
                break;

            case ScriptRootOp.SpriteBreakLoops:
                var sprite = PopInt(ref script);
                var toggle = PopByte(ref script);
                Args = new Arg[]
                {
                    new(sprite, PopBool(ref script), "sprite"),
                    new(toggle, false)
                };
                break;

            case ScriptRootOp.BrowseEvents14:
                var rctX1 = PopInt(ref script);
                var rctY1 = PopInt(ref script);
                var rctX2 = PopInt(ref script);
                var rctY2 = PopInt(ref script);
                var unk1 = PopInt(ref script);
                var unk2 = PopInt(ref script);
                var unk3 = PopInt(ref script);
                var unk4 = PopInt(ref script);
                var unk5 = PopInt(ref script);
                var flag = PopByte(ref script);
                PopByte(ref script);
                var rctFlags = PopByte(ref script);
                PopByte(ref script);
                Args = new Arg[]
                {
                    new(rctX1, (rctFlags & 1) > 0, "rctX1"),
                    new(rctY1, (rctFlags & 2) > 0, "rctY1"),
                    new(rctX2, (rctFlags & 4) > 0, "rctX2"),
                    new(rctY2, (rctFlags & 8) > 0, "rctY2"),
                    new(unk1, PopBool(ref script)),
                    new(unk2, PopBool(ref script)),
                    new(unk3, PopBool(ref script)),
                    new(unk4, PopBool(ref script)),
                    new(unk5, false),
                    new(flag, false, "flag")
                };
                break;

            case ScriptRootOp.ClickRects16:
                unk1 = PopInt(ref script);
                unk2 = PopInt(ref script);
                unk3 = PopInt(ref script);
                rctX1 = PopInt(ref script);
                rctY1 = PopInt(ref script);
                rctX2 = PopInt(ref script);
                rctY2 = PopInt(ref script);
                var flag1 = PopByte(ref script);
                var flag2 = PopByte(ref script);
                var flag3 = PopByte(ref script);
                var unk1Ind = PopBool(ref script);
                var unk2Ind = PopBool(ref script);
                var unk3Ind = PopBool(ref script);
                rctFlags = PopByte(ref script);
                PopByte(ref script);
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

            case ScriptRootOp.SpriteSetClipBox:
                rctX1 = PopInt(ref script);
                rctY1 = PopInt(ref script);
                rctX2 = PopInt(ref script);
                rctY2 = PopInt(ref script);
                rctFlags = PopByte(ref script);
                PopByte(ref script);
                Args = new Arg[]
                {
                    new(rctX1, (rctFlags & 1) > 0, "rctX1"),
                    new(rctY1, (rctFlags & 2) > 0, "rctY1"),
                    new(rctX2, (rctFlags & 4) > 0, "rctX2"),
                    new(rctY2, (rctFlags & 8) > 0, "rctY2"),
                };
                break;

            case ScriptRootOp.SpriteCombToBackground:
                sprite = PopInt(ref script);
                flag = PopByte(ref script);
                Args = new Arg[]
                {
                    new(sprite, PopBool(ref script), "sprite"),
                    new(flag, false)
                };
                break;

            case ScriptRootOp.Sub4_23:
                unk1 = PopInt(ref script);
                unk2 = PopInt(ref script);
                unk3 = PopInt(ref script);
                unk4 = PopInt(ref script);
                unk5 = PopInt(ref script);
                flag1 = PopByte(ref script);
                flag2 = PopByte(ref script);
                flag3 = PopByte(ref script);
                var flag4 = PopByte(ref script);
                var flag5 = PopByte(ref script);
                var unk4Ind = PopBool(ref script); // yes wrong order. :(
                var unk5Ind = PopBool(ref script);
                unk3Ind = PopBool(ref script);
                unk1Ind = PopBool(ref script);
                unk2Ind = PopBool(ref script);
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

            case ScriptRootOp.Cosine:
            case ScriptRootOp.Sine:
                target = PopInt(ref script);
                var value = PopInt(ref script);
                Args = new Arg[]
                {
                    new(target, false, "target"),
                    new(value, PopBool(ref script), "value")
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.SetCursor:
                Args = new Arg[]
                {
                    new(PopByte(ref script), false)
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.SetCursorPos:
                var x = PopInt(ref script);
                var y = PopInt(ref script);
                Args = new Arg[]
                {
                    new(x, PopBool(ref script)),
                    new(y, PopBool(ref script))
                };
                break;

            case ScriptRootOp.DebugStr:
                value = PopInt(ref script);
                var stringId = PopInt(ref script);
                Args = new Arg[]
                {
                    new(stringId, PopBool(ref script), "arg"),
                    new(value, false, "stringId")
                };
                PopBytes(ref script, 1);
                break;

            case ScriptRootOp.DeleteIniSection:
                Args = new Arg[]
                {
                    new(PopInt(ref script), false, "stringId")
                };
                break;

            case ScriptRootOp.CursorPos43:
                unk1 = PopInt(ref script);
                unk2 = PopInt(ref script);
                unk3 = PopInt(ref script);
                flag = PopByte(ref script);
                Args = new Arg[]
                {
                    new(unk1, PopBool(ref script)),
                    new(unk2, PopBool(ref script)),
                    new(unk3, PopBool(ref script)),
                    new(flag, false, "flag"),
                };
                break;

            case ScriptRootOp.Text44:
                unk1 = PopInt(ref script);
                unk2 = PopInt(ref script);
                unk3 = PopInt(ref script);
                data = PopBytes(ref script, 6).ToArray();
                Args = new Arg[]
                {
                    new(unk1, PopBool(ref script)),
                    new(unk2, PopBool(ref script)),
                    new(unk3, PopBool(ref script)),
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.ChangeScene54:
                var nameBytes = PopBytes(ref script, 256);
                flag1 = PopByte(ref script);
                flag2 = PopByte(ref script);
                StringArg = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
                Args = new Arg[]
                {
                    new(flag1, false),
                    new(flag2, false),
                };
                break;

            case ScriptRootOp.SetTmpString:
                StringArg = Encoding.UTF8.GetString(PopBytes(ref script, 256)).TrimEnd('\0');
                Args = Array.Empty<Arg>();
                break;

            case ScriptRootOp.BkgTransparent60:
            case ScriptRootOp.BkgTransparent61:
            case ScriptRootOp.SetCallScriptProcs62:
            case ScriptRootOp.SetErrFile:
                value = PopInt(ref script);
                Args = new Arg[]
                {
                    new(value, false)
                };
                break;

            case ScriptRootOp.SpriteGetBounds:
                sprite = PopInt(ref script);
                var spriteInd = PopBool(ref script);
                PopByte(ref script);
                Args = new Arg[]
                {
                    new(sprite, spriteInd, "sprite"),
                    new(PopInt(ref script), false),
                    new(PopInt(ref script), false),
                    new(PopInt(ref script), false),
                    new(PopInt(ref script), false)
                };
                break;

            case ScriptRootOp.SpriteGetInfo:
                sprite = PopInt(ref script);
                spriteInd = PopBool(ref script);
                PopByte(ref script);
                Args = new Arg[]
                {
                    new(sprite, spriteInd, "sprite"),
                    new(PopInt(ref script), false),
                    new(PopInt(ref script), false),
                    new(PopInt(ref script), false),
                    new(PopInt(ref script), false),
                    new(PopInt(ref script), false),
                    new(PopInt(ref script), false),
                    new(PopInt(ref script), false),
                    new(PopInt(ref script), false),
                    new(PopInt(ref script), false)
                };
                break;

            case ScriptRootOp.GetDate:
                var dayOfWeek = PopInt(ref script);
                var month = PopInt(ref script);
                var day = PopInt(ref script);
                var year = PopInt(ref script);
                Args = new Arg[]
                {
                    new(dayOfWeek, false, "dayOfWeek"),
                    new(month, false, "month"),
                    new(day, false, "day"),
                    new(year, false, "year"),
                };
                break;

            case ScriptRootOp.GetLineIntersect:
                var targetX = PopInt(ref script);
                var targetY = PopInt(ref script);
                x1 = PopInt(ref script);
                y1 = PopInt(ref script);
                x2 = PopInt(ref script);
                y2 = PopInt(ref script);
                var x3 = PopInt(ref script);
                var y3 = PopInt(ref script);
                var x4 = PopInt(ref script);
                var y4 = PopInt(ref script);
                Args = new Arg[]
                {
                    new(targetX, false, "targetX"),
                    new(targetY, false, "targetY"),
                    new(x1, PopBool(ref script), "x1"),
                    new(y1, PopBool(ref script), "y1"),
                    new(x2, PopBool(ref script), "x2"),
                    new(y2, PopBool(ref script), "y2"),
                    new(x3, PopBool(ref script), "x3"),
                    new(y3, PopBool(ref script), "y3"),
                    new(x4, PopBool(ref script), "x4"),
                    new(y4, PopBool(ref script), "y4"),
                };
                break;

            case ScriptRootOp.SpriteGetPos:
                sprite = PopInt(ref script);
                spriteInd = PopBool(ref script);
                PopByte(ref script);
                Args = new Arg[]
                {
                    new(sprite, spriteInd, "sprite"),
                    new(PopInt(ref script), false),
                    new(PopInt(ref script), false),
                };
                break;

            case ScriptRootOp.GetClock:
                var hour = PopInt(ref script);
                var minute = PopInt(ref script);
                var second = PopInt(ref script);
                var tenthSecond = PopInt(ref script);
                Args = new Arg[]
                {
                    new(hour, false, "hour"),
                    new(minute, false, "minute"),
                    new(second, false, "second"),
                    new(tenthSecond, false, "tenthSecond"),
                };
                break;

            case ScriptRootOp.RunScriptIf:
                var then = PopInt(ref script);
                var @else = PopInt(ref script);
                var hasElse = PopByte(ref script);
                var thenInd = PopBool(ref script);
                var elseInd = PopBool(ref script);
                PopByte(ref script);
                var left = PopInt(ref script);
                var right = PopInt(ref script);
                var condOp = PopByte(ref script);
                var leftInd = PopBool(ref script);
                var rightInd = PopBool(ref script);
                PopByte(ref script);
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

            case ScriptRootOp.JumpIf:
                var jumpSize = PopInt(ref script);
                left = PopInt(ref script);
                right = PopInt(ref script);
                condOp = PopByte(ref script);
                leftInd = PopBool(ref script);
                rightInd = PopBool(ref script);
                PopByte(ref script);
                Args = new Arg[]
                {
                    new(jumpSize, false, "jumpSize"),
                    new(left, leftInd, "left"),
                    new(right, rightInd, "right"),
                    new(condOp, false, "condOp")
                };
                break;

            case ScriptRootOp.JumpIfCalc:
            case ScriptRootOp.JumpIfCalc_alt:
                then = PopInt(ref script);
                @else = PopInt(ref script);
                if (then <= 10 && @else <= 10)
                    throw new NotSupportedException("Cannot figure out size of JumpIfCalc root op");
                else if (then <= 0) data = PopBytes(ref script, @else - 10).ToArray();
                else if (@else <= 0) data = PopBytes(ref script, then - 10).ToArray();
                else data = PopBytes(ref script, Math.Min(then, @else) - 10).ToArray(); // in original there is only else regarded
                Args = new Arg[]
                {
                    new(then, false, "then"),
                    new(@else, false, "else")
                };
                break;

            case ScriptRootOp.RunScriptIfResLoaded:
                resIndex = PopInt(ref script);
                var scriptIndex = PopInt(ref script);
                Args = new Arg[]
                {
                    new(resIndex, PopBool(ref script), "resIndex"),
                    new(scriptIndex, PopBool(ref script), "scriptIndex")
                };
                break;

            case ScriptRootOp.BufferCDC_94:
                unk1 = PopInt(ref script);
                unk2 = PopInt(ref script);
                unk3 = PopInt(ref script);
                unk4 = PopInt(ref script);
                unk5 = PopInt(ref script);
                flag1 = PopByte(ref script);
                flag2 = PopByte(ref script);
                flag3 = PopByte(ref script);
                unk1Ind = PopBool(ref script);
                unk2Ind = PopBool(ref script);
                unk3Ind = PopBool(ref script);
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

            case ScriptRootOp.BufferCDC_96:
                unk1 = PopInt(ref script);
                unk2 = PopInt(ref script);
                flag1 = PopByte(ref script);
                flag2 = PopByte(ref script);
                flag3 = PopByte(ref script);
                unk1Ind = PopBool(ref script);
                unk2Ind = PopBool(ref script);
                PopByte(ref script);
                Args = new Arg[]
                {
                    new Arg(unk1, unk1Ind, "unk1"),
                    new Arg(unk2, unk2Ind, "unk2"),
                    new Arg(flag1, false, "flag1"),
                    new Arg(flag2, false, "flag2"),
                    new Arg(flag3, false, "flag3"),
                };
                break;

            case ScriptRootOp.BufferCDC_97:
                unk1 = PopInt(ref script);
                unk2 = PopInt(ref script);
                Args = new Arg[]
                {
                    new(unk1, PopBool(ref script)),
                    new(unk2, PopBool(ref script)),
                };
                break;

            case ScriptRootOp.BufferCDC_99:
                unk1 = PopInt(ref script);
                unk2 = PopInt(ref script);
                unk3 = PopInt(ref script);
                unk4 = PopInt(ref script);
                Args = new Arg[]
                {
                    new(unk1, PopBool(ref script)),
                    new(unk2, false, "out1"),
                    new(unk3, false, "out2"),
                    new(unk4, false, "out3"),
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.SetBuffer3E5_101:
                Args = new Arg[]
                {
                    new(PopInt(ref script), false, "target"),
                    new(PopInt(ref script), PopBool(ref script), "value")
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.Jump:
                Args = new Arg[]
                {
                    new(PopInt(ref script), false, "jumpSize")
                };
                break;

            case ScriptRootOp.GetKeyState:
                Args = new Arg[]
                {
                    new(PopInt(ref script), false, "target"),
                    new(PopInt(ref script), PopBool(ref script), "key")
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.Set1943_107:
                unk1 = PopInt(ref script);
                unk2 = PopInt(ref script);
                Args = new Arg[]
                {
                    new(unk1, PopBool(ref script), "unk1"),
                    new(unk2, PopBool(ref script), "unk2"),
                    new(PopByte(ref script), false, "flag1"),
                    new(PopByte(ref script), false, "flag2"),
                    new(PopByte(ref script), false, "flag3"),
                    new(PopByte(ref script), false, "flag4"),
                    new(PopByte(ref script), false, "flag5"),
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.DeleteTimer:
                Args = new Arg[]
                {
                    new(PopInt(ref script), PopBool(ref script))
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.SpriteSetLevel:
                resIndex = PopInt(ref script);
                unk1 = PopInt(ref script);
                Args = new Arg[]
                {
                    new(resIndex, PopBool(ref script), "resIndex"),
                    new(unk1, PopBool(ref script), "unk1")
                };
                break;

            case ScriptRootOp.Send4C8_116:
            case ScriptRootOp.Set3EF7_120:
            case ScriptRootOp.Set3F0B_138:
                Args = new Arg[]
                {
                    new(PopInt(ref script), PopBool(ref script))
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.LoadResource:
            case ScriptRootOp.FreeResource:
            case ScriptRootOp.LoadPaletteResource:
                Args = new Arg[]
                {
                    new(PopInt(ref script), PopBool(ref script))
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.AudioMute:
            case ScriptRootOp.AudioPlayCDTrack:
                Args = new Arg[]
                {
                    new(PopUShort(ref script), false)
                };
                break;

            case ScriptRootOp.SpriteOffset:
                sprite = PopInt(ref script);
                x = PopInt(ref script);
                y = PopInt(ref script);
                Args = new Arg[]
                {
                    new(sprite, PopBool(ref script), "sprite"),
                    new(x, PopBool(ref script), "x"),
                    new(y, PopBool(ref script), "y")
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.AudioPlayMidi:
                unk1 = PopInt(ref script);
                unk1Ind = PopBool(ref script);
                PopByte(ref script);
                flag1 = PopByte(ref script);
                flag2 = PopByte(ref script);
                flag3 = PopByte(ref script);
                unk2Ind = PopBool(ref script);
                unk2 = PopInt(ref script);
                Args = new Arg[]
                {
                    new(unk1, unk1Ind, "unk1"),
                    new(flag1, false, "flag1"),
                    new(flag2, false, "flag2"),
                    new(flag3, false, "flag3"),
                    new(unk2, unk2Ind, "unk2"),
                };
                break;

            case ScriptRootOp.AudioPlayWave:
                unk1 = PopInt(ref script);
                unk1Ind = PopBool(ref script);
                PopByte(ref script);
                unk2 = PopInt(ref script);
                unk3 = PopInt(ref script);
                unk4 = PopInt(ref script);
                unk5 = PopInt(ref script);
                var unk6 = PopInt(ref script);
                var unk7 = PopInt(ref script);
                flag1 = PopByte(ref script);
                flag2 = PopByte(ref script);
                flag3 = PopByte(ref script);
                var unk7Ind = PopBool(ref script);
                var unk8 = PopInt(ref script);
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

            case ScriptRootOp.Post:
                unk1 = PopInt(ref script);
                unk2 = PopInt(ref script);
                unk3 = PopInt(ref script);
                Args = new Arg[]
                {
                    new(unk1, PopBool(ref script)),
                    new(unk2, PopBool(ref script)),
                    new(unk3, PopBool(ref script)),
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.RandomValue:
                target = PopInt(ref script);
                left = PopInt(ref script);
                right = PopInt(ref script);
                Args = new Arg[]
                {
                    new(target, false, "target"),
                    new(left, PopBool(ref script), "left"),
                    new(right, PopBool(ref script), "right")
                };
                break;

            case ScriptRootOp.RunRandomOf:
            case ScriptRootOp.RunNextOf:
                var scriptCount = PopByte(ref script);
                var except = PopByte(ref script);
                var runArrayOp = PopByte(ref script);
                var indFlags = PopByte(ref script);
                if (runArrayOp != 0)
                {
                    Args = new Arg[]
                    {
                        new(except, false, "except"),
                        new(runArrayOp, false, "runArrayOp"),
                        new(PopInt(ref script), indFlags != 0, "arrayIndex")
                    };
                    PopBytes(ref script, 5 * sizeof(int));
                }
                else
                {
                    Args = new Arg[]
                    {
                        new(except, false, "except"),
                        new(runArrayOp, false, "runArrayOp"),
                        new(scriptCount, false, "scriptCount"),
                        new(PopInt(ref script), (indFlags & (1 << 0)) != 0),
                        new(PopInt(ref script), (indFlags & (1 << 1)) != 0),
                        new(PopInt(ref script), (indFlags & (1 << 2)) != 0),
                        new(PopInt(ref script), (indFlags & (1 << 3)) != 0),
                        new(PopInt(ref script), (indFlags & (1 << 4)) != 0),
                        new(PopInt(ref script), (indFlags & (1 << 5)) != 0)
                    };
                }
                break;

            case ScriptRootOp.ReadIni:
            case ScriptRootOp.WriteIni:
                Args = new Arg[]
                {
                    new(PopInt(ref script), false, "value"),
                    new(PopInt(ref script), false, "key"),
                    new(PopInt(ref script), false, "topic"),
                    new(PopByte(ref script), false, "encrypted"),
                    new(PopByte(ref script), false, "isString"),
                };
                break;

            case ScriptRootOp.Return:
            case ScriptRootOp.ComplexCalc:
                Args = Array.Empty<Arg>();
                data = PopBytes(ref script, PopInt(ref script) - 2 - 4).ToArray();
                break;

            case ScriptRootOp.Animate:
                var animType = PopInt(ref script);
                var highResIndex = PopInt(ref script);
                var lowResIndex = PopInt(ref script);
                var highResInd = PopBool(ref script);
                var lowResInd = PopBool(ref script);
                var bkgIdx = PopByte(ref script);
                PopByte(ref script);
                var preAnimType = PopInt(ref script);
                var preAnimArg0 = PopInt(ref script);
                var preAnimArg1 = PopInt(ref script);
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

            case ScriptRootOp.SimpleCalc:
                target = PopInt(ref script);
                var opCount = PopInt(ref script);
                args = new List<Arg>(1 + opCount * 3)
                {
                    new(target, false, "target")
                };
                for (int i = 0; i < opCount; i++)
                {
                    value = PopInt(ref script);
                    var op = PopByte(ref script);
                    PopByte(ref script);
                    var negateValue = PopByte(ref script);
                    valueInd = PopBool(ref script);
                    args.Add(new(op, false, "op" + i));
                    args.Add(new(negateValue, false, "neg" + i));
                    args.Add(new(value, valueInd, "value" + i));
                }
                Args = args;
                PopBytes(ref script, 8 * (3 - opCount));
                break;

            case ScriptRootOp.SpriteChangePalette:
                var offset = PopInt(ref script);
                var count = PopInt(ref script);
                var reset = PopByte(ref script);
                var offsetInd = PopBool(ref script);
                var countInd = PopBool(ref script);
                var r = PopByte(ref script);
                var g = PopByte(ref script);
                var b = PopByte(ref script);
                PopByte(ref script);
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

            case ScriptRootOp.ExtractFile:
                resIndex = PopInt(ref script);
                target = PopInt(ref script);
                flag = PopByte(ref script);
                resIndexInd = PopBool(ref script);
                Args = new Arg[]
                {
                    new(resIndex, resIndexInd, "resIndex"),
                    new(target, false, "targetName"),
                    new(flag, false, "setAsWallpaper")
                };
                break;

            case ScriptRootOp.SpriteSetPos:
                sprite = PopInt(ref script);
                x = PopInt(ref script);
                y = PopInt(ref script);
                Args = new Arg[]
                {
                    new(sprite, PopBool(ref script), "sprite"),
                    new(x, PopBool(ref script), "x"),
                    new(y, PopBool(ref script), "y")
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.SpriteSetQueue:
                sprite = PopInt(ref script);
                var queue = PopInt(ref script);
                flag = PopByte(ref script);
                Args = new Arg[]
                {
                    new(sprite, PopBool(ref script), "sprite"),
                    new(queue, PopBool(ref script), "queue"),
                    new(flag, false, "hideSprite")
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.SetString:
                target = PopInt(ref script);
                stringId = PopInt(ref script);
                PopByte(ref script);
                var stringIdInd = PopBool(ref script);
                var formatCount = PopByte(ref script);
                PopBytes(ref script, 3);
                args = new List<Arg>(2 + formatCount * 2)
                {
                    new(target, false, "target"),
                    new(stringId, stringIdInd, "string")
                };
                for (int i = 0; i < formatCount; i++)
                {
                    value = PopInt(ref script);
                    flag = PopByte(ref script);
                    valueInd = PopBool(ref script);
                    args.Add(new(value, valueInd && flag != 0, "value" + i));
                    args.Add(new(flag, false, "isNumber" + i));
                }
                PopBytes(ref script, 6 * (6 - formatCount));
                Args = args;
                break;

            case ScriptRootOp.SetText:
                target = PopInt(ref script);
                value = PopInt(ref script);
                stringId = PopInt(ref script);
                Args = new Arg[]
                {
                    new(target, PopBool(ref script), "target"),
                    new(value, PopBool(ref script), "value"),
                    new(stringId, PopBool(ref script), "string"),
                    new(PopByte(ref script), false, "useValue")
                };
                break;

            case ScriptRootOp.SetTextNum195:
                target = PopInt(ref script);
                value = PopInt(ref script);
                Args = new Arg[]
                {
                    new(target, PopBool(ref script), "target"),
                    new(value, PopBool(ref script), "value")
                };
                break;

            case ScriptRootOp.SetTimer:
                target = PopInt(ref script);
                scriptIndex = PopInt(ref script);
                var duration = PopInt(ref script);
                var durationInd = PopBool(ref script);
                var scriptIndexInd = PopBool(ref script);
                durationInd &= PopBool(ref script);
                var repeats = PopByte(ref script);
                Args = new Arg[]
                {
                    new(target, false, "target"),
                    new(scriptIndex, scriptIndexInd, "script"),
                    new(duration, durationInd, "duration"),
                    new(repeats, false, "repeats")
                };
                break;

            case ScriptRootOp.AudioSetVolume:
                flag1 = PopByte(ref script);
                flag2 = PopByte(ref script);
                valueInd = PopBool(ref script);
                PopByte(ref script);
                value = PopInt(ref script);
                Args = new Arg[]
                {
                    new(flag1, false, "flag1"),
                    new(flag2, false, "flag2"),
                    new(value, valueInd, "value"),
                };
                break;

            case ScriptRootOp.AudioStopCD:
                Args = Array.Empty<Arg>();
                PopUShort(ref script);
                break;

            case ScriptRootOp.AudioStopMidi:
                Args = new Arg[]
                {
                    new(PopInt(ref script), PopBool(ref script), "target")
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.AudioStopWave:
                target = PopInt(ref script);
                var targetInd = PopBool(ref script);
                PopByte(ref script);
                value = PopInt(ref script);
                Args = new Arg[]
                {
                    new(target, targetInd, "target"),
                    new(value, false, "value")
                };
                break;

            case ScriptRootOp.StringCompare:
            case ScriptRootOp.StringCompareI:
                target = PopInt(ref script);
                left = PopInt(ref script);
                right = PopInt(ref script);
                Args = new Arg[]
                {
                    new(target, false, "target"),
                    new(left, false, "left"),
                    new(right, false, "right"),
                };
                break;

            case ScriptRootOp.RunArrayOp:
                resIndex = PopInt(ref script);
                var fallback = PopInt(ref script);
                var indexVar = PopInt(ref script);
                flag1 = PopByte(ref script);
                flag2 = PopByte(ref script);
                resIndexInd = PopBool(ref script);
                var fallbackInd = PopBool(ref script);
                Args = new Arg[]
                {
                    new(resIndex, resIndexInd, "array"),
                    new(indexVar, false, "indexVar"),
                    new(fallback, fallbackInd, "fallback"),
                    new(flag1, false, "useFallback"),
                    new(flag2, false, "incrementIndex")
                };
                break;

            case ScriptRootOp.Switch:
                value = PopInt(ref script);
                var offsetToCases = PopInt(ref script);
                var defaultJump = PopInt(ref script);
                var caseCount = PopUShort(ref script);
                PopByte(ref script);
                valueInd = PopBool(ref script);
                PopBytes(ref script, offsetToCases - 18);
                args = new List<Arg>(2 + caseCount * 3)
                {
                    new(value, valueInd, "value"),
                    new(defaultJump, false, "defaultJump")
                };
                for (int i = 0; i < caseCount; i++)
                {
                    var compare = PopInt(ref script);
                    var jump = PopInt(ref script);
                    var compareInd = PopInt(ref script) != 0;
                    args.Add(new(compare, compareInd, "compare" + i));
                    args.Add(new(jump, false, "jump" + i));
                }
                Args = args;
                break;

            case ScriptRootOp.CalcSwitch:
                PopInt(ref script);
                offsetToCases = PopInt(ref script);
                defaultJump = PopInt(ref script);
                caseCount = PopUShort(ref script);
                data = PopBytes(ref script, offsetToCases - 16).ToArray();
                args = new List<Arg>(1 + caseCount * 3)
                {
                    new(defaultJump, false, "defaultJump")
                };
                for (int i = 0; i < caseCount; i++)
                {
                    var compare = PopInt(ref script);
                    var jump = PopInt(ref script);
                    var compareInd = PopInt(ref script) != 0;
                    args.Add(new(compare, compareInd, "compare" + i));
                    args.Add(new(jump, false, "jump" + i));
                    if (jump >= 0 && jump < offsetToCases + 2 + caseCount * 12)
                        throw new InvalidDataException("CalcSwitch jumps into itself");
                }
                Args = args;
                break;

            case ScriptRootOp.SpriteSwap222:
                sprite = PopInt(ref script);
                var sprite2 = PopInt(ref script);
                queue = PopInt(ref script);
                flag = PopByte(ref script);
                spriteInd = PopBool(ref script);
                var sprite2Ind = PopBool(ref script);
                var queueInd = PopBool(ref script);
                Args = new Arg[]
                {
                    new(sprite, spriteInd, "sprite1"),
                    new(sprite2, sprite2Ind, "sprite2"),
                    new(queue, queueInd, "queue"),
                    new(flag, false, "freeFlag")
                };
                break;

            case ScriptRootOp.Math224:
                targetX = PopInt(ref script);
                targetY = PopInt(ref script);
                unk1 = PopInt(ref script);
                unk2 = PopInt(ref script);
                unk3 = PopInt(ref script);
                unk4 = PopInt(ref script);
                unk5 = PopInt(ref script);
                unk6 = PopInt(ref script);
                unk7 = PopInt(ref script);
                unk1Ind = PopBool(ref script);
                unk2Ind = PopBool(ref script);
                unk3Ind = PopBool(ref script);
                unk4Ind = PopBool(ref script);
                unk5Ind = PopBool(ref script);
                var unk6Ind = PopBool(ref script);
                unk7Ind = PopBool(ref script);
                PopByte(ref script);
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

            case ScriptRootOp.SpriteIsVisible:
                Args = new Arg[]
                {
                    new(PopInt(ref script), false, "target"),
                    new(PopInt(ref script), PopBool(ref script), "sprite")
                };
                PopByte(ref script);
                break;

            case ScriptRootOp.SetMapTransform:
                var zoom = PopInt(ref script);
                var offsetX = PopInt(ref script);
                var offsetY = PopInt(ref script);
                Args = new Arg[]
                {
                    new(zoom, PopBool(ref script), "zoom"),
                    new(offsetX, PopBool(ref script), "offsetX"),
                    new(offsetY, PopBool(ref script), "offsetY")
                };
                PopByte(ref script);
                break;

            default: throw new NotSupportedException($"Not supported operation: {Op}");
        }
    }
}
