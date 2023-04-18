using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    SetResult = 161,
    BackupIni = 163,
    Calc = 169,
    SpriteSetPos = 184,
    SetTimer = 196,
    AudioSetVolume = 198,
    Sine = 202,
    AudioStopCD = 210,
    AudioStopMidi,
    AudioStopWave,
    StringCompare,
    StringCompareI,
    FreeResource = 223,
    SpriteIsVisible = 225,
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

    public ScriptRootInstruction(ref ReadOnlySpan<byte> script)
    {
        Op = (ScriptRootOp)PopUShort(ref script);
        data = Array.Empty<byte>();
        switch(Op)
        {
            case ScriptRootOp.RunMessage:
                var resIndex = PopInt(ref script);
                var resIndexIsIndirect = PopBool(ref script);
                var indirectArgMask = PopByte(ref script);
                var localScopeSize = PopByte(ref script);
                var argCount = PopByte(ref script);
                var args = new List<Arg>(2 + argCount)
                {
                    new(resIndex, resIndexIsIndirect, "resIndex"),
                    new(localScopeSize, false, "localScope")
                };
                for (int i = 0; i < argCount; i++)
                    args.Add(new(PopInt(ref script), (indirectArgMask & (1 << i)) > 0));
                Args = args;
                break;

            case ScriptRootOp.Nop:
            case ScriptRootOp.CloseWindow57:
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
                PopByte(ref script);
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
                PopBytes(ref script, 3);
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
                then = PopInt(ref script);
                @else = PopInt(ref script);
                if (then <= 0 && @else <= 0)
                    throw new NotSupportedException("Cannot figure out size of JumpIfCalc root op");
                else if (then <= 0) data = PopBytes(ref script, @else).ToArray();
                else if (@else <= 0) data = PopBytes(ref script, then).ToArray();
                else data = PopBytes(ref script, Math.Min(then, @else)).ToArray();
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

            default: throw new NotSupportedException($"Not supported operation: {Op}");
        }
    }
}
