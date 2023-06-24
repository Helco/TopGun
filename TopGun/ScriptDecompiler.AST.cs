using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TopGun;

partial class ScriptDecompiler
{
    private class CalcStackEntry
    {
        public int FinalizeInsert { get; }
        public int FinalizeIndex { get; private set; }
        public int RefCount { get; set; }
        public ASTExpression ValueExpression { get; private set; }
        public ASTExpression RefExpression { get; private set; }

        public CalcStackEntry(ASTExpression valueExpression, int finalizeInsert)
        {
            ValueExpression = RefExpression = valueExpression;
            FinalizeInsert = finalizeInsert;
        }

        public void Finalize(int index)
        {
            RefCount = 2;
            FinalizeIndex = index;
            RefExpression = new ASTTmpValue { Index = index };
        }

        public void WriteTo(TextWriter writer, int indent) => RefExpression.WriteTo(writer, indent);
    }

    private static void WriteIndent(TextWriter writer, int indent)
    {
        while (indent-- > 0)
            writer.Write('\t');
    }

    private abstract class ASTNode
    {
        public abstract void WriteTo(TextWriter writer, int indent);
    }

    private abstract class ASTExpression : ASTNode
    {
        public virtual bool IsConstant { get; } = false;
        public abstract int Precedence { get; }

        protected void WriteExpr(TextWriter writer, int indent, CalcStackEntry entry) =>
            WriteExpr(writer, indent, entry.RefExpression);
        protected void WriteExpr(TextWriter writer, int indent, ASTExpression expr)
        {
            if (Precedence > expr.Precedence)
            {
                writer.Write('(');
                expr.WriteTo(writer, indent);
                writer.Write(')');
            }
            else
                expr.WriteTo(writer, indent);
        }
    }

    private abstract class ASTInstruction : ASTNode { }

    private class ASTRootOpInstruction : ASTInstruction
    {
        public ScriptRootInstruction RootInstruction { get; init; }
        public IReadOnlyList<ASTInstruction> CalcBody { get; init; } = Array.Empty<ASTInstruction>();

        public override void WriteTo(TextWriter writer, int indent)
        {
            WriteIndent(writer, indent);
            writer.Write(RootInstruction.ToStringWithoutData());
            if (!CalcBody.Any())
            {
                writer.WriteLine();
                return;
            }

            writer.WriteLine(" {");
            foreach (var instruction in CalcBody)
                instruction.WriteTo(writer, indent + 1);
            WriteIndent(writer, indent);
            writer.WriteLine("}");
        }
    }

    private class ASTTmpDeclaration : ASTInstruction
    {
        public int Index { get; init; }
        public ASTExpression Value { get; init; } = null!;

        public override void WriteTo(TextWriter writer, int indent)
        {
            WriteIndent(writer, indent);
            writer.Write("tmp");
            writer.Write(Index);
            writer.Write(" = ");
            Value.WriteTo(writer, indent);
            writer.WriteLine(";");
        }
    }

    private class ASTTmpValue : ASTExpression
    {
        public int Index { get; init; }
        public override bool IsConstant => true;
        public override int Precedence => 100;

        public override void WriteTo(TextWriter writer, int indent)
        {
            writer.Write("tmp");
            writer.Write(Index);
        }
    }

    private class ASTExprInstr : ASTInstruction
    {
        public ASTExpression Expression { get; init; } = null!;

        public override void WriteTo(TextWriter writer, int indent)
        {
            WriteIndent(writer, indent);
            Expression.WriteTo(writer, indent);
            writer.WriteLine(';');
        }
    }

    private class ASTImmediate : ASTExpression
    {
        public int Value { get; init; }
        public override bool IsConstant => true;
        public override int Precedence => 100;

        public override void WriteTo(TextWriter writer, int indent)
        {
            writer.Write(Value);
        }
    }

    private abstract class ASTVarReference : ASTExpression
    {
        public int Index { get; init; }
        public override int Precedence => 100;
    }

    private class ASTGlobalVarValue : ASTVarReference
    {
        public override void WriteTo(TextWriter writer, int indent)
        {
            writer.Write("global");
            writer.Write(Index);
        }
    }

    private class ASTLocalVarValue : ASTVarReference
    {
        public override void WriteTo(TextWriter writer, int indent)
        {
            writer.Write("local");
            writer.Write(Index);
        }
    }

