using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TopGun;

partial class ScriptDecompiler
{
    private static readonly IReadOnlySet<ScriptOp> BranchingOps = new HashSet<ScriptOp>()
    {
        ScriptOp.JumpIf,
        ScriptOp.JumpIfCalc,
        ScriptOp.JumpIfCalc_dup,
        ScriptOp.Switch,
        ScriptOp.CalcSwitch
    };
    private static readonly IReadOnlySet<ScriptOp> SplittingOps = new HashSet<ScriptOp>(BranchingOps)
    {
        ScriptOp.Jump,
        ScriptOp.Return,
        ScriptOp.Exit
    };

    private void CreateInitialBlocks()
    {
        allBlocks.Clear();
        allBlocks.Add(astExit);
        allBlocks.Add(astEntry);
        var curBlock = astEntry;
        while (true)
        {
            var splittingOp = curBlock.Instructions
                .OfType<ASTRootOpInstruction>()
                .FirstOrDefault(i => SplittingOps.Contains(i.RootInstruction.Op));
            if (splittingOp == null || splittingOp == curBlock.Instructions.Last())
                break;
            curBlock = curBlock.SplitAfter(splittingOp);
            allBlocks.Add(curBlock);

            if (splittingOp.RootInstruction.Op != ScriptOp.JumpIf)
            {
                // JumpIf is the only op with a default fallthrough, all else have explicit targets or break execution
                curBlock.InboundForward.Single().Outbound.Clear();
                curBlock.InboundForward.Clear();
            }
        }
    }

    private void SetOutboundEdges()
    {
        var edges = new List<(int fromOffset, int targetOffset)>()
        {
            (allBlocks.Last().Instructions.Last().StartTotalOffset, script.Length)
        };

        foreach (var block in allBlocks.Skip(1)) // skip exit block
        {
            var instr = ((ASTRootOpInstruction)block.Instructions.Last()).RootInstruction;
            if (instr.Op is ScriptOp.Return or ScriptOp.Exit)
            {
                SetEdge(block, astExit);
                continue;
            }

            foreach (var arg in instr.Args.Where(a => a.Type == ScriptRootInstruction.ArgType.InstructionOffset))
                edges.Add((instr.Offset, instr.Offset + arg.Value));
        }

        foreach (var (from, targetOffset) in edges)
            SetEdge(FindBlockAt(from, false), FindBlockAt(targetOffset, true));

        void SetEdge(ASTBlock from, ASTBlock to)
        {
            from.Outbound.Add(to);
        }

        ASTBlock FindBlockAt(int offset, bool split)
        {
            if (offset == script.Length)
                return astExit;
            else if (offset < 0 || offset > script.Length)
                throw new ArgumentOutOfRangeException(nameof(offset), "Invalid target offset, outside of script range");

            foreach (var block in allBlocks)
            {
                var instruction = block.Instructions.FirstOrDefault(i => i.StartTotalOffset == offset);
                if (instruction == null)
                    continue;
                else if (block.Instructions.First() == instruction)
                    return block;
                else if (!split && block.Instructions.Last() == instruction)
                    return block;
                var newBlock = block.SplitBefore(instruction);
                allBlocks.Add(newBlock);
                return newBlock;
            }

            throw new ArgumentException("Invalid target offset, does not point to root instruction");
        }
    }

    /// <remarks>Using depth-first traversal, pre-order</remarks>
    private void SetInboundEdges()
    {
        var visited = new HashSet<ASTBlock>(allBlocks.Count);
        var stack = new Stack<(ASTBlock, IEnumerator<ASTBlock>)>();
        stack.Push((astEntry, astEntry.Outbound.GetEnumerator()));
        while (stack.Any())
        {
            var (parent, edgeIt) = stack.Pop();
            visited.Add(parent);
            if (!edgeIt.MoveNext())
                continue;
            var child = edgeIt.Current;

            (stack.Any(t => t.Item1 == child)
                ? child.InboundBackward
                : child.InboundForward).Add(parent);

            stack.Push((parent, edgeIt));
            if (!visited.Contains(child))
                stack.Push((child, child.Outbound.GetEnumerator()));
        }
    }

    private interface IBlockIterator
    {
        ASTBlock Start { get; }

        int GetOrder(ASTBlock block);
        void SetOrder(ASTBlock block, int order);
        ASTBlock? GetImmediateDominator(ASTBlock block);
        void SetImmediateDominator(ASTBlock block, ASTBlock dominator);
        IEnumerable<ASTBlock> GetOutbound(ASTBlock block);
        IEnumerable<ASTBlock> GetInbound(ASTBlock block);
    }

    private class ForwardBlockIterator : IBlockIterator
    {
        public ASTBlock Start { get; }
        public ForwardBlockIterator(ASTBlock start) => Start = start;
        public int GetOrder(ASTBlock block) => block.PostOrderI;
        public void SetOrder(ASTBlock block, int order) => block.PostOrderI = order;
        public ASTBlock? GetImmediateDominator(ASTBlock block) => block.ImmediatePreDominator;
        public void SetImmediateDominator(ASTBlock block, ASTBlock dominator) => block.ImmediatePreDominator = dominator;
        public IEnumerable<ASTBlock> GetOutbound(ASTBlock block) => block.Outbound;
        public IEnumerable<ASTBlock> GetInbound(ASTBlock block) => block.InboundForward.Concat(block.InboundBackward);
    }

