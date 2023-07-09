using System;
using System.Collections.Generic;
using System.Linq;

namespace TopGun;

partial class ScriptDecompiler
{
    private abstract class GroupingConstruct
    {
        public required ScriptDecompiler Decompiler { get; init; }
        public required int MergeOffset { get; init; }
        public ASTBlock Merge => Decompiler.blocksByOffset[MergeOffset];
        public GroupingConstruct? Parent { get; set; }
        public List<GroupingConstruct> Children { get; } = new();
        public HashSet<ASTBlock> Body { get; init; } = new();
        public IEnumerable<GroupingConstruct> AllChildren => // implicitly sorted descending by rank
            Children.SelectMany(c => c.AllChildren).Append(this);

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

        public static int DescendingSizeComparison(GroupingConstruct a, GroupingConstruct b) => b.Body.Count - a.Body.Count;

        public void AddChild(GroupingConstruct child)
        {
            var parentChild = Children.SingleOrDefault(c => c.Body.Overlaps(child.Body));
            if (parentChild == null)
            {
                child.Parent = this;
                Children.Add(child);
                Children.Sort(DescendingSizeComparison);
                return;
            }
            if (!parentChild.Body.IsSupersetOf(child.Body))
                throw new NotSupportedException("Unsupported control flow with overlapping, non-nested construct bodies");
            parentChild.AddChild(child);
        }

        public static List<GroupingConstruct> CreateHierarchy(IEnumerable<GroupingConstruct> allConstructs)
        {
            var dummyRoot = new DummyConstruct()
            {
                Decompiler = null!,
                Body = null!,
                MergeOffset =-1
            };
            foreach (var child in allConstructs.OrderByDescending(c => c.Body.Count))
                dummyRoot.AddChild(child);
            dummyRoot.Children.ForEach(c => c.Parent = null);
            return dummyRoot.Children;
        }

        public abstract void Construct();
    }

    private class DummyConstruct : GroupingConstruct
    {
        // used as a root construct to create the hierarchy
        public override void Construct()
        {
            throw new NotImplementedException("Attempted to construct a dummy construct");
        }
    }

    private class NaturalLoop : GroupingConstruct
    {
        public ASTBlock Header { get; init; } = null!;

        public override void Construct()
        {
            var backTarget = Header;
            var targetInbounds = backTarget.Inbound.ToArray();
            var targetOutbounds = backTarget.Outbound.ToArray();
            if (targetInbounds.Length != 2 || targetOutbounds.Length != 2)
                throw new NotSupportedException("Loop with unexpected structure, header has not two inbound and two outbound blocks");

            var backSource = (ASTNormalBlock)(Body.Contains(targetInbounds[0]) ? targetInbounds[0] : targetInbounds[1]);
            var externalInbound = backSource == targetInbounds[0] ? targetInbounds[1] : targetInbounds[0];
            if (!Body.Contains(backSource) || Body.Contains(externalInbound))
                throw new NotSupportedException("Loop with unexpected structure, header inbounds are not supported");

            if (backSource.Outbound.Count() != 1)
                throw new NotSupportedException("Loop with unexpected structure, back-edge block is branching");
            backSource.ConstructProvidesControlFlow = true;
            backSource.LastInstructionIsRedundantControlFlow = true;
            
            var loopEntry = Body.Contains(targetOutbounds[0]) ? targetOutbounds[0] : targetOutbounds[1];
            var externalOutbound = loopEntry == targetOutbounds[0] ? targetOutbounds[1] : targetOutbounds[0];
            if (!Body.Contains(loopEntry) || Body.Contains(externalOutbound))
                throw new NotSupportedException("Loop with unexpected structure, header outbounds are not supported");

            var astLoop = new ASTLoop()
            {
                BlocksByOffset = Header.BlocksByOffset,
                IsPostCondition = false,
                Condition = backTarget,
                BodyOffset = loopEntry.StartTotalOffset,
                Loop = Body,
                ContinueOffset = Merge == Parent?.Merge ? null : Merge.StartTotalOffset,
                InboundOffsets = Header.InboundOffsets,
                OutboundOffsets = Header.OutboundOffsets
            };
            foreach (var block in Body)
                block.Parent = astLoop;
            astLoop.BlocksByOffset[Header.StartTotalOffset] = astLoop;

            if (Parent != null)
            {
                Parent.Body.ExceptWith(Body);
                Parent.Body.Add(astLoop);
            }
        }
    }