    private class ASTGlobalVarAddress : ASTVarReference
    {
        public override bool IsConstant => true;
        public override void WriteTo(TextWriter writer, int indent)
        {
            writer.Write("&global");
            writer.Write(Index);
        }
    }

    private class ASTLocalVarAddress : ASTVarReference
    {
        public override bool IsConstant => true;
        public override void WriteTo(TextWriter writer, int indent)
        {
            writer.Write("&local");
            writer.Write(Index);
        }
    }

    private class ASTArrayAccess : ASTExpression
    {
        public CalcStackEntry Array { get; init; } = null!;
        public CalcStackEntry Index { get; init; } = null!;
        public override int Precedence => 50;

        public override void WriteTo(TextWriter writer, int indent)
        {
            WriteExpr(writer, indent, Array);
            writer.Write('[');
            Index.WriteTo(writer, indent);
            writer.Write(']');
        }
    }

    private enum UnaryOp
    {
        PreIncrement,
        PostIncrement,
        PreDecrement,
        PostDecrement,
        Negate,
        BooleanNot,
        BitNot,
        Dereference
    }
    private readonly record struct UnaryOpInfo(string Name, bool IsPrefix = true);
    private static readonly IReadOnlyDictionary<UnaryOp, UnaryOpInfo> unaryOpInfos = new Dictionary<UnaryOp, UnaryOpInfo>()
    {
        { UnaryOp.PreIncrement, new("++") },
        { UnaryOp.PostIncrement, new("++", IsPrefix: false) },
        { UnaryOp.PreDecrement, new("--") },
        { UnaryOp.PostDecrement, new("--", IsPrefix: false) },
        { UnaryOp.Negate, new("-") },
        { UnaryOp.BooleanNot, new("!") },
        { UnaryOp.BitNot, new("~") },
        { UnaryOp.Dereference, new("*") },
    };

    private class ASTUnary : ASTExpression
    {
        public UnaryOp Op { get; init; }
        public CalcStackEntry Value { get; init; } = null!;
        public override int Precedence => 20;

        public override void WriteTo(TextWriter writer, int indent)
        {
            var info = unaryOpInfos[Op];
            if (info.IsPrefix)
                writer.Write(info.Name);
            WriteExpr(writer, indent, Value);
            if (!info.IsPrefix)
                writer.Write(info.Name);
        }
    }

