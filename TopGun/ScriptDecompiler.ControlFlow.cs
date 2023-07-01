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
        ScriptOp.Return,
        ScriptOp.Exit
    };

    private void CreateInitialBlocks()
    {
        allBlocks.Clear();
        allBlocks.Add(astExit);
        allBlocks.Add(astEntry);

        if (((ASTNormalBlock)astEntry).Instructions.Last().HasFallthrough)
            astEntry.AddOutbound(astExit); // implicit exit at end of script

        var curBlock = (ASTNormalBlock)astEntry;
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
                curBlock.Inbound.Single().Outbound.Clear();
                curBlock.Inbound.Clear();
            }
        }
    }

    private void SetBlockEdges()
    {
        var instrByOffset = allBlocks
            .OfType<ASTNormalBlock>()
            .SelectMany(b => b.Instructions)
            .OfType<ASTRootOpInstruction>()
            .ToDictionary(i => i.StartTotalOffset, i => i);

        foreach (var instr in instrByOffset.Values.Where(i => i.RootInstruction.Op is ScriptOp.Return or ScriptOp.Exit))
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
            allBlocks.Add(newBlock);
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
        newBlock.Inbound.UnionWith(oldBlock.Inbound);
        newBlock.Parent = oldBlock.Parent;
    }

    private class NaturalLoop
    {
        public NaturalLoop? Parent { get; set; }
        public List<NaturalLoop> Children { get; } = new();
        public required ASTBlock Header { get; init; }
        public required HashSet<ASTBlock> Body { get; init; }
        public IEnumerable<NaturalLoop> AllLoops => Children.Prepend(this);

        public int Rank
        {
            get 
            {
                int rank = 0;
                var cur = this;
                while(cur != null)
                {
                    rank++;
                    cur = cur.Parent;
                }
                return rank;
            }
        }

        public static int DescendingSizeComparison(NaturalLoop a, NaturalLoop b) => b.Body.Count - a.Body.Count;

        public void AddChild(NaturalLoop loop)
        {
            var parentChild = Children.SingleOrDefault(c => c.Body.Overlaps(loop.Body));
            if (parentChild == null)
            {
                loop.Parent = this;
                Children.Add(loop);
                Children.Sort(DescendingSizeComparison);
                return;
            }
            if (!parentChild.Body.IsSupersetOf(loop.Body))
                throw new NotSupportedException("Unsupported control flow with overlapping, non-nested loop bodies");
            parentChild.AddChild(loop);
        }
    }

    private List<NaturalLoop> DetectLoops()
    {
        var headers = new HashSet<ASTBlock>();
        var sortedBySize = new List<NaturalLoop>();
        var backEdges = allBlocks.SelectMany(header => header.Inbound
            .Where(bodyEnd => header.PreDominates(bodyEnd))
            .Select(bodyEnd => (header, bodyEnd)));
        foreach (var (header, bodyEnd) in backEdges)
        {
            var body = new HashSet<ASTBlock>() { bodyEnd };
            foreach (var potentialBody in allBlocks.Where(header.PreDominates))
                CheckAndMarkReachable(body, header, potentialBody);
            body.Add(header);
            
            if (!headers.Add(header))
                throw new NotSupportedException("Unsupported control flow with merged loops");
            sortedBySize.Add(new()
            {
                Header = header,
                Body = body
            });
        }
        
        // Construct hierarchy by using a dummy "root-of-roots" loop
        var rootLoop = new NaturalLoop()
        {
            Header = null!,
            Body = null!
        };
        sortedBySize.Sort(NaturalLoop.DescendingSizeComparison);
        sortedBySize.ForEach(rootLoop.AddChild);
        rootLoop.Children.ForEach(c => c.Parent = null);
        return rootLoop.Children;

        void CheckAndMarkReachable(HashSet<ASTBlock> body, ASTBlock header, ASTBlock start)
        {
            // Using pre-order traversal.
            if (start == header || body.Contains(start))
                return;
            var visited = new HashSet<ASTBlock>(allBlocks.Count);
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
                    return;
                }

                stack.Push((parent, edgeIt));
                if (child != header && !visited.Contains(child))
                    stack.Push((child, child.Outbound.GetEnumerator()));
            }
        }
    }

    private void ConstructLoops(IReadOnlyList<NaturalLoop> rootLoops)
    {
        var allLoops = rootLoops.SelectMany(l => l.AllLoops).OrderByDescending(l => l.Rank);
        foreach (var loop in allLoops)
        {
            var backTarget = loop.Header;
            var targetInbounds = backTarget.Inbound.ToArray();
            var targetOutbounds = backTarget.Outbound.ToArray();
            if (targetInbounds.Length != 2 || targetOutbounds.Length != 2)
                throw new NotSupportedException("Loop with unexpected structure, header has not two inbound and two outbound blocks");

            var backSource = loop.Body.Contains(targetInbounds[0]) ? targetInbounds[0] : targetInbounds[1];
            var externalInbound = backSource == targetInbounds[0] ? targetInbounds[1] : targetInbounds[0];
            if (!loop.Body.Contains(backSource) || loop.Body.Contains(externalInbound))
                throw new NotSupportedException("Loop with unexpected structure, header inbounds are not supported");

            if (backSource.Outbound.Count != 1)
                throw new NotSupportedException("Loop with unexpected structure, back-edge block is branching");
            backSource.NeedsExplicitControlFlow = false;
            var jumpBackInstr = ((ASTNormalBlock)backSource).Instructions.RemoveLast();
            if ((jumpBackInstr as ASTRootOpInstruction)?.RootInstruction.Op != ScriptOp.Jump)
                throw new NotSupportedException("Should the back-edge source block not end with an unconditional jump?");
            
            var loopEntry = loop.Body.Contains(targetOutbounds[0]) ? targetOutbounds[0] : targetOutbounds[1];
            var externalOutbound = loopEntry == targetOutbounds[0] ? targetOutbounds[1] : targetOutbounds[0];
            if (!loop.Body.Contains(loopEntry) || loop.Body.Contains(externalOutbound))
                throw new NotSupportedException("Loop with unexpected structure, header outbounds are not supported");

            var astLoop = new ASTLoop()
            {
                IsPostCondition = false,
                Condition = backTarget,
                Body = loopEntry,
                Loop = loop.Body,
                ContinueBlock = externalOutbound
            };
            foreach (var block in loop.Body)
                block.Parent = astLoop;
            allBlocks.Add(astLoop);

            if (loop.Parent != null)
            {
                loop.Parent.Body.ExceptWith(loop.Body);
                loop.Parent.Body.Add(astLoop);
            }
        }
    }

    private void ConstructSelections() { }

    private void ConstructGotos()
    {
        foreach (var block in allBlocks.OfType<ASTNormalBlock>())
        {

            if (block.Instructions.LastOrDefault() is not ASTRootOpInstruction rootInstr ||
                !block.NeedsExplicitControlFlow)
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

    private void ConstructSimpleContinues()
    {
        foreach (var block in allBlocks.OfType<ASTNormalBlock>())
        {
            if (block.ContinueBlock != null ||
                !block.NeedsExplicitControlFlow ||
                !block.Instructions.Last().HasFallthrough)
                continue;
            block.ContinueBlock = allBlocks.FirstOrDefault(b => b.Parent == block.Parent && b.StartTotalOffset == block.EndTotalOffset);
        }
    }
}
