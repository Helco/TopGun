﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TopGun;

partial class ScriptDecompiler
{
    private abstract class ASTNode
    {
        public ASTNode? Parent { get; set; }
        public int StartOwnOffset { get; set; } = -1;
        public int EndOwnOffset { get; set; } = -1;
        public TextPosition StartTextPosition { get; private set; } // only valid after WriteTo
        public TextPosition EndTextPosition { get; private set; }
        public virtual IEnumerable<ASTNode> Children => Enumerable.Empty<ASTNode>();
        public IEnumerable<ASTNode> AllChildren => Children.SelectMany(c => c.AllChildren).Prepend(this);

        private int offsetDepHash = 0, lastStartTotalOffset = -1, lastEndTotalOffset = -1;

        public virtual int StartTotalOffset
        {
            get
            {
                CalculateTotalOffsets();
                return lastStartTotalOffset;
            }
        }
        public virtual int EndTotalOffset
        {
            get
            {
                CalculateTotalOffsets();
                return lastEndTotalOffset;
            }
        }

        private void CalculateTotalOffsets()
        {
            var curOffsetDepHash = HashCode.Combine(StartOwnOffset, EndOwnOffset);
            curOffsetDepHash = Children.Aggregate(curOffsetDepHash, HashCode.Combine);
            if (curOffsetDepHash == offsetDepHash)
                return;

            var children = Children.Where(c => c.StartTotalOffset >= 0 && c.EndTotalOffset >= 0);
            if (!children.Any())
            {
                lastStartTotalOffset = StartOwnOffset;
                lastEndTotalOffset = EndOwnOffset;
                return;
            }

            offsetDepHash = curOffsetDepHash;
            var childrenStart = children.Min(c => c.StartTotalOffset);
            var childrenEnd = children.Max(c => c.EndTotalOffset);
            lastStartTotalOffset = StartOwnOffset >= 0 ? Math.Min(StartOwnOffset, childrenStart) : childrenStart;
            lastEndTotalOffset = EndOwnOffset >= 0 ? Math.Max(EndOwnOffset, childrenEnd) : childrenEnd;
        }

        public void FixChildrenParents()
        {
            foreach (var child in Children)
            {
                child.FixChildrenParents();
                child.Parent = this;
            }
        }

        public void ResetTextPosition() => StartTextPosition = EndTextPosition = default;

        public virtual void WriteTo(CodeWriter writer)
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

    private interface IASTNodeWithSymbol
    {
        void ApplyScriptSymbolMap(ScriptSymbolMap map) {}
        void ApplySymbolMap(SymbolMap map) {}
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

    private abstract class ASTInstruction : ASTNode
    {
        public virtual bool CanFallthough => true;
    }

    private class ASTRootOpInstruction : ASTInstruction
    {
        public ScriptRootInstruction RootInstruction { get; init; }
        public List<ASTInstruction> CalcBody { get; set; } = new();
        public override IEnumerable<ASTNode> Children => CalcBody;
        public override bool CanFallthough => !SplittingOps
            .Except(new[] { ScriptOp.JumpIf })
            .Contains(RootInstruction.Op);

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
        private readonly string referencePrefix, symbolPrefix;
        protected string? resolvedSymbol;
        public int Index { get; init; }
        public override int Precedence => 100;

        public ASTVarReference(string referencePrefix, string symbolPrefix)
        {
            this.referencePrefix = referencePrefix;
            this.symbolPrefix = symbolPrefix;
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.Write(referencePrefix);
            if (resolvedSymbol == null)
            {
                writer.Write(symbolPrefix);
                writer.Write(Index);
            }
            else
                writer.Write(resolvedSymbol);
        }
    }

    private enum VariableType
    {
        Scene,
        System,
        Local
    }

    private class ASTSystemVarValue : ASTVarReference, IASTNodeWithSymbol
    {
        public ASTSystemVarValue() : base("", "system") { }

        public void ApplySymbolMap(SymbolMap map)
        {
            if (map.SystemVariables.TryGetValue(Index, out var name))
                resolvedSymbol = name;
        }
    }

    private class ASTSceneVarValue : ASTVarReference, IASTNodeWithSymbol
    {
        public ASTSceneVarValue() : base("", "scene") { }

