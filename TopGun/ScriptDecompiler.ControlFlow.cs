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
        
        IEnumerable<ASTBlock> GetOutbound(ASTBlock block);
        IEnumerable<ASTBlock> GetInbound(ASTBlock block);
    }

    private class ForwardBlockIterator : IBlockIterator
    {
        public ASTBlock Start { get; }
        public ForwardBlockIterator(ASTBlock start) => Start = start;
        public IEnumerable<ASTBlock> GetOutbound(ASTBlock block) => block.Outbound;
        public IEnumerable<ASTBlock> GetInbound(ASTBlock block) => block.Inbound;
    }

    private class BackwardBlockIterator : IBlockIterator
    {
        public ASTBlock Start { get; }
        public BackwardBlockIterator(ASTBlock start) => Start = start;
        public IEnumerable<ASTBlock> GetOutbound(ASTBlock block) => block.Inbound;
        public IEnumerable<ASTBlock> GetInbound(ASTBlock block) => block.Outbound;
    }

    private class EdgeIgnoringBlockIterator : IBlockIterator
    {
        public required IBlockIterator Parent { get; init; }
        public required HashSet<(int, int)> IgnoreEdges { get; init; }

        public ASTBlock Start => Parent.Start;
        public IEnumerable<ASTBlock> GetOutbound(ASTBlock block) => FilterEdges(block, Parent.GetOutbound(block));
        public IEnumerable<ASTBlock> GetInbound(ASTBlock block) => FilterEdges(block, Parent.GetInbound(block));
        private IEnumerable<ASTBlock> FilterEdges(ASTBlock block, IEnumerable<ASTBlock> targets) => targets
            .Where(other => !IgnoreEdges.Contains((block.StartTotalOffset, other.StartTotalOffset)) &&
                            !IgnoreEdges.Contains((other.StartTotalOffset, block.StartTotalOffset)));
    }

    private class DominanceTree
    {
        private readonly IBlockIterator iterator;
        private readonly Dictionary<ASTBlock, ASTBlock?> immediate = new();
        private readonly Dictionary<ASTBlock, int> orderNumber = new();

        public DominanceTree(IBlockIterator iterator)
        {
            this.iterator = iterator;
            SetPostOrder();
            SetDominators();
        }

        public IEnumerable<ASTBlock> Get(ASTBlock block)
        {
            ASTBlock? cur, next = immediate[block];
            if (next == null)
                yield break;
            do
            {
                yield return next;
                cur = next;
                next = immediate[cur];
            } while (next != null && next != cur);
        }

        public IEnumerable<ASTBlock> GetDominatees(ASTBlock dom) => immediate.Keys.Where(to => Dominates(dom, to));

        public bool Dominates(ASTBlock from, ASTBlock to) => Get(to).Contains(from);

        private void SetPostOrder()
        {
            var it = iterator;
            var visited = new HashSet<ASTBlock>();
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
                    orderNumber[parent] = next++;
            }
        }

        /// <remarks>Cooper, Harvey, Kennedy - "A Simple, Fast Dominance Algorithm"</remarks>
        private void SetDominators()
        {
            var it = iterator;
            var revOrderBlocks = orderNumber.Keys
                .Except(new[] { it.Start })
                .OrderByDescending(b => orderNumber[b])
                .ToArray();
            foreach (var block in revOrderBlocks)
                immediate[block] = null;
            immediate[it.Start] = it.Start;

            bool changed;
            do
            {
                changed = false;
                foreach (var block in revOrderBlocks)
                {
                    var inbound = it.GetInbound(block)
                        .Where(b => immediate[b] != null);
                    if (!inbound.Any())
                        continue;

                    var oldDominator = immediate[block];
                    var newDominator = inbound.Aggregate(Intersect!) ?? block;
                    if (oldDominator != newDominator)
                    {
                        immediate[block] = newDominator;
                        changed = true;
                    }
                }
            } while (changed);
        }

        private ASTBlock? Intersect(ASTBlock b1, ASTBlock b2)
        {
            if (b1 == null)
                return null;
            while (b1 != b2)
            {
                while (orderNumber[b1] < orderNumber[b2])
                {
                    if (b1 == immediate[b1])
                        return null;
                    b1 = immediate[b1]!;
                }
                while (orderNumber[b2] < orderNumber[b1])
                {
                    if (b2 == immediate[b2])
                        return null;
                    b2 = immediate[b2]!;
                }
            }
            return b1;
        }
    }

    /// <remarks>Olga Nicole Volgin - "Analysis of Flow of Control for Reverse Engineering of Sequence Diagrams" - Section 5.4</remarks>
    private HashSet<(int, int)> FindControllingEdgesAtExit()
    {
        var earlyExitNodes = astExit.Inbound
            .OfType<ASTNormalBlock>()
            .Where(b => b.Instructions.Last() is ASTRootOpInstruction { RootInstruction.Op: ScriptOp.Exit });

        return earlyExitNodes
            .SelectMany(exitNode => postDominance.GetDominatees(exitNode).Append(exitNode)
                .SelectMany(postDom => postDom.Inbound.Select(
                    branch => (exitNode, postDom, branch))))
            .Where(t => !postDominance.Dominates(t.branch, t.exitNode))
            .Select(t => (t.branch.StartTotalOffset, t.postDom.StartTotalOffset))
            .ToHashSet();

        // we do not care about which controlling edge belongs to which early exit node
        // we just remove all controlling edges from all early exit nodes and hope that
        // the resulting post-dominance tree is normalized in regards to selection control flow
    }

    private List<GroupingConstruct> DetectLoops()
    {
        var headers = new HashSet<ASTBlock>();
        var allLoops = new List<GroupingConstruct>();
        var backEdges = blocksByOffset.Values.SelectMany(header => header.Inbound
            .Where(bodyEnd => preDominance.Dominates(header, bodyEnd))
            .Select(bodyEnd => (header, bodyEnd)));
        foreach (var (header, bodyEnd) in backEdges)
        {
            var body = new HashSet<ASTBlock>() { bodyEnd };
            FindReachableFromTo(body, bodyEnd, header, new BackwardBlockIterator(ASTEntry));
            
            if (!headers.Add(header))
                throw new NotSupportedException("Unsupported control flow with merged loops");
            allLoops.Add(new NaturalLoop()
            {
                Decompiler = this,
                Header = header,
                Body = body
            });
        }

        return allLoops;
    }

    private HashSet<ASTBlock> FindReachableFromTo(HashSet<ASTBlock> body, ASTBlock from, ASTBlock to, IBlockIterator iterator)
    {
        // this is not quite standard and will only correctly work with 
        //   - natural loops that have exactly one backedge
        //   - selections
        // (or similar: structures that are fenced by exactly one predetermined block)
        // that are fully nested or disjoint

        body.Add(to);
        if (from == to)
            return body;
        var stack = new Stack<(ASTBlock, IEnumerator<ASTBlock>)>();
        stack.Push((from, iterator.GetOutbound(from).GetEnumerator()));
        while(stack.Any())
        {
            var (parent, edgeIt) = stack.Peek();
            body.Add(parent);
            if (!edgeIt.MoveNext())
            {
                stack.Pop();
                continue;
            }
            var child = edgeIt.Current;
            
            if (!body.Contains(child) && child != from)
                stack.Push((child, iterator.GetOutbound(child).GetEnumerator()));
        }
        return body;
    }

    private List<GroupingConstruct> DetectSelections()
    {
        var headers = blocksByOffset.Values
            .Where(b => b.Parent == null)
            .Where(b => b.Outbound.Count() > 1)
            .Where(b => b.Inbound.All(i => !preDominance.Dominates(b, i))); // no back edges
        var allSelections = new List<GroupingConstruct>();
        foreach (var header in headers)
        {
            var filteredBranches = header.Outbound.Where(branch => !controllingEdgesAtExit.Contains((header.StartTotalOffset, branch.StartTotalOffset)));
            ASTBlock? mergeBlock;

            if (filteredBranches.Count() > 1)
                mergeBlock = header.Outbound
                    .Select(b => postDominance.Get(b).Prepend(b))
                    .Aggregate((a, b) => a.Intersect(b).ToArray())
                    .First(); // there will always be one due to the exit node
            else if (filteredBranches.Count() == 1) // no longer a merging selection
                mergeBlock = filteredBranches.Single();
            else
                throw new NotSupportedException("I have not thought about selections that always early-exit");

            var branches = header.Outbound
                .Select(branch => FindReachableFromTo(new(), branch, mergeBlock, new ForwardBlockIterator(ASTEntry)).Where(bodyBlock => bodyBlock == branch || preDominance.Dominates(branch, bodyBlock)).ToHashSet())
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

        return allSelections;
    }
}
