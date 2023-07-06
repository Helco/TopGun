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
        blocksByOffset.Clear();
        blocksByOffset.Add(astExit.StartTotalOffset, astExit);
        blocksByOffset.Add(0, astEntry);

        if (((ASTNormalBlock)astEntry).Instructions.Last().CanFallthough)
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

    private void SetPostOrderNumber() => SetPostOrder(new ForwardBlockIterator(astEntry));
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

    private void SetPreDominators() => SetDominators(new ForwardBlockIterator(astEntry));
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

    private class GroupingConstruct<T> where T : GroupingConstruct<T>, new()
    {
        public T? Parent { get; set; }
        public List<T> Children { get; } = new();
        public HashSet<ASTBlock> Body { get; init; } = new();
        public IEnumerable<T> AllChildren => Children.SelectMany(c => c.AllChildren).Prepend((T)this);

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

        public static int DescendingSizeComparison(T a, T b) => b.Body.Count - a.Body.Count;

        public void AddChild(T child)
        {
            var parentChild = Children.SingleOrDefault(c => c.Body.Overlaps(child.Body));
            if (parentChild == null)
            {
                child.Parent = (T)this;
                Children.Add(child);
                Children.Sort(DescendingSizeComparison);
                return;
            }
            if (!parentChild.Body.IsSupersetOf(child.Body))
                throw new NotSupportedException("Unsupported control flow with overlapping, non-nested construct bodies");
            parentChild.AddChild(child);
        }

        public static List<T> CreateHierarchy(List<T> allConstructs)
        {
            var dummyRoot = new T()
            {
                Body = null!
            };
            allConstructs.Sort(DescendingSizeComparison);
            allConstructs.ForEach(dummyRoot.AddChild);
            dummyRoot.Children.ForEach(c => c.Parent = null);
            return dummyRoot.Children;
        }
    }

    private class NaturalLoop : GroupingConstruct<NaturalLoop>
    {
        public ASTBlock Header { get; init; } = null!; 
    }

    private class Selection : GroupingConstruct<Selection>
    {
        public ASTBlock Header { get; init; } = null!;
        public ASTBlock Merge { get; init; } = null!;
    }

    private List<NaturalLoop> DetectLoops()
    {
        var headers = new HashSet<ASTBlock>();
        var allLoops = new List<NaturalLoop>();
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
            allLoops.Add(new()
            {
                Header = header,
                Body = body
            });
        }

        return NaturalLoop.CreateHierarchy(allLoops);
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

    private void ConstructLoops(IReadOnlyList<NaturalLoop> rootLoops)
    {
        var allLoops = rootLoops.SelectMany(l => l.AllChildren).OrderByDescending(l => l.Rank);
        foreach (var loop in allLoops)
        {
            var backTarget = loop.Header;
            var targetInbounds = backTarget.Inbound.ToArray();
            var targetOutbounds = backTarget.Outbound.ToArray();
            if (targetInbounds.Length != 2 || targetOutbounds.Length != 2)
                throw new NotSupportedException("Loop with unexpected structure, header has not two inbound and two outbound blocks");

            var backSource = (ASTNormalBlock)(loop.Body.Contains(targetInbounds[0]) ? targetInbounds[0] : targetInbounds[1]);
            var externalInbound = backSource == targetInbounds[0] ? targetInbounds[1] : targetInbounds[0];
            if (!loop.Body.Contains(backSource) || loop.Body.Contains(externalInbound))
                throw new NotSupportedException("Loop with unexpected structure, header inbounds are not supported");

            if (backSource.Outbound.Count() != 1)
                throw new NotSupportedException("Loop with unexpected structure, back-edge block is branching");
            backSource.ConstructProvidesControlFlow = true;
            backSource.LastInstructionIsRedundantControlFlow = true;
            var jumpBackInstr = ((ASTNormalBlock)backSource).Instructions.RemoveLast();
            if ((jumpBackInstr as ASTRootOpInstruction)?.RootInstruction.Op != ScriptOp.Jump)
                throw new NotSupportedException("Should the back-edge source block not end with an unconditional jump?");
            
            var loopEntry = loop.Body.Contains(targetOutbounds[0]) ? targetOutbounds[0] : targetOutbounds[1];
            var externalOutbound = loopEntry == targetOutbounds[0] ? targetOutbounds[1] : targetOutbounds[0];
            if (!loop.Body.Contains(loopEntry) || loop.Body.Contains(externalOutbound))
                throw new NotSupportedException("Loop with unexpected structure, header outbounds are not supported");

            var astLoop = new ASTLoop()
            {
                BlocksByOffset = blocksByOffset,
                IsPostCondition = false,
                Condition = backTarget,
                BodyOffset = loopEntry.StartTotalOffset,
                Loop = loop.Body,
                ContinueOffset = externalOutbound?.StartTotalOffset
            };
            foreach (var block in loop.Body)
                block.Parent = astLoop;
            blocksByOffset[loop.Header.StartTotalOffset] = astLoop;

            if (loop.Parent != null)
            {
                loop.Parent.Body.ExceptWith(loop.Body);
                loop.Parent.Body.Add(astLoop);
            }
            else if (loop.Header == astEntry)
                astEntry = astLoop;
        }
    }

    private List<Selection> DetectSelections()
    {
        var headers = blocksByOffset.Values
            .Where(b => b.Parent == null)
            .Where(b => b.Outbound.Count() > 1);
        var allSelections = new List<Selection>();
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

            allSelections.Add(new()
            {
                Header = header,
                Body = branches.SelectMany(a => a).Prepend(header).ToHashSet(),
                Merge = mergeBlock
            });
        }

        return Selection.CreateHierarchy(allSelections);
    }

    private void ConstructSelections(List<Selection> rootSelections)
    {
        var allSelections = rootSelections.SelectMany(s => s.AllChildren).OrderByDescending(s => s.Rank);
        foreach (var selection in allSelections)
        {
            var lastInstruction = ((ASTNormalBlock)selection.Header).Instructions.Last();
            var lastOp = ((ASTRootOpInstruction)lastInstruction).RootInstruction.Op;
            switch (lastOp)
            {
                case ScriptOp.JumpIf:
                    ConstructJumpIf(selection);
                    break;
                case ScriptOp.JumpIfCalc:
                case ScriptOp.JumpIfCalc_dup:
                    ConstructJumpIfCalc(selection);
                    break;
                case ScriptOp.Switch:
                case ScriptOp.CalcSwitch:
                    ConstructSwitch(selection, lastOp == ScriptOp.CalcSwitch);
                    break;
            }
        }
    }

    private void ConstructJumpIfCalc(Selection selection)
    {
        if (selection.Header.Outbound.Count() > 2)
            throw new Exception("Something went wrong trying to construct a JumpIfCalc construct");

        var lastInstruction = ((ASTRootOpInstruction)((ASTNormalBlock)selection.Header).Instructions.Last()).RootInstruction;
        var thenOffset = lastInstruction.Offset + lastInstruction.Args[0].Value;
        var elseOffset = lastInstruction.Offset + lastInstruction.Args[1].Value;
        
        var astIfElse = new ASTIfElse()
        {
            BlocksByOffset = blocksByOffset,
            Condition = selection.Header,
            ThenOffset = thenOffset,
            ElseOffset = elseOffset == selection.Merge.StartTotalOffset ? null : elseOffset,
            ContinueOffset = selection.Merge == selection.Parent?.Merge ? null : selection.Merge.StartTotalOffset,
            StartOwnOffset = selection.Header.StartTotalOffset,
            EndOwnOffset = selection.Header.EndTotalOffset
        };
        //((ASTNormalBlock)selection.Header).Instructions.RemoveLast(); // the JumpIfCalc instruction
        blocksByOffset[selection.Header.StartTotalOffset] = astIfElse;

        foreach (var lastBlock in selection.Merge.Inbound.Union(selection.Body))
            lastBlock.ConstructProvidesControlFlow = true;
        selection.Header.Parent = astIfElse;
        foreach (var body in selection.Body)
            body.Parent = astIfElse;

        if (selection.Parent != null)
        {
            selection.Parent.Body.ExceptWith(selection.Body);
            selection.Parent.Body.Remove(selection.Header);
            selection.Parent.Body.Add(astIfElse);
        }
        else if (selection.Header == astEntry)
            astEntry = astIfElse;
    }

    private void ConstructJumpIf(Selection selection)
    {
        if (selection.Header.Outbound.Count() != 2)
            throw new Exception("Something went wrong trying to construct a JumpIf");

        var lastInstruction = ((ASTRootOpInstruction)((ASTNormalBlock)selection.Header).Instructions.Last()).RootInstruction;
        var jumpTarget = lastInstruction.Offset + lastInstruction.Args[0].Value;
        var thenBlock = selection.Header.Outbound.SingleOrDefault(b => b.StartTotalOffset != jumpTarget);
        var elseBlock = selection.Header.Outbound.SingleOrDefault(b => b.StartTotalOffset == jumpTarget);
        if (elseBlock != selection.Merge)
            throw new Exception("Something went wrong trying to construct a JumpIf");

        var header = (ASTNormalBlock)selection.Header;
        var astCondition = new ASTNormalBlock()
        {
            BlocksByOffset = blocksByOffset,
            Instructions = new()
            {
                new ASTReturn()
                {
                    Value = ExpressionFromConditionOp(
                        lastInstruction.Args[1],
                        lastInstruction.Args[2],
                        (ScriptRootConditionOp)lastInstruction.Args[3].Value,
                        negateByInstruction: true
                    )
                }
            }
        };
        astCondition.FixChildrenParents();
        blocksByOffset[--nextPseudoOffset] = astCondition;

        var astIfElse = new ASTIfElse()
        {
            BlocksByOffset = blocksByOffset,
            Prefix = header.Instructions.Any() ? header : null,
            Condition = astCondition,
            ThenOffset = thenBlock?.StartTotalOffset,
            ElseOffset = null,
            ContinueOffset = selection.Merge == selection.Parent?.Merge ? null : selection.Merge.StartTotalOffset,
            StartOwnOffset = header.StartTotalOffset,
            EndOwnOffset = header.EndTotalOffset
        };
        //header.Instructions.RemoveLast(); // the JumpIf instruction
        blocksByOffset[header.StartTotalOffset] = astIfElse;
        foreach (var lastBlock in selection.Merge.Inbound.Union(selection.Body))
            lastBlock.ConstructProvidesControlFlow = true;
        selection.Header.Parent = astIfElse;
        astCondition.Parent = astIfElse;
        foreach (var body in selection.Body)
            body.Parent = astIfElse;

        if (selection.Parent != null)
        {
            selection.Parent.Body.ExceptWith(selection.Body);
            selection.Parent.Body.Remove(selection.Header);
            selection.Parent.Body.Add(astIfElse);
        }
        else if (selection.Header == astEntry)
            astEntry = astIfElse;
    }

    private void ConstructSwitch(Selection selection, bool isCalcSwitch)
    {        
    }

    private void ConstructGotos()
    {
        return; // would not work for selections nor loops, rework entirely
        foreach (var block in blocksByOffset.Values.OfType<ASTNormalBlock>())
        {
            if (block.Instructions.LastOrDefault() is not ASTRootOpInstruction rootInstr ||
                block.ConstructProvidesControlFlow)
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
                var targetBlock = blocksByOffset[target] // TODO: you kno
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

    private void ConstructContinues()
    {
        // does not work for selections nor loops
        foreach (var block in blocksByOffset.Values)
        {
            
            if (block.ContinueBlock != null ||
                block.ConstructProvidesControlFlow ||
                !block.CanFallthrough ||
                block.EndTotalOffset == astExit.StartTotalOffset)
                continue;
            block.ContinueOffset = block.EndTotalOffset;
        }
    }
}