        public void ApplySymbolMap(SymbolMap map)
        {
            if (map.SceneVariables.TryGetValue(Index, out var name))
                resolvedSymbol = name;
        }
    }

    private class ASTLocalVarValue : ASTVarReference, IASTNodeWithSymbol
    {
        public ASTLocalVarValue() : base("", "local") { }

        public void ApplyScriptSymbolMap(ScriptSymbolMap map)
        {
            if (map.Locals.TryGetValue(Index, out var name))
                resolvedSymbol = name;
        }
    }

    private class ASTSystemVarAddress : ASTVarReference, IASTNodeWithSymbol
    {
        public override bool IsConstant => true;
        public ASTSystemVarAddress() : base("&", "system") { }

        public void ApplySymbolMap(SymbolMap map)
        {
            if (map.SystemVariables.TryGetValue(Index, out var name))
                resolvedSymbol = name;
        }
    }

    private class ASTSceneVarAddress : ASTVarReference, IASTNodeWithSymbol
    {
        public override bool IsConstant => true;
        public ASTSceneVarAddress() : base("&", "scene") { }

        public void ApplySymbolMap(SymbolMap map)
        {
            if (map.SceneVariables.TryGetValue(Index, out var name))
                resolvedSymbol = name;
        }
    }

    private class ASTLocalVarAddress : ASTVarReference, IASTNodeWithSymbol
    {
        public override bool IsConstant => true;
        public ASTLocalVarAddress() : base("&", "local") { }

