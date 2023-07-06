using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
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
        ScriptOp.Exit
    };

    private void CreateInitialBlocks()
    {
        if (((ASTNormalBlock)ASTEntry).Instructions.Last().CanFallthough)
            ASTEntry.AddOutbound(astExit); // implicit exit at end of script

        var curBlock = (ASTNormalBlock)ASTEntry;
        while (true)
        {
            var splittingOp = curBlock.Instructions
                .OfType<ASTRootOpInstruction>()
                .FirstOrDefault(i => SplittingOps.Contains(i.RootInstruction.Op));
            if (splittingOp == null || splittingOp == curBlock.Instructions.Last())
                break;
            curBlock = curBlock.SplitAfter(splittingOp);
            blocksByOffset.Add(curBlock.StartTotalOffset, curBlock);

            if (splittingOp.RootInstruction.Op != ScriptOp.JumpIf)
                // JumpIf is the only op with a default fallthrough, all else have explicit targets or break execution
                curBlock.RemoveInbound(curBlock.Inbound.Single());
        }

        // to preserve debug info when cleared during transformations
        foreach (var block in blocksByOffset.Values)
            block.StartOwnOffset = block.EndOwnOffset = block.StartTotalOffset;
    }

    private void SetBlockEdges()
    {
        var instrByOffset = blocksByOffset.Values
            .OfType<ASTNormalBlock>()
            .SelectMany(b => b.Instructions)
            .OfType<ASTRootOpInstruction>()
            .ToDictionary(i => i.StartTotalOffset, i => i);

        foreach (var instr in instrByOffset.Values.Where(i => i.RootInstruction.Op == ScriptOp.Exit))
            ((ASTBlock)instr.Parent!).AddOutbound(astExit);

        var edges = instrByOffset.Values.SelectMany(instr => instr.RootInstruction.Args
            .Where(a => a.Type == ScriptRootInstruction.ArgType.InstructionOffset)
            .Select(arg => (instr.StartTotalOffset, instr.StartTotalOffset + arg.Value)));
        foreach (var (from, to) in edges)
            FindBlockEndingWith(from).AddOutbound(FindBlockStartingWith(to));

        (ASTBlock, ASTRootOpInstruction) FindInstructionAt(int offset)
        {
            if (offset == script.Length)
                return (astExit, null!);
            else if (!instrByOffset.TryGetValue(offset, out var instr))
                throw new ArgumentException("Invalid offset to instruction");
            else
                return ((ASTBlock)instr.Parent!, instr);
        }

        ASTBlock FindBlockStartingWith(int offset)
        {
            var (block, instr) = FindInstructionAt(offset);
            if (block.StartTotalOffset == offset)
                return block;
            var newBlock = ((ASTNormalBlock)block).SplitBefore(instr);
            blocksByOffset.Add(newBlock.StartTotalOffset, newBlock);
            return newBlock;
        }

        ASTBlock FindBlockEndingWith(int offset)
        {
            var (block, instr) = FindInstructionAt(offset);
            if (((ASTNormalBlock)block).Instructions.Last() != instr)
                throw new Exception("Splitting went wrong, source is not at end of block");
            return block;
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
        public IEnumerable<ASTBlock> GetInbound(ASTBlock block) => block.Inbound;
    }

    private class BackwardBlockIterator : IBlockIterator
    {
        public ASTBlock Start { get; }
        public BackwardBlockIterator(ASTBlock start) => Start = start;
        public int GetOrder(ASTBlock block) => block.PostOrderRevI;
        public void SetOrder(ASTBlock block, int order) => block.PostOrderRevI = order;
        public ASTBlock? GetImmediateDominator(ASTBlock block) => block.ImmediatePostDominator;
        public void SetImmediateDominator(ASTBlock block, ASTBlock dominator) => block.ImmediatePostDominator = dominator;
        public IEnumerable<ASTBlock> GetOutbound(ASTBlock block) => block.Inbound;
        public IEnumerable<ASTBlock> GetInbound(ASTBlock block) => block.Outbound;
    }

    private void SetPostOrderNumber() => SetPostOrder(new ForwardBlockIterator(ASTEntry));
    private void SetPostOrderRevNumber() => SetPostOrder(new BackwardBlockIterator(astExit));

    /// <remarks>Using depth-first traversal, post-order</remarks>
    private void SetPostOrder(IBlockIterator it)
    {
        var visited = new HashSet<ASTBlock>(blocksByOffset.Count);
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

    private void SetPreDominators() => SetDominators(new ForwardBlockIterator(ASTEntry));
    private void SetPostDominators() => SetDominators(new BackwardBlockIterator(astExit));

    /// <remarks>Cooper, Harvey, Kennedy - "A Simple, Fast Dominance Algorithm"</remarks>
    private void SetDominators(IBlockIterator it)
    {
        var revOrderBlocks = blocksByOffset.Values
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

    private List<NaturalLoop> DetectLoops()
    {
        var headers = new HashSet<ASTBlock>();
        var allLoops = new List<GroupingConstruct>();
        var backEdges = blocksByOffset.Values.SelectMany(header => header.Inbound
            .Where(bodyEnd => header.PreDominates(bodyEnd))
            .Select(bodyEnd => (header, bodyEnd)));
        foreach (var (header, bodyEnd) in backEdges)
        {
            var body = new HashSet<ASTBlock>() { bodyEnd };
            foreach (var potentialBody in blocksByOffset.Values.Where(header.PreDominates))
                FindReachableBlocksFrom(body, header, potentialBody);
            body.Add(header);
            
            if (!headers.Add(header))
                throw new NotSupportedException("Unsupported control flow with merged loops");
            allLoops.Add(new NaturalLoop()
            {
                Decompiler = this,
                Header = header,
                Body = body
            });
        }

        return NaturalLoop.CreateHierarchy(allLoops).OfType<NaturalLoop>().ToList();
    }

    private HashSet<ASTBlock> FindReachableBlocksFrom(HashSet<ASTBlock> body, ASTBlock header, ASTBlock start)
    {
        // Using pre-order traversal.
        if (start == header || body.Contains(start))
            return body;
        var visited = new HashSet<ASTBlock>(blocksByOffset.Count);
        var stack = new Stack<(ASTBlock, IEnumerator<ASTBlock>)>();
        stack.Push((start, start.Outbound.GetEnumerator()));
        while (stack.Any())
        {
            var (parent, edgeIt) = stack.Pop();
            visited.Add(parent);
            if (!edgeIt.MoveNext())
                continue;
            var child = edgeIt.Current;

            // All blocks in body are reachable so all finish the search
            if (body.Contains(child))
            {
                // the stack now contains a path with only reachable nodes so we add all of them
                // for weird control graphs not all blocks on our path are dominated by the header
                // so we check again. However all of them reach the backedge source.
                body.UnionWith(stack
                    .Select(t => t.Item1)
                    .Prepend(parent)
                    .Prepend(child)
                    .Where(header.PreDominates));
            }

            stack.Push((parent, edgeIt));
            if (child != header && !visited.Contains(child))
                stack.Push((child, child.Outbound.GetEnumerator()));
        }
        return body;
    }

    private List<Selection> DetectSelections()
    {
        var headers = blocksByOffset.Values
            .Where(b => b.Parent == null)
            .Where(b => b.Outbound.Count() > 1);
        var allSelections = new List<GroupingConstruct>();
        foreach (var header in headers)
        {
            var mergeBlock = header.Outbound
                .Select(b => b.PostDominators.Prepend(b))
                .Aggregate((a, b) => a.Intersect(b).ToArray())
                .FirstOrDefault();
            if (mergeBlock == null) // You remember that loops have no dominator info calculated?
                throw new NotSupportedException("Could not find merge block for selection");

            var branches = header.Outbound
                .Select(branch => FindReachableBlocksFrom(new() { mergeBlock }, header, branch))
                .ToArray();
            foreach (var branch in branches)
                branch.Remove(mergeBlock);
            for (int i = 0; i < branches.Length; i++)
            {
                for (int j = i + 1; j < branches.Length; j++)
                {
                    if (branches[i].Overlaps(branches[j]))
                        throw new NotSupportedException("Unsupported selection with merged branches");
                }
            }

            var body = branches.SelectMany(a => a).Prepend(header).ToHashSet();
            var lastOp = ((ASTRootOpInstruction)((ASTNormalBlock)header).Instructions.Last()).RootInstruction.Op;
            allSelections.Add(lastOp switch
            {
                ScriptOp.JumpIf => new JumpIfSelection()
                {
                    Decompiler = this,
                    Header = header,
                    Body = body,
                    Merge = mergeBlock
                },
                ScriptOp.JumpIfCalc or
                ScriptOp.JumpIfCalc_dup => new JumpIfCalcSelection()
                {
                    Decompiler = this,
                    Header = header,
                    Body = body,
                    Merge = mergeBlock
                },
                ScriptOp.Switch or
                ScriptOp.CalcSwitch => new SwitchSelection()
                {
                    Decompiler = this,
                    Header = header,
                    Body = body,
                    Merge = mergeBlock
                },
                _ => throw new Exception("Somehow selection header has unknown last instruction")
            });
        }

        return Selection.CreateHierarchy(allSelections).OfType<Selection>().ToList();
    }
}