    private abstract class Selection : GroupingConstruct
    {
        public ASTBlock Header { get; init; } = null!;
        public HashSet<(int from, int to)> BranchFallthroughs { get; init; } = new();
    }

    private class JumpIfSelection : Selection
    {
        public override void Construct()
        {
            if (Header.Outbound.Count() != 2)
                throw new Exception("Something went wrong trying to construct a JumpIf");
            if (BranchFallthroughs.Any())
                throw new NotSupportedException("JumpIf selections do not support branch fallthroughs");

            var lastInstruction = ((ASTRootOpInstruction)((ASTNormalBlock)Header).Instructions.Last()).RootInstruction;
            var jumpTarget = lastInstruction.Offset + lastInstruction.Args[0].Value;
            var thenBlock = Header.Outbound.SingleOrDefault(b => b.StartTotalOffset != jumpTarget);
            var elseBlock = Header.Outbound.SingleOrDefault(b => b.StartTotalOffset == jumpTarget);
            if (elseBlock != Merge)
                throw new Exception("Something went wrong trying to construct a JumpIf");

            var header = (ASTNormalBlock)Header;
            var astCondition = new ASTNormalBlock()
            {
                BlocksByOffset = header.BlocksByOffset,
                Instructions = new()
                {
                    new ASTReturn()
                    {
                        Value = Decompiler.ExpressionFromConditionOp(
                            lastInstruction.Args[1],
                            lastInstruction.Args[2],
                            (ScriptRootConditionOp)lastInstruction.Args[3].Value,
                            negateByInstruction: true
                        )
                    }
                }
            };
            astCondition.FixChildrenParents();
            Header.BlocksByOffset[-Header.StartTotalOffset] = astCondition;

            var astIfElse = new ASTIfElse()
            {
                BlocksByOffset = Header.BlocksByOffset,
                Prefix = header.Instructions.Any() ? header : null,
                Condition = astCondition,
                ThenOffset = thenBlock?.StartTotalOffset,
                ElseOffset = null,
                ContinueOffset = Merge == Parent?.Merge ? null : Merge.StartTotalOffset,
                StartOwnOffset = header.StartTotalOffset,
                EndOwnOffset = header.EndTotalOffset,
                InboundOffsets = Header.InboundOffsets,
                OutboundOffsets = Header.OutboundOffsets
            };
            Header.BlocksByOffset[header.StartTotalOffset] = astIfElse;
            foreach (var lastBlock in Merge.Inbound.Intersect(Body))
                lastBlock.ConstructProvidesControlFlow = true;
            Header.Parent = astIfElse;
            astCondition.Parent = astIfElse;
            foreach (var body in Body)
                body.Parent = astIfElse;

            if (Parent != null)
            {
                Parent.Body.ExceptWith(Body);
                Parent.Body.Remove(Header);
                Parent.Body.Add(astIfElse);
            }
        }
    }

    private class JumpIfCalcSelection : Selection
    {
        public override void Construct()
        {
            if (Header.Outbound.Count() > 2)
                throw new Exception("Something went wrong trying to construct a JumpIfCalc construct");
            if (BranchFallthroughs.Any())
                throw new NotSupportedException("JumpIfCalc selections do not support branch fallthroughs");

            var lastInstruction = ((ASTRootOpInstruction)((ASTNormalBlock)Header).Instructions.Last()).RootInstruction;
            var thenOffset = lastInstruction.Offset + lastInstruction.Args[0].Value;
            var elseOffset = lastInstruction.Offset + lastInstruction.Args[1].Value;
            
            var astIfElse = new ASTIfElse()
            {
                BlocksByOffset = Header.BlocksByOffset,
                Condition = Header,
                ThenOffset = thenOffset,
                ElseOffset = elseOffset == Merge.StartTotalOffset ? null : elseOffset,
                ContinueOffset = Merge == Parent?.Merge ? null : Merge.StartTotalOffset,
                StartOwnOffset = Header.StartTotalOffset,
                EndOwnOffset = Header.EndTotalOffset,
                InboundOffsets = Header.InboundOffsets,
                OutboundOffsets = Header.OutboundOffsets
            };
            Header.BlocksByOffset[Header.StartTotalOffset] = astIfElse;

            foreach (var lastBlock in Merge.Inbound.Intersect(Body))
                lastBlock.ConstructProvidesControlFlow = true;
            Header.Parent = astIfElse;
            foreach (var body in Body)
                body.Parent = astIfElse;

            if (Parent != null)
            {
                Parent.Body.ExceptWith(Body);
                Parent.Body.Remove(Header);
                Parent.Body.Add(astIfElse);
            }
        }
    }