        public void ApplyScriptSymbolMap(ScriptSymbolMap map)
        {
            if (map.Locals.TryGetValue(Index, out var name))
                resolvedSymbol = name;
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
        public int LocalScopeSize { get; set; }
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
    
    private class ASTScriptCall : ASTCall, IASTNodeWithSymbol
    {
        private string? resolvedSymbol;

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
            if (ScriptIndex.RefExpression is ASTImmediate immediate)
            {
                writer.Write("Script[");
                if (resolvedSymbol != null)
                    writer.Write(resolvedSymbol);
                else
                    ScriptIndex.WriteTo(writer);
            }
            else
            {
                writer.Write("DynamicScript[");
                ScriptIndex.WriteTo(writer);
            }
            writer.Write(']');
            WriteArgsTo(writer);
        }

        public void ApplySymbolMap(SymbolMap symbolMap)
        {
            if (ScriptIndex.RefExpression is not ASTImmediate immediate)
                return;

            if (symbolMap.Scripts.TryGetValue(immediate.Value, out var name) && name.Name != null)
                resolvedSymbol = name.Name;
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
        public required ASTExpression Value { get; set; }
        public override IEnumerable<ASTNode> Children => Value == null ? Array.Empty<ASTNode>() : new[] { Value };
        public override bool CanFallthough => false;

        public override void ReplaceChild(ASTNode oldChild, ASTNode newChild)
        {
            if (Value != null && Value == oldChild)
                Value = (ASTExpression)newChild;
            else
                base.ReplaceChild(oldChild, newChild);
        }

        protected override void WriteToInternal(CodeWriter writer)
        {
            if (Value == null)
            {
                writer.WriteLine("return;");
                return;
            }
            writer.Write("return = ");
            Value.WriteTo(writer);
            writer.WriteLine(';');
        }
    }

    private class ASTGoto : ASTInstruction
    {
        public required int Target { get; set; }
        public override bool CanFallthough => false;

        protected override void WriteToInternal(CodeWriter writer)
        {
            writer.WriteLine($"goto {Target:D4};");
        }
    }

    private abstract class ASTBlock : ASTNode
    {
        public required IDictionary<int, ASTBlock> BlocksByOffset { get; init; }

        public int? ContinueOffset { get; set; }
        public ASTBlock? ContinueBlock => ContinueOffset.HasValue ? BlocksByOffset[ContinueOffset.Value] : null;
        public override IEnumerable<ASTNode> Children => ContinueBlock == null ? Array.Empty<ASTNode>() : new[] { ContinueBlock };

        public HashSet<int> OutboundOffsets { get; init; } = new();
        public IEnumerable<ASTBlock> Outbound => OutboundOffsets.Select(o => BlocksByOffset[o]);
        public HashSet<int> InboundOffsets { get; init; } = new();
        public IEnumerable<ASTBlock> Inbound => InboundOffsets.Select(o => BlocksByOffset[o]);
        public abstract bool CanFallthrough { get; }
        public bool ConstructProvidesControlFlow { get; set; } = false;
        public bool IsLabeled { get; set; }
        public bool IsReplacedByNonBlock { get; set; } // so it does not have to be written

        public ScriptRootInstruction LastRootInstruction
        {
            // this is such a common functionality that we sacrifice a bit of encapsulation for it
            get
            {
                if (this is not ASTNormalBlock normalBlock ||
                    normalBlock.Instructions.LastOrDefault() is not ASTRootOpInstruction astRootOp)
                    throw new ArgumentException("The block does not have a last root instruction");
                return astRootOp.RootInstruction;
            }
        }

        public void AddOutbound(ASTBlock other)
        {
            OutboundOffsets.Add(other.StartTotalOffset);
            other.InboundOffsets.Add(StartTotalOffset);
        }
        public void AddInbound(ASTBlock other) => other.AddOutbound(this);

        public void RemoveOutbound(ASTBlock old)
        {
            OutboundOffsets.Remove(old.StartTotalOffset);
            old.InboundOffsets.Remove(StartTotalOffset);
        }
        public void RemoveInbound(ASTBlock old) => old.RemoveOutbound(this);

        public override void WriteTo(CodeWriter writer)
        {
            if (StartTextPosition != default && EndTextPosition != default && this is not ASTExitBlock)
                throw new Exception("Attempted to write block twice. Das schlecht.");
            base.WriteTo(writer);
            ContinueBlock?.WriteTo(writer);
        }

        public override string ToString() => $"{GetType().Name} {StartTotalOffset} -> {EndTotalOffset}";

        protected static void WriteExpressionOrBlock(CodeWriter writer, ASTExpression? expression, ASTBlock? block)
        {
            if (expression != null)
            {
                writer.Write('(');
                expression.WriteTo(writer);
                writer.Write(')');
            }
            else if (block != null)
            {
                writer.WriteLine("({");
                block.WriteTo(writer.Indented);
                writer.Write("})");
            }
        }
    }

    private class ASTNormalBlock : ASTBlock
    {
        public List<ASTInstruction> Instructions { get; init; } = new();
        public override IEnumerable<ASTNode> Children => base.Children.Concat(Instructions);
        public bool LastInstructionIsRedundantControlFlow { get; set; } = false;
        public override bool CanFallthrough => Instructions.Last().CanFallthough;

        public ASTNormalBlock SplitBefore(ASTInstruction instruction) => SplitAfter(Instructions.IndexOf(instruction) - 1);
        public ASTNormalBlock SplitAfter(ASTInstruction instruction) => SplitAfter(Instructions.IndexOf(instruction));
        public ASTNormalBlock SplitAfter(int index)
        {
            if (index < 0 || index + 1 >= Instructions.Count)
                throw new ArgumentOutOfRangeException("Invalid target for splitting ASTBlock");

            var newBlock = new ASTNormalBlock()
            {
                Instructions = Instructions.Skip(index + 1).ToList(),
                BlocksByOffset = BlocksByOffset
            };
            foreach (var instr in newBlock.Instructions)
                instr.Parent = newBlock;
            foreach (var outbound in Outbound.ToArray())
            {
                RemoveOutbound(outbound);
                newBlock.AddOutbound(outbound);
            }
            AddOutbound(newBlock);
            Instructions.RemoveRange(index + 1, Instructions.Count - index - 1);
            return newBlock;
        }

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
            var targetWriter = writer;
            if (IsLabeled)
            {
                writer.WriteLine($"label {StartTotalOffset:D4}:");
                targetWriter = writer.Indented;
            }
            var instructions = LastInstructionIsRedundantControlFlow
                ? Instructions.SkipLast(1)
                : Instructions;
            foreach (var instruction in instructions)
                instruction.WriteTo(targetWriter);
        }
    }

    private class ASTExitBlock : ASTBlock
    {
        public override bool CanFallthrough => false;

        protected override void WriteToInternal(CodeWriter writer)
        {
        }
    }