    private enum BinaryOp
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        Modulo,
        ShiftLeft,
        ShiftRight,
        BooleanAnd,
        BooleanOr,
        BitAnd,
        BitOr,
        BitXor,
        Equals,
        NotEquals,
        Lesser,
        Greater,
        LessOrEquals,
        GreaterOrEquals,
    }
    private readonly record struct BinaryOpInfo(string Name, int Precedence);
    private static readonly IReadOnlyDictionary<BinaryOp, BinaryOpInfo> binaryOpInfos = new Dictionary<BinaryOp, BinaryOpInfo>()
    {
        { BinaryOp.BooleanOr, new("||", 0) },
        { BinaryOp.BooleanAnd, new("&&", 1) },
        { BinaryOp.BitOr, new("|", 2) },
        { BinaryOp.BitXor, new("^", 3) },
        { BinaryOp.BitAnd, new("&", 4) },
        { BinaryOp.Equals, new("==", 5) },
        { BinaryOp.NotEquals, new("!=", 5) },
        { BinaryOp.Lesser, new("<", 6) },
        { BinaryOp.Greater, new(">", 6) },
        { BinaryOp.LessOrEquals, new("<=", 6) },
        { BinaryOp.GreaterOrEquals, new(">=", 6) },
        { BinaryOp.ShiftLeft, new("<<", 7) },
        { BinaryOp.ShiftRight, new(">>", 7) },
        { BinaryOp.Add, new("+", 8) },
        { BinaryOp.Subtract, new("-", 8) },
        { BinaryOp.Multiply, new("*", 9) },
        { BinaryOp.Divide, new("/", 9) },
        { BinaryOp.Modulo, new("%", 9) },
    };

    private class ASTBinary : ASTExpression
    {
        public BinaryOp Op { get; init; }
        public CalcStackEntry Left { get; init; } = null!;
        public CalcStackEntry Right { get; init; } = null!;
        public override int Precedence => binaryOpInfos[Op].Precedence;

        public override void WriteTo(TextWriter writer, int indent)
        {
            WriteExpr(writer, indent, Left);
            writer.Write(' ');
            writer.Write(binaryOpInfos[Op].Name);
            writer.Write(' ');
            WriteExpr(writer, indent, Right);
        }
    }

    private class ASTAssign : ASTInstruction
    {
        public CalcStackEntry Address { get; init; } = null!;
        public CalcStackEntry Value { get; init; } = null!;

        public override void WriteTo(TextWriter writer, int indent)
        {
            WriteIndent(writer, indent);
            Address.WriteTo(writer, indent);
            writer.Write(" = ");
            Value.WriteTo(writer, indent);
            writer.WriteLine(";");
        }
    }

    private abstract class ASTCall : ASTExpression
    {
        public IReadOnlyList<CalcStackEntry> Args { get; init; } = Array.Empty<CalcStackEntry>();
        public int LocalScopeSize { get; init; }
        public override int Precedence => 50;

        protected void WriteArgsTo(TextWriter writer, int indent)
        {
            writer.Write('<');
            writer.Write(LocalScopeSize);
            writer.Write(">(");

            if (Args.Count == 1)
                Args[0].WriteTo(writer, indent);
            else if (Args.Count > 1)
            {
                writer.WriteLine();
                foreach (var arg in Args.SkipLast(1))
                {
                    WriteIndent(writer, indent + 1);
                    arg.WriteTo(writer, indent + 1);
                    writer.WriteLine(',');
                }
                if (Args.Any())
                {
                    WriteIndent(writer, indent + 1);
                    Args.Last().WriteTo(writer, indent + 1);
                    writer.WriteLine();
                    WriteIndent(writer, indent);
                }
            }

            writer.Write(')');
        }
    }

    private class ASTDynamicProcCall : ASTCall
    {
        public CalcStackEntry ProcId { get; init; } = null!;

        public override void WriteTo(TextWriter writer, int indent)
        {
            writer.Write("Dynamic[");
            ProcId.WriteTo(writer, indent);
            writer.Write(']');
            WriteArgsTo(writer, indent);
        }
    }

    private class ASTUnknownExternalProcCall : ASTCall
    {
        public int ProcId { get; init; }

        public override void WriteTo(TextWriter writer, int indent)
        {
            writer.Write("UnknownExternal[");
            writer.Write(ProcId);
            writer.Write(']');
            WriteArgsTo(writer, indent);
        }
    }

    private class ASTExternalProcCall : ASTCall
    {
        public string Plugin { get; init; } = "";
        public string Proc { get; init; } = "";

        public override void WriteTo(TextWriter writer, int indent)
        {
            writer.Write("External[");
            writer.Write(Plugin);
            writer.Write('.');
            writer.Write(Proc);
            writer.Write(']');
            WriteArgsTo(writer, indent);
        }
    }

    private class ASTInternalProcCall : ASTCall
    {
        public int ProcId { get; init; }

        public override void WriteTo(TextWriter writer, int indent)
        {
            if (Enum.TryParse<ScriptOp>(ProcId.ToString(), out var internalProc))
                writer.Write(internalProc);
            else
            {
                writer.Write("Internal[");
                writer.Write(ProcId);
                writer.Write(']');
            }
            WriteArgsTo(writer, indent);
        }
    }
    
    private class ASTScriptCall : ASTCall
    {
        public CalcStackEntry ScriptIndex { get; init; } = null!;

        public override void WriteTo(TextWriter writer, int indent)
        {
            writer.Write("Script[");
            ScriptIndex.WriteTo(writer, indent);
            writer.Write(']');
            WriteArgsTo(writer, indent);
        }
    }

    private class ASTConditionalJump : ASTInstruction
    {
        public bool Zero { get; init; }
        public CalcStackEntry Condition { get; init; } = null!;
        public int Target { get; init; }

        public override void WriteTo(TextWriter writer, int indent)
        {
            WriteIndent(writer, indent);
            writer.Write(Zero ? "ifnot (" : "if (");
            Condition.WriteTo(writer, indent);
            writer.Write(") goto ");
            writer.Write(Target.ToString("X4"));
            writer.WriteLine(";");
        }
    }
}
