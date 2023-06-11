using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using static TopGun.SpanUtils;

namespace TopGun;

public enum ScriptCalcOp : byte
{
    Exit = 0,
    PushValue,
    PushVar,
    ReadVarArray,
    PushVarAddress,
    ReadVar,
    OffsetVar,
    WriteVar = 8,
    CallProc = 13,
    RunScript,
    Negate,
    BooleanNot,
    BitNot,
    Add,
    Sub,
    Mul,
    Div,
    Mod,
    Equals,
    LessOrEquals,
    Less,
    GreaterOrEquals,
    Greater,
    NotEquals,
    BooleanAnd,
    BooleanOr,
    BitAnd,
    BitOr,
    BitXor,
    PreIncrementVar,
    PostIncrementVar,
    PreDecrementVar,
    PostDecrementVar,
    JumpNonZero,
    JumpZero,
    PushVarValue,
    ShiftLeft,
    ShiftRight
}

public readonly struct ScriptCalcInstruction
{
    public enum ArgType
    {
        Immediate,
        Variable,
        InstructionOffset
    }

    public readonly record struct Arg(int Value, ArgType Type, string Name="");

    public int Offset { get; }
    public int EndOffset { get; }
    public ScriptCalcOp Op { get; }
    public IReadOnlyList<Arg> Args { get; }

    public override string ToString()
    {
        var text = new StringBuilder();
        text.Append($"{Offset:D4}");
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

            text.Append(arg.Type switch
            {
                ArgType.Immediate => "#",
                ArgType.Variable => "$",
                ArgType.InstructionOffset => "IP",
                _ => "?"
            });
            if (arg.Type == ArgType.InstructionOffset)
                text.Append(arg.Value.ToString("+0;-#"));
            else
                text.Append(arg.Value);
        }

        return text.ToString();
    }

    public ScriptCalcInstruction(ref ReadOnlySpan<byte> script) : this(new SpanReader(script))
    {
        script = script[EndOffset..];
    }
    public ScriptCalcInstruction(SpanReader reader) : this(ref reader) { }
    public ScriptCalcInstruction(ref SpanReader reader)
    {
        Offset = reader.Position;
        Op = (ScriptCalcOp)reader.ReadByte();
        Args = Array.Empty<Arg>();

        switch(Op)
        {
            case ScriptCalcOp.PushValue:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), ArgType.Immediate)
                };
                break;

            case ScriptCalcOp.PushVar:
            case ScriptCalcOp.PushVarAddress:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), ArgType.Variable)
                };
                break;

            case ScriptCalcOp.Exit:
            case ScriptCalcOp.ReadVarArray:
            case ScriptCalcOp.ReadVar:
            case ScriptCalcOp.OffsetVar:
            case ScriptCalcOp.WriteVar:
            case ScriptCalcOp.Negate:
            case ScriptCalcOp.BooleanNot:
            case ScriptCalcOp.BitNot:
            case ScriptCalcOp.Add:
            case ScriptCalcOp.Sub:
            case ScriptCalcOp.Mul:
            case ScriptCalcOp.Div:
            case ScriptCalcOp.Mod:
            case ScriptCalcOp.Equals:
            case ScriptCalcOp.LessOrEquals:
            case ScriptCalcOp.Less:
            case ScriptCalcOp.GreaterOrEquals:
            case ScriptCalcOp.Greater:
            case ScriptCalcOp.NotEquals:
            case ScriptCalcOp.BooleanAnd:
            case ScriptCalcOp.BooleanOr:
            case ScriptCalcOp.BitAnd:
            case ScriptCalcOp.BitOr:
            case ScriptCalcOp.BitXor:
            case ScriptCalcOp.PreIncrementVar:
            case ScriptCalcOp.PostIncrementVar:
            case ScriptCalcOp.PreDecrementVar:
            case ScriptCalcOp.PostDecrementVar:
            case ScriptCalcOp.PushVarValue:
            case ScriptCalcOp.ShiftLeft:
            case ScriptCalcOp.ShiftRight:
                break;

            case ScriptCalcOp.CallProc:
            case ScriptCalcOp.RunScript:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), ArgType.Immediate, "localScopeSize"),
                    new(reader.ReadInt(), ArgType.Immediate, "argCount")
                };
                break;

            case ScriptCalcOp.JumpNonZero:
            case ScriptCalcOp.JumpZero:
                Args = new Arg[]
                {
                    new(reader.ReadInt(), ArgType.InstructionOffset)
                };
                break;

            default: throw new NotSupportedException($"Not supported operation: {Op}");
        }

        EndOffset = reader.Position;
    }
}