    private class ASTLoop : ASTBlock
    {
        public required bool IsPostCondition { get; init; }
        public required ASTBlock Condition { get; init; }
        public ASTExpression? ConditionExpression { get; set; }
        public required int BodyOffset { get; init; }
        public ASTBlock Body => BlocksByOffset[BodyOffset];
        public HashSet<ASTBlock> Loop { get; init; } = new();
        public override IEnumerable<ASTNode> Children => base.Children.Concat(Loop);
        public override bool CanFallthrough => false;

        protected override void WriteToInternal(CodeWriter writer)
        {
            using var subWriter = writer.Indented;

            if (IsPostCondition)
            {
                writer.WriteLine("do {");
                Body.WriteTo(subWriter);
                writer.Write("} while ");
                WriteExpressionOrBlock(writer, ConditionExpression, Condition);
                writer.WriteLine(';');
            }
            else
            {
                writer.Write("while ");
                WriteExpressionOrBlock(writer, ConditionExpression, Condition);
                writer.WriteLine(" {");
                Body.WriteTo(subWriter);
                writer.WriteLine("}");
            }
        }
    }

    private class ASTIfElse : ASTBlock
    {
        // for JumpIf Condition is formed by args with potential instructions before the jump
        public ASTBlock? Prefix { get; init; }
        public required ASTBlock Condition { get; init; }
        public ASTExpression? ConditionExpression { get; set; }
        public required int? ThenOffset { get; init; }
        public ASTBlock? Then => ThenOffset == null ? null : BlocksByOffset[ThenOffset.Value];
        public required int? ElseOffset { get; init; }
        public ASTBlock? Else => ElseOffset == null ? null : BlocksByOffset[ElseOffset.Value];
        public override IEnumerable<ASTNode> Children =>
            base.Children.Concat(new[] { Prefix, Condition, Then, Else }.Where(n => n != null))!;
        public override bool CanFallthrough => false;

        protected override void WriteToInternal(CodeWriter writer)
        {
            using var subWriter = writer.Indented;
            Prefix?.WriteTo(writer);
            writer.Write("if ");
            WriteExpressionOrBlock(writer, ConditionExpression, Condition);
            writer.WriteLine(" {");
            if (Then == null)
                subWriter.WriteLine("// nothing");
            else
                Then.WriteTo(subWriter);
            if (Else == null)
            {
                writer.WriteLine("}");
                return;
            }
            writer.WriteLine("} else {");
            Else.WriteTo(subWriter);
            writer.WriteLine("}");
        }
    }

    private class ASTSwitch : ASTBlock
    {
        public readonly struct Case<TThen>
        {
            public required IReadOnlyList<int?> Compares { get; init; }
            public required TThen Then { get; init; } 
            public bool Breaks { get; init; }
        }

        public ASTBlock? Prefix { get; init; }
        public required ASTBlock Value { get; init; }
        public ASTExpression? ValueExpression { get; set; }
        public required IReadOnlyList<Case<int?>> CaseOffsets { get; init; }
        public IEnumerable<Case<ASTBlock?>> CaseBlocks => CaseOffsets
            .Select(c => new Case<ASTBlock?>()
            {
                Compares = c.Compares,
                Then = c.Then == null ? null : BlocksByOffset[c.Then.Value],
                Breaks = c.Breaks
            });
        public override IEnumerable<ASTNode> Children => base.Children
            .Concat(new[] { Prefix, Value })
            .Concat(CaseBlocks.Select(t => t.Then))
            .Where(b => b != null)!;
        public override bool CanFallthrough => false;

        protected override void WriteToInternal(CodeWriter writer)
        {
            using var subWriter = writer.Indented;
            Prefix?.WriteTo(writer);
            writer.Write("switch ");
            WriteExpressionOrBlock(writer, ValueExpression, Value);
            writer.WriteLine(" {");

            foreach (var @case in CaseBlocks)
            {
                foreach (var compare in @case.Compares)
                {
                    if (compare.HasValue)
                        writer.WriteLine($"case {compare}:");
                    else
                        writer.WriteLine("default:");
                }
                @case.Then?.WriteTo(subWriter);
                if (@case.Breaks)
                    subWriter.WriteLine("break;");
            }

            writer.WriteLine("}");
        }
    }
}