    private class BackwardBlockIterator : IBlockIterator
    {
        public ASTBlock Start { get; }
        public BackwardBlockIterator(ASTBlock start) => Start = start;
        public int GetOrder(ASTBlock block) => block.PostOrderRevI;
        public void SetOrder(ASTBlock block, int order) => block.PostOrderRevI = order;
        public ASTBlock? GetImmediateDominator(ASTBlock block) => block.ImmediatePostDominator;
        public void SetImmediateDominator(ASTBlock block, ASTBlock dominator) => block.ImmediatePostDominator = dominator;
        public IEnumerable<ASTBlock> GetOutbound(ASTBlock block) => block.InboundForward.Concat(block.InboundBackward);
        public IEnumerable<ASTBlock> GetInbound(ASTBlock block) => block.Outbound;
    }

    private void SetPostOrderNumber() => SetPostOrder(new ForwardBlockIterator(astEntry));
    private void SetPostOrderRevNumber() => SetPostOrder(new BackwardBlockIterator(astExit));

    /// <remarks>Using depth-first traversal, post-order</remarks>
    private void SetPostOrder(IBlockIterator it)
    {
        var visited = new HashSet<ASTBlock>(allBlocks.Count);
        var stack = new Stack<(ASTBlock, IEnumerator<ASTBlock>)>();
        stack.Push((it.Start, it.GetOutbound(it.Start).GetEnumerator()));
        int next = 0;
        while (stack.Any())
        {
            var (parent, edgeIt) = stack.Pop();
            visited.Add(parent);
            if (edgeIt.MoveNext())
            {
                var child = edgeIt.Current;
                stack.Push((parent, edgeIt));
                if (!visited.Contains(child))
                    stack.Push((child, it.GetOutbound(child).GetEnumerator()));
            }
            else
                it.SetOrder(parent, next++);
        }
    }

    private void SetPreDominators() => SetDominators(new ForwardBlockIterator(astEntry));
    private void SetPostDominators() => SetDominators(new BackwardBlockIterator(astExit));

    /// <remarks>Cooper, Harvey, Kennedy - "A Simple, Fast Dominance Algorithm"</remarks>
    private void SetDominators(IBlockIterator it)
    {
        var revOrderBlocks = allBlocks
            .Except(new[] { it.Start })
            .OrderByDescending(it.GetOrder)
            .ToArray();
        it.SetImmediateDominator(it.Start, it.Start);

        bool changed;
        do
        {
            changed = false;
            foreach (var block in revOrderBlocks)
            {
                var inbound = it.GetInbound(block)
                    .Where(b => it.GetImmediateDominator(b) != null);
                if (!inbound.Any())
                    continue;

                var oldDominator = it.GetImmediateDominator(block);
                var newDominator = inbound.Aggregate(Intersect!) ?? block;
                if (oldDominator != newDominator)
                {
                    it.SetImmediateDominator(block, newDominator);
                    changed = true;
                }
            }
        } while (changed);

        ASTBlock? Intersect(ASTBlock b1, ASTBlock b2)
        {
            if (b1 == null)
                return null;
            while (b1 != b2)
            {
                while (it.GetOrder(b1) < it.GetOrder(b2))
                {
                    if (b1 == it.GetImmediateDominator(b1))
                        return null;
                    b1 = it.GetImmediateDominator(b1)!;
                }
                while(it.GetOrder(b2) < it.GetOrder(b1))
                {
                    if (b2 == it.GetImmediateDominator(b2))
                        return null;
                    b2 = it.GetImmediateDominator(b2)!;
                }
            }
            return b1;
        }
    }

    private void ReplaceBlockWith(ASTBlock oldBlock, ASTBlock newBlock)
    {
        var blockIndex = allBlocks.IndexOf(oldBlock);
        if (blockIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(oldBlock), "Cannot replace invalid block");
        newBlock.PostOrderI = oldBlock.PostOrderI;
        newBlock.PostOrderRevI = oldBlock.PostOrderRevI;
        newBlock.ImmediatePreDominator = oldBlock.ImmediatePreDominator;
        newBlock.ImmediatePostDominator = oldBlock.ImmediatePostDominator;
        newBlock.Outbound.UnionWith(oldBlock.Outbound);
        newBlock.InboundForward.UnionWith(oldBlock.InboundForward);
        newBlock.InboundBackward.UnionWith(oldBlock.InboundBackward);
        newBlock.Parent = oldBlock.Parent;
    }

    private void ConstructLoops()
    {
    }

    private void ConstructSelections() { }

    private void ConstructGotos()
    {
        foreach (var block in allBlocks)
        {

            if (block is not ASTNormalBlock || block.Instructions.Last() is not ASTRootOpInstruction rootInstr)
                continue;

            int target = block.EndTotalOffset;
            var removeLast = false;
            if (rootInstr.RootInstruction.Op == ScriptOp.Jump)
            {
                removeLast = true;
                target = rootInstr.RootInstruction.Offset + rootInstr.RootInstruction.Args[0].Value;
            }
            else if (SplittingOps.Contains(rootInstr.RootInstruction.Op))
                continue;
            if (target == block.EndTotalOffset)
                continue;

            ASTInstruction addInstr;
            if (target == script.Length)
                addInstr = new ASTReturn();
            else
            {
                var targetBlock = allBlocks.FirstOrDefault(b => b.StartTotalOffset == target)
                    ?? throw new Exception($"This should not have happened, goto instruction has invalid target");
                targetBlock.IsLabeled = true;
                addInstr = new ASTGoto() { Target = target };
            }

            addInstr.Parent = block;
            if (removeLast)
            {
                block.Instructions.RemoveLast();
                addInstr.StartOwnOffset = rootInstr.StartTotalOffset;
                addInstr.EndOwnOffset = rootInstr.EndTotalOffset;
            }
            block.Instructions.Add(addInstr);
        }
    }
}
