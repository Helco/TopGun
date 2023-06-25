﻿using System;
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
            RefExpression = new ASTTmpValue { Parent = ValueExpression.Parent, Index = index };
        }

        public void WriteTo(CodeWriter writer) => RefExpression.WriteTo(writer);
    }

    private abstract class ASTNode
    {
        public ASTNode? Parent { get; set; }
        public int StartOwnOffset { get; set; } = -1;
        public int EndOwnOffset { get; set; } = -1;
        public virtual int StartTotalOffset
        {
            get
            {
                var children = Children.Where(c => c.StartTotalOffset >= 0);
                if (!children.Any())
                    return StartOwnOffset;
                var childrenStart = children.Min(c => c.StartTotalOffset);
                return StartOwnOffset >= 0 ? Math.Min(StartOwnOffset, childrenStart) : childrenStart;
            }
        }
        public virtual int EndTotalOffset
        {
            get
            {
                var children = Children.Where(c => c.EndTotalOffset >= 0);
                if (!children.Any())
                    return EndOwnOffset;
                var childrenEnd = children.Max(c => c.EndTotalOffset);
                return EndOwnOffset >= 0 ? Math.Max(EndOwnOffset, childrenEnd) : childrenEnd;
            }
        }
        public TextPosition StartTextPosition { get; private set; } // only valid after WriteTo
        public TextPosition EndTextPosition { get; private set; }
        public virtual IEnumerable<ASTNode> Children => Enumerable.Empty<ASTNode>();

        public IEnumerable<ASTNode> AllChildren => Children.SelectMany(c => c.AllChildren).Prepend(this);

        public void FixChildrenParents()
        {
            foreach (var child in Children)
            {
                child.FixChildrenParents();
                child.Parent = this;
            }
        }

        public void WriteTo(CodeWriter writer)
        {
            StartTextPosition = writer.Position;
            WriteToInternal(writer);
            EndTextPosition = writer.Position;
        }

        protected abstract void WriteToInternal(CodeWriter writer);

        public void ReplaceMeWith(ASTNode newNode) => Parent!.ReplaceChild(this, newNode);

        public virtual void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            throw new ArgumentOutOfRangeException(nameof(oldChild), "ASTNode has no children to replace");
        }
    }

    private abstract class ASTExpression : ASTNode
    {
        public virtual bool IsConstant { get; } = false;
        public abstract int Precedence { get; }

        protected void WriteExpr(CodeWriter writer, CalcStackEntry entry) =>
            WriteExpr(writer, entry.RefExpression);
        protected void WriteExpr(CodeWriter writer, ASTExpression expr)
        {
            if (Precedence > expr.Precedence)
            {
                writer.Write('(');
                expr.WriteTo(writer);
                writer.Write(')');
            }
            else
                expr.WriteTo(writer);
        }
    }

    private abstract class ASTInstruction : ASTNode { }

    private class ASTRootOpInstruction : ASTInstruction
    {
        public ScriptRootInstruction RootInstruction { get; init; }
        public List<ASTInstruction> CalcBody { get; set; } = new();
        public override IEnumerable<ASTNode> Children => CalcBody;

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            int index = -1;
            if (oldChild is ASTInstruction oldInstr)
                index = CalcBody.IndexOf(oldInstr);
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(oldChild), "Could not find child to replace");
            CalcBody[index] = (ASTInstruction)newChild;
            newChild.Parent = this;
            oldChild.Parent = null;
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write(RootInstruction.ToStringWithoutData());
            if (!CalcBody.Any())
            {
                writer.WriteLine();
                return;
            }

            writer.WriteLine(" {");
            using var subWriter = writer.Indented;
            foreach (var instruction in CalcBody)
                instruction.WriteTo(subWriter);
            writer.WriteLine("}");
        }
    }

    private class ASTTmpDeclaration : ASTInstruction
    {
        public int Index { get; init; }
        public ASTExpression Value { get; set; } = null!;
        public override IEnumerable<ASTNode> Children => new[] { Value };

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            if (Value == oldChild)
                Value = (ASTExpression)newChild;
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write("tmp");
            writer.Write(Index);
            writer.Write(" = ");
            Value.WriteTo(writer);
            writer.WriteLine(";");
        }
    }

    private class ASTTmpValue : ASTExpression
    {
        public int Index { get; init; }
        public override bool IsConstant => true;
        public override int Precedence => 100;

        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write("tmp");
            writer.Write(Index);
        }
    }

    private class ASTExprInstr : ASTInstruction
    {
        public ASTExpression Expression { get; set; } = null!;
        public override IEnumerable<ASTNode> Children => new[] { Expression };

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            if (Expression == oldChild)
                Expression = (ASTExpression)newChild;
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            Expression.WriteTo(writer);
            writer.WriteLine(';');
        }
    }

    private class ASTImmediate : ASTExpression
    {
        public int Value { get; init; }
        public override bool IsConstant => true;
        public override int Precedence => 100;

        protected override void WriteToInternal(CodeWriter writer)
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
        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write("global");
            writer.Write(Index);
        }
    }

    private class ASTLocalVarValue : ASTVarReference
    {
        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write("local");
            writer.Write(Index);
        }
    }

    private class ASTGlobalVarAddress : ASTVarReference
    {
        public override bool IsConstant => true;
        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write("&global");
            writer.Write(Index);
        }
    }

    private class ASTLocalVarAddress : ASTVarReference
    {
        public override bool IsConstant => true;
        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write("&local");
            writer.Write(Index);
        }
    }

    private class ASTArrayAccess : ASTExpression
    {
        public CalcStackEntry Array { get; set; } = null!;
        public CalcStackEntry Index { get; set; } = null!;
        public override int Precedence => 50;
        public override IEnumerable<ASTNode> Children => new[] { Array.RefExpression, Index.RefExpression };

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            if (Array.RefExpression == oldChild)
                Array = new((ASTExpression)newChild, -1);
            else if (Index.RefExpression == oldChild)
                Array = new((ASTExpression)newChild, -1);
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            WriteExpr(writer, Array);
            writer.Write('[');
            Index.WriteTo(writer);
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
        public CalcStackEntry Value { get; set; } = null!;
        public override int Precedence => 20;
        public override IEnumerable<ASTNode> Children => new[] { Value.RefExpression };

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            if (Value.RefExpression == oldChild)
                Value = new((ASTExpression)newChild, -1);
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            var info = unaryOpInfos[Op];
            if (info.IsPrefix)
                writer.Write(info.Name);
            WriteExpr(writer, Value);
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
        EvalBooleanAnd,
        LazyBooleanAnd,
        EvalBooleanOr,
        LazyBooleanOr,
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
        { BinaryOp.EvalBooleanOr, new("||", 0) },
        { BinaryOp.LazyBooleanOr, new("||?", 0) },
        { BinaryOp.EvalBooleanAnd, new("&&", 1) },
        { BinaryOp.LazyBooleanAnd, new("&&?", 1) },
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
        public BinaryOp Op { get; set; }
        public CalcStackEntry Left { get; set; } = null!;
        public CalcStackEntry Right { get; set; } = null!;
        public override int Precedence => binaryOpInfos[Op].Precedence;
        public override IEnumerable<ASTNode> Children => new[] { Left.RefExpression, Right.RefExpression };

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            if (Left.RefExpression == oldChild)
                Left = new((ASTExpression)newChild, -1);
            else if (Right.RefExpression == oldChild)
                Right = new((ASTExpression)newChild, -1);
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            WriteExpr(writer, Left);
            writer.Write(' ');
            writer.Write(binaryOpInfos[Op].Name);
            writer.Write(' ');
            WriteExpr(writer, Right);
        }
    }

    private class ASTAssign : ASTInstruction
    {
        public CalcStackEntry Address { get; set; } = null!;
        public CalcStackEntry Value { get; set; } = null!;
        public override IEnumerable<ASTNode> Children => new[] { Address.ValueExpression, Value.RefExpression };

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            if (Address.RefExpression == oldChild)
                Address = new((ASTExpression)newChild, -1);
            else if (Value.RefExpression == oldChild)
                Value = new((ASTExpression)newChild, -1);
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            Address.WriteTo(writer);
            writer.Write(" = ");
            Value.WriteTo(writer);
            writer.WriteLine(";");
        }
    }

    private abstract class ASTCall : ASTExpression
    {
        public List<CalcStackEntry> Args { get; init; } = new();
        public int LocalScopeSize { get; init; }
        public override int Precedence => 50;
        public override IEnumerable<ASTNode> Children => Args.Select(c => c.RefExpression);

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            var index = Args.FindIndex(e => e.RefExpression == oldChild);
            if (index >= 0)
                Args[index] = new((ASTExpression)newChild, -1);
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected void WriteArgsTo(CodeWriter writer)
        {
            writer.Write('<');
            writer.Write(LocalScopeSize);
            writer.Write(">(");

            if (Args.Count == 1)
                Args[0].WriteTo(writer);
            else if (Args.Count > 1)
            {
                writer.WriteLine();
                using var subWriter = writer.Indented;
                foreach (var arg in Args.SkipLast(1))
                {
                    arg.WriteTo(subWriter);
                    subWriter.WriteLine(',');
                }
                if (Args.Any())
                {
                    Args.Last().WriteTo(subWriter);
                    subWriter.WriteLine();
                }
            }

            writer.Write(')');
        }
    }

    private class ASTDynamicProcCall : ASTCall
    {
        public CalcStackEntry ProcId { get; set; } = null!;
        public override IEnumerable<ASTNode> Children => base.Children.Prepend(ProcId.RefExpression);

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            if (ProcId.RefExpression == oldChild)
                ProcId = new((ASTExpression)newChild, -1);
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write("Dynamic[");
            ProcId.WriteTo(writer);
            writer.Write(']');
            WriteArgsTo(writer);
        }
    }

    private class ASTUnknownExternalProcCall : ASTCall
    {
        public int ProcId { get; init; }

        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write("UnknownExternal[");
            writer.Write(ProcId);
            writer.Write(']');
            WriteArgsTo(writer);
        }
    }

    private class ASTExternalProcCall : ASTCall
    {
        public string Plugin { get; init; } = "";
        public string Proc { get; init; } = "";

        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write("External[");
            writer.Write(Plugin);
            writer.Write('.');
            writer.Write(Proc);
            writer.Write(']');
            WriteArgsTo(writer);
        }
    }

    private class ASTInternalProcCall : ASTCall
    {
        public int ProcId { get; init; }

        protected override void WriteToInternal(CodeWriter writer)
        {
            if (Enum.TryParse<ScriptOp>(ProcId.ToString(), out var internalProc))
                writer.Write(internalProc);
            else
            {
                writer.Write("Internal[");
                writer.Write(ProcId);
                writer.Write(']');
            }
            WriteArgsTo(writer);
        }
    }
    
    private class ASTScriptCall : ASTCall
    {
        public CalcStackEntry ScriptIndex { get; set; } = null!;
        public override IEnumerable<ASTNode> Children => base.Children.Prepend(ScriptIndex.RefExpression);

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            if (ScriptIndex.RefExpression == oldChild)
                ScriptIndex = new((ASTExpression)newChild, -1);
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write("Script[");
            ScriptIndex.WriteTo(writer);
            writer.Write(']');
            WriteArgsTo(writer);
        }
    }

    private class ASTConditionalCalcJump : ASTInstruction
    {
        public bool Zero { get; init; }
        public CalcStackEntry Condition { get; set; } = null!;
        public int Target { get; init; }
        public override IEnumerable<ASTNode> Children => new[] { Condition.RefExpression };

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            if (Condition.RefExpression == oldChild)
                Condition = new((ASTExpression)newChild, -1);
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write(Zero ? "ifnot (" : "if (");
            Condition.WriteTo(writer);
            writer.Write(") goto ");
            writer.Write(Target.ToString("X4"));
            writer.WriteLine(";");
        }
    }

    private class ASTReturn : ASTInstruction
    {
        public ASTExpression Value { get; set; } = null!;
        public override IEnumerable<ASTNode> Children => new[] { Value };

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            if (Value == oldChild)
                Value = (ASTExpression)newChild;
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write("return ");
            Value.WriteTo(writer);
            writer.WriteLine(';');
        }
    }

    private class ASTRoot : ASTNode
    {
        public List<ASTInstruction> Instructions { get; init; } = new();
        public override IEnumerable<ASTNode> Children => Instructions;

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            int index = Instructions.IndexOf((oldChild as ASTInstruction)!);
            if (index >= 0)
                Instructions[index] = (ASTInstruction)newChild;
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            Instructions.ForEach(i => i.WriteTo(writer));
        }
    }
}