    private class SwitchSelection : Selection
    {
        public override void Construct()
        {
            var lastInstruction = ((ASTRootOpInstruction)((ASTNormalBlock)Header).Instructions.Last()).RootInstruction;
            int argIndex = 0;
            ASTBlock? astPrefix = null;
            ASTBlock astValue;
            if (lastInstruction.Op == ScriptOp.Switch)
            {
                argIndex++;
                astPrefix = Header;
                ((ASTNormalBlock)astPrefix).LastInstructionIsRedundantControlFlow = true;
                astValue = new ASTNormalBlock()
                {
                    BlocksByOffset = Header.BlocksByOffset,
                    Instructions = new()
                    {
                        new ASTReturn()
                        {
                            Value = Decompiler.ExpressionFromRootArg(lastInstruction.Args[0])
                        }
                    }
                };
                astValue.FixChildrenParents();
                Header.BlocksByOffset[-Header.StartTotalOffset] = astValue;
            }
            else
                astValue = Header;

            var compareCount = (lastInstruction.Args.Count - argIndex - 1) / 2;
            var explicitCaseOffsets = Enumerable
                .Range(0, compareCount)
                .Select(i => (
                    compare: lastInstruction.Args[argIndex + 1 + i * 2].Value as int?,
                    then: lastInstruction.Args[argIndex + 2 + i * 2].Value));
            if (lastInstruction.Args[argIndex].Value != Merge.StartTotalOffset)
                explicitCaseOffsets = explicitCaseOffsets.Append((
                    compare: null, // default jump
                    then: lastInstruction.Args[argIndex].Value));
            var caseOffsets = explicitCaseOffsets.Select(t => (t.compare, then: t.then + lastInstruction.Offset))
                .GroupBy(t => t.then)
                .Select(g => new ASTSwitch.Case<int?>()
                {
                    Compares = g.Select(t => t.compare).ToArray(),
                    Then = g.Key == Merge.StartTotalOffset ? null : g.Key,
                    Breaks = true
                })
                .ToArray();

            foreach (var (from, to) in BranchFallthroughs)
            {
                var fromI = Array.FindIndex(caseOffsets, c => c.Then == from);
                var toI = Array.FindIndex(caseOffsets, c => c.Then == to);
                caseOffsets[fromI] = caseOffsets[fromI] with { Breaks = false };

                var fromBlock = Header.BlocksByOffset[from];
                var toBlock = Header.BlocksByOffset[to];
                foreach (var inbound in toBlock.Inbound.Where(i => i == fromBlock || Decompiler.postDominance.Dominates(i, fromBlock)))
                    inbound.ConstructProvidesControlFlow = true;

                if (fromI + 1 != toI)
                {
                    if (fromI < toI)
                        caseOffsets.ShiftElement(fromI, toI - 1);
                    else
                        caseOffsets.ShiftElement(fromI, toI);
                }
            }

            var astSwitch = new ASTSwitch()
            {
                BlocksByOffset = Header.BlocksByOffset,
                Prefix = astPrefix,
                Value = astValue,
                CaseOffsets = caseOffsets,
                ContinueOffset = Merge == Parent?.Merge ? null : Merge.StartTotalOffset,
                StartOwnOffset = Header.StartTotalOffset,
                EndOwnOffset = Header.EndTotalOffset,
                InboundOffsets = Header.InboundOffsets,
                OutboundOffsets = Header.OutboundOffsets
            };
            Header.BlocksByOffset[Header.StartTotalOffset] = astSwitch;
            foreach (var lastBlock in Merge.Inbound.Intersect(Body))
            {
                lastBlock.ConstructProvidesControlFlow = true;
                if (lastBlock is ASTNormalBlock lastNormalBlock)
                    // case blocks always end with a jump as a (Calc)Switch statement
                    // is always followed by the necessary Case statements causing a Jump instruction
                    // (fallthrough blocks do not end with a Jump but are not merge inbounds either)
                    lastNormalBlock.LastInstructionIsRedundantControlFlow = true;
            }
            Header.Parent = astSwitch;
            astValue.Parent = astSwitch;
            foreach (var body in Body)
                body.Parent = astSwitch;

            if (Parent != null)
            {
                Parent.Body.ExceptWith(Body);
                Parent.Body.Remove(Header);
                Parent.Body.Add(astSwitch);
            }
        }
    }

    private void ConstructContinues()
    {
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
